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
        IReadOnlyList<CarrierField>? fields = null,
        IReadOnlyList<CarrierStaticField>? staticFields = null,
        IReadOnlyList<CarrierParameter>? parameters = null,
        string? maskType = null,
        int maskBitCount = 0,
        IReadOnlyList<string>? implementedInterfaces = null,
        IReadOnlyList<Models.CarrierInterfaceStub>? deadMethods = null)
    {
        IsEligible = isEligible;
        IneligibleReason = ineligibleReason;
        ClassName = className ?? "";
        BaseClassName = baseClassName ?? "";
        Fields = fields ?? Array.Empty<CarrierField>();
        StaticFields = staticFields ?? Array.Empty<CarrierStaticField>();
        Parameters = parameters ?? Array.Empty<CarrierParameter>();
        MaskType = maskType;
        MaskBitCount = maskBitCount;
        ImplementedInterfaces = implementedInterfaces ?? Array.Empty<string>();
        DeadMethods = deadMethods ?? Array.Empty<Models.CarrierInterfaceStub>();
    }

    /// <summary>Whether the chain qualifies for carrier optimization.</summary>
    public bool IsEligible { get; }

    /// <summary>Human-readable reason if not eligible.</summary>
    public string? IneligibleReason { get; }

    /// <summary>Generated carrier class name (e.g., "Chain_0").</summary>
    public string ClassName { get; }

    /// <summary>Carrier base class name (e.g., "QueryCarrier&lt;User&gt;").</summary>
    public string BaseClassName { get; }

    /// <summary>Instance fields on the carrier class.</summary>
    public IReadOnlyList<CarrierField> Fields { get; }

    /// <summary>Static fields on the carrier class (FieldInfo caches).</summary>
    public IReadOnlyList<CarrierStaticField> StaticFields { get; }

    /// <summary>Parameters with extraction/binding metadata.</summary>
    public IReadOnlyList<CarrierParameter> Parameters { get; }

    /// <summary>CLR type for the conditional mask field (byte/ushort/uint), or null if no conditionals.</summary>
    public string? MaskType { get; }

    /// <summary>Number of conditional bits.</summary>
    public int MaskBitCount { get; }

    /// <summary>Fully qualified closed interface names this carrier implements.</summary>
    public IReadOnlyList<string> ImplementedInterfaces { get; }

    /// <summary>Dead interface methods (explicit impls that throw).</summary>
    public IReadOnlyList<Models.CarrierInterfaceStub> DeadMethods { get; }

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
