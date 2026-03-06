using System.Text.Json;
using Confluent.Kafka;
using OrderProcessing.Domain.Events;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Consumers;

public class KafkaOrderPaidConsumer : BackgroundService
{
    private const int MaxRetries = 3;
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IProducer<Null, string> _dltProducer;
    private readonly NotificationStore _notificationStore;
    private readonly ILogger<KafkaOrderPaidConsumer> _logger;
    private readonly string _topic;
    private readonly string _dltTopic;

    public KafkaOrderPaidConsumer(
        IConfiguration config,
        NotificationStore notificationStore,
        ILogger<KafkaOrderPaidConsumer> logger)
    {
        var bootstrapServers = config["Kafka:BootstrapServers"]!;
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        _consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };
        _dltProducer = new ProducerBuilder<Null, string>(producerConfig).Build();

        _topic = config["Kafka:OrderPaidTopic"]!;
        _dltTopic = $"{_topic}.DLT";
        _notificationStore = notificationStore;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            _consumer.Subscribe(_topic);
            _logger.LogInformation("[Kafka] Notification consumer started on topic: {Topic} (DLT: {Dlt})", _topic, _dltTopic);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = _consumer.Consume(stoppingToken);
                        var processed = false;

                        for (int attempt = 1; attempt <= MaxRetries; attempt++)
                        {
                            try
                            {
                                var paidEvent = JsonSerializer.Deserialize<OrderPaidEvent>(result.Message.Value)!;

                                _logger.LogInformation(
                                    "[Kafka] Sending notification for Order {OrderId}, PaymentId: {PaymentId}, Amount: {Amount} (Attempt {Attempt}/{Max})",
                                    paidEvent.OrderId, paidEvent.PaymentId, paidEvent.PaidAmount, attempt, MaxRetries);

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

                                processed = true;
                                break;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (attempt < MaxRetries)
                                {
                                    var delay = (int)Math.Pow(2, attempt) * 1000;
                                    _logger.LogWarning(ex,
                                        "[Kafka] Notification failed (Attempt {Attempt}/{Max}). Retrying in {Delay}ms...",
                                        attempt, MaxRetries, delay);
                                    Thread.Sleep(delay);
                                }
                                else
                                {
                                    _logger.LogError(ex,
                                        "[Kafka] Notification failed after {Max} attempts. Sending to DLT.",
                                        MaxRetries);
                                }
                            }
                        }

                        if (!processed)
                        {
                            _dltProducer.ProduceAsync(_dltTopic, new Message<Null, string>
                            {
                                Value = result.Message.Value
                            }).GetAwaiter().GetResult();

                            _logger.LogWarning("[Kafka] Message sent to DLT: {Dlt}", _dltTopic);
                        }

                        _consumer.Commit(result);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "[Kafka] Error consuming message from broker");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[Kafka] Notification consumer shutting down");
            }
            finally
            {
                _consumer.Close();
                _dltProducer.Dispose();
            }
        }, stoppingToken);
    }
}
