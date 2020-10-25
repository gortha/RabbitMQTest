using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQTest.Core;
using RabbitMQTest.QueueHandling;

namespace RabbitMQTest
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();

            // BUS
            services.AddSingleton<IRabbitMQPersistentConnection>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<DefaultRabbitMQPersistentConnection>>();

                var factory = new ConnectionFactory()
                {
                    //HostName = Configuration["MessageBusConnection"],
                    HostName = "localhost",
                    DispatchConsumersAsync = true,
                    //AutomaticRecoveryEnabled = true,
                    //NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
                };

                //if (!string.IsNullOrEmpty(Configuration["MessageBusUserName"]))
                //{
                    //factory.UserName = Configuration["MessageBusUserName"];
                    factory.UserName = "guest";
                //}

                //if (!string.IsNullOrEmpty(Configuration["MessageBusPassword"]))
                //{
                    //factory.Password = Configuration["MessageBusPassword"];
                    factory.Password = "guest";
                //}

                var retryCount = 5;
                if (!string.IsNullOrEmpty(Configuration["MessageBusRetryCount"]))
                {
                    retryCount = int.Parse(Configuration["MessageBusRetryCount"]);
                }

                return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
            });


            // BusManager
            services.AddSingleton<IMessageBus, MessageBusRabbitMQ>(context =>
            {
                var scopeFactory = context.GetRequiredService<IServiceScopeFactory>();
                var rabbitMQPersistentConnection = context.GetService<IRabbitMQPersistentConnection>();
                return new MessageBusRabbitMQ(rabbitMQPersistentConnection, scopeFactory);
            });

            // Message Handlers
            services.AddTransient<MyMessageHandler>();
            services.AddTransient<MyMessageHandler2>();
            services.AddTransient<IMessageHandler<MyMessage>, MyMessageHandler>();
            services.AddTransient<IMessageHandler<MyMessage2>, MyMessageHandler2>();

            // Service 
            services.AddScoped<IMyService, MyService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            ConfigureEventBus(app);
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var messageBus = app.ApplicationServices.GetRequiredService<IMessageBus>();
            messageBus.Subscribe<MyMessage, MyMessageHandler>("my_firt_queue_d");
            messageBus.BindErrorQueueWithDeadLetterExchangeStrategy<MyMessage>("my_firt_queue_d", 10000);
            messageBus.Subscribe<MyMessage2, MyMessageHandler2>("my_second_queue_d");
            
        }
    }
}
