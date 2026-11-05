using FluentAssertions;
using OrderInventory.Domain.Enums;
using OrderInventory.Domain.Exceptions;
using OrderInventory.Domain.Orders;
using OrderInventory.Domain.Pricing;

namespace OrderInventory.UnitTests;

public class DiscountCalculatorTests
{
    [Fact]
    public void No_discount_when_subtotal_is_at_or_below_10000_and_order_is_not_a_fifth()
    {
        var quote = DiscountCalculator.Calculate(10_000m, customerOrderNumber: 4);

        quote.Discount.Should().Be(0m);
        quote.Total.Should().Be(10_000m);
    }

    [Fact]
    public void Volume_discount_is_five_percent_only_when_subtotal_is_above_10000()
    {
        var quote = DiscountCalculator.Calculate(12_500m, customerOrderNumber: 1);

        quote.Discount.Should().Be(625m);
        quote.Total.Should().Be(11_875m);
    }

    [Fact]
    public void Loyalty_discount_is_ten_percent_on_every_fifth_order()
    {
        var fifth = DiscountCalculator.Calculate(200m, customerOrderNumber: 5);
        var tenth = DiscountCalculator.Calculate(200m, customerOrderNumber: 10);
        var fourth = DiscountCalculator.Calculate(200m, customerOrderNumber: 4);

        fifth.Discount.Should().Be(20m);
        fifth.Total.Should().Be(180m);
        tenth.Discount.Should().Be(20m);
        fourth.Discount.Should().Be(0m);
    }

    [Fact]
    public void Volume_and_loyalty_discounts_stack_and_each_component_is_rounded_to_two_places()
    {
        // 10000.10 * 5% = 500.005 -> 500.01; 10000.10 * 10% = 1000.010 -> 1000.01
        var quote = DiscountCalculator.Calculate(10_000.10m, customerOrderNumber: 5);

        quote.Discount.Should().Be(1_500.02m);
        quote.Total.Should().Be(8_500.08m);
    }

    [Fact]
    public void Money_rounds_the_midpoint_away_from_zero()
    {
        DiscountCalculator.RoundMoney(10.005m).Should().Be(10.01m);
        DiscountCalculator.RoundMoney(10.004m).Should().Be(10.00m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Order_number_must_be_at_least_one(int orderNumber)
    {
        var act = () => DiscountCalculator.Calculate(10m, orderNumber);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class OrderStatusPolicyTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    public void Allowed_transitions_pass(OrderStatus current, OrderStatus next)
    {
        OrderStatusPolicy.IsAllowed(current, next).Should().BeTrue();
        var act = () => OrderStatusPolicy.EnsureAllowed(current, next);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    public void Final_and_skipped_transitions_are_rejected_with_a_clear_message(OrderStatus current, OrderStatus next)
    {
        var act = () => OrderStatusPolicy.EnsureAllowed(current, next);

        act.Should().Throw<InvalidStatusTransitionException>()
            .Which.Message.Should().Contain($"from {current} to {next}")
            .And.Contain("Shipped and Cancelled are final");
    }
}
