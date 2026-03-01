namespace OrderProcessing.Domain.Events;

public class OrderPaidEvent
{
    public Guid OrderId { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
