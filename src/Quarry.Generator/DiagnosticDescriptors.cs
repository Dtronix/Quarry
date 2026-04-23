using Microsoft.CodeAnalysis;

namespace Quarry.Generators;

/// <summary>
/// Diagnostic descriptors for Quarry analyzer and generator diagnostics.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Quarry";

    /// <summary>
    /// QRY001: Query not fully analyzable.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor QueryNotAnalyzable = new(
        id: "QRY001",
        title: "Query not fully analyzable",
        messageFormat: "Query is not fully analyzable: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The query chain contains patterns that prevent compile-time analysis. " +
                     "The original runtime method will be used instead. " +
                     "Consider restructuring the query as a fluent chain without variable assignment or conditionals.");

    /// <summary>
    /// QRY002: Schema class missing required Table property.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor MissingTableProperty = new(
        id: "QRY002",
        title: "Missing Table property",
        messageFormat: "Schema class '{0}' is missing required static 'Table' property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Schema classes must define a static 'Table' property that returns the database table name.");

    /// <summary>
    /// QRY003: Invalid column type, no TypeMapping found.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidColumnType = new(
        id: "QRY003",
        title: "Invalid column type",
        messageFormat: "Column '{0}' has type '{1}' which has no registered TypeMapping",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The column type is not a supported primitive type and has no custom TypeMapping registered.");

    /// <summary>
    /// QRY004: Navigation property references unknown schema.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownNavigationSchema = new(
        id: "QRY004",
        title: "Unknown navigation target",
        messageFormat: "Navigation property '{0}' references unknown entity '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The navigation property references an entity type that is not defined in the context.");

    /// <summary>
    /// QRY005: Select projection includes unmapped property.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor UnmappedProjectionProperty = new(
        id: "QRY005",
        title: "Unmapped projection property",
        messageFormat: "Select projection includes property '{0}' which is not mapped to a database column",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The projection includes a property that does not correspond to a database column.");

    /// <summary>
    /// QRY006: Where expression contains unsupported operation.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedWhereOperation = new(
        id: "QRY006",
        title: "Unsupported Where operation",
        messageFormat: "Where expression contains unsupported operation: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The Where clause contains an operation that cannot be translated to SQL.");

    /// <summary>
    /// QRY007: Join references undefined relationship.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor UndefinedJoinRelationship = new(
        id: "QRY007",
        title: "Undefined join relationship",
        messageFormat: "Join references undefined relationship: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The navigation-based join references a relationship that is not defined in the schema.");

    /// <summary>
    /// QRY008: Potential SQL injection in Sql.Raw usage.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor PotentialSqlInjection = new(
        id: "QRY008",
        title: "Potential SQL injection",
        messageFormat: "Sql.Raw() call may be vulnerable to SQL injection if user input is included",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Sql.Raw() bypasses SQL generation and may be vulnerable to SQL injection. " +
                     "Ensure any user input is properly parameterized.");

    /// <summary>
    /// QRY009: GroupBy required when using aggregate without full entity select.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor GroupByRequiredForAggregate = new(
        id: "QRY009",
        title: "GroupBy required for aggregate",
        messageFormat: "Aggregate function '{0}' used without GroupBy for non-aggregate columns",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using aggregate functions with non-aggregate columns in the projection, " +
                     "a GroupBy clause is required.");

    /// <summary>
    /// QRY010: Multiple Key columns defined, composite keys not supported.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor CompositeKeyNotSupported = new(
        id: "QRY010",
        title: "Composite keys not supported",
        messageFormat: "Schema '{0}' has multiple Key columns; composite keys are not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Quarry does not support composite primary keys. Use a single Key<T> column.");

    /// <summary>
    /// QRY011: Select() required before execution.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor SelectRequired = new(
        id: "QRY011",
        title: "Select required before execution",
        messageFormat: "Select() must be called before {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Select() clause is required before executing a query to specify the projection.");

    /// <summary>
    /// QRY012: Update/Delete requires Where() or All().
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor WhereOrAllRequired = new(
        id: "QRY012",
        title: "Where or All required",
        messageFormat: "{0} requires either Where() to filter rows or All() to explicitly affect all rows",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Update and Delete operations require either a Where clause to filter rows, " +
                     "or an explicit All() call to confirm affecting all rows.");

    /// <summary>
    /// QRY013: GUID key requires ClientGenerated() modifier.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor GuidKeyRequiresClientGenerated = new(
        id: "QRY013",
        title: "GUID key requires ClientGenerated",
        messageFormat: "Key<Guid> column '{0}' requires ClientGenerated() modifier",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "GUID primary keys cannot be auto-generated by most databases. " +
                     "Use the ClientGenerated() modifier to indicate the value is generated client-side.");

    /// <summary>
    /// QRY014: Anonymous type projection not supported.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor AnonymousTypeNotSupported = new(
        id: "QRY014",
        title: "Anonymous type projection not supported",
        messageFormat: "Anonymous type projections are not supported. Use a named record, class, or tuple instead: Select(u => new MyDto {{ ... }}) or Select(u => (u.Id, u.Name)).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Quarry requires named types for Select() projections to enable compile-time optimization.");

    /// <summary>
    /// QRY015: Ambiguous context resolution for entity type.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousContextResolution = new(
        id: "QRY015",
        title: "Ambiguous context resolution",
        messageFormat: "Multiple QuarryContext types found for entity '{0}'. Unable to determine dialect from call site. Using '{1}' ({2}). Assign the query builder from a typed context property to resolve.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Multiple QuarryContext subclasses register the same entity type, and the call site " +
                     "could not be resolved to a specific context. The first registered context's dialect " +
                     "will be used. To fix, ensure the query builder is obtained from a typed context property.");

    // ─── Subquery diagnostics (QRY020–QRY024) ─────────────────────────

    /// <summary>
    /// QRY020: .All() called without predicate.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor AllRequiresPredicate = new(
        id: "QRY020",
        title: "All() requires a predicate",
        messageFormat: "All() must be called with a predicate lambda",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The All() method on a navigation collection requires a predicate. " +
                     "Calling All() without a predicate is semantically meaningless.");

    /// <summary>
    /// QRY021: Navigation property's related entity not found in registry.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor SubqueryEntityNotFound = new(
        id: "QRY021",
        title: "Subquery entity not found",
        messageFormat: "Navigation property '{0}' references entity '{1}' which was not found in the context",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The navigation subquery references an entity that is not registered in any QuarryContext.");

    /// <summary>
    /// QRY022: FK property not found on inner entity's columns.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor SubqueryForeignKeyNotFound = new(
        id: "QRY022",
        title: "Subquery FK column not found",
        messageFormat: "Foreign key property '{0}' not found on entity '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The navigation's foreign key property was not found as a column on the related entity.");

    /// <summary>
    /// QRY023: FK-to-PK correlation couldn't be resolved.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor SubqueryCorrelationAmbiguous = new(
        id: "QRY023",
        title: "Subquery correlation ambiguous",
        messageFormat: "Could not determine primary key column on '{0}' for subquery correlation with FK '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The FK-to-PK correlation for the subquery could not be resolved. " +
                     "The outer entity may have multiple primary keys or no matching column.");

    /// <summary>
    /// QRY024: Subquery method called on non-navigation property.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor SubqueryOnNonNavigation = new(
        id: "QRY024",
        title: "Subquery on non-navigation property",
        messageFormat: "Method '{0}' was called on '{1}' which is not a Many<T> navigation property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Subquery methods (Any, All, Count) can only be called on Many<T> navigation properties.");

    /// <summary>
    /// QRY025: Subquery on entity with composite primary key (unsupported).
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor SubqueryCompositePrimaryKey = new(
        id: "QRY025",
        title: "Subquery on composite-PK entity unsupported",
        messageFormat: "Navigation subquery on entity '{0}' is not supported because it has a composite primary key",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Navigation subqueries require a single primary key column for FK-to-PK correlation. " +
                     "Entities with composite primary keys cannot be used as the outer entity in a subquery.");

    /// <summary>
    /// QRY016: Generated SQL contains unbound parameter placeholders.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor UnboundParameterPlaceholder = new(
        id: "QRY016",
        title: "Unbound parameter placeholder in generated SQL",
        messageFormat: "Generated SQL contains parameter placeholder '{0}' that is not bound to a value. The query will fail at runtime.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generated interceptor SQL references a parameter placeholder (@p0, @p1, etc.) " +
                     "that has no corresponding parameter binding. This typically indicates a bug in " +
                     "the expression translator where a value was parameterized but not tracked for binding.");

    /// <summary>
    /// QRY017: TypeMapping TCustom does not match column type.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor TypeMappingMismatch = new(
        id: "QRY017",
        title: "TypeMapping type mismatch",
        messageFormat: "Column '{0}' has type '{1}' but TypeMapping '{2}' expects '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The column's declared type does not match the TCustom type parameter of the TypeMapping. " +
                     "This will cause compile errors or incorrect behavior in generated code.");

    /// <summary>
    /// QRY018: Duplicate TypeMapping for the same custom type.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateTypeMapping = new(
        id: "QRY018",
        title: "Duplicate TypeMapping for custom type",
        messageFormat: "Custom type '{0}' is mapped by multiple TypeMapping classes: '{1}' and '{2}'. Only one mapping per custom type is allowed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two or more TypeMapping classes map the same TCustom type. " +
                     "The runtime TypeMappingRegistry allows only one mapping per custom type. " +
                     "Remove the duplicate mapping or consolidate into a single TypeMapping class.");

    /// <summary>
    /// QRY019: Clause not translatable at compile time.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor ClauseNotTranslatable = new(
        id: "QRY019",
        title: "Clause not translatable at compile time",
        messageFormat: "{0} clause could not be translated to SQL at compile time. The original runtime method will be used instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The source generator could not translate this clause expression to SQL. " +
                     "The query will use the original runtime method, which evaluates the expression tree at runtime. " +
                     "Consider restructuring the expression for compile-time analysis.");

    // ─── Custom EntityReader diagnostics (QRY026–QRY027) ────────────────────

    /// <summary>
    /// QRY026: Custom entity reader detected and active for entity.
    /// Severity: Info
    /// </summary>
    public static readonly DiagnosticDescriptor CustomEntityReaderActive = new(
        id: "QRY026",
        title: "Custom entity reader active",
        messageFormat: "Custom entity reader '{0}' is active for entity '{1}'. The generated reader will delegate to this class.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A custom EntityReader<T> class has been detected on the schema and will be used for entity materialization.");

    /// <summary>
    /// QRY027: [EntityReader] type doesn't inherit EntityReader&lt;T&gt; or T doesn't match entity.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidEntityReaderType = new(
        id: "QRY027",
        title: "Invalid entity reader type",
        messageFormat: "Type '{0}' specified in [EntityReader] on '{1}' must inherit from EntityReader<{2}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The type specified in the [EntityReader] attribute must inherit from EntityReader<T> " +
                     "where T matches the entity type derived from the schema class name.");

    // ─── Index diagnostics (QRY028) ─────────────────────────────────────

    /// <summary>
    /// QRY028: Column-level Unique() overlaps with explicit unique Index().
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor UniqueColumnOverlapsIndex = new(
        id: "QRY028",
        title: "Redundant unique constraint",
        messageFormat: "Column '{0}' has a .Unique() modifier that overlaps with unique index '{1}' on the same column",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A column has both a .Unique() modifier and an explicit single-column unique Index() on the same column. " +
                     "Remove one to avoid redundancy.");

    // ─── Sql.Raw diagnostics (QRY029) ──────────────────────────────────

    /// <summary>
    /// QRY029: Sql.Raw template placeholder mismatch.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor SqlRawPlaceholderMismatch = new(
        id: "QRY029",
        title: "Sql.Raw placeholder mismatch",
        messageFormat: "Sql.Raw template error: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The Sql.Raw template placeholders ({0}, {1}, ...) do not match the supplied arguments. " +
                     "Placeholders must be sequential starting from {0} and match the argument count.");

    // ─── Chain analysis diagnostics (QRY030–QRY032) ───────────────────

    /// <summary>
    /// QRY030: Query chain fully analyzed.
    /// Severity: Info
    /// </summary>
    public static readonly DiagnosticDescriptor ChainOptimized = new(
        id: "QRY030",
        title: "Query chain optimized",
        messageFormat: "Query chain fully analyzed at {0}: pre-built dispatch ({1} variants)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The query chain was fully analyzed and all clause combinations were enumerated. " +
                     "A pre-built SQL dispatch table will be emitted with zero runtime string work.");

    /// <summary>
    /// QRY031: RawSqlAsync/RawSqlScalarAsync type parameter is not a concrete type.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableRawSqlTypeParameter = new(
        id: "QRY031",
        title: "Unresolvable RawSql type parameter",
        messageFormat: "RawSqlAsync<{0}> cannot be source-generated because '{0}' is not a concrete type. Use a named DTO class or a primitive type as the type argument.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RawSqlAsync<T> and RawSqlScalarAsync<T> require a concrete type argument so the source generator " +
                     "can emit a typed reader delegate. When T is an open generic type parameter, the generator cannot " +
                     "determine the property layout at compile time.");

    /// <summary>
    /// QRY032: Query chain not analyzable for pre-built SQL.
    /// Severity: Info
    /// </summary>
    public static readonly DiagnosticDescriptor ChainNotAnalyzable = new(
        id: "QRY032",
        title: "Query chain not analyzable",
        messageFormat: "Query chain at {0} not analyzable for pre-built SQL: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The query chain could not be analyzed for pre-built SQL optimization. " +
                     "Restructure the query to enable static analysis.");

    /// <summary>
    /// QRY033: Forked query chain — builder variable consumed by multiple execution paths.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor ForkedQueryChain = new(
        id: "QRY033",
        title: "Forked query chain",
        messageFormat: "Query builder variable '{0}' is consumed by multiple execution paths. Each execution path must use its own builder chain expression.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A query builder variable is used as the receiver for multiple execution-terminating calls " +
                     "(e.g., ExecuteFetchAllAsync). Each execution path must use its own independent builder chain " +
                     "to avoid confusing aliasing behavior from the immutable builder contract.");

    // ─── Trace diagnostics (QRY034) ───────────────────────────────────

    /// <summary>
    /// QRY034: .Trace() requires QUARRY_TRACE preprocessor symbol.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor TraceWithoutFlag = new(
        id: "QRY034",
        title: ".Trace() requires QUARRY_TRACE",
        messageFormat: ".Trace() found on chain at {0} but QUARRY_TRACE is not defined. Add <DefineConstants>QUARRY_TRACE</DefineConstants> to enable trace output.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // ─── PreparedQuery diagnostics (QRY035–QRY036) ────────────────────

    /// <summary>
    /// QRY035: PreparedQuery escapes scope.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor PreparedQueryEscapesScope = new(
        id: "QRY035",
        title: "PreparedQuery escapes scope",
        messageFormat: "PreparedQuery variable '{0}' escapes the declaring method scope ({1}). Keep .Prepare() and its terminals in the same method body.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A PreparedQuery variable must not be returned from a method, passed as an argument, " +
                     "captured in a lambda, or assigned to a field. The source generator requires all terminal " +
                     "calls to be visible in the same method body.");

    /// <summary>
    /// QRY036: PreparedQuery has no terminals.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor PreparedQueryNoTerminals = new(
        id: "QRY036",
        title: "PreparedQuery has no terminals",
        messageFormat: ".Prepare() called at {0} but no terminal methods (.ToDiagnostics(), .ExecuteFetchAllAsync(), etc.) are invoked on the resulting variable. Remove the unused .Prepare() call.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A .Prepare() call that produces a PreparedQuery with no terminal invocations is dead code. " +
                     "Either invoke at least one terminal on the prepared variable, or remove the .Prepare() call.");

    // ─── Manifest diagnostics ──────────────────────────────────────────

    /// <summary>
    /// QRY040: SQL manifest write failed.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor ManifestWriteFailed = new(
        id: "QRY040",
        title: "SQL manifest write failed",
        messageFormat: "Failed to write SQL manifest to '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Quarry SQL manifest file could not be written to the specified path. " +
                     "Check that the QuarrySqlManifestPath directory exists and is writable.");

    // ─── RawSql compile-time resolution diagnostics (QRY041) ──────────

    /// <summary>
    /// QRY041: RawSqlAsync column expression without alias.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor RawSqlUnresolvableColumn = new(
        id: "QRY041",
        title: "RawSqlAsync column expression without alias",
        messageFormat: "RawSqlAsync column at position {0} is an expression without an alias. " +
                       "Add 'AS alias' for compile-time column resolution. " +
                       "Falling back to runtime ordinal discovery.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A column in the RawSqlAsync SQL literal is a complex expression (function call, " +
                     "arithmetic, etc.) without an AS alias. The generator cannot determine the result " +
                     "column name at compile time and falls back to runtime ordinal discovery.");

    // ─── Navigation join diagnostics (QRY060–QRY065) ──────────────────

    /// <summary>
    /// QRY060: No matching FK column for One&lt;T&gt; navigation.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor NoFkForOneNavigation = new(
        id: "QRY060",
        title: "No FK column for One<T> navigation",
        messageFormat: "No Ref<{0}, K> column found for One<{0}> navigation '{1}' on schema '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// QRY061: Ambiguous FK for One&lt;T&gt; navigation.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousFkForOneNavigation = new(
        id: "QRY061",
        title: "Ambiguous FK for One<T> navigation",
        messageFormat: "Ambiguous FK for One<{0}> navigation '{1}': multiple Ref<{0}, K> columns found ({2}). Use HasOne<{0}>(nameof(column)) to disambiguate.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// QRY062: HasOne references invalid column.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor HasOneInvalidColumn = new(
        id: "QRY062",
        title: "HasOne references invalid column",
        messageFormat: "HasOne<{0}>(nameof({1})) references '{1}' which is not a Ref<{0}, K> column",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// QRY063: Navigation target entity not found.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor NavigationTargetNotFound = new(
        id: "QRY063",
        title: "Navigation target entity not found",
        messageFormat: "Navigation '{0}' on '{1}' could not be resolved — target entity '{2}' not found in any registered context",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// QRY064: HasManyThrough junction navigation is not a Many&lt;T&gt;.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor HasManyThroughInvalidJunction = new(
        id: "QRY064",
        title: "HasManyThrough invalid junction navigation",
        messageFormat: "HasManyThrough junction navigation '{0}' does not reference a Many<T> property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// QRY065: HasManyThrough target navigation is not a One&lt;T&gt;.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor HasManyThroughInvalidTarget = new(
        id: "QRY065",
        title: "HasManyThrough invalid target navigation",
        messageFormat: "HasManyThrough target navigation '{0}' does not reference a One<T> property on junction entity '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ─── Migration diagnostics (QRY050–QRY055) ────────────────────────

    /// <summary>
    /// QRY050: Schema changed since last snapshot.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor SchemaChangedSinceSnapshot = new(
        id: "QRY050",
        title: "Schema changed since last snapshot",
        messageFormat: "Schema has changed since the last migration snapshot. Run 'dotnet quarry migrate add' to scaffold a new migration.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The current schema does not match the latest migration snapshot.");

    /// <summary>
    /// QRY051: Migration references unknown table/column.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor MigrationReferencesUnknown = new(
        id: "QRY051",
        title: "Migration references unknown table/column",
        messageFormat: "Migration references unknown table or column '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A migration class references a table or column name that does not exist in the known schema.");

    /// <summary>
    /// QRY052: Migration version gap or duplicate.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor MigrationVersionError = new(
        id: "QRY052",
        title: "Migration version gap or duplicate",
        messageFormat: "Migration version error: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The migration version sequence has a gap or duplicate.");

    /// <summary>
    /// QRY053: Pending migrations detected.
    /// Severity: Info
    /// </summary>
    public static readonly DiagnosticDescriptor PendingMigrations = new(
        id: "QRY053",
        title: "Pending migrations detected",
        messageFormat: "{0} pending migration(s) detected",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "There are migration classes with versions higher than the latest snapshot.");

    /// <summary>
    /// QRY054: Destructive migration without backup.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor DestructiveWithoutBackup = new(
        id: "QRY054",
        title: "Destructive migration step without backup",
        messageFormat: "Destructive migration step without backup in Migration {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A migration contains destructive steps (DropTable, DropColumn) but the Backup method is empty.");

    /// <summary>
    /// QRY055: Nullable to non-null without data migration.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor NullableToNonNullWithoutMigration = new(
        id: "QRY055",
        title: "Nullable to non-null change without data migration",
        messageFormat: "Nullable to non-null change without data migration step in Migration {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A column changed from nullable to non-null without an accompanying data migration step.");

    // ─── Set operation diagnostics (QRY070–QRY071) ─────────────────────

    /// <summary>
    /// QRY070: INTERSECT ALL is not supported by this SQL dialect.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor IntersectAllNotSupported = new(
        id: "QRY070",
        title: "INTERSECT ALL not supported",
        messageFormat: "INTERSECT ALL is not supported by the {0} dialect",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "INTERSECT ALL is only supported by PostgreSQL. SQLite, MySQL, and SQL Server do not support this operation.");

    /// <summary>
    /// QRY071: EXCEPT ALL is not supported by this SQL dialect.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor ExceptAllNotSupported = new(
        id: "QRY071",
        title: "EXCEPT ALL not supported",
        messageFormat: "EXCEPT ALL is not supported by the {0} dialect",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "EXCEPT ALL is only supported by PostgreSQL. SQLite, MySQL, and SQL Server do not support this operation.");

    /// <summary>
    /// QRY072: Set operation operands have different column counts.
    /// Severity: Warning
    /// </summary>
    public static readonly DiagnosticDescriptor SetOperationProjectionMismatch = new(
        id: "QRY072",
        title: "Set operation projection mismatch",
        messageFormat: "Set operation operand has {0} columns but the main query has {1} columns — the SQL engine may reject this or produce unexpected results",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "All operands of a UNION/INTERSECT/EXCEPT must project the same number of columns.");

    /// <summary>
    /// QRY073: Navigation aggregate in Select projection could not be bound.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor ProjectionSubqueryUnresolved = new(
        id: "QRY073",
        title: "Navigation aggregate in projection could not be resolved",
        messageFormat: "Navigation property '{0}' on parameter '{1}' could not be resolved for aggregate '{2}' in Select projection. Verify the navigation exists on the source entity and that the chain is built in a single fluent expression.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Aggregate methods on Many<T> navigation properties (Sum, Min, Max, Avg, Average, Count) require the navigation to be discoverable on the outer entity at compile time. If the navigation appears valid, ensure it is declared on the schema class and that the Select() lambda parameter matches the outer entity.");

    // ─── CTE diagnostics (QRY080–QRY082) ────────────────────────────────

    /// <summary>
    /// QRY080: CTE inner query could not be analyzed.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor CteInnerChainNotAnalyzable = new(
        id: "QRY080",
        title: "CTE inner query not analyzable",
        messageFormat: "Quarry could not analyze the inner query passed to With<{0}>(...). Make sure the inner query is a complete chain (e.g. db.Orders().Where(...).Select(...)) and not a method-group, lambda, or external variable. The CTE will be skipped and any FromCte<{0}>() in the same chain will fail at runtime.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The inner query passed to With() must be an inline fluent chain on the same context so that the source generator can compose its SQL into the outer WITH clause.");

    /// <summary>
    /// QRY081: FromCte&lt;T&gt;() has no matching With&lt;T&gt;() earlier in the same chain.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor FromCteWithoutWith = new(
        id: "QRY081",
        title: "FromCte without matching With",
        messageFormat: "FromCte<{0}>() has no matching With<{0}>(...) earlier in the same chain. Each FromCte<T>() must be preceded by a With<T>(innerQuery) that defines the CTE.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "FromCte<T>() reads from a CTE that must be defined earlier in the same fluent chain via With<T>(innerQuery).");

    /// <summary>
    /// QRY082: Multiple With&lt;T&gt;(...) calls in the same chain produce duplicate CTE names.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateCteName = new(
        id: "QRY082",
        title: "Duplicate CTE name in chain",
        messageFormat: "Multiple With<{0}>(...) calls in the same chain produce duplicate CTE name '{0}'. Each CTE in a chain must use a distinct DTO type so the generated WITH clause has unique aliases.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each CTE in a multi-CTE chain must have a unique short name (the unqualified DTO type name). Reusing the same DTO type for two With<T>() calls would emit a WITH clause with duplicate aliases and is rejected at compile time.");

    /// <summary>
    /// QRY900: Internal generator error.
    /// Severity: Error
    /// </summary>
    public static readonly DiagnosticDescriptor InternalError = new(
        id: "QRY900",
        title: "Internal generator error",
        messageFormat: "Internal error in Quarry generator: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An unexpected error occurred in the Quarry source generator.");
}
