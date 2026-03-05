using System.Text.Json;
using Confluent.Kafka;

namespace OrderProcessing.PaymentApi.Services;

public class KafkaPublisher : IDisposable
{
    private readonly IProducer<Null, string> _producer;
    private readonly string _topicName;

    public KafkaPublisher(string bootstrapServers, string topicName)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        };
        _producer = new ProducerBuilder<Null, string>(config).Build();
        _topicName = topicName;
    }

    public async Task PublishAsync<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        await _producer.ProduceAsync(_topicName, new Message<Null, string>
        {
            Value = json
        });
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
