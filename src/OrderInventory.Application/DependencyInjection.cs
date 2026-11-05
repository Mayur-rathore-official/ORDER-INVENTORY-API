using Microsoft.Extensions.DependencyInjection;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Auth;
using OrderInventory.Application.Common;
using OrderInventory.Application.Orders;
using OrderInventory.Application.Products;
using OrderInventory.Application.Reports;

namespace OrderInventory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IOrderPlacementGate, NoOpOrderPlacementGate>();
        services.AddSingleton<IProductCacheVersion, ProductCacheVersion>();
        return services;
    }
}
