using System;

namespace RabbitMQTest.Core
{
    public abstract class Message
    {
        public DateTime Timestamp { get; protected set; }

        protected Message()
        {
            Timestamp = DateTime.Now;
        }
    }

    
}
