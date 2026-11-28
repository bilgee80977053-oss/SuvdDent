using System.Text.Json;
using DentalApi.Services;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class InvoiceDraftReviewBreakdownTests
{
    private static InvoiceDraftSnapshot Draft(params InvoiceChargeLine[] lines) =>
        InvoiceDraftSnapshot.Create(new(101, 301), new(new(101, 301),
            Guid.Parse("29f45b52-1efd-4b19-b49e-5845d0d7ffa5"),
            Guid.Parse("e4cdad36-bb25-4530-aced-4dd77ca5658b"),
            InvoiceAmountSnapshot.Calculate("XTS", 2, lines)));

    [Fact]
    public void Ordered_lines_and_nonpayable_total_agree_with_the_frozen_pricing_snapshot()
    {
        InvoiceChargeLine[] source = [new(200, 15, 8), new(100, 50, 3), new(75, 0, 0)];
        var draft = Draft(source);
        source[0] = new(900, 0, 15);

        var review = InvoiceDraftReviewBreakdown.Project(draft);

        Assert.Equal(("draftReview", false, "XTS", 2), (review.Status, review.Payable, review.Currency, review.Scale));
        Assert.Equal(("375", "65", "11", "321"),
            (review.SubtotalUnits, review.DiscountUnits, review.TaxUnits, review.ReviewTotalUnits));
        Assert.Collection(review.Lines,
            line => Assert.Equal((1, "200", "15", "8", "193"), Amounts(line)),
            line => Assert.Equal((2, "100", "50", "3", "53"), Amounts(line)),
            line => Assert.Equal((3, "75", "0", "0", "75"), Amounts(line)));
        Assert.All(review.Lines, line => Assert.Equal("invoice.review.charge", line.LabelKey));
        var collection = Assert.IsAssignableFrom<IList<InvoiceDraftReviewBreakdown.Line>>(review.Lines);
        Assert.Throws<NotSupportedException>(() => collection[0] = collection[1]);
    }

    [Fact]
    public void Web_serialization_preserves_exact_units_and_has_no_scope_or_payment_destination_fields()
    {
        var review = InvoiceDraftReviewBreakdown.Project(Draft(new InvoiceChargeLine(long.MaxValue, 10, 10)));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(review, JsonSerializerOptions.Web));
        var root = doc.RootElement;
        Assert.Equal(
            new[] { "currency", "discountUnits", "lines", "payable", "reviewTotalUnits",
                "scale", "status", "subtotalUnits", "taxUnits" },
            root.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.False(root.GetProperty("payable").GetBoolean());
        Assert.Equal("draftReview", root.GetProperty("status").GetString());
        Assert.Equal("9223372036854775807", root.GetProperty("reviewTotalUnits").GetString());
        var line = Assert.Single(root.GetProperty("lines").EnumerateArray());
        Assert.Equal(new[] { "chargeUnits", "discountUnits", "labelKey", "reviewTotalUnits", "sequence", "taxUnits" },
            line.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal("9223372036854775807", line.GetProperty("reviewTotalUnits").GetString());
        Assert.Equal("invoice.review.charge", line.GetProperty("labelKey").GetString());
    }

    [Fact]
    public void Zero_draft_remains_nonpayable_without_implying_settlement()
    {
        var review = InvoiceDraftReviewBreakdown.Project(Draft(new InvoiceChargeLine(150, 150, 0)));
        Assert.Equal("draftReview", review.Status);
        Assert.False(review.Payable);
        Assert.Equal("0", review.ReviewTotalUnits);
        Assert.Equal("0", Assert.Single(review.Lines).ReviewTotalUnits);
    }

    [Fact]
    public void Bounded_projection_rejects_oversized_drafts_instead_of_truncating()
    {
        var lines = Enumerable.Repeat(new InvoiceChargeLine(1, 0, 0), InvoiceDraftReviewBreakdown.MaximumLines + 1).ToArray();
        var boundary = InvoiceDraftReviewBreakdown.Project(Draft(lines[..^1]));
        Assert.Equal(InvoiceDraftReviewBreakdown.MaximumLines, boundary.Lines[^1].Sequence);
        Assert.Equal("100", boundary.ReviewTotalUnits);
        Assert.Throws<ArgumentException>("draft", () => InvoiceDraftReviewBreakdown.Project(Draft(lines)));
        Assert.Throws<ArgumentNullException>("draft", () => InvoiceDraftReviewBreakdown.Project(null!));
    }

    private static (int, string, string, string, string) Amounts(InvoiceDraftReviewBreakdown.Line line) =>
        (line.Sequence, line.ChargeUnits, line.DiscountUnits, line.TaxUnits, line.ReviewTotalUnits);
}
