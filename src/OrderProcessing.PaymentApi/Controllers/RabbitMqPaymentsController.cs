using Microsoft.AspNetCore.Mvc;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Controllers;

[ApiController]
[Route("api/rabbitmq/payments")]
public class RabbitMqPaymentsController : ControllerBase
{
    private readonly PaymentStore _store;

    public RabbitMqPaymentsController(PaymentStore store)
    {
        _store = store;
    }

    [HttpGet("{orderId:guid}")]
    public IActionResult GetPayment(Guid orderId)
    {
        var payment = _store.Get(orderId);
        return payment is null ? NotFound() : Ok(payment);
    }
}
