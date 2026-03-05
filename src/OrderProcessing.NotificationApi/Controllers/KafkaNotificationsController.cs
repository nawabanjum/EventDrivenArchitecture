using Microsoft.AspNetCore.Mvc;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Controllers;

[ApiController]
[Route("api/kafka/notifications")]
public class KafkaNotificationsController : ControllerBase
{
    private readonly NotificationStore _store;

    public KafkaNotificationsController(NotificationStore store)
    {
        _store = store;
    }

    [HttpGet("{orderId:guid}")]
    public IActionResult GetNotification(Guid orderId)
    {
        var notification = _store.Get(orderId);
        return notification is null ? NotFound() : Ok(notification);
    }
}
