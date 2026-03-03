using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace OrderProcessing.PaymentApi.Services;

public class RabbitMqPublisher : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly string _exchangeName;

    public RabbitMqPublisher(IConnection connection, string exchangeName)
    {
        _channel = connection.CreateChannelAsync().GetAwaiter().GetResult();
        _exchangeName = exchangeName;

        _channel.ExchangeDeclareAsync(
            exchange: _exchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false).GetAwaiter().GetResult();
    }

    public async Task PublishAsync<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true
        };

        await _channel.BasicPublishAsync(
            exchange: _exchangeName,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
    }
}
