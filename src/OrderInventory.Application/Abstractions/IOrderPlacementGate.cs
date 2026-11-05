namespace OrderInventory.Application.Abstractions;

/// <summary>
/// Test seam. Production uses a no-op. The concurrency integration test blocks here
/// so two requests reserve the same row version before either save commits.
/// </summary>
public interface IOrderPlacementGate
{
    Task OnProductsReservedAsync(CancellationToken cancellationToken);
}
