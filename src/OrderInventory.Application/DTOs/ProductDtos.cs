using OrderInventory.Application.Common;

namespace OrderInventory.Application.Dtos;

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    int StockQuantity,
    bool IsActive,
    string RowVersion);

public sealed record ProductQuery(
    int Page,
    int PageSize,
    string? Name,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? InStock,
    string SortBy,
    bool Descending,
    bool IncludeInactive);

public sealed record CreateProductRequest(string Sku, string Name, decimal Price, int StockQuantity, bool IsActive);

public sealed record UpdateProductRequest(
    string Sku,
    string Name,
    decimal Price,
    int StockQuantity,
    bool IsActive,
    string RowVersion);

public sealed record ProductListResult(PagedResult<ProductResponse> Page, bool CacheHit);
