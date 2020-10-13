using System.Threading.Tasks;

namespace RabbitMQTest.QueueHandling
{
    public interface IMyService
    {
        Task<int> Execute(MyMessage myMessage);
    }
}