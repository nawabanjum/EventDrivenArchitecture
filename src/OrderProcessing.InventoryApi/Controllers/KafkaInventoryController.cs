using Microsoft.AspNetCore.Mvc;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Controllers;

[ApiController]
[Route("api/kafka/inventory")]
public class KafkaInventoryController : ControllerBase
{
    private readonly InventoryStore _store;

    public KafkaInventoryController(InventoryStore store)
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
