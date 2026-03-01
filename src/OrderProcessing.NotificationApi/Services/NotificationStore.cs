using System.Collections.Concurrent;

namespace OrderProcessing.NotificationApi.Services;

public class NotificationStore
{
    private readonly ConcurrentDictionary<Guid, NotificationRecord> _notifications = new();

    public void Add(NotificationRecord record) => _notifications[record.OrderId] = record;

    public NotificationRecord? Get(Guid orderId) =>
        _notifications.TryGetValue(orderId, out var record) ? record : null;
}

public class NotificationRecord
{
    public Guid OrderId { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime NotifiedAt { get; set; }
}
