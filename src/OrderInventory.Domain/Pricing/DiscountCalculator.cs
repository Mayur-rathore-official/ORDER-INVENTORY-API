namespace OrderInventory.Domain.Pricing;

public readonly record struct DiscountQuote(decimal SubTotal, decimal Discount, decimal Total);

/// <summary>
/// Server-side discount rules. Each component is rounded to 2 decimal places
/// with midpoint rounding away from zero, then summed.
/// A volume discount of 5% applies when the subtotal is strictly above 10,000.
/// A loyalty discount of 10% of the subtotal applies on every 5th order (5, 10, 15, ...).
/// </summary>
public static class DiscountCalculator
{
    public const decimal VolumeThreshold = 10_000m;
    public const decimal VolumeRate = 0.05m;
    public const decimal LoyaltyRate = 0.10m;

    public static DiscountQuote Calculate(decimal subTotal, int customerOrderNumber)
    {
        if (subTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(subTotal), "Subtotal cannot be negative.");

        if (customerOrderNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(customerOrderNumber), "Order number must be at least 1.");

        decimal discount = 0m;

        if (subTotal > VolumeThreshold)
            discount += RoundMoney(subTotal * VolumeRate);

        if (customerOrderNumber % 5 == 0)
            discount += RoundMoney(subTotal * LoyaltyRate);

        if (discount > subTotal)
            discount = subTotal;

        var total = RoundMoney(subTotal - discount);
        return new DiscountQuote(subTotal, discount, total);
    }

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
