namespace RabbitMQTest.Core
{
    public interface IMessageBus
    {
        void Publish<T>(T message, string queueName) where T : Message;

        void Subscribe<T, TH>(string queueName)
            where T : Message
            where TH : IMessageHandler<T>;

        void BindErrorQueueWithDeadLetterExchangeStrategy<T>(string sourceQueueName, int timeToLiveInMs) where T : Message;
        void RetryPublish<T>(T message, int retryCount, int retryMax = 3)
            where T : Message;
    }
}
