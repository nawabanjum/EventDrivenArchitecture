using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace OrderProcessing.PaymentApi.Services;

public class ServiceBusPublisher
{
    private readonly ServiceBusSender _sender;

    public ServiceBusPublisher(ServiceBusClient client, string topicName)
    {
        _sender = client.CreateSender(topicName);
    }

    public async Task PublishAsync<T>(T message)
    {
        var json = JsonSerializer.Serialize(message);
        var serviceBusMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json"
        };
        await _sender.SendMessageAsync(serviceBusMessage);
    }
}
