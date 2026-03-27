using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Complete carrier optimization plan for a query chain.
/// Replaces CarrierClassInfo + CarrierStrategy as a single self-contained type.
/// </summary>
internal sealed class CarrierPlan : IEquatable<CarrierPlan>
{
    public CarrierPlan(
        bool isEligible,
        string? ineligibleReason = null,
        string? className = null,
        string? baseClassName = null,
        IReadOnlyList<Models.CarrierField>? fields = null,
        IReadOnlyList<Models.CarrierStaticField>? staticFields = null,
        IReadOnlyList<CarrierParameter>? parameters = null,
        string? maskType = null,
        int maskBitCount = 0,
        IReadOnlyList<string>? implementedInterfaces = null)
    {
        IsEligible = isEligible;
        IneligibleReason = ineligibleReason;
        ClassName = className ?? "";
        BaseClassName = baseClassName ?? "";
        Fields = fields ?? Array.Empty<Models.CarrierField>();
        StaticFields = staticFields ?? Array.Empty<Models.CarrierStaticField>();
        Parameters = parameters ?? Array.Empty<CarrierParameter>();
        MaskType = maskType;
        MaskBitCount = maskBitCount;
        ImplementedInterfaces = implementedInterfaces ?? Array.Empty<string>();
    }

    /// <summary>Whether the chain qualifies for carrier optimization.</summary>
    public bool IsEligible { get; }

    /// <summary>Human-readable reason if not eligible.</summary>
    public string? IneligibleReason { get; }

    /// <summary>Generated carrier class name (e.g., "Chain_0"). Assigned during emission.</summary>
    public string ClassName { get; set; }

    /// <summary>Carrier base class name (e.g., "QueryCarrier&lt;User&gt;"). Assigned during emission.</summary>
    public string BaseClassName { get; set; }

    /// <summary>Instance fields on the carrier class.</summary>
    public IReadOnlyList<Models.CarrierField> Fields { get; }

    /// <summary>Static fields on the carrier class (FieldInfo caches).</summary>
    public IReadOnlyList<Models.CarrierStaticField> StaticFields { get; }

    /// <summary>Parameters with extraction/binding metadata.</summary>
    public IReadOnlyList<CarrierParameter> Parameters { get; }

    /// <summary>CLR type for the conditional mask field (byte/ushort/uint), or null if no conditionals.</summary>
    public string? MaskType { get; }

    /// <summary>Number of conditional bits.</summary>
    public int MaskBitCount { get; }

    /// <summary>Fully qualified closed interface names this carrier implements. Assigned during emission.</summary>
    public IReadOnlyList<string> ImplementedInterfaces { get; set; }

    /// <summary>Creates an ineligible plan with a reason.</summary>
    public static CarrierPlan Ineligible(string reason)
    {
        return new CarrierPlan(isEligible: false, ineligibleReason: reason);
    }

    public bool Equals(CarrierPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsEligible == other.IsEligible
            && IneligibleReason == other.IneligibleReason
            && ClassName == other.ClassName
            && BaseClassName == other.BaseClassName
            && MaskType == other.MaskType
            && MaskBitCount == other.MaskBitCount
            && EqualityHelpers.SequenceEqual(Fields, other.Fields)
            && EqualityHelpers.SequenceEqual(StaticFields, other.StaticFields)
            && EqualityHelpers.SequenceEqual(Parameters, other.Parameters);
    }

    public override bool Equals(object? obj) => Equals(obj as CarrierPlan);

    public override int GetHashCode()
    {
        return HashCode.Combine(IsEligible, ClassName, BaseClassName, Fields.Count);
    }
}
