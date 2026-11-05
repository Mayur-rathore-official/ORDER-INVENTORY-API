# Part C — code review

The handler below passes a single-user test: one customer, one product, enough stock, no retries. It loses stock, double-charges, and returns 500 once two requests overlap, a client retries, or the app runs on more than one machine.

## Reviewed code

```csharp
public class OrderController : ControllerBase
{
    private readonly AppDbContext _context;
    private static readonly List<string> ProcessedKeys = new();

    public OrderController(AppDbContext context) => _context = context;

    [HttpPost("api/orders")]
    public async Task<IActionResult> PlaceOrder(PlaceOrderDto dto)
    {
        if (!string.IsNullOrEmpty(dto.IdempotencyKey) && ProcessedKeys.Contains(dto.IdempotencyKey))
            return Ok(_context.Orders.Find(dto.OrderId));

        var order = new Order
        {
            CustomerId = dto.CustomerId,
            Status = dto.Status,
            Discount = dto.Discount,
            Total = dto.Total,
            CreatedAt = DateTime.Now
        };

        foreach (var line in dto.Items)
        {
            var product = _context.Products.Find(line.ProductId);
            if (product.StockQuantity < line.Quantity)
                throw new Exception("Out of stock");

            product.StockQuantity -= line.Quantity;
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            });
            await _context.SaveChangesAsync();
        }

        order.SubTotal = order.Items.Sum(i => (double)i.UnitPrice * i.Quantity);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        ProcessedKeys.Add(dto.IdempotencyKey);

        _ = Task.Run(() => SendEmail(order.Id));
        return Ok(order);
    }

    [HttpPatch("api/orders/{id}/status")]
    public async Task<IActionResult> SetStatus(Guid id, string status)
    {
        var order = _context.Orders.Include(o => o.Items).First(o => o.Id == id);
        order.Status = status;
        if (status == "Cancelled")
        {
            foreach (var item in order.Items)
                item.Product.StockQuantity += item.Quantity;
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(500, "Please try again");
        }

        return Ok(order);
    }

    private static void SendEmail(Guid orderId)
    {
        var db = new AppDbContext();
        var order = db.Orders.Find(orderId);
        // SMTP send using order.Customer.Email
    }
}
```

## Problems

1. **Stock is saved one line at a time, outside a transaction.** `SaveChangesAsync` inside the loop commits each decrement immediately. If the second product is missing or the process dies, the first product stays decremented and no order exists. A test with one line never sees this. The reservation and the order insert have to commit or roll back together.

2. **There is no concurrency token.** Both requests can read `StockQuantity = 1`, both pass the `if`, and both write `0`. The last commit wins. The test is single-threaded, so it stays green. `Product.RowVersion` has to be a SQL Server `rowversion`, and a `DbUpdateConcurrencyException` has to become **409 Conflict**, not 500. A 500 makes the client retry and can hide a lost update.

3. **The stock check and the write are not the same unit of work.** Even after a transaction is added, the default isolation level still lets two transactions read the old quantity. The row version (or an atomic `UPDATE ... WHERE StockQuantity >= @qty`) is what makes the loser fail. A transaction alone does not.

4. **Discount, total, status, and unit price come from the client.** A caller can send `Discount = 999999`, `Total = 0.01`, `Status = "Shipped"`, and a unit price below the catalog price. Money has to be `decimal`, calculated on the server, and rounded to 2 places. `UnitPrice` is the product price at order time. New orders start as `Pending`.

5. **`(double)` throws the money away.** Binary floating point cannot represent 0.10 or 12,499 exactly. Totals drift by fractions of a cent and then fail equality checks. Use `decimal` with precision `(18, 2)` and `MidpointRounding.AwayFromZero`.

6. **`DateTime.Now` is the server's local time.** The expiry job on another host, or a column compared with `SYSUTCDATETIME()`, will cancel a fresh order or keep a stale one. Store UTC (`DateTime.UtcNow` / `datetime2`).

7. **Idempotency lives in a static `List<string>`.** It is not shared across processes, it is not cleaned up, and `Contains` plus `Add` is not atomic. Two retries can both miss the list and both reduce stock. A replay also calls `Find(dto.OrderId)` with an id the client invented, so it returns the wrong order or null. Persist `(CustomerId, Key)` with a unique index, store a hash of the body, and return the original order. The same key with a different body is a 409, not a second order.

8. **`Find` and `First` are synchronous, and `product` is used without a null check.** A missing id becomes a `NullReferenceException` and a 500. Lookups should be `async` and a missing product should be 404 before any stock changes.

9. **Cancelling does not load `Product`, so `item.Product` is null.** The include is only `Items`. Restoring stock throws, the status may already have been set in memory, and the client sees 500. Include the product, validate the transition (`Pending → Confirmed → Shipped`, and `Pending` or `Confirmed → Cancelled`), and restore stock in the same transaction. `Shipped` and `Cancelled` are final and return 400 with a message that names both statuses.

10. **Any customer can set any status on any order.** There is no authentication check. Customers may only cancel their own `Pending` order. Confirm and ship are admin actions. Someone else's order is 403.

11. **The concurrency catch turns a conflict into 500.** Clients and gateways retry 500s. The second buyer of the last unit must see 409 and must not be told to blindly retry the same write.

12. **`Task.Run` plus `new AppDbContext()` drops the notification.** The request can finish before the email runs, the new context has no connection configuration, and `Customer` was never loaded. Exceptions inside `Task.Run` are unobserved. A durable outbox row committed with the order is what survives a crash between commit and SMTP.

13. **The controller owns a `DbContext` field and also constructs another one.** A scoped context used from `Task.Run` after the request ends is disposed. The static list also retains every key for the life of the process.

14. **`Ok(order)` serializes navigation graphs and returns 200 for a create.** A new order is 201. A replay is 200 with the original body. Domain entities also leak `PasswordHash` if a user graph is attached.

## Corrected version

```csharp
[HttpPost]
public async Task<ActionResult<OrderResponse>> Place(
    CreateOrderRequest request,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CancellationToken cancellationToken)
{
    var result = await _orders.PlaceAsync(Actor(), idempotencyKey, request, cancellationToken);
    if (result.Replayed)
        return Ok(result.Order);

    return CreatedAtAction(nameof(Get), new { id = result.Order.Id }, result.Order);
}
```

The service keeps the rules off the controller:

- Require an `Idempotency-Key`. If that customer already committed the same key and the same body hash, return the stored order and do not touch stock. A different body for the same key is 409. The unique index `(CustomerId, Key)` closes the race the static list misses.
- Open one database transaction, load the products, reject the whole order when any line is inactive or short, and do this before any quantity changes.
- Copy `UnitPrice` from `Product.Price`. Compute `SubTotal`, `Discount`, and `Total` with `DiscountCalculator` (`decimal`, 2 places). The client never supplies them.
- Decrement stock on the tracked products and insert the order and the idempotency row in that same transaction.
- On `DbUpdateConcurrencyException`, roll back and return **409**. The row version means the other buyer of the last unit already committed.
- On a unique-key violation, roll back and return the winner's order when the body hash matches.
- Status changes use `OrderStatusPolicy`. Cancellation loads each product and puts the quantity back in the same transaction. Illegal moves return 400 and name the current and requested status.

That is the behavior implemented in `OrderService`, `DiscountCalculator`, `OrderStatusPolicy`, and `ExceptionHandlingMiddleware`.
