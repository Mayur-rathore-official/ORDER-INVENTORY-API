namespace OrderInventory.Domain.Entities;

public class IdempotencyRecord
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public User Customer { get; set; } = null!;

    public string Key { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
