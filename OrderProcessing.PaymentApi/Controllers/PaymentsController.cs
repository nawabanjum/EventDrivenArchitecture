using Microsoft.AspNetCore.Mvc;
using OrderProcessing.PaymentApi.Services;

namespace OrderProcessing.PaymentApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly PaymentStore _paymentStore;

    public PaymentsController(PaymentStore paymentStore)
    {
        _paymentStore = paymentStore;
    }

    [HttpGet("{orderId:guid}")]
    public IActionResult GetPaymentStatus(Guid orderId)
    {
        var payment = _paymentStore.Get(orderId);

        if (payment is null)
            return NotFound(new { message = $"No payment found for order {orderId}" });

        return Ok(payment);
    }
}
