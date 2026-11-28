using System.Globalization;

namespace DentalApi.Services;

/// <summary>
/// An internal patient-facing draft representation for a future authorized reader.
/// It is neither an issued invoice nor an amount due, and grants no access or authorization.
/// Monetary fields contain decimal strings of integer units at Scale, so JSON clients can
/// retain exact amounts even beyond their safe numeric integer range.
/// </summary>
internal sealed class InvoiceDraftReviewBreakdown
{
    public const int MaximumLines = 100;

    private InvoiceDraftReviewBreakdown(InvoiceAmountSnapshot amount)
    {
        Currency = amount.Currency;
        Scale = amount.Scale;
        SubtotalUnits = Units(amount.SubtotalUnits);
        DiscountUnits = Units(amount.DiscountUnits);
        TaxUnits = Units(amount.TaxUnits);
        ReviewTotalUnits = Units(amount.TotalUnits);

        Lines = Array.AsReadOnly(amount.Lines.Select((line, index) => new Line(
            index + 1, Units(line.ChargeUnits), Units(line.DiscountUnits), Units(line.TaxUnits),
            Units(checked(line.ChargeUnits - line.DiscountUnits + line.TaxUnits)))).ToArray());
    }

    public string Status => "draftReview";
    public bool Payable => false;
    public string Currency { get; }
    public int Scale { get; }
    public string SubtotalUnits { get; }
    public string DiscountUnits { get; }
    public string TaxUnits { get; }
    public string ReviewTotalUnits { get; }
    public IReadOnlyList<Line> Lines { get; }

    public static InvoiceDraftReviewBreakdown Project(InvoiceDraftSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        // Do not publish a truncated breakdown whose displayed lines cannot explain its total.
        if (draft.Amount.Lines.Count > MaximumLines)
            throw new ArgumentException("The draft exceeds the review breakdown line limit.", nameof(draft));

        return new InvoiceDraftReviewBreakdown(draft.Amount);
    }

    private static string Units(long units) => units.ToString(CultureInfo.InvariantCulture);

    internal sealed record Line(
        int Sequence, string ChargeUnits, string DiscountUnits, string TaxUnits, string ReviewTotalUnits)
    {
        // Presentation code chooses the localized generic label. Never copy a catalog label,
        // clinical note, patient label or arbitrary caller text into the projection.
        public string LabelKey => "invoice.review.charge";
    }
}
