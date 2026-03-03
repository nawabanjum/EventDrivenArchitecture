using Microsoft.AspNetCore.Mvc;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Controllers;

[ApiController]
[Route("api/rabbitmq/inventory")]
public class RabbitMqInventoryController : ControllerBase
{
    private readonly InventoryStore _store;

    public RabbitMqInventoryController(InventoryStore store)
    {
        _store = store;
    }

    [HttpGet("{orderId:guid}")]
    public IActionResult GetReservation(Guid orderId)
    {
        var reservation = _store.Get(orderId);
        return reservation is null ? NotFound() : Ok(reservation);
    }
}
