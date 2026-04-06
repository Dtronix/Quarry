using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.IR;

/// <summary>
/// A Common Table Expression definition within a query plan.
/// Contains the CTE name (derived from the DTO class name), the fully rendered
/// inner SQL, inner query parameters, and column metadata from the DTO's public properties.
/// </summary>
internal sealed class CteDef : IEquatable<CteDef>
{
    public CteDef(
        string name,
        string innerSql,
        IReadOnlyList<QueryParameter> innerParameters,
        IReadOnlyList<CteColumn> columns,
        QueryPlan? innerPlan = null,
        int parameterOffset = 0)
    {
        Name = name;
        InnerSql = innerSql;
        InnerParameters = innerParameters;
        Columns = columns;
        InnerPlan = innerPlan;
        ParameterOffset = parameterOffset;
    }

    /// <summary>
    /// The CTE name, derived from the DTO class name (e.g., "OrderCountDto").
    /// Used as the identifier in WITH "name" AS (...) and in JOIN/FROM references.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The fully assembled SQL of the inner query (e.g., SELECT "UserId", COUNT(*) FROM "orders" GROUP BY "UserId").
    /// </summary>
    public string InnerSql { get; }

    /// <summary>
    /// Parameters from the inner query. These are merged into the outer query's
    /// global parameter list and must appear before the outer query's own parameters
    /// (since CTE SQL renders first).
    /// </summary>
    public IReadOnlyList<QueryParameter> InnerParameters { get; }

    /// <summary>
    /// Column metadata derived from the DTO class's public properties.
    /// Used for binding column references in the outer query when joining to or selecting from this CTE.
    /// </summary>
    public IReadOnlyList<CteColumn> Columns { get; }

    /// <summary>
    /// The inner chain's <see cref="QueryPlan"/>. Used at code generation time to look up
    /// the inner chain's carrier class name (via the same QueryPlan reference) so that
    /// CTE inner parameter values can be copied from the inner carrier into the outer carrier
    /// at runtime. Null only if the inner chain failed analysis.
    /// </summary>
    public QueryPlan? InnerPlan { get; }

    /// <summary>
    /// The starting index in the outer carrier's parameter slots where this CTE's inner
    /// parameters live. The CteDefinition interceptor body must copy
    /// <c>__inner.P0..P(InnerParameters.Count-1)</c> into
    /// <c>__outer.P{ParameterOffset}..P{ParameterOffset + InnerParameters.Count - 1}</c>.
    /// </summary>
    public int ParameterOffset { get; }

    public bool Equals(CteDef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && InnerSql == other.InnerSql
            && ParameterOffset == other.ParameterOffset
            && EqualityHelpers.SequenceEqual(InnerParameters, other.InnerParameters)
            && EqualityHelpers.SequenceEqual(Columns, other.Columns);
    }

    public override bool Equals(object? obj) => Equals(obj as CteDef);

    public override int GetHashCode() => HashCode.Combine(Name, InnerSql, Columns.Count, ParameterOffset);
}

/// <summary>
/// A column in a CTE definition, derived from a DTO class's public property.
/// </summary>
internal sealed class CteColumn : IEquatable<CteColumn>
{
    public CteColumn(string propertyName, string columnName, string clrType)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
        ClrType = clrType;
    }

    /// <summary>The C# property name on the DTO class.</summary>
    public string PropertyName { get; }

    /// <summary>The SQL column name (same as property name for DTOs).</summary>
    public string ColumnName { get; }

    /// <summary>The C# type name (e.g., "int", "string", "decimal").</summary>
    public string ClrType { get; }

    public bool Equals(CteColumn? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && ColumnName == other.ColumnName
            && ClrType == other.ClrType;
    }

    public override bool Equals(object? obj) => Equals(obj as CteColumn);

    public override int GetHashCode() => HashCode.Combine(PropertyName, ColumnName, ClrType);
}
