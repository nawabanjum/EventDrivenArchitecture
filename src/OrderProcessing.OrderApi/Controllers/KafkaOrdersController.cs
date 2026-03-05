using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Domain.Events;
using OrderProcessing.Domain.Models;
using OrderProcessing.OrderApi.Services;

namespace OrderProcessing.OrderApi.Controllers;

[ApiController]
[Route("api/kafka/orders")]
public class KafkaOrdersController : ControllerBase
{
    private readonly KafkaPublisher _publisher;
    private readonly ILogger<KafkaOrdersController> _logger;

    public KafkaOrdersController(
        KafkaPublisher publisher,
        ILogger<KafkaOrdersController> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult PlaceOrder([FromBody] Order order)
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

        _publisher.Publish(orderPlacedEvent);

        _logger.LogInformation(
            "[Kafka] Order {OrderId} placed and event published", order.Id);

        return CreatedAtAction(nameof(PlaceOrder), new { id = order.Id }, order);
    }
}
