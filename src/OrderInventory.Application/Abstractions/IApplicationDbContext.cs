using OrderInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OrderInventory.Application.Abstractions;

public interface IApplicationDbContext
{
    Microsoft.EntityFrameworkCore.DbSet<User> Users { get; }

    Microsoft.EntityFrameworkCore.DbSet<Product> Products { get; }

    Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; }

    Microsoft.EntityFrameworkCore.DbSet<OrderItem> OrderItems { get; }

    Microsoft.EntityFrameworkCore.DbSet<RefreshToken> RefreshTokens { get; }

    Microsoft.EntityFrameworkCore.DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    void ClearChangeTracker();

    void SetOriginalProductRowVersion(Product product, byte[] rowVersion);
}
