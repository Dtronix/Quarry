using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.Utilities;

/// <summary>
/// Shared CLR type classification helpers used by code-generation emitters.
/// Single source of truth for value-type detection, reader-method resolution,
/// unresolved-type detection, and tuple type name construction.
/// </summary>
internal static class TypeClassification
{
    private static readonly HashSet<string> s_valueTypes = new(StringComparer.Ordinal)
    {
        // C# keyword types
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "char", "nint", "nuint",
        // BCL names
        "Boolean", "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32",
        "Int64", "UInt64", "Single", "Double", "Decimal", "Char",
        // Date/time types
        "DateTime", "DateTimeOffset", "TimeSpan", "DateOnly", "TimeOnly",
        // Other value types
        "Guid"
    };

    /// <summary>
    /// Returns true if the CLR type needs an explicit cast from its DbDataReader method
    /// due to a sign mismatch (e.g., GetInt32 → uint, GetByte → sbyte).
    /// </summary>
    public static bool NeedsSignCast(string clrType)
        => clrType is "uint" or "UInt32" or "System.UInt32"
            or "ushort" or "UInt16" or "System.UInt16"
            or "ulong" or "UInt64" or "System.UInt64"
            or "sbyte" or "SByte" or "System.SByte";

    /// <summary>
    /// Returns true if the type name represents a known value type (including tuples).
    /// Strips nullable suffix before checking.
    /// </summary>
    public static bool IsValueType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;

        var stripped = typeName!.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;

        // Tuples are ValueTuple<> structs
        if (stripped.Length > 0 && stripped[0] == '(')
            return true;

        if (s_valueTypes.Contains(stripped))
            return true;

        // Qualified value types (e.g. System.Int32, System.DateTime)
        if (stripped.Contains('.'))
        {
            var unqualified = stripped.Substring(stripped.LastIndexOf('.') + 1);
            if (s_valueTypes.Contains(unqualified))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the type name represents a reference type.
    /// Unknown types default to reference-type semantics (safer for nullable annotations).
    /// </summary>
    public static bool IsReferenceType(string typeName)
    {
        var baseName = typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;

        if (baseName.StartsWith("Nullable<") || baseName.StartsWith("System.Nullable<"))
            return false;

        if (baseName.EndsWith("[]"))
            return true;

        if (s_valueTypes.Contains(baseName))
            return false;

        // Tuples are value types
        if (baseName.Length > 0 && baseName[0] == '(')
            return false;

        // Qualified value types (e.g. System.Int32)
        if (baseName.Contains('.'))
        {
            var unqualified = baseName.Substring(baseName.LastIndexOf('.') + 1);
            if (s_valueTypes.Contains(unqualified))
                return false;
        }

        // Unknown types default to reference type (safer for nullable annotations)
        return true;
    }

    /// <summary>
    /// Returns true if the type name is a non-nullable value type.
    /// </summary>
    public static bool IsNonNullableValueType(string typeName)
    {
        if (typeName.EndsWith("?"))
            return false;

        if (s_valueTypes.Contains(typeName))
            return true;

        // Tuples are value types
        if (typeName.Length > 0 && typeName[0] == '(')
            return true;

        // Check unqualified name (e.g., "System.DateTime" → "DateTime")
        var dotIndex = typeName.LastIndexOf('.');
        if (dotIndex >= 0 && s_valueTypes.Contains(typeName.Substring(dotIndex + 1)))
            return true;

        return false;
    }

    /// <summary>
    /// Gets the DbDataReader method name for a CLR type string.
    /// Strips nullable suffix before resolving.
    /// </summary>
    public static string GetReaderMethod(string clrType)
    {
        var baseType = clrType.TrimEnd('?');
        return baseType switch
        {
            "bool" or "Boolean" or "System.Boolean" => "GetBoolean",
            "byte" or "Byte" or "System.Byte" => "GetByte",
            "sbyte" or "SByte" or "System.SByte" => "GetByte",
            "short" or "Int16" or "System.Int16" => "GetInt16",
            "ushort" or "UInt16" or "System.UInt16" => "GetInt16",
            "int" or "Int32" or "System.Int32" => "GetInt32",
            "uint" or "UInt32" or "System.UInt32" => "GetInt32",
            "long" or "Int64" or "System.Int64" => "GetInt64",
            "ulong" or "UInt64" or "System.UInt64" => "GetInt64",
            "float" or "Single" or "System.Single" => "GetFloat",
            "double" or "Double" or "System.Double" => "GetDouble",
            "decimal" or "Decimal" or "System.Decimal" => "GetDecimal",
            "string" or "String" or "System.String" => "GetString",
            "char" or "Char" or "System.Char" => "GetChar",
            "Guid" or "System.Guid" => "GetGuid",
            "DateTime" or "System.DateTime" => "GetDateTime",
            "DateTimeOffset" or "System.DateTimeOffset" => "GetFieldValue<DateTimeOffset>",
            "TimeSpan" or "System.TimeSpan" => "GetFieldValue<TimeSpan>",
            "DateOnly" or "System.DateOnly" => "GetFieldValue<DateOnly>",
            "TimeOnly" or "System.TimeOnly" => "GetFieldValue<TimeOnly>",
            _ => "GetValue"
        };
    }

    /// <summary>
    /// Checks if a type name is unresolved. Treats "object" as unresolved because
    /// the semantic model uses "object" for error types on generated entities.
    /// Use this in chain analysis and pipeline orchestration.
    /// </summary>
    public static bool IsUnresolvedTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return true;

        if (typeName == "?" || typeName == "object")
            return true;

        // "? " (question mark followed by space) means unresolved type with trailing info
        if (typeName!.StartsWith("? "))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a type name is unresolved, but treats "object" as a valid resolved type.
    /// Use this in projection analysis where "object" is a legitimate fallback type
    /// that will be enriched later by chain-level analysis.
    /// </summary>
    public static bool IsUnresolvedTypeNameLenient(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return true;

        if (typeName == "?")
            return true;

        if (typeName!.StartsWith("? "))
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether a result type name is unresolved and needs patching.
    /// Handles both simple types and tuple types with unresolved elements.
    /// A null input returns false (null means "no result type", which is valid for entity-only queries).
    /// </summary>
    public static bool IsUnresolvedResultType(string? resultTypeName)
    {
        if (resultTypeName == null)
            return false;
        if (resultTypeName.Length == 0 || resultTypeName == "?" || resultTypeName == "object")
            return true;

        // Tuple types with unresolved elements
        if (resultTypeName.StartsWith("(") && resultTypeName.EndsWith(")"))
        {
            var inner = resultTypeName.Substring(1, resultTypeName.Length - 2);

            // If the inner string starts with a space, the first element's type is empty
            if (inner.Length > 0 && inner[0] == ' ')
                return true;

            foreach (var element in SplitTupleElements(inner))
            {
                var trimmed = element.Trim();
                if (trimmed.Length == 0)
                    return true;

                // Extract the type part, handling nested tuples that contain spaces.
                // For nested tuples like "(object, int)", find the matching close paren
                // before looking for a name suffix.
                string typePart;
                if (trimmed.StartsWith("("))
                {
                    int closeIdx = FindMatchingCloseParen(trimmed, 0);
                    typePart = closeIdx >= 0 ? trimmed.Substring(0, closeIdx + 1) : trimmed;
                }
                else
                {
                    // Named tuple element: "type name" format. Check the type part.
                    var spaceIdx = trimmed.LastIndexOf(' ');
                    if (spaceIdx >= 0)
                    {
                        typePart = trimmed.Substring(0, spaceIdx).Trim();
                        if (typePart.Length == 0 || typePart == "object" || typePart == "?")
                            return true;
                    }
                    else
                    {
                        typePart = trimmed;
                        if (typePart == "object" || typePart == "?")
                            return true;
                    }
                }

                // Recursively check nested tuples
                if (typePart.StartsWith("(") && IsUnresolvedResultType(typePart))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the index of the closing parenthesis matching the opening one at <paramref name="startIdx"/>.
    /// </summary>
    private static int FindMatchingCloseParen(string s, int startIdx)
    {
        int depth = 0;
        for (int i = startIdx; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    /// <summary>
    /// Splits tuple element strings at top-level commas only, respecting nested parentheses.
    /// </summary>
    private static List<string> SplitTupleElements(string inner)
    {
        var elements = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            var ch = inner[i];
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
            else if (ch == ',' && depth == 0)
            {
                elements.Add(inner.Substring(start, i - start));
                start = i + 1;
            }
        }

        elements.Add(inner.Substring(start));
        return elements;
    }

    /// <summary>
    /// Builds a tuple type name from projected columns.
    /// </summary>
    /// <param name="columns">The projected columns to build the tuple from.</param>
    /// <param name="fallbackToObject">If true, uses "object" for unresolved types. If false, returns "" on any unresolved type.</param>
    public static string BuildTupleTypeName(IReadOnlyList<ProjectedColumn> columns, bool fallbackToObject = true)
    {
        var parts = new List<string>();
        foreach (var col in columns)
        {
            var typeName = col.ClrType;
            // Use lenient check — this method handles the "object" fallback
            // explicitly via the fallbackToObject parameter.
            if (IsUnresolvedTypeNameLenient(typeName))
                typeName = col.FullClrType;

            if (IsUnresolvedTypeNameLenient(typeName))
            {
                if (!fallbackToObject)
                    return "";
                typeName = "object";
            }

            if (col.IsNullable && !typeName!.EndsWith("?"))
                typeName += "?";

            // Omit default ItemN names — they cause CS9154 warnings
            var isDefaultName = col.PropertyName.StartsWith("Item") &&
                int.TryParse(col.PropertyName.Substring(4), out var idx) &&
                idx == col.Ordinal + 1;

            parts.Add(isDefaultName ? typeName! : $"{typeName} {col.PropertyName}");
        }

        return $"({string.Join(", ", parts)})";
    }
}
