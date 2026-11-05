using OrderInventory.Application.Common;

namespace OrderInventory.Application.Dtos;

public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string Status,
    decimal SubTotal,
    decimal Discount,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record PlaceOrderResult(OrderResponse Order, bool Replayed);

public sealed record ChangeOrderStatusRequest(string Status);

public sealed record OrderListQuery(int Page, int PageSize, string? Status);
