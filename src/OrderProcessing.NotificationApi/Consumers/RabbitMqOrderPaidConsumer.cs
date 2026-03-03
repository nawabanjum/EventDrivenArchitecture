using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderProcessing.Domain.Events;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Consumers;

public class RabbitMqOrderPaidConsumer : BackgroundService
{
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
                var paidEvent = JsonSerializer.Deserialize<OrderPaidEvent>(json)!;

                _logger.LogInformation(
                    "[RabbitMQ] Sending notification for Order {OrderId}, PaymentId: {PaymentId}, Amount: {Amount}",
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
                    "[RabbitMQ] Notification sent for Order {OrderId}", paidEvent.OrderId);

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMQ] Error sending notification");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ] Notification consumer started on queue: {Queue}", queueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
