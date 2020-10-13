using System.Threading.Tasks;

namespace RabbitMQTest.Core
{
    public interface IMessageHandler
    {

    }

    public interface IMessageHandler<in TMessage> : IMessageHandler
       where TMessage : Message
    {
        Task Handle(TMessage message);
    }
}
