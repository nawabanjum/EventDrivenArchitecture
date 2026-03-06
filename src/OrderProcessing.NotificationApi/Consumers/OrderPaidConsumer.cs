using System.Text.Json;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Domain.Events;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Consumers;

public class OrderPaidConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly NotificationStore _notificationStore;
    private readonly ILogger<OrderPaidConsumer> _logger;

    public OrderPaidConsumer(
        ServiceBusClient serviceBusClient,
        IConfiguration configuration,
        NotificationStore notificationStore,
        ILogger<OrderPaidConsumer> logger)
    {
        var topicName = configuration["AzureServiceBus:OrderPaidTopic"]!;
        var subscriptionName = configuration["AzureServiceBus:SubscriptionName"]!;

        _processor = serviceBusClient.CreateProcessor(topicName, subscriptionName);
        _notificationStore = notificationStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("OrderPaidConsumer started listening on order-paid topic");

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await _processor.StopProcessingAsync();
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var orderPaid = JsonSerializer.Deserialize<OrderPaidEvent>(body);

        if (orderPaid is null)
        {
            _logger.LogWarning("Received null OrderPaidEvent — sending to dead-letter queue");
            await args.DeadLetterMessageAsync(args.Message, "InvalidMessage", "Deserialized to null");
            return;
        }

        try
        {
            _logger.LogInformation(
                "Sending notification for Order {OrderId}: Payment {PaymentId} of {Amount:C} (Attempt {Attempt})",
                orderPaid.OrderId, orderPaid.PaymentId, orderPaid.PaidAmount, args.Message.DeliveryCount);

            _notificationStore.Add(new NotificationRecord
            {
                OrderId = orderPaid.OrderId,
                PaymentId = orderPaid.PaymentId,
                PaidAmount = orderPaid.PaidAmount,
                Status = "Sent",
                NotifiedAt = DateTime.UtcNow
            });

            _logger.LogInformation("Notification sent for Order {OrderId}", orderPaid.OrderId);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error sending notification for Order {OrderId} (Attempt {Attempt}). Abandoning for retry.",
                orderPaid.OrderId, args.Message.DeliveryCount);

            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus error: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
