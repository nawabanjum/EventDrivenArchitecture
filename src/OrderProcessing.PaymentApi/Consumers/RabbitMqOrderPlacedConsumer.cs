using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderProcessing.Domain.Events;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Consumers;

public class RabbitMqOrderPlacedConsumer : BackgroundService
{
    private const int MaxRetries = 3;
    private readonly IConnection _connection;
    private readonly IConfiguration _configuration;
    private readonly PaymentStore _paymentStore;
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<RabbitMqOrderPlacedConsumer> _logger;

    public RabbitMqOrderPlacedConsumer(
        IConnection connection,
        IConfiguration configuration,
        PaymentStore paymentStore,
        RabbitMqPublisher publisher,
        ILogger<RabbitMqOrderPlacedConsumer> logger)
    {
        _connection = connection;
        _configuration = configuration;
        _paymentStore = paymentStore;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        var exchangeName = _configuration["RabbitMQ:OrderPlacedExchange"]!;
        var queueName = _configuration["RabbitMQ:PaymentQueueName"]!;
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

        // Prefetch 1 message at a time (fair dispatch)
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
                        "[RabbitMQ] Processing payment for Order {OrderId}, Amount: {Amount} (Attempt {Attempt}/{Max})",
                        orderEvent.OrderId, orderEvent.TotalAmount, attempt, MaxRetries);

                    // Simulate payment processing
                    await Task.Delay(500, stoppingToken);

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

                    // Publish OrderPaidEvent to RabbitMQ
                    var orderPaidEvent = new OrderPaidEvent
                    {
                        OrderId = orderEvent.OrderId,
                        PaymentId = paymentId,
                        PaidAmount = orderEvent.TotalAmount,
                        PaidAt = paidAt
                    };
                    await _publisher.PublishAsync(orderPaidEvent);

                    _logger.LogInformation(
                        "[RabbitMQ] Payment completed for Order {OrderId}, PaymentId: {PaymentId}",
                        orderEvent.OrderId, paymentId);

                    // Acknowledge the message
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt < MaxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                        _logger.LogWarning(ex,
                            "[RabbitMQ] Payment processing failed (Attempt {Attempt}/{Max}). Retrying in {Delay}s...",
                            attempt, MaxRetries, delay.TotalSeconds);
                        await Task.Delay(delay, stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "[RabbitMQ] Payment processing failed after {Max} attempts. Sending to DLQ.",
                            MaxRetries);
                        // Reject without requeue — message routes to DLX/DLQ
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                }
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ] Payment consumer started on queue: {Queue} (DLQ: {Dlq})", queueName, dlqName);

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
