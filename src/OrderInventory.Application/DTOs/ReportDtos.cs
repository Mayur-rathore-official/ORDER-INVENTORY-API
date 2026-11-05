namespace OrderInventory.Application.Dtos;

public sealed record TopCustomerResponse(
    Guid CustomerId,
    string FullName,
    string Email,
    decimal TotalSpend,
    int OrderCount);
