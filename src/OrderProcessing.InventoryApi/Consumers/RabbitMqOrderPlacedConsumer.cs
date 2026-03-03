using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderProcessing.Domain.Events;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Consumers;

public class RabbitMqOrderPlacedConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IConfiguration _configuration;
    private readonly InventoryStore _inventoryStore;
    private readonly ILogger<RabbitMqOrderPlacedConsumer> _logger;

    public RabbitMqOrderPlacedConsumer(
        IConnection connection,
        IConfiguration configuration,
        InventoryStore inventoryStore,
        ILogger<RabbitMqOrderPlacedConsumer> logger)
    {
        _connection = connection;
        _configuration = configuration;
        _inventoryStore = inventoryStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        var exchangeName = _configuration["RabbitMQ:OrderPlacedExchange"]!;
        var queueName = _configuration["RabbitMQ:InventoryQueueName"]!;

        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(queueName, exchangeName, routingKey: string.Empty,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(json)!;

                _logger.LogInformation(
                    "[RabbitMQ] Reserving inventory for Order {OrderId}: {Product} x {Quantity}",
                    orderEvent.OrderId, orderEvent.Product, orderEvent.Quantity);

                // Simulate inventory reservation
                await Task.Delay(300, stoppingToken);

                _inventoryStore.Add(new InventoryReservation
                {
                    OrderId = orderEvent.OrderId,
                    Product = orderEvent.Product,
                    Quantity = orderEvent.Quantity,
                    Status = "Reserved",
                    ReservedAt = DateTime.UtcNow
                });

                _logger.LogInformation(
                    "[RabbitMQ] Inventory reserved for Order {OrderId}", orderEvent.OrderId);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMQ] Error reserving inventory");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ] Inventory consumer started on queue: {Queue}", queueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
