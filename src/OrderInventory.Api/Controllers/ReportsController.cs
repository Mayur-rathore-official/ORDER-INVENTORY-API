using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Dtos;

namespace OrderInventory.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("top-customers")]
    public async Task<ActionResult<IReadOnlyList<TopCustomerResponse>>> TopCustomers([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        return Ok(await _reports.GetTopCustomersAsync(days, cancellationToken));
    }
}
