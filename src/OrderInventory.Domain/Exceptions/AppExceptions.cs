using OrderInventory.Domain.Enums;

namespace OrderInventory.Domain.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(string message) : base(message)
    {
    }

    public abstract int StatusCode { get; }

    public abstract string Title { get; }
}

public sealed class ValidationException : AppException
{
    public ValidationException(string message) : base(message)
    {
    }

    public override int StatusCode => 400;

    public override string Title => "Validation failed";
}

public sealed class InvalidStatusTransitionException : AppException
{
    public InvalidStatusTransitionException(OrderStatus current, OrderStatus requested)
        : base($"Cannot change order status from {current} to {requested}. Allowed transitions are Pending → Confirmed → Shipped, and Pending or Confirmed → Cancelled. Shipped and Cancelled are final.")
    {
    }

    public override int StatusCode => 400;

    public override string Title => "Invalid status transition";
}

public sealed class UnauthorizedAppException : AppException
{
    public UnauthorizedAppException(string message) : base(message)
    {
    }

    public override int StatusCode => 401;

    public override string Title => "Unauthorized";
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message)
    {
    }

    public override int StatusCode => 403;

    public override string Title => "Forbidden";
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public override int StatusCode => 404;

    public override string Title => "Not found";
}

public sealed class ConflictException : AppException
{
    public ConflictException(string message) : base(message)
    {
    }

    public override int StatusCode => 409;

    public override string Title => "Conflict";
}

public sealed class InsufficientStockException : AppException
{
    public InsufficientStockException(string sku, int requested, int available)
        : base($"Insufficient stock for product '{sku}'. Requested {requested}, available {available}. The order was not placed and no stock was changed.")
    {
    }

    public override int StatusCode => 409;

    public override string Title => "Insufficient stock";
}

public sealed class ConcurrencyConflictException : AppException
{
    public ConcurrencyConflictException()
        : base("Concurrency conflict: the same stock was updated by another request. This order was not placed and no stock was taken.")
    {
    }

    public override int StatusCode => 409;

    public override string Title => "Concurrency conflict";
}
