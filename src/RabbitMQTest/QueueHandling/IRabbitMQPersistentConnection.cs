using RabbitMQ.Client;
using System;

namespace RabbitMQTest.QueueHandling
{
    public interface IRabbitMQPersistentConnection
        : IDisposable
    {
        bool IsConnected { get; }

        bool TryConnect();

        IModel CreateModel();
    }
}
