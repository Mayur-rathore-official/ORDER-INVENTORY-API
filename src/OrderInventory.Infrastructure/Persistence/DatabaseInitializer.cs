using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.Abstractions;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Enums;

namespace OrderInventory.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    public const string AdminEmail = "admin@store.local";
    public const string AdminPassword = "Admin123!";
    public const string CustomerPassword = "Customer123!";

    public static readonly string[] CustomerEmails =
    [
        "customer1@store.local",
        "customer2@store.local",
        "customer3@store.local"
    ];

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwords;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(AppDbContext db, IPasswordHasher passwords, ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _passwords = passwords;
        _logger = logger;
    }

    public async Task InitializeAsync(bool seed, CancellationToken cancellationToken = default)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            await _db.Database.MigrateAsync(cancellationToken);
        else
            await _db.Database.EnsureCreatedAsync(cancellationToken);

        if (!seed)
        {
            _logger.LogInformation("Database schema is ready; seed is disabled");
            return;
        }

        if (await _db.Users.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Database already contains users; seed skipped");
            return;
        }

        var adminHash = _passwords.Hash(AdminPassword);
        var customerHash = _passwords.Hash(CustomerPassword);

        _db.Users.AddRange(
            new User
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
                FullName = "Store Admin",
                Email = AdminEmail,
                PasswordHash = adminHash,
                Role = UserRole.Admin
            },
            new User
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"),
                FullName = "Ava Shah",
                Email = CustomerEmails[0],
                PasswordHash = customerHash,
                Role = UserRole.Customer
            },
            new User
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"),
                FullName = "Liam Patel",
                Email = CustomerEmails[1],
                PasswordHash = customerHash,
                Role = UserRole.Customer
            },
            new User
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3"),
                FullName = "Noah Singh",
                Email = CustomerEmails[2],
                PasswordHash = customerHash,
                Role = UserRole.Customer
            });

        _db.Products.AddRange(CreateProducts());
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded 1 admin, 3 customers, and 15 products");
    }

    private static IEnumerable<Product> CreateProducts()
    {
        var catalog = new (string Sku, string Name, decimal Price, int Stock, bool Active)[]
        {
            ("SKU-1001", "Mechanical Keyboard", 2499.00m, 40, true),
            ("SKU-1002", "Wireless Mouse", 899.00m, 80, true),
            ("SKU-1003", "27 Inch Monitor", 12499.00m, 15, true),
            ("SKU-1004", "USB-C Dock", 3499.50m, 25, true),
            ("SKU-1005", "Laptop Stand", 1499.00m, 50, true),
            ("SKU-1006", "Noise Cancelling Headphones", 7999.00m, 20, true),
            ("SKU-1007", "Webcam HD", 2199.00m, 30, true),
            ("SKU-1008", "Desk Lamp", 999.00m, 60, true),
            ("SKU-1009", "Ergonomic Chair", 18999.00m, 8, true),
            ("SKU-1010", "Notebook Pack", 499.00m, 100, true),
            ("SKU-1011", "Portable SSD 1TB", 4599.00m, 18, true),
            ("SKU-1012", "Drawing Tablet", 6299.00m, 12, true),
            ("SKU-1013", "Last Unit Widget", 1500.00m, 1, true),
            ("SKU-1014", "Out of Stock Cable", 299.00m, 0, true),
            ("SKU-1015", "Discontinued Adapter", 199.00m, 5, false)
        };

        var index = 1;
        foreach (var item in catalog)
        {
            yield return new Product
            {
                Id = Guid.Parse($"cccccccc-cccc-cccc-cccc-{index:000000000000}"),
                Sku = item.Sku,
                Name = item.Name,
                Price = item.Price,
                StockQuantity = item.Stock,
                IsActive = item.Active
            };
            index++;
        }
    }
}
