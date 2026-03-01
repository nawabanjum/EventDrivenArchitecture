namespace OrderProcessing.Domain.Events;

public class OrderPlacedEvent
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
}
