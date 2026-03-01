using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Domain.Events;
using OrderProcessing.Domain.Models;
using OrderProcessing.OrderApi.Services;

namespace OrderProcessing.OrderApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ServiceBusPublisher _publisher;

    public OrdersController(ServiceBusClient serviceBusClient, IConfiguration configuration)
    {
        var topicName = configuration["AzureServiceBus:OrderPlacedTopic"]!;
        _publisher = new ServiceBusPublisher(serviceBusClient, topicName);
    }

    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] Order order)
    {
        order.Id = Guid.NewGuid();
        order.Status = "Pending";
        order.CreatedAt = DateTime.UtcNow;

        var orderPlacedEvent = new OrderPlacedEvent
        {
            OrderId = order.Id,
            CustomerName = order.CustomerName,
            Product = order.Product,
            Quantity = order.Quantity,
            TotalAmount = order.TotalAmount,
            PlacedAt = order.CreatedAt
        };

        await _publisher.PublishAsync(orderPlacedEvent);

        return CreatedAtAction(nameof(PlaceOrder), new { id = order.Id }, order);
    }
}
