using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderProcessing.Domain.Events;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Consumers;

public class RabbitMqOrderPaidConsumer : BackgroundService
{
    private const int MaxRetries = 3;
    private readonly IConnection _connection;
    private readonly IConfiguration _configuration;
    private readonly NotificationStore _notificationStore;
    private readonly ILogger<RabbitMqOrderPaidConsumer> _logger;

    public RabbitMqOrderPaidConsumer(
        IConnection connection,
        IConfiguration configuration,
        NotificationStore notificationStore,
        ILogger<RabbitMqOrderPaidConsumer> logger)
    {
        _connection = connection;
        _configuration = configuration;
        _notificationStore = notificationStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        var exchangeName = _configuration["RabbitMQ:OrderPaidExchange"]!;
        var queueName = _configuration["RabbitMQ:NotificationQueueName"]!;
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
                    var paidEvent = JsonSerializer.Deserialize<OrderPaidEvent>(json)!;

                    _logger.LogInformation(
                        "[RabbitMQ] Sending notification for Order {OrderId}, PaymentId: {PaymentId}, Amount: {Amount} (Attempt {Attempt}/{Max})",
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
                        "[RabbitMQ] Notification sent for Order {OrderId}", paidEvent.OrderId);

                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt < MaxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                        _logger.LogWarning(ex,
                            "[RabbitMQ] Notification failed (Attempt {Attempt}/{Max}). Retrying in {Delay}s...",
                            attempt, MaxRetries, delay.TotalSeconds);
                        await Task.Delay(delay, stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "[RabbitMQ] Notification failed after {Max} attempts. Sending to DLQ.",
                            MaxRetries);
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                }
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ] Notification consumer started on queue: {Queue} (DLQ: {Dlq})", queueName, dlqName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
