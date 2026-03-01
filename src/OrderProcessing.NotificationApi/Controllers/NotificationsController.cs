using Microsoft.AspNetCore.Mvc;
using OrderProcessing.NotificationApi.Services;

namespace OrderProcessing.NotificationApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationStore _notificationStore;

    public NotificationsController(NotificationStore notificationStore)
    {
        _notificationStore = notificationStore;
    }

    [HttpGet("{orderId:guid}")]
    public IActionResult GetNotificationStatus(Guid orderId)
    {
        var notification = _notificationStore.Get(orderId);

        if (notification is null)
            return NotFound(new { message = $"No notification found for order {orderId}" });

        return Ok(notification);
    }
}
