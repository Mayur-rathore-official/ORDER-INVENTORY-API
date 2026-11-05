using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderInventory.Application.Abstractions;

namespace OrderInventory.Infrastructure.Background;

public sealed class OrderMaintenanceOptions
{
    public const string SectionName = "OrderMaintenance";

    public int PendingTimeoutMinutes { get; set; } = 30;

    public int ScanIntervalSeconds { get; set; } = 30;
}

public sealed class PendingOrderCancellationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OrderMaintenanceOptions> _options;
    private readonly ILogger<PendingOrderCancellationService> _logger;

    public PendingOrderCancellationService(
        IServiceScopeFactory scopeFactory,
        IOptions<OrderMaintenanceOptions> options,
        ILogger<PendingOrderCancellationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.ScanIntervalSeconds));
        var timeoutMinutes = Math.Max(1, _options.Value.PendingTimeoutMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
                var cancelled = await orders.CancelExpiredPendingOrdersAsync(timeoutMinutes, stoppingToken);
                if (cancelled > 0)
                    _logger.LogInformation("Automatically cancelled {Count} pending orders older than {Minutes} minutes", cancelled, timeoutMinutes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending order cancellation scan failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
