using System.Text.Json;
using Confluent.Kafka;
using OrderProcessing.Domain.Events;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Consumers;

public class KafkaOrderPlacedConsumer : BackgroundService
{
    private const int MaxRetries = 3;
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly IProducer<Null, string> _dltProducer;
    private readonly InventoryStore _inventoryStore;
    private readonly ILogger<KafkaOrderPlacedConsumer> _logger;
    private readonly string _topic;
    private readonly string _dltTopic;

    public KafkaOrderPlacedConsumer(
        IConfiguration config,
        InventoryStore inventoryStore,
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
        _inventoryStore = inventoryStore;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            _consumer.Subscribe(_topic);
            _logger.LogInformation("[Kafka] Inventory consumer started on topic: {Topic} (DLT: {Dlt})", _topic, _dltTopic);

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
                                    "[Kafka] Reserving inventory for Order {OrderId}: {Product} x {Quantity} (Attempt {Attempt}/{Max})",
                                    orderEvent.OrderId, orderEvent.Product, orderEvent.Quantity, attempt, MaxRetries);

                                Thread.Sleep(300);

                                _inventoryStore.Add(new InventoryReservation
                                {
                                    OrderId = orderEvent.OrderId,
                                    Product = orderEvent.Product,
                                    Quantity = orderEvent.Quantity,
                                    Status = "Reserved",
                                    ReservedAt = DateTime.UtcNow
                                });

                                _logger.LogInformation(
                                    "[Kafka] Inventory reserved for Order {OrderId}", orderEvent.OrderId);

                                processed = true;
                                break;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (attempt < MaxRetries)
                                {
                                    var delay = (int)Math.Pow(2, attempt) * 1000;
                                    _logger.LogWarning(ex,
                                        "[Kafka] Inventory reservation failed (Attempt {Attempt}/{Max}). Retrying in {Delay}ms...",
                                        attempt, MaxRetries, delay);
                                    Thread.Sleep(delay);
                                }
                                else
                                {
                                    _logger.LogError(ex,
                                        "[Kafka] Inventory reservation failed after {Max} attempts. Sending to DLT.",
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
                _logger.LogInformation("[Kafka] Inventory consumer shutting down");
            }
            finally
            {
                _consumer.Close();
                _dltProducer.Dispose();
            }
        }, stoppingToken);
    }
}
