using Microsoft.CodeAnalysis;

namespace Quarry.Analyzers;

/// <summary>
/// Diagnostic descriptors for Quarry query analysis rules (QRA series).
/// </summary>
internal static class AnalyzerDiagnosticDescriptors
{
    private const string Category = "QuarryAnalyzer";

    // ── QRA1xx: Simplification ──

    public static readonly DiagnosticDescriptor CountComparedToZero = new(
        id: "QRA101",
        title: "Count compared to zero",
        messageFormat: "Consider using Any() instead of Count() {0} 0",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Count() compared to zero can be replaced with Any() for clarity and potential performance improvement.");

    public static readonly DiagnosticDescriptor SingleValueIn = new(
        id: "QRA102",
        title: "Single-value IN clause",
        messageFormat: "IN clause with single value can be simplified to '== {0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An IN clause containing a single value is equivalent to an equality comparison.");

    public static readonly DiagnosticDescriptor TautologicalCondition = new(
        id: "QRA103",
        title: "Tautological condition",
        messageFormat: "Redundant tautological condition detected",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The WHERE clause contains a condition that is always true, such as 1 = 1 or x = x.");

    public static readonly DiagnosticDescriptor ContradictoryCondition = new(
        id: "QRA104",
        title: "Contradictory condition",
        messageFormat: "Contradictory WHERE clause: condition is always false",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The WHERE clause contains conflicting conditions that can never be satisfied simultaneously.");

    public static readonly DiagnosticDescriptor RedundantCondition = new(
        id: "QRA105",
        title: "Redundant condition",
        messageFormat: "Redundant condition: '{0}' is subsumed by '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A WHERE condition is already implied by another stronger condition on the same column.");

    public static readonly DiagnosticDescriptor NullableWithoutNullCheck = new(
        id: "QRA106",
        title: "Nullable column without null check",
        messageFormat: "Column '{0}' is nullable but compared with '==' without null handling",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A nullable column is compared using equality without handling the null case.");

    // ── QRA2xx: Wasteful Patterns ──

    public static readonly DiagnosticDescriptor UnusedJoin = new(
        id: "QRA201",
        title: "Unused join",
        messageFormat: "Joined table '{0}' is not referenced in SELECT, WHERE, or ORDER BY",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A joined table is not used in any subsequent clause, adding unnecessary overhead.");

    public static readonly DiagnosticDescriptor WideTableSelect = new(
        id: "QRA202",
        title: "Wide table SELECT *",
        messageFormat: "SELECT * on table '{0}' with {1} columns; consider a DTO projection",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Selecting all columns from a wide table may transfer unnecessary data. Consider projecting only needed columns.");

    public static readonly DiagnosticDescriptor OrderByWithoutLimit = new(
        id: "QRA203",
        title: "ORDER BY without LIMIT",
        messageFormat: "ORDER BY without LIMIT/OFFSET; consider adding pagination",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An ORDER BY clause without LIMIT or OFFSET may sort the entire result set unnecessarily.");

    public static readonly DiagnosticDescriptor DuplicateProjectionColumn = new(
        id: "QRA204",
        title: "Duplicate projection column",
        messageFormat: "Column '{0}' is projected multiple times",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The same column appears multiple times in the SELECT projection.");

    public static readonly DiagnosticDescriptor CartesianProduct = new(
        id: "QRA205",
        title: "Cartesian product",
        messageFormat: "JOIN without ON condition may produce a Cartesian product",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A JOIN without a meaningful ON condition produces a Cartesian product of the two tables.");

    // ── QRA3xx: Performance / Index ──

    public static readonly DiagnosticDescriptor LeadingWildcardLike = new(
        id: "QRA301",
        title: "Leading wildcard LIKE",
        messageFormat: "Contains() translates to LIKE '%...%' which cannot use an index",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "String.Contains() translates to a LIKE pattern with a leading wildcard, preventing index usage.");

    public static readonly DiagnosticDescriptor FunctionOnColumnInWhere = new(
        id: "QRA302",
        title: "Function on column in WHERE",
        messageFormat: "Function '{0}' on column '{1}' in WHERE prevents index usage",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Applying a function to a column in a WHERE clause prevents the database from using an index on that column.");

    public static readonly DiagnosticDescriptor OrOnDifferentColumns = new(
        id: "QRA303",
        title: "OR across different columns",
        messageFormat: "OR across different columns may prevent index usage; consider UNION",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "OR conditions spanning different columns typically cannot be served by a single index.");

    public static readonly DiagnosticDescriptor WhereOnNonIndexedColumn = new(
        id: "QRA304",
        title: "WHERE on non-indexed column",
        messageFormat: "WHERE on column '{0}' which is not covered by any declared index",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Filtering on a column without an index may result in a full table scan.");

    // ── QRA4xx: Patterns ──

    public static readonly DiagnosticDescriptor QueryInsideLoop = new(
        id: "QRA401",
        title: "Query inside loop",
        messageFormat: "Quarry query call site inside a loop; potential N+1 problem",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Executing a database query inside a loop can cause N+1 query problems.");

    public static readonly DiagnosticDescriptor MultipleQueriesSameTable = new(
        id: "QRA402",
        title: "Multiple queries on same table",
        messageFormat: "Multiple independent queries on '{0}'; consider combining",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Multiple independent queries on the same table within a method could potentially be combined.");

    // ── QRA5xx: Dialect-Specific ──

    public static readonly DiagnosticDescriptor DialectOptimization = new(
        id: "QRA501",
        title: "Dialect-specific optimization",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A dialect-specific optimization is available for this query pattern.");

    public static readonly DiagnosticDescriptor SuboptimalForDialect = new(
        id: "QRA502",
        title: "Suboptimal for dialect",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "This query uses a feature that is suboptimal or unsupported for the target SQL dialect.");
}
