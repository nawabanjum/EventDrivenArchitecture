using Confluent.Kafka;
using OrderProcessing.Domain.Models;
using System.Text.Json;

namespace OrderProcessing.OrderApi.Services;

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

    public void Publish<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        _producer.Produce(_topicName, new Message<Null, string>
        {
            Value = json
        }, DeliveryReportHandler);
        _producer.Flush();
    }

    private void DeliveryReportHandler(DeliveryReport<Null, string> report)
    {
        if (report.Error.IsError)
        {
            Console.WriteLine("[Kafka] Delivery failed: {0}", report.Error.Reason);
        }
        else
        {
            Console.WriteLine("[Kafka] Delivered to {0} [partition {1}] @ offset {2}",
                report.Topic, report.Partition.Value, report.Offset.Value);
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
