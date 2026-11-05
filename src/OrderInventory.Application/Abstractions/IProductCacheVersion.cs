namespace OrderInventory.Application.Abstractions;

public interface IProductCacheVersion
{
    int Current { get; }

    void Invalidate();
}
