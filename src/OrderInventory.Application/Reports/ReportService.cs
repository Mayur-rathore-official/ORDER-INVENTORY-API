using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Dtos;
using OrderInventory.Domain.Enums;
using OrderInventory.Domain.Exceptions;

namespace OrderInventory.Application.Reports;

public sealed class ReportService : IReportService
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IApplicationDbContext db, IClock clock, ILogger<ReportService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TopCustomerResponse>> GetTopCustomersAsync(int days, CancellationToken cancellationToken)
    {
        var query = BuildQuery(days);
        _logger.LogInformation("Top customers report SQL: {Sql}", query.ToQueryString());
        return await query.ToListAsync(cancellationToken);
    }

    public string GetTopCustomersSql(int days) => BuildQuery(days).ToQueryString();

    private IQueryable<TopCustomerResponse> BuildQuery(int days)
    {
        if (days is < 1 or > 366)
            throw new ValidationException("Days must be between 1 and 366.");

        var since = _clock.UtcNow.AddDays(-days);

        return
            (from order in _db.Orders.AsNoTracking()
             join customer in _db.Users.AsNoTracking() on order.CustomerId equals customer.Id
             where (order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.Shipped)
                   && order.CreatedAt >= since
             group order by new { customer.Id, customer.FullName, customer.Email } into g
             orderby g.Sum(order => order.Total) descending
             select new TopCustomerResponse(
                 g.Key.Id,
                 g.Key.FullName,
                 g.Key.Email,
                 g.Sum(order => order.Total),
                 g.Count()))
            .Take(5);
    }
}
