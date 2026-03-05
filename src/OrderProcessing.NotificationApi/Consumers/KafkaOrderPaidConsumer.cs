using System.Text.Json;
using Confluent.Kafka;
using OrderProcessing.Domain.Events;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Consumers;

public class KafkaOrderPaidConsumer : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly NotificationStore _notificationStore;
    private readonly ILogger<KafkaOrderPaidConsumer> _logger;
    private readonly string _topic;

    public KafkaOrderPaidConsumer(
        IConfiguration config,
        NotificationStore notificationStore,
        ILogger<KafkaOrderPaidConsumer> logger)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        _consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        _topic = config["Kafka:OrderPaidTopic"]!;
        _notificationStore = notificationStore;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            _consumer.Subscribe(_topic);
            _logger.LogInformation("[Kafka] Notification consumer started on topic: {Topic}", _topic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(stoppingToken);
                    var paidEvent = JsonSerializer.Deserialize<OrderPaidEvent>(result.Message.Value)!;

                    _logger.LogInformation(
                        "[Kafka] Sending notification for Order {OrderId}, PaymentId: {PaymentId}, Amount: {Amount}",
                        paidEvent.OrderId, paidEvent.PaymentId, paidEvent.PaidAmount);

                    _notificationStore.Add(new NotificationRecord
                    {
                        OrderId = paidEvent.OrderId,
                        PaymentId = paidEvent.PaymentId,
                        PaidAmount = paidEvent.PaidAmount,
                        Status = "Sent",
                        NotifiedAt = DateTime.UtcNow
                    });

                    _logger.LogInformation(
                        "[Kafka] Notification sent for Order {OrderId}", paidEvent.OrderId);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "[Kafka] Error consuming message");
                }
            }

            _consumer.Close();
        }, stoppingToken);
    }
}
