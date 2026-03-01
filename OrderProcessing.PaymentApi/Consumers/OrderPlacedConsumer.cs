using System.Text.Json;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Domain.Events;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Consumers;

public class OrderPlacedConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ServiceBusPublisher _publisher;
    private readonly PaymentStore _paymentStore;
    private readonly ILogger<OrderPlacedConsumer> _logger;

    public OrderPlacedConsumer(
        ServiceBusClient serviceBusClient,
        IConfiguration configuration,
        PaymentStore paymentStore,
        ILogger<OrderPlacedConsumer> logger)
    {
        var topicName = configuration["AzureServiceBus:OrderPlacedTopic"]!;
        var subscriptionName = configuration["AzureServiceBus:SubscriptionName"]!;
        var orderPaidTopic = configuration["AzureServiceBus:OrderPaidTopic"]!;

        _processor = serviceBusClient.CreateProcessor(topicName, subscriptionName);
        _publisher = new ServiceBusPublisher(serviceBusClient, orderPaidTopic);
        _paymentStore = paymentStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("OrderPlacedConsumer started listening on order-placed topic");

        // Keep the processor running until cancellation is requested
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

        _logger.LogInformation("Processing payment for Order {OrderId}, Amount: {Amount}",
            orderPlaced.OrderId, orderPlaced.TotalAmount);

        // Simulate payment processing
        await Task.Delay(500);

        var paymentId = Guid.NewGuid().ToString();
        var paidAt = DateTime.UtcNow;

        _paymentStore.Add(new PaymentRecord
        {
            OrderId = orderPlaced.OrderId,
            PaymentId = paymentId,
            Amount = orderPlaced.TotalAmount,
            Status = "Paid",
            PaidAt = paidAt
        });

        var orderPaidEvent = new OrderPaidEvent
        {
            OrderId = orderPlaced.OrderId,
            PaymentId = paymentId,
            PaidAmount = orderPlaced.TotalAmount,
            PaidAt = paidAt
        };

        await _publisher.PublishAsync(orderPaidEvent);

        _logger.LogInformation("Payment completed for Order {OrderId}. Published OrderPaidEvent", orderPlaced.OrderId);

        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error processing message: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
