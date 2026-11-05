using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Common;
using OrderInventory.Application.Dtos;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Enums;
using OrderInventory.Domain.Exceptions;
using OrderInventory.Domain.Orders;
using OrderInventory.Domain.Pricing;

namespace OrderInventory.Application.Orders;

public sealed class OrderService : IOrderService
{
    public const int MaxLines = 50;

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IOrderPlacementGate _gate;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IApplicationDbContext db,
        IClock clock,
        IOrderPlacementGate gate,
        ILogger<OrderService> logger)
    {
        _db = db;
        _clock = clock;
        _gate = gate;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceAsync(
        RequestActor actor,
        string? idempotencyKey,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var key = (idempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 8 or > 128)
            throw new ValidationException("Idempotency-Key header is required and must be between 8 and 128 characters.");

        var lines = NormalizeLines(request.Items);
        var requestHash = HashLines(lines);

        var replay = await FindReplayAsync(actor.UserId, key, cancellationToken);
        if (replay is not null)
            return ReplayOrConflict(replay, requestHash);

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count != productIds.Count)
            throw new NotFoundException("One or more products were not found.");

        var byId = products.ToDictionary(p => p.Id);
        foreach (var line in lines)
        {
            var product = byId[line.ProductId];
            if (!product.IsActive)
                throw new ValidationException($"Product '{product.Sku}' is not available.");

            if (product.StockQuantity < line.Quantity)
                throw new InsufficientStockException(product.Sku, line.Quantity, product.StockQuantity);
        }

        var priorOrders = await _db.Orders.CountAsync(o => o.CustomerId == actor.UserId, cancellationToken);
        var orderNumber = priorOrders + 1;
        var now = _clock.UtcNow;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = actor.UserId,
            Status = OrderStatus.Pending,
            CreatedAt = now
        };

        foreach (var line in lines)
        {
            var product = byId[line.ProductId];
            product.StockQuantity -= line.Quantity;
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = product.Id,
                Product = product,
                Quantity = line.Quantity,
                UnitPrice = product.Price
            });
        }

        order.SubTotal = DiscountCalculator.RoundMoney(order.Items.Sum(i => i.UnitPrice * i.Quantity));
        var quote = DiscountCalculator.Calculate(order.SubTotal, orderNumber);
        order.Discount = quote.Discount;
        order.Total = quote.Total;

        var customer = await _db.Users.FirstAsync(u => u.Id == actor.UserId, cancellationToken);
        order.Customer = customer;

        _db.Orders.Add(order);
        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CustomerId = actor.UserId,
            Key = key,
            RequestHash = requestHash,
            Order = order,
            CreatedAt = now
        });

        // Both competing requests must observe the same row version before either transaction commits.
        await _gate.OnProductsReservedAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _db.ClearChangeTracker();
            _logger.LogWarning(ex, "Optimistic concurrency conflict while placing an order for customer {CustomerId}", actor.UserId);
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _db.ClearChangeTracker();

            var winner = await FindReplayAsync(actor.UserId, key, cancellationToken);
            if (winner is not null)
                return ReplayOrConflict(winner, requestHash);

            throw;
        }

        _logger.LogInformation(
            "Placed order {OrderId} for customer {CustomerId}. SubTotal {SubTotal}, Discount {Discount}, Total {Total}",
            order.Id,
            actor.UserId,
            order.SubTotal,
            order.Discount,
            order.Total);

        return new PlaceOrderResult(Map(order), Replayed: false);
    }

    public Task<PagedResult<OrderResponse>> ListMineAsync(RequestActor actor, OrderListQuery query, CancellationToken cancellationToken) =>
        ListAsync(query, actor.UserId, cancellationToken);

    public Task<PagedResult<OrderResponse>> ListAllAsync(OrderListQuery query, CancellationToken cancellationToken) =>
        ListAsync(query, customerId: null, cancellationToken);

    public async Task<OrderResponse> GetAsync(RequestActor actor, Guid id, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(id, cancellationToken);
        EnsureCanView(actor, order);
        return Map(order);
    }

    public async Task<OrderResponse> ChangeStatusAsync(
        RequestActor actor,
        Guid id,
        string status,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var requested) || !Enum.IsDefined(requested))
            throw new ValidationException("Status must be one of: Pending, Confirmed, Shipped, Cancelled.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

            if (order is null)
                throw new NotFoundException($"Order {id} was not found.");

            if (!actor.IsAdmin)
            {
                if (order.CustomerId != actor.UserId)
                    throw new ForbiddenException("You can only update your own orders.");

                if (requested != OrderStatus.Cancelled || order.Status != OrderStatus.Pending)
                    throw new ForbiddenException("Customers may only cancel their own pending orders.");
            }

            OrderStatusPolicy.EnsureAllowed(order.Status, requested);

            if (requested == OrderStatus.Cancelled)
                RestoreStock(order);

            order.Status = requested;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Order {OrderId} status changed to {Status} by {ActorId}", order.Id, requested, actor.UserId);
            return Map(order);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            _db.ClearChangeTracker();
            _logger.LogWarning(ex, "Optimistic concurrency conflict while updating order {OrderId}", id);
            throw new ConcurrencyConflictException();
        }
        catch
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<int> CancelExpiredPendingOrdersAsync(int pendingTimeoutMinutes, CancellationToken cancellationToken)
    {
        if (pendingTimeoutMinutes < 1)
            throw new ValidationException("Pending timeout must be at least 1 minute.");

        var cutoff = _clock.UtcNow.AddMinutes(-pendingTimeoutMinutes);
        var ids = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt <= cutoff)
            .Select(o => new { o.Id, o.CustomerId })
            .ToListAsync(cancellationToken);

        var cancelled = 0;
        foreach (var stale in ids)
        {
            try
            {
                await ChangeStatusAsync(
                    new RequestActor(stale.CustomerId, IsAdmin: true),
                    stale.Id,
                    OrderStatus.Cancelled.ToString(),
                    cancellationToken);
                cancelled++;
            }
            catch (AppException ex)
            {
                _logger.LogWarning(ex, "Skipped auto-cancel for order {OrderId}", stale.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-cancel order {OrderId}", stale.Id);
            }
        }

        return cancelled;
    }

    private async Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery query, Guid? customerId, CancellationToken cancellationToken)
    {
        var (page, pageSize) = NormalizePaging(query.Page, query.PageSize);
        var orders = _db.Orders.AsNoTracking().AsQueryable();

        if (customerId is Guid id)
            orders = orders.Where(o => o.CustomerId == id);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status) || !Enum.IsDefined(status))
                throw new ValidationException("Status filter must be one of: Pending, Confirmed, Shipped, Cancelled.");

            orders = orders.Where(o => o.Status == status);
        }

        var total = await orders.CountAsync(cancellationToken);
        var pageItems = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .Include(o => o.Customer)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderResponse>(pageItems.Select(Map).ToList(), page, pageSize, total);
    }

    private async Task<Order> LoadOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .Include(o => o.Customer)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order ?? throw new NotFoundException($"Order {id} was not found.");
    }

    private async Task<IdempotencyRecord?> FindReplayAsync(Guid customerId, string key, CancellationToken cancellationToken) =>
        await _db.IdempotencyRecords
            .Include(r => r.Order)
            .ThenInclude(o => o.Items)
            .ThenInclude(i => i.Product)
            .Include(r => r.Order)
            .ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.Key == key, cancellationToken);

    private static PlaceOrderResult ReplayOrConflict(IdempotencyRecord record, string requestHash)
    {
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            throw new ConflictException("This Idempotency-Key was already used with a different request.");

        return new PlaceOrderResult(Map(record.Order), Replayed: true);
    }

    private static List<CreateOrderItemRequest> NormalizeLines(IReadOnlyList<CreateOrderItemRequest>? items)
    {
        if (items is null || items.Count == 0)
            throw new ValidationException("An order must contain at least one item.");

        if (items.Count > MaxLines)
            throw new ValidationException($"An order cannot contain more than {MaxLines} lines.");

        if (items.Any(i => i.Quantity <= 0))
            throw new ValidationException("Quantity must be greater than zero.");

        var merged = items
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                long quantity = 0;
                foreach (var line in g)
                {
                    quantity += line.Quantity;
                    if (quantity > int.MaxValue)
                        throw new ValidationException("Quantity is too large.");
                }

                return new CreateOrderItemRequest(g.Key, (int)quantity);
            })
            .OrderBy(i => i.ProductId)
            .ToList();

        if (merged.Count > MaxLines)
            throw new ValidationException($"An order cannot contain more than {MaxLines} lines.");

        return merged;
    }

    private static string HashLines(IReadOnlyList<CreateOrderItemRequest> lines)
    {
        var payload = string.Join('|', lines.Select(l => $"{l.ProductId:N}:{l.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static void RestoreStock(Order order)
    {
        foreach (var item in order.Items)
        {
            if (item.Product is null)
                throw new InvalidOperationException($"Order item {item.Id} is missing its product.");

            var updated = (long)item.Product.StockQuantity + item.Quantity;
            if (updated > int.MaxValue)
                throw new ValidationException("Restoring stock would overflow the stock quantity.");

            item.Product.StockQuantity = (int)updated;
        }
    }

    private static void EnsureCanView(RequestActor actor, Order order)
    {
        if (!actor.IsAdmin && order.CustomerId != actor.UserId)
            throw new ForbiddenException("You can only view your own orders.");
    }

    internal static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1)
            throw new ValidationException("Page must be at least 1.");

        if (pageSize is < 1 or > 100)
            throw new ValidationException("Page size must be between 1 and 100.");

        return (page, pageSize);
    }

    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // The transaction may already be completed by the provider.
        }
    }

    internal static OrderResponse Map(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Customer?.FullName ?? string.Empty,
        order.Status.ToString(),
        order.SubTotal,
        order.Discount,
        order.Total,
        order.CreatedAt,
        order.Items
            .OrderBy(i => i.Product?.Sku)
            .Select(i => new OrderItemResponse(
                i.Id,
                i.ProductId,
                i.Product?.Name ?? string.Empty,
                i.Product?.Sku ?? string.Empty,
                i.Quantity,
                i.UnitPrice))
            .ToList());
}
