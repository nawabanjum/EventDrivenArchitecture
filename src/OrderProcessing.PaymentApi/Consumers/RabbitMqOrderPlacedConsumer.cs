using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderProcessing.Domain.Events;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Consumers;

public class RabbitMqOrderPlacedConsumer : BackgroundService
{
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

        // Declare exchange and queue, then bind them
        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(queueName, exchangeName, routingKey: string.Empty,
            cancellationToken: stoppingToken);

        // Prefetch 1 message at a time (fair dispatch)
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
                    "[RabbitMQ] Processing payment for Order {OrderId}, Amount: {Amount}",
                    orderEvent.OrderId, orderEvent.TotalAmount);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMQ] Error processing payment");
                // Negative ack - requeue the message
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("[RabbitMQ] Payment consumer started on queue: {Queue}", queueName);

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
