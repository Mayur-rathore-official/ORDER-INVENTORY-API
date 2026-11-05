using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Products;
using OrderInventory.Application.Reports;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.UnitTests;

public class QueryTranslationTests
{
    [Fact]
    public void Top_customers_translates_to_a_single_grouped_sql_query()
    {
        using var db = CreateSqlServerContext();
        var clock = new FixedClock(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var service = new ReportService(db, clock, NullLogger<ReportService>.Instance);

        var sql = service.GetTopCustomersSql(30);

        sql.Should().Contain("GROUP BY");
        sql.Should().Contain("TOP");
        sql.Split("SELECT", StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();

        var root = FindRepoRoot();
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        File.WriteAllText(Path.Combine(root, "artifacts", "top-customers.sql"), sql);
    }

    [Fact]
    public void Product_filters_sorting_and_paging_stay_in_the_database_query()
    {
        using var db = CreateSqlServerContext();
        var filtered = ProductService.ApplyFilters(
            db.Products.AsNoTracking(),
            name: "Monitor",
            minPrice: 100m,
            maxPrice: 20_000m,
            inStock: true,
            includeInactive: false);
        var sql = ProductService.ApplySort(filtered, "price", descending: true)
            .Skip(20)
            .Take(10)
            .ToQueryString();

        sql.Should().Contain("WHERE");
        sql.Should().Contain("LIKE");
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("OFFSET");
        sql.Should().Contain("StockQuantity");
    }

    private static AppDbContext CreateSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=OrderInventory;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrderInventory.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
