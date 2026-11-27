using System.Globalization;
using DentalApi.Clinical;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class RoutineToothIdentifierTests
{
    [Fact]
    public void Parses_exactly_the_routine_chart_and_no_other_ascii_pair()
    {
        const string permanent = "11 12 13 14 15 16 17 18 21 22 23 24 25 26 27 28 31 32 33 34 35 36 37 38 41 42 43 44 45 46 47 48";
        const string primary = "51 52 53 54 55 61 62 63 64 65 71 72 73 74 75 81 82 83 84 85";
        var permanentCodes = permanent.Split(' ').ToHashSet(StringComparer.Ordinal);
        var primaryCodes = primary.Split(' ').ToHashSet(StringComparer.Ordinal);
        var accepted = new HashSet<string>(StringComparer.Ordinal);

        for (var first = 0; first < 128; first++)
        for (var second = 0; second < 128; second++)
        {
            var code = string.Concat((char)first, (char)second);
            var expected = permanentCodes.Contains(code) || primaryCodes.Contains(code);
            Assert.Equal(expected, RoutineToothIdentifier.TryParse(code, out var tooth));
            if (!expected)
            {
                Assert.Null(tooth);
                continue;
            }

            Assert.NotNull(tooth);
            Assert.Equal(code, tooth.Code);
            Assert.Equal(int.Parse(code.AsSpan(0, 1), CultureInfo.InvariantCulture), tooth.Quadrant);
            Assert.Equal(int.Parse(code.AsSpan(1, 1), CultureInfo.InvariantCulture), tooth.Position);
            Assert.Equal(primaryCodes.Contains(code), tooth.IsPrimary);
            Assert.True(accepted.Add(tooth.Code));
        }

        Assert.Equal(32, permanentCodes.Count);
        Assert.Equal(20, primaryCodes.Count);
        Assert.Equal(52, accepted.Count);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("1")] [InlineData("111")] [InlineData(" 11")]
    [InlineData("11 ")] [InlineData("11\n")] [InlineData("1-1")] [InlineData("+11")]
    [InlineData("１１")] [InlineData("١١")] [InlineData("1١")] [InlineData("¹¹")] [InlineData("11\u200b")]
    public void Rejects_noncanonical_input_without_normalizing(string? code)
    {
        Assert.False(RoutineToothIdentifier.TryParse(code, out var tooth));
        Assert.Null(tooth);
    }

    [Theory]
    [InlineData((int)StructuredTreatmentScope.Visit, null, true)]
    [InlineData((int)StructuredTreatmentScope.Visit, "", false)]
    [InlineData((int)StructuredTreatmentScope.Visit, " ", false)]
    [InlineData((int)StructuredTreatmentScope.Visit, "11", false)]
    [InlineData((int)StructuredTreatmentScope.SingleTooth, "18", true)]
    [InlineData((int)StructuredTreatmentScope.SingleTooth, "85", true)]
    [InlineData((int)StructuredTreatmentScope.SingleTooth, "86", false)]
    [InlineData((int)StructuredTreatmentScope.SingleTooth, null, false)]
    [InlineData((int)StructuredTreatmentScope.SingleTooth, "", false)]
    [InlineData((int)StructuredTreatmentScope.Unspecified, null, false)]
    [InlineData((int)StructuredTreatmentScope.Unspecified, "11", false)]
    [InlineData(99, "11", false)]
    public void Requires_an_explicit_and_consistent_target(int rawScope, string? code, bool expected)
    {
        var scope = (StructuredTreatmentScope)rawScope;
        Assert.Equal(expected, StructuredTreatmentTarget.TryCreate(scope, code, out var target));
        if (expected)
        {
            Assert.NotNull(target);
            Assert.Equal(scope, target.Scope);
            Assert.Equal(code, target.Tooth?.Code);
        }
        else
            Assert.Null(target);
    }
}
