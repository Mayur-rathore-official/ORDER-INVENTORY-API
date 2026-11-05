using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderInventory.Application.Abstractions;
using OrderInventory.Application.Common;
using OrderInventory.Application.Dtos;

namespace OrderInventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders)
    {
        _orders = orders;
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Place(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _orders.PlaceAsync(Actor(), idempotencyKey, request, cancellationToken);
        if (result.Replayed)
            return Ok(result.Order);

        return CreatedAtAction(nameof(Get), new { id = result.Order.Id }, result.Order);
    }

    [HttpGet("my")]
    public async Task<ActionResult<PagedResult<OrderResponse>>> Mine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _orders.ListMineAsync(Actor(), new OrderListQuery(page, pageSize, status), cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _orders.ListAllAsync(new OrderListQuery(page, pageSize, status), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _orders.GetAsync(Actor(), id, cancellationToken));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderResponse>> ChangeStatus(Guid id, ChangeOrderStatusRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _orders.ChangeStatusAsync(Actor(), id, request.Status, cancellationToken));
    }

    private RequestActor Actor()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("The access token is missing a user id.");

        return new RequestActor(Guid.Parse(idValue), User.IsInRole("Admin"));
    }
}
