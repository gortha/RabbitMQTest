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
}
