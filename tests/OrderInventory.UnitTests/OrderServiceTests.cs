using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Common;
using OrderInventory.Application.Dtos;
using OrderInventory.Application.Orders;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Enums;
using OrderInventory.Domain.Exceptions;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.UnitTests;

public class OrderServiceTests
{
    [Fact]
    public async Task Placing_an_order_reserves_stock_for_every_line()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var keyboard = await db.AddProductAsync(2499m, 10, "SKU-KEY");
        var mouse = await db.AddProductAsync(899m, 8, "SKU-MOUSE");

        var result = await db.Orders.PlaceAsync(
            Customer(customer),
            "idem-reserve-1",
            Lines((keyboard.Id, 2), (mouse.Id, 3)),
            CancellationToken.None);

        result.Replayed.Should().BeFalse();
        result.Order.Status.Should().Be("Pending");
        result.Order.SubTotal.Should().Be(2499m * 2 + 899m * 3);
        (await db.StockAsync(keyboard.Id)).Should().Be(8);
        (await db.StockAsync(mouse.Id)).Should().Be(5);
        (await db.Context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_short_line_rejects_the_whole_order_and_leaves_stock_unchanged()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var keyboard = await db.AddProductAsync(2499m, 10, "SKU-KEY");
        var mouse = await db.AddProductAsync(899m, 1, "SKU-MOUSE");

        var act = () => db.Orders.PlaceAsync(
            Customer(customer),
            "idem-short-1",
            Lines((keyboard.Id, 2), (mouse.Id, 3)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientStockException>()
            .WithMessage("*SKU-MOUSE*");
        (await db.StockAsync(keyboard.Id)).Should().Be(10);
        (await db.StockAsync(mouse.Id)).Should().Be(1);
        (await db.Context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_lines_for_the_same_product_reserve_the_combined_quantity()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 3, "SKU-WIDGET");

        await db.Orders.PlaceAsync(
            Customer(customer),
            "idem-merge-1",
            new CreateOrderRequest(
            [
                new CreateOrderItemRequest(widget.Id, 1),
                new CreateOrderItemRequest(widget.Id, 2)
            ]),
            CancellationToken.None);

        (await db.StockAsync(widget.Id)).Should().Be(0);
        var order = await db.Context.Orders.Include(o => o.Items).SingleAsync();
        order.Items.Should().ContainSingle().Which.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task Repeating_the_same_idempotency_key_returns_the_original_order_and_does_not_reduce_stock_again()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 5, "SKU-WIDGET");
        var request = Lines((widget.Id, 1));

        var first = await db.Orders.PlaceAsync(Customer(customer), "idem-replay-1", request, CancellationToken.None);
        var second = await db.Orders.PlaceAsync(Customer(customer), "idem-replay-1", request, CancellationToken.None);

        second.Replayed.Should().BeTrue();
        second.Order.Id.Should().Be(first.Order.Id);
        (await db.StockAsync(widget.Id)).Should().Be(4);
        (await db.Context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_with_a_different_body_conflicts_and_does_not_change_stock()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 5, "SKU-WIDGET");

        await db.Orders.PlaceAsync(Customer(customer), "idem-body-1", Lines((widget.Id, 1)), CancellationToken.None);
        var act = () => db.Orders.PlaceAsync(Customer(customer), "idem-body-1", Lines((widget.Id, 2)), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await db.StockAsync(widget.Id)).Should().Be(4);
        (await db.Context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_a_pending_order_restores_stock()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 5, "SKU-WIDGET");
        var placed = await db.Orders.PlaceAsync(Customer(customer), "idem-cancel-1", Lines((widget.Id, 2)), CancellationToken.None);

        var cancelled = await db.Orders.ChangeStatusAsync(Customer(customer), placed.Order.Id, "Cancelled", CancellationToken.None);

        cancelled.Status.Should().Be("Cancelled");
        (await db.StockAsync(widget.Id)).Should().Be(5);
    }

    [Fact]
    public async Task Admin_can_confirm_then_ship_and_cannot_leave_a_final_status()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 5, "SKU-WIDGET");
        var placed = await db.Orders.PlaceAsync(Customer(customer), "idem-ship-1", Lines((widget.Id, 1)), CancellationToken.None);

        var confirmed = await db.Orders.ChangeStatusAsync(Admin(), placed.Order.Id, "Confirmed", CancellationToken.None);
        var shipped = await db.Orders.ChangeStatusAsync(Admin(), placed.Order.Id, "Shipped", CancellationToken.None);
        var act = () => db.Orders.ChangeStatusAsync(Admin(), placed.Order.Id, "Cancelled", CancellationToken.None);

        confirmed.Status.Should().Be("Confirmed");
        shipped.Status.Should().Be("Shipped");
        await act.Should().ThrowAsync<InvalidStatusTransitionException>();
        (await db.StockAsync(widget.Id)).Should().Be(4);
    }

    [Fact]
    public async Task Customer_cannot_confirm_or_cancel_someone_elses_order()
    {
        await using var db = new OrderDatabase();
        var owner = await db.AddCustomerAsync();
        var other = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 5, "SKU-WIDGET");
        var placed = await db.Orders.PlaceAsync(Customer(owner), "idem-authz-1", Lines((widget.Id, 1)), CancellationToken.None);

        var confirm = () => db.Orders.ChangeStatusAsync(Customer(owner), placed.Order.Id, "Confirmed", CancellationToken.None);
        var steal = () => db.Orders.ChangeStatusAsync(Customer(other), placed.Order.Id, "Cancelled", CancellationToken.None);

        await confirm.Should().ThrowAsync<ForbiddenException>();
        await steal.Should().ThrowAsync<ForbiddenException>();
        (await db.StockAsync(widget.Id)).Should().Be(4);
        (await db.Context.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_order_restores_stock_and_pending_cannot_skip_to_shipped()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 5, "SKU-WIDGET");
        var placed = await db.Orders.PlaceAsync(Customer(customer), "idem-restore-1", Lines((widget.Id, 2)), CancellationToken.None);

        var skip = () => db.Orders.ChangeStatusAsync(Admin(), placed.Order.Id, "Shipped", CancellationToken.None);
        await skip.Should().ThrowAsync<InvalidStatusTransitionException>();

        await db.Orders.ChangeStatusAsync(Admin(), placed.Order.Id, "Confirmed", CancellationToken.None);
        var cancelled = await db.Orders.ChangeStatusAsync(Admin(), placed.Order.Id, "Cancelled", CancellationToken.None);

        cancelled.Status.Should().Be("Cancelled");
        (await db.StockAsync(widget.Id)).Should().Be(5);
    }

    [Fact]
    public async Task Server_applies_the_volume_discount_above_10000()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var monitor = await db.AddProductAsync(12_500m, 3, "SKU-MON");

        var result = await db.Orders.PlaceAsync(Customer(customer), "idem-volume-1", Lines((monitor.Id, 1)), CancellationToken.None);

        result.Order.SubTotal.Should().Be(12_500m);
        result.Order.Discount.Should().Be(625m);
        result.Order.Total.Should().Be(11_875m);
    }

    [Fact]
    public async Task The_fifth_order_stacks_the_loyalty_discount_on_top_of_the_volume_discount()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var small = await db.AddProductAsync(100m, 20, "SKU-SMALL");
        var large = await db.AddProductAsync(20_000m, 2, "SKU-LARGE");

        for (var n = 1; n <= 4; n++)
        {
            var early = await db.Orders.PlaceAsync(Customer(customer), $"idem-fifth-{n}", Lines((small.Id, 1)), CancellationToken.None);
            early.Order.Discount.Should().Be(0m);
        }

        var fifth = await db.Orders.PlaceAsync(Customer(customer), "idem-fifth-5", Lines((large.Id, 1)), CancellationToken.None);

        fifth.Order.SubTotal.Should().Be(20_000m);
        fifth.Order.Discount.Should().Be(3_000m);
        fifth.Order.Total.Should().Be(17_000m);
    }

    [Fact]
    public async Task Pending_orders_older_than_30_minutes_are_cancelled_and_stock_is_restored()
    {
        await using var db = new OrderDatabase();
        var customer = await db.AddCustomerAsync();
        var widget = await db.AddProductAsync(1500m, 4, "SKU-WIDGET");
        db.Clock.UtcNow = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        await db.Orders.PlaceAsync(Customer(customer), "idem-expire-1", Lines((widget.Id, 1)), CancellationToken.None);

        db.Clock.UtcNow = db.Clock.UtcNow.AddMinutes(31);
        var cancelled = await db.Orders.CancelExpiredPendingOrdersAsync(30, CancellationToken.None);

        cancelled.Should().Be(1);
        (await db.Context.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Cancelled);
        (await db.StockAsync(widget.Id)).Should().Be(4);
    }

    private static RequestActor Customer(User user) => new(user.Id, false);

    private static RequestActor Admin() => new(Guid.NewGuid(), true);

    private static CreateOrderRequest Lines(params (Guid ProductId, int Quantity)[] lines) =>
        new(lines.Select(line => new CreateOrderItemRequest(line.ProductId, line.Quantity)).ToList());

    private sealed class OrderDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public OrderDatabase()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            Context = new AppDbContext(options);
            Context.Database.EnsureCreated();
            Clock = new FakeClock();
            Orders = new OrderService(Context, Clock, new NoOpOrderPlacementGate(), NullLogger<OrderService>.Instance);
        }

        public AppDbContext Context { get; }

        public FakeClock Clock { get; }

        public OrderService Orders { get; }

        public async Task<User> AddCustomerAsync()
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Ava Shah",
                Email = $"{Guid.NewGuid():N}@store.test",
                PasswordHash = "not-used",
                Role = UserRole.Customer
            };
            Context.Users.Add(user);
            await Context.SaveChangesAsync();
            return user;
        }

        public async Task<Product> AddProductAsync(decimal price, int stock, string sku)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = sku,
                Price = price,
                StockQuantity = stock,
                IsActive = true
            };
            Context.Products.Add(product);
            await Context.SaveChangesAsync();
            return product;
        }

        public async Task<int> StockAsync(Guid productId)
        {
            var product = await Context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
            return product.StockQuantity;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    }
}
