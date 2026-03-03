// using Azure.Messaging.ServiceBus;
using RabbitMQ.Client;
using OrderProcessing.NotificationApi.Consumers;
using OrderProcessing.NotificationApi.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Azure Service Bus Registration (commented out) ---
// builder.Services.AddSingleton(new ServiceBusClient(
//     builder.Configuration["AzureServiceBus:ConnectionString"]));
// builder.Services.AddHostedService<OrderPaidConsumer>();
// --- End Azure Service Bus ---
builder.Services.AddSingleton<NotificationStore>();

// --- RabbitMQ Registration ---
var rabbitFactory = new ConnectionFactory
{
    HostName = builder.Configuration["RabbitMQ:HostName"],
    Port = int.Parse(builder.Configuration["RabbitMQ:Port"]!),
    UserName = builder.Configuration["RabbitMQ:UserName"],
    Password = builder.Configuration["RabbitMQ:Password"]
};

var rabbitConnection = await rabbitFactory.CreateConnectionAsync();
builder.Services.AddSingleton(rabbitConnection);
builder.Services.AddHostedService<RabbitMqOrderPaidConsumer>();
// --- End RabbitMQ ---

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
