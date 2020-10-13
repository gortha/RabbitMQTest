namespace RabbitMQTest.Core
{
    public interface IMessageBus
    {
        void Publish<T>(T message, string queueName) where T : Message;

        void Subscribe<T, TH>(string queueName)
            where T : Message
            where TH : IMessageHandler<T>;
    }
}
