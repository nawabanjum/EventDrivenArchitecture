using Azure.Messaging.ServiceBus;
using OrderProcessing.NotificationApi.Consumers;
using OrderProcessing.NotificationApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton(new ServiceBusClient(
    builder.Configuration["AzureServiceBus:ConnectionString"]));
builder.Services.AddSingleton<NotificationStore>();
builder.Services.AddHostedService<OrderPaidConsumer>();

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
