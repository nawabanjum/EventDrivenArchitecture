using Azure.Messaging.ServiceBus;
using OrderProcessing.PaymentApi.Consumers;
using OrderProcessing.PaymentApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton(new ServiceBusClient(
    builder.Configuration["AzureServiceBus:ConnectionString"]));
builder.Services.AddSingleton<PaymentStore>();
builder.Services.AddHostedService<OrderPlacedConsumer>();

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
