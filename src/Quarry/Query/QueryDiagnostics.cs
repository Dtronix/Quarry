using System.Collections.Generic;
using System.Linq;

namespace Quarry;

/// <summary>
/// Contains compile-time analysis results, SQL output, bound parameters,
/// and optimization metadata for a query chain.
/// </summary>
public sealed class QueryDiagnostics
{
    internal QueryDiagnostics(
        string sql,
        IReadOnlyList<DiagnosticParameter> parameters,
        DiagnosticQueryKind kind,
        SqlDialect dialect,
        string tableName,
        DiagnosticOptimizationTier tier = DiagnosticOptimizationTier.RuntimeBuild,
        bool isCarrierOptimized = false,
        IReadOnlyList<ClauseDiagnostic>? clauses = null,
        object? rawState = null,
        int insertRowCount = 0,
        // New parameters
        string? tierReason = null,
        string? disqualifyReason = null,
        ulong activeMask = 0,
        int conditionalBitCount = 0,
        IReadOnlyDictionary<ulong, SqlVariantDiagnostic>? sqlVariants = null,
        IReadOnlyList<DiagnosticParameter>? allParameters = null,
        IReadOnlyList<ProjectionColumnDiagnostic>? projectionColumns = null,
        string? projectionKind = null,
        string? projectionNonOptimalReason = null,
        string? carrierClassName = null,
        string? carrierIneligibleReason = null,
        string? schemaName = null,
        IReadOnlyList<JoinDiagnostic>? joins = null,
        bool isDistinct = false,
        int? limit = null,
        int? offset = null,
        string? identityColumnName = null,
        IReadOnlyList<string>? unmatchedMethodNames = null)
    {
        Sql = sql;
        Kind = kind;
        Dialect = dialect;
        TableName = tableName;
        Tier = tier;
        IsCarrierOptimized = isCarrierOptimized;
        Clauses = clauses ?? [];
        RawState = rawState;
        InsertRowCount = insertRowCount;

        // New properties
        TierReason = tierReason;
        DisqualifyReason = disqualifyReason;
        ActiveMask = activeMask;
        ConditionalBitCount = conditionalBitCount;
        SqlVariants = sqlVariants;
        AllParameters = allParameters ?? parameters;
        ProjectionColumns = projectionColumns;
        ProjectionKind = projectionKind;
        ProjectionNonOptimalReason = projectionNonOptimalReason;
        CarrierClassName = carrierClassName;
        CarrierIneligibleReason = carrierIneligibleReason;
        SchemaName = schemaName;
        Joins = joins;
        IsDistinct = isDistinct;
        Limit = limit;
        Offset = offset;
        IdentityColumnName = identityColumnName;
        UnmatchedMethodNames = unmatchedMethodNames;

        // When clauses carry per-clause parameters, derive the top-level list
        // from active clauses only — inactive conditional clause params are excluded.
        Parameters = Clauses.Count > 0 && Clauses.Any(c => c.Parameters.Count > 0)
            ? Clauses.Where(c => c.IsActive).SelectMany(c => c.Parameters).ToList()
            : parameters;
    }

    // ── Existing properties ──

    /// <summary>Gets the generated SQL string.</summary>
    public string Sql { get; }

    /// <summary>Gets the active parameters only (filtered by mask for conditional chains).</summary>
    public IReadOnlyList<DiagnosticParameter> Parameters { get; }

    /// <summary>Gets the optimization tier applied to this query chain.</summary>
    public DiagnosticOptimizationTier Tier { get; }

    /// <summary>Gets whether this chain was optimized using a generated carrier class.</summary>
    public bool IsCarrierOptimized { get; }

    /// <summary>Gets the kind of query (Select, Delete, Update, Insert).</summary>
    public DiagnosticQueryKind Kind { get; }

    /// <summary>Gets the SQL dialect used for this query.</summary>
    public SqlDialect Dialect { get; }

    /// <summary>Gets the primary table name for this query.</summary>
    public string TableName { get; }

    /// <summary>Gets the clause breakdown for this query.</summary>
    public IReadOnlyList<ClauseDiagnostic> Clauses { get; }

    // ── New properties ──

    /// <summary>Human-readable explanation of the optimization tier classification.</summary>
    public string? TierReason { get; }

    /// <summary>Why the chain is RuntimeBuild (null for PrebuiltDispatch).</summary>
    public string? DisqualifyReason { get; }

    /// <summary>The runtime mask value read from the carrier. Zero for unconditional chains.</summary>
    public ulong ActiveMask { get; }

    /// <summary>Number of conditional bits in the clause mask.</summary>
    public int ConditionalBitCount { get; }

    /// <summary>Complete map of all possible mask values to their SQL strings and parameter counts. Null for RuntimeBuild chains.</summary>
    public IReadOnlyDictionary<ulong, SqlVariantDiagnostic>? SqlVariants { get; }

    /// <summary>Every parameter in the chain regardless of mask state, with full metadata.</summary>
    public IReadOnlyList<DiagnosticParameter> AllParameters { get; }

    /// <summary>Projection column metadata for SELECT queries.</summary>
    public IReadOnlyList<ProjectionColumnDiagnostic>? ProjectionColumns { get; }

    /// <summary>Projection kind (Entity, Dto, Tuple, SingleColumn, etc.).</summary>
    public string? ProjectionKind { get; }

    /// <summary>Why the projection is not optimal (null if optimal).</summary>
    public string? ProjectionNonOptimalReason { get; }

    /// <summary>Generated carrier class name (non-null for all PrebuiltDispatch chains).</summary>
    public string? CarrierClassName { get; }

    /// <summary>Why carrier optimization was not used (null when carrier-optimized).</summary>
    public string? CarrierIneligibleReason { get; }

    /// <summary>Database schema name (null if default schema).</summary>
    public string? SchemaName { get; }

    /// <summary>Join metadata for multi-entity chains.</summary>
    public IReadOnlyList<JoinDiagnostic>? Joins { get; }

    /// <summary>Whether DISTINCT is applied.</summary>
    public bool IsDistinct { get; }

    /// <summary>LIMIT value (literal or parameter-sourced).</summary>
    public int? Limit { get; }

    /// <summary>OFFSET value (literal or parameter-sourced).</summary>
    public int? Offset { get; }

    /// <summary>Identity column name for INSERT chains with identity columns.</summary>
    public string? IdentityColumnName { get; }

    /// <summary>Method names in the chain that could not be matched to known operations.</summary>
    public IReadOnlyList<string>? UnmatchedMethodNames { get; }

    // ── Internal properties ──

    internal object? RawState { get; }
    internal int InsertRowCount { get; }
}

/// <summary>
/// Represents a bound parameter in a diagnostic query result.
/// </summary>
public sealed class DiagnosticParameter
{
    public DiagnosticParameter(string name, object? value,
        string? typeName = null, string? typeMappingClass = null,
        bool isSensitive = false, bool isEnum = false, bool isCollection = false,
        bool isConditional = false, int? conditionalBitIndex = null)
    {
        Name = name;
        Value = value;
        TypeName = typeName;
        TypeMappingClass = typeMappingClass;
        IsSensitive = isSensitive;
        IsEnum = isEnum;
        IsCollection = isCollection;
        IsConditional = isConditional;
        ConditionalBitIndex = conditionalBitIndex;
    }

    /// <summary>Gets the parameter placeholder name (e.g., "@p0").</summary>
    public string Name { get; }

    /// <summary>Gets the parameter value.</summary>
    public object? Value { get; }

    /// <summary>CLR type name of the parameter.</summary>
    public string? TypeName { get; }

    /// <summary>Custom type mapping class (null if none).</summary>
    public string? TypeMappingClass { get; }

    /// <summary>Whether this parameter contains sensitive data.</summary>
    public bool IsSensitive { get; }

    /// <summary>Whether this parameter is an enum type.</summary>
    public bool IsEnum { get; }

    /// <summary>Whether this parameter is a collection (IN clause).</summary>
    public bool IsCollection { get; }

    /// <summary>Whether this parameter belongs to a conditional clause.</summary>
    public bool IsConditional { get; }

    /// <summary>Bit index of the owning conditional term (null if non-conditional).</summary>
    public int? ConditionalBitIndex { get; }
}

/// <summary>
/// Describes a single clause in a diagnostic query result.
/// </summary>
public sealed class ClauseDiagnostic
{
    public ClauseDiagnostic(string clauseType, string sqlFragment, bool isConditional = false, bool isActive = true,
        IReadOnlyList<DiagnosticParameter>? parameters = null,
        ClauseSourceLocation? sourceLocation = null,
        int? conditionalBitIndex = null, DiagnosticBranchKind? branchKind = null)
    {
        ClauseType = clauseType;
        SqlFragment = sqlFragment;
        IsConditional = isConditional;
        IsActive = isActive;
        Parameters = parameters ?? [];
        SourceLocation = sourceLocation;
        ConditionalBitIndex = conditionalBitIndex;
        BranchKind = branchKind;
    }

    /// <summary>Gets the clause type (e.g., "Where", "OrderBy", "Select", "Limit").</summary>
    public string ClauseType { get; }

    /// <summary>Gets the translated SQL fragment for this clause.</summary>
    public string SqlFragment { get; }

    /// <summary>Gets whether this clause is conditionally applied.</summary>
    public bool IsConditional { get; }

    /// <summary>Gets whether this clause is active (included in the current query variant).</summary>
    public bool IsActive { get; }

    /// <summary>Gets the bound parameters owned by this clause.</summary>
    public IReadOnlyList<DiagnosticParameter> Parameters { get; }

    /// <summary>Source location (file, line, column) where this clause was defined.</summary>
    public ClauseSourceLocation? SourceLocation { get; }

    /// <summary>Bit index for conditional clauses (null for non-conditional).</summary>
    public int? ConditionalBitIndex { get; }

    /// <summary>Branch kind for conditional clauses (Independent or MutuallyExclusive).</summary>
    public DiagnosticBranchKind? BranchKind { get; }
}

/// <summary>Represents a single SQL variant keyed by mask value.</summary>
public sealed class SqlVariantDiagnostic
{
    public SqlVariantDiagnostic(string sql, int parameterCount)
    {
        Sql = sql;
        ParameterCount = parameterCount;
    }

    /// <summary>The SQL string for this variant.</summary>
    public string Sql { get; }

    /// <summary>Number of parameters in this variant.</summary>
    public int ParameterCount { get; }
}

/// <summary>Describes a column in a SELECT projection.</summary>
public sealed class ProjectionColumnDiagnostic
{
    public ProjectionColumnDiagnostic(string propertyName, string columnName, string clrType, int ordinal,
        bool isNullable = false, string? typeMappingClass = null,
        bool isForeignKey = false, string? foreignKeyEntityName = null, bool isEnum = false)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
        ClrType = clrType;
        Ordinal = ordinal;
        IsNullable = isNullable;
        TypeMappingClass = typeMappingClass;
        IsForeignKey = isForeignKey;
        ForeignKeyEntityName = foreignKeyEntityName;
        IsEnum = isEnum;
    }

    public string PropertyName { get; }
    public string ColumnName { get; }
    public string ClrType { get; }
    public int Ordinal { get; }
    public bool IsNullable { get; }
    public string? TypeMappingClass { get; }
    public bool IsForeignKey { get; }
    public string? ForeignKeyEntityName { get; }
    public bool IsEnum { get; }
}

/// <summary>Describes a JOIN operation in the query chain.</summary>
public sealed class JoinDiagnostic
{
    public JoinDiagnostic(string tableName, string? schemaName, string joinKind, string alias, string onConditionSql)
    {
        TableName = tableName;
        SchemaName = schemaName;
        JoinKind = joinKind;
        Alias = alias;
        OnConditionSql = onConditionSql;
    }

    public string TableName { get; }
    public string? SchemaName { get; }
    public string JoinKind { get; }
    public string Alias { get; }
    public string OnConditionSql { get; }
}

/// <summary>Source location where a clause was defined.</summary>
public sealed class ClauseSourceLocation
{
    public ClauseSourceLocation(string filePath, int line, int column)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
    }

    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
}

/// <summary>Branch kind for conditional clauses.</summary>
public enum DiagnosticBranchKind
{
    /// <summary>Independent conditional — consumes 1 bit, if/no-else.</summary>
    Independent,
    /// <summary>Mutually exclusive — if/else both assign, consumes 1 bit for two clauses.</summary>
    MutuallyExclusive
}

/// <summary>The optimization tier applied to a query chain at compile time.</summary>
public enum DiagnosticOptimizationTier
{
    /// <summary>SQL is built at runtime by the query builder (no compile-time optimization).</summary>
    RuntimeBuild,
    /// <summary>SQL is pre-built at compile time with a dispatch table for conditional clauses.</summary>
    PrebuiltDispatch
}

/// <summary>The kind of query for diagnostic purposes.</summary>
public enum DiagnosticQueryKind
{
    /// <summary>SELECT query.</summary>
    Select,
    /// <summary>DELETE query.</summary>
    Delete,
    /// <summary>UPDATE query.</summary>
    Update,
    /// <summary>INSERT query.</summary>
    Insert
}
