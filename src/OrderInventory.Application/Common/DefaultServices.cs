using OrderInventory.Application.Abstractions;

namespace OrderInventory.Application.Common;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class NoOpOrderPlacementGate : IOrderPlacementGate
{
    public Task OnProductsReservedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ProductCacheVersion : IProductCacheVersion
{
    private int _version = 1;

    public int Current => Volatile.Read(ref _version);

    public void Invalidate() => Interlocked.Increment(ref _version);
}
