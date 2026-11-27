using System.Diagnostics.CodeAnalysis;

namespace DentalApi.Clinical;

// Routine FDI individual teeth only; oral areas and atypical designations are outside this type.
internal sealed record RoutineToothIdentifier
{
    private RoutineToothIdentifier(string code) => Code = code;

    internal string Code { get; }
    internal int Quadrant => Code[0] - '0';
    internal int Position => Code[1] - '0';
    internal bool IsPrimary => Quadrant >= 5;

    internal static bool TryParse(string? value, [NotNullWhen(true)] out RoutineToothIdentifier? tooth)
    {
        tooth = null;
        if (value is not { Length: 2 } ||
            value[0] is < '1' or > '8' || value[1] is < '1' or > '8' ||
            (value[0] >= '5' && value[1] > '5'))
            return false;

        tooth = new(value);
        return true;
    }
}

internal enum StructuredTreatmentScope
{
    Unspecified = 0,
    Visit,
    SingleTooth
}

// A target is valid only with an explicit scope. Null is not an implicit visit target.
internal sealed record StructuredTreatmentTarget
{
    private StructuredTreatmentTarget(StructuredTreatmentScope scope, RoutineToothIdentifier? tooth)
    {
        Scope = scope;
        Tooth = tooth;
    }

    internal StructuredTreatmentScope Scope { get; }
    internal RoutineToothIdentifier? Tooth { get; }

    internal static bool TryCreate(StructuredTreatmentScope scope, string? toothCode,
        [NotNullWhen(true)] out StructuredTreatmentTarget? target)
    {
        target = null;
        switch (scope)
        {
            case StructuredTreatmentScope.Visit when toothCode is null:
                target = new(scope, null);
                return true;
            case StructuredTreatmentScope.SingleTooth when RoutineToothIdentifier.TryParse(toothCode, out var tooth):
                target = new(scope, tooth);
                return true;
            default:
                return false;
        }
    }
}
