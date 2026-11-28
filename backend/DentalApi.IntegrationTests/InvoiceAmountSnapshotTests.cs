using DentalApi.Services;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class InvoiceAmountSnapshotTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(28)]
    public void Snapshots_exact_integer_amounts_without_rounding_or_inferred_tax(int scale)
    {
        // XTS is the test currency code; these are illustrative amounts, not clinical tariffs.
        var invoice = InvoiceAmountSnapshot.Calculate("XTS", scale,
            [new(1205, 105, 73), new(200, 0, 0)]);

        Assert.Equal("XTS", invoice.Currency);
        Assert.Equal(scale, invoice.Scale);
        Assert.Equal(1405L, invoice.SubtotalUnits);
        Assert.Equal(105L, invoice.DiscountUnits);
        Assert.Equal(73L, invoice.TaxUnits);
        Assert.Equal(1373L, invoice.TotalUnits);
    }

    [Fact]
    public void Free_lines_and_explicit_zero_tax_are_representable()
    {
        var invoice = InvoiceAmountSnapshot.Calculate("XTS", 0, [new(0, 0, 0), new(200, 200, 0)]);
        Assert.Equal(200L, invoice.SubtotalUnits);
        Assert.Equal(200L, invoice.DiscountUnits);
        Assert.Equal(0L, invoice.TotalUnits);
    }

    [Fact]
    public void Snapshot_preserves_line_order_and_is_not_changed_by_the_input_collection()
    {
        var first = new InvoiceChargeLine(5, 0, 0);
        var second = new InvoiceChargeLine(8, 2, 1);
        var input = new[] { first, second };
        var invoice = InvoiceAmountSnapshot.Calculate("XTS", 2, input);
        input[0] = new(99, 0, 0);

        Assert.Equal(new[] { first, second }, invoice.Lines);
        var mutableInterface = Assert.IsAssignableFrom<IList<InvoiceChargeLine>>(invoice.Lines);
        Assert.Throws<NotSupportedException>(() => mutableInterface[0] = input[0]);
        Assert.Equal(12L, invoice.TotalUnits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xts")]
    [InlineData("XT")]
    [InlineData(" XTS")]
    [InlineData("X1S")]
    public void Rejects_invalid_currency_format(string currency) =>
        Assert.Throws<ArgumentException>(() => InvoiceAmountSnapshot.Calculate(currency, 0, [new(1, 0, 0)]));

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void Rejects_out_of_range_scale(int scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceAmountSnapshot.Calculate("XTS", scale, [new(1, 0, 0)]));

    [Fact]
    public void Rejects_empty_or_null_priced_lines()
    {
        Assert.Throws<ArgumentException>(() => InvoiceAmountSnapshot.Calculate("XTS", 0, []));
        Assert.Throws<ArgumentNullException>(() => InvoiceAmountSnapshot.Calculate("XTS", 0, null!));
        Assert.Throws<ArgumentNullException>(() => InvoiceAmountSnapshot.Calculate(null!, 0, [new(1, 0, 0)]));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    [InlineData(1, 2, 0)]
    public void Rejects_negative_amounts_and_discounts_larger_than_the_line_charge(
        long charge, long discount, long tax) =>
        Assert.Throws<ArgumentException>(() =>
            InvoiceAmountSnapshot.Calculate("XTS", 2, [new(charge, discount, tax)]));

    [Fact]
    public void Rejects_overflow_in_snapshot_columns_or_total()
    {
        Assert.Throws<OverflowException>(() =>
            InvoiceAmountSnapshot.Calculate("XTS", 2, [new(long.MaxValue, 0, 0), new(1, 1, 0)]));
        Assert.Throws<OverflowException>(() =>
            InvoiceAmountSnapshot.Calculate("XTS", 2, [new(1, 1, long.MaxValue), new(1, 1, 1)]));
        Assert.Throws<OverflowException>(() =>
            InvoiceAmountSnapshot.Calculate("XTS", 2, [new(long.MaxValue, 0, 1)]));
    }

    [Fact]
    public void Does_not_overflow_when_discount_offsets_tax_at_the_boundary()
    {
        var invoice = InvoiceAmountSnapshot.Calculate("XTS", 2, [new(long.MaxValue, 1, 1)]);
        Assert.Equal(long.MaxValue, invoice.SubtotalUnits);
        Assert.Equal(long.MaxValue, invoice.TotalUnits);
    }
}
