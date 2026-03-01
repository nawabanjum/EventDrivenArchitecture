using System.Text.Json;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Domain.Events;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Consumers;

public class OrderPlacedConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly InventoryStore _inventoryStore;
    private readonly ILogger<OrderPlacedConsumer> _logger;

    public OrderPlacedConsumer(
        ServiceBusClient serviceBusClient,
        IConfiguration configuration,
        InventoryStore inventoryStore,
        ILogger<OrderPlacedConsumer> logger)
    {
        var topicName = configuration["AzureServiceBus:OrderPlacedTopic"]!;
        var subscriptionName = configuration["AzureServiceBus:SubscriptionName"]!;

        _processor = serviceBusClient.CreateProcessor(topicName, subscriptionName);
        _inventoryStore = inventoryStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("OrderPlacedConsumer started listening on order-placed topic");

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await _processor.StopProcessingAsync();
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var orderPlaced = JsonSerializer.Deserialize<OrderPlacedEvent>(body);

        if (orderPlaced is null)
        {
            _logger.LogWarning("Received null OrderPlacedEvent");
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        _logger.LogInformation("Reserving inventory for Order {OrderId}, Product: {Product}, Qty: {Quantity}",
            orderPlaced.OrderId, orderPlaced.Product, orderPlaced.Quantity);

        // Simulate inventory reservation
        await Task.Delay(300);

        _inventoryStore.Add(new InventoryReservation
        {
            OrderId = orderPlaced.OrderId,
            Product = orderPlaced.Product,
            Quantity = orderPlaced.Quantity,
            Status = "Reserved",
            ReservedAt = DateTime.UtcNow
        });

        _logger.LogInformation("Inventory reserved for Order {OrderId}", orderPlaced.OrderId);

        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error processing message: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
