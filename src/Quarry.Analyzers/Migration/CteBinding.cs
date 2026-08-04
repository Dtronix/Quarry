using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql.Parser;

namespace Quarry.Analyzers.Migration;

/// <summary>
/// One column exposed by a projected CTE: the source entity property it reads from and the
/// property name the synthesized DTO exposes it under.
/// </summary>
internal sealed class CteProjection
{
    /// <summary>The property name on the CTE body's source entity (e.g. "UserId").</summary>
    public string SourcePropertyName { get; }

    /// <summary>The property name on the synthesized DTO.</summary>
    public string DtoPropertyName { get; }

    /// <summary>The source column, used for its CLR type when emitting the DTO.</summary>
    public ColumnInfo SourceColumn { get; }

    public CteProjection(string sourcePropertyName, string dtoPropertyName, ColumnInfo sourceColumn)
    {
        SourcePropertyName = sourcePropertyName;
        DtoPropertyName = dtoPropertyName;
        SourceColumn = sourceColumn;
    }
}

/// <summary>
/// Binds one SQL common table expression to the Quarry chain constructs that reproduce it.
/// </summary>
/// <remarks>
/// Quarry names a CTE after the C# type passed to <c>With&lt;T&gt;</c>, so the SQL CTE name is
/// only preserved in spirit: the emitted <c>WITH</c> name becomes <see cref="DtoTypeName"/>.
/// That is safe because the outer <c>FROM</c> reference changes in lockstep.
/// </remarks>
internal sealed class CteBinding
{
    /// <summary>The CTE name as written in the SQL.</summary>
    public string CteName { get; }

    /// <summary>The type name used for <c>With&lt;T&gt;</c> and <c>FromCte&lt;T&gt;</c>.</summary>
    public string DtoTypeName { get; }

    /// <summary>The entity the CTE body selects from.</summary>
    public EntityInfo SourceEntity { get; }

    /// <summary>The mapping for <see cref="SourceEntity"/>, for its accessor property name.</summary>
    public EntityMapping SourceMapping { get; }

    /// <summary>
    /// True when the body is <c>SELECT *</c>, which maps onto <c>With&lt;TEntity&gt;</c> and needs
    /// no synthesized type.
    /// </summary>
    public bool IsWholeEntity { get; }

    /// <summary>The projected columns. Empty when <see cref="IsWholeEntity"/> is true.</summary>
    public IReadOnlyList<CteProjection> Projections { get; }

    /// <summary>
    /// The entity the outer query resolves column references against. For a whole-entity CTE
    /// this is <see cref="SourceEntity"/>; for a projected CTE it is a synthetic entity whose
    /// column names are those the CTE exposes.
    /// </summary>
    public EntityInfo ExposedEntity { get; }

    /// <summary>The parsed CTE body.</summary>
    public SqlSelectStatement Body { get; }

    public CteBinding(
        string cteName,
        string dtoTypeName,
        EntityInfo sourceEntity,
        EntityMapping sourceMapping,
        bool isWholeEntity,
        IReadOnlyList<CteProjection> projections,
        EntityInfo exposedEntity,
        SqlSelectStatement body)
    {
        CteName = cteName;
        DtoTypeName = dtoTypeName;
        SourceEntity = sourceEntity;
        SourceMapping = sourceMapping;
        IsWholeEntity = isWholeEntity;
        Projections = projections;
        ExposedEntity = exposedEntity;
        Body = body;
    }
}
