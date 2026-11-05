using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderInventory.Application.Abstractions;
using OrderInventory.Infrastructure.Background;
using OrderInventory.Infrastructure.Persistence;
using OrderInventory.Infrastructure.Security;

namespace OrderInventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool registerBackgroundService = true)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Default is required. Set ConnectionStrings__Default.");

        var provider = configuration["Database:Provider"] ?? "SqlServer";
        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var sqlite = new SqliteConnectionStringBuilder(connectionString)
                {
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    DefaultTimeout = 30
                };
                options.UseSqlite(sqlite.ToString());
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<DatabaseInitializer>();
        services.Configure<OrderMaintenanceOptions>(configuration.GetSection(OrderMaintenanceOptions.SectionName));

        if (registerBackgroundService)
            services.AddHostedService<PendingOrderCancellationService>();

        return services;
    }
}
