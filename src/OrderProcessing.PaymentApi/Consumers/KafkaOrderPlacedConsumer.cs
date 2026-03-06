using System.Text.Json;
using Confluent.Kafka;
using OrderProcessing.Domain.Events;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Consumers;

public class KafkaOrderPlacedConsumer : BackgroundService
{
    private const int MaxRetries = 3;
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IProducer<Null, string> _dltProducer;
    private readonly PaymentStore _paymentStore;
    private readonly KafkaPublisher _kafkaPublisher;
    private readonly ILogger<KafkaOrderPlacedConsumer> _logger;
    private readonly string _topic;
    private readonly string _dltTopic;

    public KafkaOrderPlacedConsumer(
        IConfiguration config,
        PaymentStore paymentStore,
        KafkaPublisher kafkaPublisher,
        ILogger<KafkaOrderPlacedConsumer> logger)
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

        _topic = config["Kafka:OrderPlacedTopic"]!;
        _dltTopic = $"{_topic}.DLT";
        _paymentStore = paymentStore;
        _kafkaPublisher = kafkaPublisher;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            _consumer.Subscribe(_topic);
            _logger.LogInformation("[Kafka] Payment consumer started on topic: {Topic} (DLT: {Dlt})", _topic, _dltTopic);

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
                                var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(result.Message.Value)!;

                                _logger.LogInformation(
                                    "[Kafka] Processing payment for Order {OrderId}, Amount: {Amount} (Attempt {Attempt}/{Max})",
                                    orderEvent.OrderId, orderEvent.TotalAmount, attempt, MaxRetries);

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

                                processed = true;
                                break;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (attempt < MaxRetries)
                                {
                                    var delay = (int)Math.Pow(2, attempt) * 1000;
                                    _logger.LogWarning(ex,
                                        "[Kafka] Payment processing failed (Attempt {Attempt}/{Max}). Retrying in {Delay}ms...",
                                        attempt, MaxRetries, delay);
                                    Thread.Sleep(delay);
                                }
                                else
                                {
                                    _logger.LogError(ex,
                                        "[Kafka] Payment processing failed after {Max} attempts. Sending to DLT.",
                                        MaxRetries);
                                }
                            }
                        }

                        if (!processed)
                        {
                            // Publish to Dead Letter Topic
                            _dltProducer.ProduceAsync(_dltTopic, new Message<Null, string>
                            {
                                Value = result.Message.Value
                            }).GetAwaiter().GetResult();

                            _logger.LogWarning("[Kafka] Message sent to DLT: {Dlt}", _dltTopic);
                        }

                        // Commit offset only after processing or DLT routing
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
                _logger.LogInformation("[Kafka] Payment consumer shutting down");
            }
            finally
            {
                _consumer.Close();
                _dltProducer.Dispose();
            }
        }, stoppingToken);
    }
}
