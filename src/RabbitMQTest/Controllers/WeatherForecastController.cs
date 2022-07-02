using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RabbitMQTest.Core;
using RabbitMQTest.QueueHandling;

namespace RabbitMQTest.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly IMessageBus _messageBus;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, IMessageBus messageBus)
        {
            _logger = logger;
            _messageBus = messageBus;
        }

        public static int ID = 1;
        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            _messageBus.Publish(new MyMessage()
            {
                Id = ID,
                Content = Guid.NewGuid().ToString()
            }, "my_first_queue_d");
            _messageBus.Publish(new MyMessage2()
            {
                Id = ID + 100,
                Content = Guid.NewGuid().ToString()
            }, "my_second_queue_d");
            ID++;
            var rng = new Random();
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = rng.Next(-20, 55),
                Summary = Summaries[rng.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
}
