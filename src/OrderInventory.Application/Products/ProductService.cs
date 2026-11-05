using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Common;
using OrderInventory.Application.Dtos;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Exceptions;

namespace OrderInventory.Application.Products;

public sealed class ProductService : IProductService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase) { "name", "price", "sku" };
    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IProductCacheVersion _cacheVersion;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IApplicationDbContext db,
        IMemoryCache cache,
        IProductCacheVersion cacheVersion,
        ILogger<ProductService> logger)
    {
        _db = db;
        _cache = cache;
        _cacheVersion = cacheVersion;
        _logger = logger;
    }

    public async Task<ProductListResult> ListAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Orders.OrderService.NormalizePaging(query.Page, query.PageSize);
        var sortBy = (query.SortBy ?? "name").Trim().ToLowerInvariant();
        if (!SortFields.Contains(sortBy))
            throw new ValidationException("Sort by must be one of: name, price, sku.");

        if (query.MinPrice is < 0 || query.MaxPrice is < 0)
            throw new ValidationException("Price filters cannot be negative.");

        if (query.MinPrice is decimal min && query.MaxPrice is decimal max && min > max)
            throw new ValidationException("Minimum price cannot be greater than maximum price.");

        var name = string.IsNullOrWhiteSpace(query.Name) ? null : query.Name.Trim();
        var cacheKey = string.Join('|',
            "products",
            _cacheVersion.Current,
            page,
            pageSize,
            name?.ToLowerInvariant() ?? "",
            query.MinPrice?.ToString() ?? "",
            query.MaxPrice?.ToString() ?? "",
            query.InStock?.ToString() ?? "",
            sortBy,
            query.Descending,
            query.IncludeInactive);

        if (_cache.TryGetValue(cacheKey, out PagedResult<ProductResponse>? cached) && cached is not null)
            return new ProductListResult(cached, CacheHit: true);

        var products = ApplyFilters(_db.Products.AsNoTracking(), name, query.MinPrice, query.MaxPrice, query.InStock, query.IncludeInactive);
        products = ApplySort(products, sortBy, query.Descending);

        var total = await products.CountAsync(cancellationToken);
        var items = await products
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var pageResult = new PagedResult<ProductResponse>(items.Select(Map).ToList(), page, pageSize, total);
        _cache.Set(cacheKey, pageResult, TimeSpan.FromMinutes(2));

        return new ProductListResult(pageResult, CacheHit: false);
    }

    public async Task<ProductResponse> GetAsync(Guid id, bool allowInactive, CancellationToken cancellationToken)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null || (!product.IsActive && !allowInactive))
            throw new NotFoundException($"Product {id} was not found.");

        return Map(product);
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = NormalizeSku(request.Sku),
            Name = NormalizeName(request.Name),
            Price = ValidatePrice(request.Price),
            StockQuantity = ValidateStock(request.StockQuantity),
            IsActive = request.IsActive
        };

        _db.Products.Add(product);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            throw new ConflictException($"SKU '{product.Sku}' already exists.");
        }

        _cacheVersion.Invalidate();
        _logger.LogInformation("Created product {ProductId} ({Sku})", product.Id, product.Sku);
        return Map(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Product {id} was not found.");

        _db.SetOriginalProductRowVersion(product, DecodeRowVersion(request.RowVersion));

        product.Sku = NormalizeSku(request.Sku);
        product.Name = NormalizeName(request.Name);
        product.Price = ValidatePrice(request.Price);
        product.StockQuantity = ValidateStock(request.StockQuantity);
        product.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ClearChangeTracker();
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            throw new ConflictException($"SKU '{product.Sku}' already exists.");
        }

        _cacheVersion.Invalidate();
        _logger.LogInformation("Updated product {ProductId}", product.Id);
        return Map(product);
    }

    internal static IQueryable<Product> ApplyFilters(
        IQueryable<Product> products,
        string? name,
        decimal? minPrice,
        decimal? maxPrice,
        bool? inStock,
        bool includeInactive)
    {
        if (!includeInactive)
            products = products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(name))
            products = products.Where(p => p.Name.Contains(name));

        if (minPrice is decimal min)
            products = products.Where(p => p.Price >= min);

        if (maxPrice is decimal max)
            products = products.Where(p => p.Price <= max);

        if (inStock == true)
            products = products.Where(p => p.StockQuantity > 0);
        else if (inStock == false)
            products = products.Where(p => p.StockQuantity <= 0);

        return products;
    }

    internal static IQueryable<Product> ApplySort(IQueryable<Product> products, string sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            ("price", false) => products.OrderBy(p => p.Price).ThenBy(p => p.Name),
            ("price", true) => products.OrderByDescending(p => p.Price).ThenBy(p => p.Name),
            ("sku", false) => products.OrderBy(p => p.Sku),
            ("sku", true) => products.OrderByDescending(p => p.Sku),
            ("name", true) => products.OrderByDescending(p => p.Name),
            _ => products.OrderBy(p => p.Name)
        };

    private static string NormalizeSku(string? sku)
    {
        var value = (sku ?? string.Empty).Trim().ToUpperInvariant();
        if (value.Length is < 2 or > 64)
            throw new ValidationException("SKU must be between 2 and 64 characters.");

        return value;
    }

    private static string NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 200)
            throw new ValidationException("Product name must be between 2 and 200 characters.");

        return value;
    }

    private static decimal ValidatePrice(decimal price)
    {
        if (price <= 0)
            throw new ValidationException("Price must be greater than zero.");

        return DiscountCalculatorRound(price);
    }

    private static decimal DiscountCalculatorRound(decimal price) =>
        Domain.Pricing.DiscountCalculator.RoundMoney(price);

    private static int ValidateStock(int stock)
    {
        if (stock < 0)
            throw new ValidationException("Stock quantity cannot be negative.");

        return stock;
    }

    private static byte[] DecodeRowVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ValidationException("RowVersion is required.");

        try
        {
            var bytes = Convert.FromBase64String(rowVersion);
            if (bytes.Length == 0)
                throw new ValidationException("RowVersion is required.");

            return bytes;
        }
        catch (FormatException)
        {
            throw new ValidationException("RowVersion must be a base64 string.");
        }
    }

    internal static ProductResponse Map(Product product) => new(
        product.Id,
        product.Sku,
        product.Name,
        product.Price,
        product.StockQuantity,
        product.IsActive,
        Convert.ToBase64String(product.RowVersion));
}
