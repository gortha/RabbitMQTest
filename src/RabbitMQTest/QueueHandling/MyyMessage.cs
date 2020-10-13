using RabbitMQTest.Core;

namespace RabbitMQTest.QueueHandling
{
    public class MyMessage : Message
    {
        public int Id { get; set; }
        public string Content { get; set; }
    }

    public class MyMessage2 : Message
    {
        public int Id { get; set; }
        public string Content { get; set; }
    }
}
