using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderProcessing.Domain.Events;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Consumers;

public class RabbitMqOrderPlacedConsumer : BackgroundService
{
    private const int MaxRetries = 3;
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
        var dlxExchangeName = $"{exchangeName}.dlx";
        var dlqName = $"{queueName}.dlq";

        // Declare Dead Letter Exchange and Queue
        await channel.ExchangeDeclareAsync(dlxExchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(dlqName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(dlqName, dlxExchangeName, routingKey: string.Empty,
            cancellationToken: stoppingToken);

        // Declare main exchange and queue with DLX routing
        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false,
            autoDelete: false, arguments: new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", dlxExchangeName }
            }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(queueName, exchangeName, routingKey: string.Empty,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(json)!;

                    _logger.LogInformation(
                        "[RabbitMQ] Reserving inventory for Order {OrderId}: {Product} x {Quantity} (Attempt {Attempt}/{Max})",
                        orderEvent.OrderId, orderEvent.Product, orderEvent.Quantity, attempt, MaxRetries);

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
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt < MaxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                        _logger.LogWarning(ex,
                            "[RabbitMQ] Inventory reservation failed (Attempt {Attempt}/{Max}). Retrying in {Delay}s...",
                            attempt, MaxRetries, delay.TotalSeconds);
                        await Task.Delay(delay, stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "[RabbitMQ] Inventory reservation failed after {Max} attempts. Sending to DLQ.",
                            MaxRetries);
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                }
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ] Inventory consumer started on queue: {Queue} (DLQ: {Dlq})", queueName, dlqName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
