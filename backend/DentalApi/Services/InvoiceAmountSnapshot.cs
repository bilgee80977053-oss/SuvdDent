namespace DentalApi.Services;

/// <summary>
/// One already-priced line. All amounts use the invoice's declared currency and scale.
/// Tax and discount are supplied explicitly; this component does not determine either.
/// </summary>
internal readonly record struct InvoiceChargeLine(long ChargeUnits, long DiscountUnits, long TaxUnits);

/// <summary>
/// An immutable breakdown of invoice amounts, not an issued invoice, estimate, or proof of payment.
/// A stored unit represents 10^-Scale currency units; there is no conversion or rounding here.
/// </summary>
internal sealed class InvoiceAmountSnapshot
{
    private InvoiceAmountSnapshot(
        string currency, int scale, IReadOnlyList<InvoiceChargeLine> lines,
        long subtotalUnits, long discountUnits, long taxUnits, long totalUnits)
    {
        Currency = currency;
        Scale = scale;
        Lines = lines;
        SubtotalUnits = subtotalUnits;
        DiscountUnits = discountUnits;
        TaxUnits = taxUnits;
        TotalUnits = totalUnits;
    }

    public string Currency { get; }
    public int Scale { get; }
    public IReadOnlyList<InvoiceChargeLine> Lines { get; }
    public long SubtotalUnits { get; }
    public long DiscountUnits { get; }
    public long TaxUnits { get; }
    public long TotalUnits { get; }

    // Currency and scale are supplied by the caller. The format check is not a currency policy.
    // Negative amounts, credit notes, refunds and allocations are outside this calculation.
    public static InvoiceAmountSnapshot Calculate(
        string currency, int scale, IEnumerable<InvoiceChargeLine> pricedLines)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(pricedLines);
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Use a three-letter uppercase currency code.", nameof(currency));
        if (scale is < 0 or > 28)
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be between 0 and 28.");

        var lines = new List<InvoiceChargeLine>();
        long subtotal = 0, discount = 0, tax = 0;
        foreach (var line in pricedLines)
        {
            if (line.ChargeUnits < 0 || line.DiscountUnits < 0 || line.TaxUnits < 0 ||
                line.DiscountUnits > line.ChargeUnits)
                throw new ArgumentException(
                    "Each line requires non-negative amounts and a discount no greater than its charge.",
                    nameof(pricedLines));

            checked
            {
                subtotal += line.ChargeUnits;
                discount += line.DiscountUnits;
                tax += line.TaxUnits;
            }
            lines.Add(line);
        }

        if (lines.Count == 0)
            throw new ArgumentException("At least one priced line is required.", nameof(pricedLines));

        // Subtract first so a representable net amount is not rejected by a gross-plus-tax intermediate.
        var total = checked(subtotal - discount + tax);
        return new(currency, scale, lines.AsReadOnly(), subtotal, discount, tax, total);
    }
}
