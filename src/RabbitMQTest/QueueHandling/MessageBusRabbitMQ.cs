using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.DataProtection;
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
        private const string EXCHANGE_NAME = "mymessage_bus";
        private const string ERROR_SUFIX = "_error";
        private static string EXCHANGE_ERROR_NAME = $"{EXCHANGE_NAME}{ERROR_SUFIX}";
        private const int MAX_RETRY_COUNT = 3;
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
                string messageTypeName = message.GetType().Name;
                channel.ExchangeDeclare(exchange: EXCHANGE_NAME, type: ExchangeType.Direct, durable: true);

                channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, null);
                var properties = channel.CreateBasicProperties();
                properties.DeliveryMode = 2; // persistent

                var msg = JsonConvert.SerializeObject(message);
                var body = Encoding.UTF8.GetBytes(msg);
                
                channel.BasicPublish(exchange: EXCHANGE_NAME, routingKey: messageTypeName, properties, body);
            }
        }

        public void RetryPublish<T>(T message, int retryCount, int retryMax = MAX_RETRY_COUNT)
            where T : Message
        {
            var msg = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(msg);
            SendToErrorQueue(exchangeSource: EXCHANGE_NAME, routingKeySource: typeof(T).Name, body, retryCount, retryMax);
        }

        public void BindErrorQueueWithDeadLetterExchangeStrategy<T>(string sourceQueueName, int timeToLive) where T : Message
        {
            if (!_rabbitMQPersistentConnection.IsConnected)
            {
                _rabbitMQPersistentConnection.TryConnect();
            }

            using (var channel = _rabbitMQPersistentConnection.CreateModel())
            {
                // Declare Error queue
                var errorQueueName = $"{sourceQueueName}{ERROR_SUFIX}";
                var sourceRoutingKey = typeof(T).Name;
                var errorRoutingKey = $"{sourceRoutingKey}{ERROR_SUFIX}";
                channel.ExchangeDeclare(exchange: EXCHANGE_ERROR_NAME, type: ExchangeType.Direct, durable: true);

                // Bind Dead Letter Exchange and RoutingKey
                var arguments = new Dictionary<string, object>()
                {
                    {"x-message-ttl", timeToLive },
                    {"x-dead-letter-exchange", EXCHANGE_NAME },
                    {"x-dead-letter-routing-key", sourceRoutingKey },
                };

                channel.QueueDeclare(queue: errorQueueName, durable: true, exclusive: false, autoDelete: false, arguments);
                channel.QueueBind(queue: errorQueueName,
                                      exchange: EXCHANGE_ERROR_NAME,
                                      routingKey: errorRoutingKey);
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
            (IModel Channel, string queueName) achannel = (null, null);
            if (!_consumerChannels.TryGetValue(messageTypeName, out var channel))
            {
                //channel = CreateConsumerChannel(messageTypeName, queueName);
                achannel = CreateConsumerChannel(messageTypeName, queueName);
                channel = achannel.Channel;
            }
            if (channel == null)
            {
                //_logger.LogError("StartBasicConsume can't call on _consumerChannel == null");
                return;
            }

            var consumer = new AsyncEventingBasicConsumer(channel);
            //var consumer = new ConsumerFailingOnConsumeOk(channel);
            consumer.Received += Consumer_Received;
            queueName = achannel.queueName;
            channel.BasicConsume(queueName, autoAck: false, consumer);

        }

        private async Task Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var channel = ((AsyncEventingBasicConsumer)sender).Model;

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
                        await (Task)handlerGenericType.GetMethod("Handle").Invoke(handler, new object[] { msg, GetRetryCount(e.BasicProperties) });

                    }
                }                
            }
            catch (Exception ex)
            {
                //throw;
                //channel.BasicNack(e.DeliveryTag, false, false);                
                SendToErrorQueue(e.Exchange, e.RoutingKey, e.Body.ToArray(), GetRetryCount(e.BasicProperties));
            }

            //var channel = ((ConsumerFailingOnConsumeOk)sender).Model;
            channel.BasicAck(e.DeliveryTag, multiple: false);
        }


        private void SendToErrorQueue(string exchangeSource, string routingKeySource, byte[] body, int retryCount, int retryMax = MAX_RETRY_COUNT)
        {            
            
            if (!_rabbitMQPersistentConnection.IsConnected)
            {
                _rabbitMQPersistentConnection.TryConnect();
            }

            using (var channel = _rabbitMQPersistentConnection.CreateModel())
            {                
                if (retryCount < retryMax)
                {
                    var properties = channel.CreateBasicProperties();
                    SetRetryCount(properties, ++retryCount);
                    Console.WriteLine($"Retry count: {retryCount}");
                    properties.DeliveryMode = 2; // persistent
                    channel.BasicPublish($"{exchangeSource}{ERROR_SUFIX}", $"{routingKeySource}{ERROR_SUFIX}", properties, body);
                }
                else
                {
                    // reject the message to dead letter queue.
                    //channel.BasicNack(deliveryTag, false, false);

                    // Log Fatal with info message - exchange - routingKey
                }
            }
        }

        private int GetRetryCount(IBasicProperties properties)
        {
            // use the headers field of the message properties to keep track of 
            // the number of retries
            return (int?)properties.Headers?["Retries"] ?? 0;
        }
        private void SetRetryCount(IBasicProperties properties, int retryCount)
        {
            properties.Headers = properties.Headers ?? new Dictionary<string, object>();
            properties.Headers["Retries"] = retryCount;
        }

        private (IModel Channel, string queueName) CreateConsumerChannel(string messageTypeName, string queueName)
        {
            if (!_rabbitMQPersistentConnection.IsConnected)
            {
                _rabbitMQPersistentConnection.TryConnect();
            }

            var channel = _rabbitMQPersistentConnection.CreateModel();
            channel.ExchangeDeclare(EXCHANGE_NAME, type: ExchangeType.Direct, durable: true);
            channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, null);

            // For many consumer at the times
            //queueName = channel.QueueDeclare().QueueName;

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
                //model = CreateConsumerChannel(messageTypeName, queueName);
                var aa = CreateConsumerChannel(messageTypeName, queueName);
                model = aa.Channel;
                queueName = aa.queueName;
                StartBasicConsume(messageTypeName, queueName);
            };
            _consumerChannels[messageTypeName] = channel;
            return (channel, queueName);
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
