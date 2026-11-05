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

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<DatabaseInitializer>();
        services.Configure<OrderMaintenanceOptions>(configuration.GetSection(OrderMaintenanceOptions.SectionName));

        if (registerBackgroundService)
            services.AddHostedService<PendingOrderCancellationService>();

        return services;
    }
}
