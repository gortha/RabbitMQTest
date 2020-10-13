using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQTest.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RabbitMQTest.QueueHandling
{
    public class MessageBusRabbitMQ : IMessageBus
    {
        private readonly IRabbitMQPersistentConnection _rabbitMQPersistentConnection;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IList<Type> _messageTypes;
        private readonly IDictionary<string, List<Type>> _messageHandlers;
        private readonly IDictionary<string, IModel> _consumerChannels;
        const string EXCHANGE_NAME = "mymessage_bus";
        public MessageBusRabbitMQ(IRabbitMQPersistentConnection rabbitMQPersistentConnection, IServiceScopeFactory serviceScopeFactory)
        {
            _rabbitMQPersistentConnection = rabbitMQPersistentConnection;
            _serviceScopeFactory = serviceScopeFactory;
            _messageTypes = new List<Type>();
            _messageHandlers = new Dictionary<string, List<Type>>();
            _consumerChannels = new Dictionary<string, IModel>();
        }

        public void Publish<T>(T message, string queueName) where T : Message
        {
            if (!_rabbitMQPersistentConnection.IsConnected)
            {
                _rabbitMQPersistentConnection.TryConnect();
            }

            using (var channel = _rabbitMQPersistentConnection.CreateModel())
            {

                channel.ExchangeDeclare(exchange: EXCHANGE_NAME, type: "direct");
                channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, null);
                var properties = channel.CreateBasicProperties();
                properties.DeliveryMode = 2; // persistent

                var msg = JsonConvert.SerializeObject(message);
                var body = Encoding.UTF8.GetBytes(msg);
                var messageTypeName = message.GetType().Name;
                channel.BasicPublish(exchange: EXCHANGE_NAME, routingKey: messageTypeName, properties, body);
            }
        }

        public void Subscribe<T, TH>(string queueName)
            where T : Message
            where TH : IMessageHandler<T>
        {
            var messageType = typeof(T);
            var messageTypeName = messageType.Name;
            var handlerType = typeof(TH);

            if (!_messageTypes.Contains(messageType))
            {
                _messageTypes.Add(messageType);
            }

            if (!_messageHandlers.ContainsKey(messageTypeName))
            {
                _messageHandlers.Add(messageTypeName, new List<Type>());
            }

            if (_messageHandlers[messageTypeName].Any(s => s.GetType() == handlerType))
            {
                throw new ArgumentException(
                    $"Handler Type {handlerType.Name} already is registered for '{messageTypeName}'", nameof(handlerType));
            }

            _messageHandlers[messageTypeName].Add(handlerType);

            StartBasicConsume(messageTypeName, queueName);
        }
        
        private void StartBasicConsume(string messageTypeName, string queueName)
        {
            if (!_consumerChannels.TryGetValue(messageTypeName, out var channel))
            {
                channel = CreateConsumerChannel(messageTypeName, queueName);
            }
            if (channel == null)
            {
                //_logger.LogError("StartBasicConsume can't call on _consumerChannel == null");
                return;
            }

            var consumer = new AsyncEventingBasicConsumer(channel);
            //var consumer = new ConsumerFailingOnConsumeOk(channel);
            consumer.Received += Consumer_Received;
            channel.BasicConsume(queueName, autoAck: false, consumer);

        }

        private async Task Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                var messageId = e.BasicProperties.MessageId;
                var messageTypeName = e.RoutingKey;
                var message = Encoding.UTF8.GetString(e.Body.ToArray());

                if (!_messageHandlers.TryGetValue(messageTypeName, out var handlerTypes))
                {
                    // logger Warning
                }

                foreach (var handlerType in handlerTypes)
                {
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        var handler = scope.ServiceProvider.GetService(handlerType);
                        var messageType = _messageTypes.FirstOrDefault(m => m.Name == messageTypeName);
                        if (messageType is null)
                        {
                            // logger Warning
                        }
                        var msg = JsonConvert.DeserializeObject(message, messageType);
                        var handlerGenericType = typeof(IMessageHandler<>).MakeGenericType(messageType);
                        await (Task)handlerGenericType.GetMethod("Handle").Invoke(handler, new object[] { msg });

                    }
                }
            }
            catch (Exception ex)
            {
                //throw;
            }
            var channel = ((AsyncEventingBasicConsumer)sender).Model;
            //var channel = ((ConsumerFailingOnConsumeOk)sender).Model;
            channel.BasicAck(e.DeliveryTag, multiple: false);
        }



        private IModel CreateConsumerChannel(string messageTypeName, string queueName)
        {
            if (!_rabbitMQPersistentConnection.IsConnected)
            {
                _rabbitMQPersistentConnection.TryConnect();
            }

            var channel = _rabbitMQPersistentConnection.CreateModel();
            channel.ExchangeDeclare(EXCHANGE_NAME, type: "direct");
            channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, null);
            channel.QueueBind(queue: queueName,
                                      exchange: EXCHANGE_NAME,
                                      routingKey: messageTypeName);

            channel.CallbackException += (sender, e) =>
            {
                //_logger.LogWarning(ea.Exception, "Recreating RabbitMQ consumer channel");
                if (!_consumerChannels.TryGetValue(messageTypeName, out var model))
                {
                    // log error
                }

                model.Dispose();
                model = CreateConsumerChannel(messageTypeName, queueName);
                StartBasicConsume(messageTypeName, queueName);
            };
            _consumerChannels[messageTypeName] = channel;
            return channel;
        }

        private static int mycount = 0;
        private class ConsumerFailingOnConsumeOk : AsyncEventingBasicConsumer
        {
            public ConsumerFailingOnConsumeOk(IModel model) : base(model)
            {
            }

            //public override Task HandleBasicConsumeOk(string consumerTag)
            //{
            //    if (mycount == 0)
            //    {
            //        mycount++;
            //        throw new Exception("Falaye  Exceptionnn");
            //    }

            //    return base.HandleBasicConsumeOk(consumerTag);
            //}

            //public override Task HandleBasicCancelOk(string consumerTag)
            //{
            //    return Task.CompletedTask;
            //}

            public override Task HandleBasicDeliver(string consumerTag, ulong deliveryTag, bool redelivered, string exchange, string routingKey, IBasicProperties properties, ReadOnlyMemory<byte> body)
            {
                if (mycount == 0)
                {
                    mycount++;
                    throw new Exception("Falaye  Exceptionnn");
                }

                return base.HandleBasicDeliver(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body);
            }

            //public override Task HandleModelShutdown(object model, ShutdownEventArgs reason)
            //{
            //    return Task.CompletedTask;
            //}
        }
    }
}
