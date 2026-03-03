using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Domain.Events;
using OrderProcessing.Domain.Models;
using OrderProcessing.OrderApi.Services;

namespace OrderProcessing.OrderApi.Controllers;

[ApiController]
[Route("api/rabbitmq/orders")]
public class RabbitMqOrdersController : ControllerBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<RabbitMqOrdersController> _logger;

    public RabbitMqOrdersController(
        RabbitMqPublisher publisher,
        ILogger<RabbitMqOrdersController> logger)
    {
        _publisher = publisher;
        _logger = logger;
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

        _logger.LogInformation(
            "[RabbitMQ] Order {OrderId} placed and event published", order.Id);

        return CreatedAtAction(nameof(PlaceOrder), new { id = order.Id }, order);
    }
}
