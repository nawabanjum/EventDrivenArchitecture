using Microsoft.AspNetCore.Mvc;
using OrderProcessing.InventoryApi.Services;

namespace OrderProcessing.InventoryApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly InventoryStore _inventoryStore;

    public InventoryController(InventoryStore inventoryStore)
    {
        _inventoryStore = inventoryStore;
    }

    [HttpGet("{orderId:guid}")]
    public IActionResult GetReservationStatus(Guid orderId)
    {
        var reservation = _inventoryStore.Get(orderId);

        if (reservation is null)
            return NotFound(new { message = $"No inventory reservation found for order {orderId}" });

        return Ok(reservation);
    }
}
