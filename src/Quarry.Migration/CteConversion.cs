using System;
using System.Collections.Generic;
using System.Text;
using Quarry.Shared.Sql.Parser;

namespace Quarry.Migration;

/// <summary>
/// One column exposed by a projected CTE.
/// </summary>
internal sealed class CteProjection
{
    /// <summary>The property name on the CTE body's source entity.</summary>
    public string SourcePropertyName { get; }

    /// <summary>The property name the synthesized DTO exposes it under.</summary>
    public string DtoPropertyName { get; }

    /// <summary>CLR type name for the synthesized DTO property.</summary>
    public string ClrTypeName { get; }

    public CteProjection(string sourcePropertyName, string dtoPropertyName, string clrTypeName)
    {
        SourcePropertyName = sourcePropertyName;
        DtoPropertyName = dtoPropertyName;
        ClrTypeName = clrTypeName;
    }
}

/// <summary>
/// Binds one SQL common table expression to the chain constructs that reproduce it.
/// </summary>
/// <remarks>
/// Quarry names a CTE after the C# type passed to <c>With&lt;T&gt;</c>, so the emitted WITH name
/// becomes <see cref="DtoTypeName"/> rather than the SQL CTE name. That is safe because the
/// outer FROM reference changes with it.
/// </remarks>
internal sealed class CteBinding
{
    /// <summary>The CTE name as written in the SQL.</summary>
    public string CteName { get; }

    /// <summary>The type used for <c>With&lt;T&gt;</c> and <c>FromCte&lt;T&gt;</c>.</summary>
    public string DtoTypeName { get; }

    /// <summary>The entity the CTE body reads from.</summary>
    public EntityMapping SourceEntity { get; }

    /// <summary>True when the body is <c>SELECT *</c>, needing no synthesized type.</summary>
    public bool IsWholeEntity { get; }

    /// <summary>Projected columns; empty when <see cref="IsWholeEntity"/>.</summary>
    public IReadOnlyList<CteProjection> Projections { get; }

    /// <summary>
    /// Column name (as the CTE exposes it) → property name on the type the outer query sees.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExposedColumns { get; }

    /// <summary>The parsed CTE body.</summary>
    public SqlSelectStatement Body { get; }

    public CteBinding(
        string cteName,
        string dtoTypeName,
        EntityMapping sourceEntity,
        bool isWholeEntity,
        IReadOnlyList<CteProjection> projections,
        IReadOnlyDictionary<string, string> exposedColumns,
        SqlSelectStatement body)
    {
        CteName = cteName;
        DtoTypeName = dtoTypeName;
        SourceEntity = sourceEntity;
        IsWholeEntity = isWholeEntity;
        Projections = projections;
        ExposedColumns = exposedColumns;
        Body = body;
    }

    /// <summary>
    /// Renders the synthesized DTO class, or null when the CTE reuses an entity type.
    /// </summary>
    public string? BuildDtoDeclaration()
    {
        if (IsWholeEntity) return null;

        var sb = new StringBuilder();
        sb.Append("public class ").Append(DtoTypeName).Append('\n').Append("{\n");
        foreach (var p in Projections)
            sb.Append("    public ").Append(p.ClrTypeName).Append(' ').Append(p.DtoPropertyName).Append(" { get; set; }\n");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Converts a SQL identifier (snake_case, kebab, spaces) to a PascalCase C# name.
    /// </summary>
    public static string ToPascalCase(string name)
    {
        var sb = new StringBuilder(name.Length);
        var upperNext = true;

        foreach (var ch in name)
        {
            if (ch == '_' || ch == '-' || ch == ' ')
            {
                upperNext = true;
                continue;
            }

            if (sb.Length == 0 && !char.IsLetter(ch))
                continue; // a C# identifier cannot start with a digit

            sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }

        return sb.ToString();
    }
}
