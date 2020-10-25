using RabbitMQTest.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RabbitMQTest.QueueHandling
{
    public class MyMessageHandler : IMessageHandler<MyMessage>
    {
        private readonly IMyService _myService;
        private readonly IMessageBus _messageBus;
        public MyMessageHandler(IMyService myService, IMessageBus messageBus)
        {
            _myService = myService;
            _messageBus = messageBus;
        }

        public async Task Handle(MyMessage message, int retryCount)
        {
            try
            {
                Console.WriteLine("Consumed " + message.Id);
                throw new Exception("Errour");
                var id = await _myService.Execute(message);
            }
            catch (Exception ex)
            {
                _messageBus.RetryPublish(new MyMessage()
                {
                    Content = "Nouveau " + DateTime.Now.ToShortDateString(),
                    Id = message.Id
                },  retryCount);
            }
        }
    }

    public class MyService : IMyService
    {
        public Task<int> Execute(MyMessage myMessage)
        {
            return Task.FromResult<int>(myMessage.Id);
        }
    }

    public class MyMessageHandler2 : IMessageHandler<MyMessage2>
    {

        public Task Handle(MyMessage2 message, int retryCount)
        {
            return Task.CompletedTask;
        }
    }

    public class MyMessageHandler3 : IMessageHandler<MyMessage>
    {
        private readonly IMyService _myService;

        public MyMessageHandler3(IMyService myService)
        {
            _myService = myService;
        }

        public async Task Handle(MyMessage message, int retryCount)
        {
            throw new Exception("Errour");
            var id = await _myService.Execute(message);
        }
    }
}
