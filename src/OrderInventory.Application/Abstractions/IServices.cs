using OrderInventory.Application.Common;
using OrderInventory.Application.Dtos;

namespace OrderInventory.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
}

public interface IProductService
{
    Task<ProductListResult> ListAsync(ProductQuery query, CancellationToken cancellationToken);

    Task<ProductResponse> GetAsync(Guid id, bool allowInactive, CancellationToken cancellationToken);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);
}

public interface IOrderService
{
    Task<PlaceOrderResult> PlaceAsync(RequestActor actor, string? idempotencyKey, CreateOrderRequest request, CancellationToken cancellationToken);

    Task<PagedResult<OrderResponse>> ListMineAsync(RequestActor actor, OrderListQuery query, CancellationToken cancellationToken);

    Task<PagedResult<OrderResponse>> ListAllAsync(OrderListQuery query, CancellationToken cancellationToken);

    Task<OrderResponse> GetAsync(RequestActor actor, Guid id, CancellationToken cancellationToken);

    Task<OrderResponse> ChangeStatusAsync(RequestActor actor, Guid id, string status, CancellationToken cancellationToken);

    Task<int> CancelExpiredPendingOrdersAsync(int pendingTimeoutMinutes, CancellationToken cancellationToken);
}

public interface IReportService
{
    Task<IReadOnlyList<TopCustomerResponse>> GetTopCustomersAsync(int days, CancellationToken cancellationToken);

    string GetTopCustomersSql(int days);
}
