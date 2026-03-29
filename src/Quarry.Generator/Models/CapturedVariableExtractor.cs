using System;
using Quarry.Generators.IR;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a per-variable [UnsafeAccessor] extractor for a captured variable within a clause.
/// Instead of one extractor per parameter (__ExtractP{n}), this model creates one extractor
/// per unique captured variable (__ExtractVar_{name}_{clauseIndex}).
/// </summary>
internal sealed class CapturedVariableExtractor : IEquatable<CapturedVariableExtractor>
{
    public CapturedVariableExtractor(
        string methodName,
        string variableName,
        string variableType,
        string displayClassName,
        CaptureKind captureKind,
        bool isStaticField)
    {
        MethodName = methodName;
        VariableName = variableName;
        VariableType = variableType;
        DisplayClassName = displayClassName;
        CaptureKind = captureKind;
        IsStaticField = isStaticField;
    }

    /// <summary>
    /// The [UnsafeAccessor] method name on the carrier (e.g., "__ExtractVar_x_0").
    /// Unique within the carrier class.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// The original source variable name (e.g., "x", "viewModel").
    /// Used as the local variable name in the generated interceptor body.
    /// </summary>
    public string VariableName { get; }

    /// <summary>
    /// Fully-qualified CLR type of the captured variable on the display class.
    /// Used as the return type of the [UnsafeAccessor] extern method.
    /// </summary>
    public string VariableType { get; }

    /// <summary>
    /// The display class or containing class for the [UnsafeAccessorType] attribute.
    /// For ClosureCapture: the compiler-generated display class.
    /// For FieldCapture: the containing class (display class suffix stripped).
    /// </summary>
    public string DisplayClassName { get; }

    /// <summary>
    /// How the variable is captured: ClosureCapture or FieldCapture.
    /// </summary>
    public CaptureKind CaptureKind { get; }

    /// <summary>
    /// For FieldCapture: whether the field is static on the containing class.
    /// Selects UnsafeAccessorKind.StaticField vs UnsafeAccessorKind.Field.
    /// </summary>
    public bool IsStaticField { get; }

    public bool Equals(CapturedVariableExtractor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MethodName == other.MethodName
            && VariableName == other.VariableName
            && VariableType == other.VariableType
            && DisplayClassName == other.DisplayClassName
            && CaptureKind == other.CaptureKind
            && IsStaticField == other.IsStaticField;
    }

    public override bool Equals(object? obj) => Equals(obj as CapturedVariableExtractor);
    public override int GetHashCode() => HashCode.Combine(MethodName, VariableName, VariableType, DisplayClassName, CaptureKind, IsStaticField);
}
