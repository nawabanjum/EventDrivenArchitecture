using System.Collections.Concurrent;

namespace OrderProcessing.InventoryApi.Services;

public class InventoryStore
{
    private readonly ConcurrentDictionary<Guid, InventoryReservation> _reservations = new();

    public void Add(InventoryReservation reservation) => _reservations[reservation.OrderId] = reservation;

    public InventoryReservation? Get(Guid orderId) =>
        _reservations.TryGetValue(orderId, out var reservation) ? reservation : null;
}

public class InventoryReservation
{
    public Guid OrderId { get; set; }
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime ReservedAt { get; set; }
}
