using System.Collections.Concurrent;

namespace OrderProcessing.PaymentApi.Services;

public class PaymentStore
{
    private readonly ConcurrentDictionary<Guid, PaymentRecord> _payments = new();

    public void Add(PaymentRecord record) => _payments[record.OrderId] = record;

    public PaymentRecord? Get(Guid orderId) =>
        _payments.TryGetValue(orderId, out var record) ? record : null;
}

public class PaymentRecord
{
    public Guid OrderId { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime PaidAt { get; set; }
}
