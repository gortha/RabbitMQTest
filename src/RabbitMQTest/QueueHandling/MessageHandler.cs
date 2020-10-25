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

        public MyMessageHandler(IMyService myService)
        {
            _myService = myService;
        }

        public async Task Handle(MyMessage message)
        {
            Console.WriteLine("Consumed " + message.Id);
            throw new Exception("Errour");
            var id = await _myService.Execute(message);            
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

        public Task Handle(MyMessage2 message)
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

        public async Task Handle(MyMessage message)
        {
            throw new Exception("Errour");
            var id = await _myService.Execute(message);
        }
    }
}
