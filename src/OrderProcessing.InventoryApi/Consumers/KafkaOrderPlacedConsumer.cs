using System.Text.Json;
using Confluent.Kafka;
using OrderProcessing.Domain.Events;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Consumers;

public class KafkaOrderPlacedConsumer : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly InventoryStore _inventoryStore;
    private readonly ILogger<KafkaOrderPlacedConsumer> _logger;
    private readonly string _topic;

    public KafkaOrderPlacedConsumer(
        IConfiguration config,
        InventoryStore inventoryStore,
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
        _inventoryStore = inventoryStore;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            _consumer.Subscribe(_topic);
            _logger.LogInformation("[Kafka] Inventory consumer started on topic: {Topic}", _topic);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = _consumer.Consume(stoppingToken);
                        var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(result.Message.Value)!;

                        _logger.LogInformation(
                            "[Kafka] Reserving inventory for Order {OrderId}: {Product} x {Quantity}",
                            orderEvent.OrderId, orderEvent.Product, orderEvent.Quantity);

                        // Simulate inventory reservation
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
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "[Kafka] Error consuming message");
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
            }
        }, stoppingToken);
    }
}
