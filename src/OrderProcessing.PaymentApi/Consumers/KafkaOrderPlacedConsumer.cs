using System.Text.Json;
using Confluent.Kafka;
using OrderProcessing.Domain.Events;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Consumers;

public class KafkaOrderPlacedConsumer : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly PaymentStore _paymentStore;
    private readonly KafkaPublisher _kafkaPublisher;
    private readonly ILogger<KafkaOrderPlacedConsumer> _logger;
    private readonly string _topic;

    public KafkaOrderPlacedConsumer(
        IConfiguration config,
        PaymentStore paymentStore,
        KafkaPublisher kafkaPublisher,
        ILogger<KafkaOrderPlacedConsumer> logger)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        _consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        _topic = config["Kafka:OrderPlacedTopic"]!;
        _paymentStore = paymentStore;
        _kafkaPublisher = kafkaPublisher;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            _consumer.Subscribe(_topic);
            _logger.LogInformation("[Kafka] Payment consumer started on topic: {Topic}", _topic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(stoppingToken);
                    var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(result.Message.Value)!;

                    _logger.LogInformation(
                        "[Kafka] Processing payment for Order {OrderId}, Amount: {Amount}",
                        orderEvent.OrderId, orderEvent.TotalAmount);

                    // Simulate payment processing
                    Thread.Sleep(500);

                    var paymentId = Guid.NewGuid().ToString();
                    var paidAt = DateTime.UtcNow;

                    _paymentStore.Add(new PaymentRecord
                    {
                        OrderId = orderEvent.OrderId,
                        PaymentId = paymentId,
                        Amount = orderEvent.TotalAmount,
                        Status = "Paid",
                        PaidAt = paidAt
                    });

                    // Publish OrderPaidEvent to Kafka
                    var orderPaidEvent = new OrderPaidEvent
                    {
                        OrderId = orderEvent.OrderId,
                        PaymentId = paymentId,
                        PaidAmount = orderEvent.TotalAmount,
                        PaidAt = paidAt
                    };
                    _kafkaPublisher.PublishAsync(orderPaidEvent).GetAwaiter().GetResult();

                    _logger.LogInformation(
                        "[Kafka] Payment completed for Order {OrderId}, PaymentId: {PaymentId}",
                        orderEvent.OrderId, paymentId);
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
