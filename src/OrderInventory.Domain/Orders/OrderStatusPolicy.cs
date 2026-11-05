using OrderInventory.Domain.Enums;
using OrderInventory.Domain.Exceptions;

namespace OrderInventory.Domain.Orders;

public static class OrderStatusPolicy
{
    public static bool IsAllowed(OrderStatus current, OrderStatus next) => (current, next) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
        (OrderStatus.Pending, OrderStatus.Cancelled) => true,
        (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
        _ => false
    };

    public static void EnsureAllowed(OrderStatus current, OrderStatus next)
    {
        if (!IsAllowed(current, next))
            throw new InvalidStatusTransitionException(current, next);
    }
}
