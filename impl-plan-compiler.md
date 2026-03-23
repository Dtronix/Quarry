# Quarry Generator: Compiler Architecture Implementation Plan

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Constraints and Decisions](#2-constraints-and-decisions)
3. [Current Architecture Analysis](#3-current-architecture-analysis)
4. [Target Architecture Overview](#4-target-architecture-overview)
5. [Core IR Type Definitions](#5-core-ir-type-definitions)
6. [Phase 1: Unified SqlExpr IR](#6-phase-1-unified-sqlexpr-ir)
7. [Phase 2: Layered Call Site IR](#7-phase-2-layered-call-site-ir)
8. [Phase 3: QueryPlan IR](#8-phase-3-queryplan-ir)
9. [Phase 4: Incremental Pipeline Restructure](#9-phase-4-incremental-pipeline-restructure)
10. [Phase 5: Carrier Redesign](#10-phase-5-carrier-redesign)
11. [Phase 6: Codegen Consolidation](#11-phase-6-codegen-consolidation)
12. [Cross-Cutting Concerns](#12-cross-cutting-concerns)
13. [Migration Strategy](#13-migration-strategy)
14. [Risk Analysis](#14-risk-analysis)
15. [Phase Dependency Graph](#15-phase-dependency-graph)

---

## 1. Executive Summary

### What This Is

A restructuring of the Quarry source generator (24K lines across ~60 files) from its current organically-grown architecture into a principled compiler pipeline with distinct intermediate representations (IRs) at each stage.

### Why

The current generator has evolved into what is effectively a multi-pass compiler, but without the architectural clarity that enables a compiler to scale. Specifically:

- **Incrementality is broken**: A single `.Collect()` call collapses all usage sites into one array, causing every edit anywhere to re-process every query site in the compilation. In a large project with hundreds of query sites, this means the generator does O(N) expression translation work on every keystroke.
- **Dual expression translators**: Two independent systems (`ExpressionSyntaxTranslator` and `SyntacticClauseTranslator`) do the same job (C# expression to SQL) with different input types. Bug fixes and feature additions require dual implementation.
- **God object data flow**: `UsageSiteInfo` (25+ nullable properties) serves as discovery result, enriched result, and chain member simultaneously. Its `Equals()` compares all 25 fields on every incremental cache check.
- **Self-referencing queries are unnatural**: Subqueries require special handling rather than being a natural composition of the same query representation.

### What Changes

The generator is restructured into six phases, each producing a well-typed IR consumed by the next. The key new abstractions are:

- **`SqlExpr`**: A unified expression IR that replaces both expression translators.
- **`RawCallSite` / `BoundCallSite` / `TranslatedCallSite`**: Layered call site types replacing the monolithic `UsageSiteInfo`.
- **`QueryPlan`**: A compositional query representation that makes subqueries first-class and decouples query semantics from SQL rendering.
- **Carrier as a codegen strategy**: Carrier class generation is reimagined as an optimization pass over `QueryPlan`, not an IR concern.

### Scope

The affected files total ~15,500 lines in the core pipeline:

| File Group | Lines | Role |
|---|---|---|
| `QuarryGenerator.cs` | 2,961 | Pipeline orchestration, enrichment, chain building |
| `InterceptorCodeGenerator.*` (7 files) | 5,912 | Code emission for all interceptor kinds |
| `ChainAnalyzer.cs` | 1,159 | Dataflow analysis and tier classification |
| `ClauseTranslator.cs` | 1,067 | Semantic expression-to-SQL translation |
| `ExpressionSyntaxTranslator.cs` | 1,569 | Core expression translator (semantic model) |
| `SyntacticClauseTranslator.cs` | 538 | Deferred expression translator (no semantic model) |
| `SyntacticExpressionParser.cs` | 503 | Syntax-to-SyntacticExpression parser |
| `CompileTimeSqlBuilder.cs` | 976 | Pre-built SQL assembly |
| `SqlFragmentTemplate.cs` | 217 | Parameterized SQL fragment rendering |

Supporting models (`UsageSiteInfo`, `ClauseInfo`, `ChainAnalysisResult`, `PrebuiltChainInfo`, `CarrierClassInfo`, etc.) add another ~2,500 lines.

---

## 2. Constraints and Decisions

### Hard Constraints

| Constraint | Reason |
|---|---|
| **netstandard2.0 target** | Roslyn source generators are loaded into the compiler host process which requires netstandard2.0 assemblies. No `record` types, no `init` properties, no `Span<T>`, no `required` members. |
| **Roslyn 5.0 API surface** | The generator uses `IIncrementalGenerator` with `CreateSyntaxProvider`, `Combine`, `Collect`, `SelectMany`, `RegisterSourceOutput`. These are the only pipeline primitives available. |
| **All types must implement `IEquatable<T>`** | The incremental pipeline uses equality to determine cache hits. Every type flowing through the pipeline must have correct, deterministic `Equals()` and `GetHashCode()`. |
| **Transforms must be pure functions** | Pipeline transforms (`Select`, `SelectMany`) must be deterministic — same input must produce same output. No mutable shared state across invocations. |
| **`SyntaxNode` references cannot be stored in cached values** | Roslyn syntax nodes hold references to the full syntax tree. Storing them in pipeline values prevents garbage collection and causes unbounded memory growth. Call site locations must be stored as value types (file path, line, column). |

### Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Generated output compatibility | **Allow improvements** | Generated interceptor code may change in structure or quality. Behavioral equivalence is validated by existing tests. |
| Test strategy | **Existing tests only** | The existing test suite validates end-to-end correctness. Each phase must keep all existing tests green. New unit tests are added only for new IR types. |
| Carrier optimization | **Redesign** | Carrier generation becomes a codegen strategy applied over `QueryPlan` rather than an IR-level concern entangled with chain analysis. |
| Expression IR approach | **Syntax-first, semantic-enriched** | All expressions are parsed from syntax into `SqlExpr`. Semantic model annotations are applied as an optional enrichment pass, not a separate translator. |

---

## 3. Current Architecture Analysis

### Pipeline Passes (As-Is)

The current generator implements seven logical passes, though they are not cleanly separated:

**Pass 1 — Context Discovery** (`ContextParser`, `SchemaParser`)
Discovers `[QuarryContext]` classes, extracts entity and column metadata. Produces `ContextInfo` containing `EntityInfo[]` with column definitions, naming styles, indexes, and navigation properties. This pass is already well-structured as a per-context incremental node.

**Pass 2 — Usage Site Discovery** (`UsageSiteDiscovery`, `AnalyzabilityChecker`)
Scans for `InvocationExpressionSyntax` nodes on Quarry builder types. For each method call, determines the `InterceptorKind`, checks analyzability, and attempts expression translation. This pass produces `UsageSiteInfo` — the first problem, because it tries to do discovery AND translation in one step.

**Pass 3 — Expression Translation, Semantic Path** (`ClauseTranslator`, `ExpressionSyntaxTranslator`)
When the semantic model can resolve the entity type, expressions are translated to SQL fragments immediately during discovery. Uses `ExpressionTranslationContext` with `SemanticModel`, `EntityInfo`, and dialect info. Produces `ClauseInfo` with SQL string and `ParameterInfo[]`.

**Pass 4 — Expression Translation, Syntactic Fallback** (`SyntacticExpressionParser`)
When semantic resolution fails (common during the first compilation pass when entity types are being generated), the expression is parsed into a `SyntacticExpression` AST and stored as `PendingClauseInfo` for deferred translation.

**Pass 5 — Enrichment** (`QuarryGenerator.GroupByFileAndProcess()`)
Matches usage sites to entity metadata from contexts. Resolves `PendingClauseInfo` via `SyntacticClauseTranslator`. Discovers navigation join chain members. This is the bottleneck — it runs inside `GroupByFileAndProcess()` after `.Collect()`, meaning ALL sites are re-enriched on every change.

**Pass 6 — Chain Analysis** (`ChainAnalyzer`)
Performs intra-method dataflow analysis on query chains. Walks backward from execution sites through variable assignments and conditional branches. Classifies chains into three optimization tiers:
- **Tier 1 (PrebuiltDispatch)**: Up to 4 conditional bits (16 variants). Complete SQL strings are materialized at compile time.
- **Tier 2 (PrequotedFragments)**: Pre-quoted SQL fragments concatenated at runtime.
- **Tier 3 (RuntimeBuild)**: Falls back to the runtime `SqlBuilder`.

**Pass 7 — SQL Building and Code Generation** (`CompileTimeSqlBuilder`, `InterceptorCodeGenerator`, `CarrierClassBuilder`)
For Tier 1 chains, builds a `Dictionary<ulong, PrebuiltSqlResult>` mapping each bitmask to a complete SQL string. `SqlFragmentTemplate` handles parameter index remapping without regex. `CarrierClassBuilder` determines carrier eligibility and field layout. `InterceptorCodeGenerator` (6 partial files, ~5,900 lines) emits the final C# source.

### Structural Problems in Detail

#### Problem 1: The `.Collect()` Bottleneck

In `QuarryGenerator.Initialize()`:

```csharp
var perFileGroups = compilationAndContexts
    .Combine(usageSites.Collect())
    .SelectMany(GroupByFileAndProcess);
```

`usageSites.Collect()` aggregates every discovered usage site across the entire compilation into a single `ImmutableArray<UsageSiteInfo>`. The Roslyn incremental pipeline treats this collected array as a single node — if ANY element changes, the entire downstream transform re-runs.

Inside `GroupByFileAndProcess()`, the following work happens for ALL sites:
1. `BuildEntityLookup()` — O(entities) dictionary construction
2. `EnrichUsageSiteWithEntityInfo()` — O(sites) with expression translation per site
3. `DiscoverNavigationJoinChainMembers()` — O(enriched sites) join resolution
4. `AnalyzeExecutionChainsWithDiagnostics()` — O(execution sites x sites per method) dataflow analysis
5. `CompileTimeSqlBuilder.Build*SqlMap()` — O(chains x masks) SQL assembly
6. File grouping and `FileInterceptorGroup` construction

Steps 2, 4, and 5 are the expensive ones. Step 2 in particular involves `SyntacticClauseTranslator.Translate()` for every pending clause, which walks the syntactic expression tree and performs column resolution.

When a developer edits a single `.Where()` lambda in one file, the entire pipeline re-runs for every file in the project. In a project with 200 query sites, this means ~200 expression translations, ~50 chain analyses, and ~50 SQL builds — all because one site changed.

#### Problem 2: Dual Expression Translation

Two independent systems translate C# expressions to SQL:

**Semantic path** (`ExpressionSyntaxTranslator`): Works with Roslyn `ExpressionSyntax` nodes and a `SemanticModel`. Can resolve overloads, constant-fold enum values, determine captured variable types, and resolve method calls like `string.Contains()` to `LIKE` patterns. Produces SQL strings directly.

**Syntactic path** (`SyntacticExpressionParser` + `SyntacticClauseTranslator`): Works without `SemanticModel`. Parses expressions into a `SyntacticExpression` AST (9 node types: `PropertyAccess`, `Literal`, `Parameter`, `Binary`, `Unary`, `MethodCall`, `MemberAccess`, `CapturedVariable`, `Unknown`). The AST is stored in `PendingClauseInfo` and later translated by `SyntacticClauseTranslator` when `EntityInfo` becomes available.

The syntactic path exists because during the first compilation pass of an incremental generator, entity types generated by Phase 1 may not yet be resolved in the `SemanticModel`. The semantic path fails to resolve `ITypeSymbol` for the entity, so the syntactic path provides a fallback.

The duplication means:
- Every new expression kind (e.g., `CollectionExpressionSyntax` for C# 12 collection literals) must be handled in both translators
- Bug fixes to SQL generation logic must be verified in both paths
- The `SyntacticExpression` hierarchy partially mirrors Roslyn's own syntax model
- Two different parameter extraction strategies must stay in sync

#### Problem 3: `UsageSiteInfo` as God Object

`UsageSiteInfo` is constructed during discovery with only location and method info populated. During enrichment, a new copy is created with clause info, entity binding, context binding, dialect, and various resolved type names added. The same type is then referenced inside `ChainedClauseSite` (chain analysis), `PrebuiltChainInfo` (SQL building), and passed to `InterceptorCodeGenerator` (codegen).

The type has 25+ properties, many of which are nullable and only meaningful for specific `InterceptorKind` values:
- `ProjectionInfo` — only for `Select`
- `ClauseInfo` — only for clause methods
- `InsertInfo` / `UpdateInfo` — only for insert/update
- `JoinedEntityTypeName` / `JoinedEntityTypeNames` — only for join methods
- `RawSqlTypeInfo` — only for `RawSqlAsync`
- `ConstantIntValue` — only for `Limit`/`Offset`
- `ValueTypeName` — only for `Set`

The `Equals()` implementation compares all 25+ fields. Every time the incremental pipeline checks whether a cached value is still valid, it runs this comparison. For N usage sites, the pipeline performs O(N) full equality checks per generation cycle.

---

## 4. Target Architecture Overview

### Pipeline Stages

The target architecture has six cleanly separated stages, each consuming one IR and producing the next:

```
Stage 1: Discovery       RawCallSite[]        (syntax only, no semantic model)
Stage 2: Binding         BoundCallSite[]      (entity + context resolved)
Stage 3: Translation     TranslatedCallSite[] (SqlExpr translated, parameters extracted)
Stage 4: Optimization    QueryPlan[]          (chains analyzed, plans built)
Stage 5: SQL Assembly    AssembledPlan[]       (SQL strings materialized per mask)
Stage 6: Code Emission   string (C# source)   (interceptors + carriers emitted)
```

Each stage is implemented as one or more `Select`/`SelectMany` transforms in the incremental pipeline. Stages 1-3 operate per-site (no `.Collect()` needed). Stage 4 requires per-method grouping. Stages 5-6 are per-chain and per-file respectively.

### Key Design Principles

**Principle 1: Each IR carries only what its stage needs.**
`RawCallSite` has ~8 fields. `BoundCallSite` has ~12. `TranslatedCallSite` has ~15. Compare to the current `UsageSiteInfo` with 25+. Smaller types mean faster equality checks and clearer responsibilities.

**Principle 2: Expression translation happens once, in one system.**
The `SqlExpr` IR unifies the semantic and syntactic paths. Discovery always produces `SqlExpr` from syntax. Binding resolves column references. Rendering produces SQL strings. One pipeline, one set of bugs.

**Principle 3: Query semantics are separated from SQL text.**
`QueryPlan` represents the logical structure of a query (what tables, what conditions, what ordering) without committing to a SQL dialect or parameter format. SQL rendering happens only at Stage 5, after all optimizations.

**Principle 4: Carrier is a codegen optimization, not an IR property.**
`QueryPlan` and `AssembledPlan` have no concept of carriers. The codegen stage inspects the plan's properties and decides whether to emit carrier-optimized code. This isolates carrier logic from the data pipeline.

---

## 5. Core IR Type Definitions

This section defines the key types introduced by the refactor. All types target netstandard2.0 (sealed classes with manual `IEquatable<T>`, no records).

### 5.1 SqlExpr — Unified Expression IR

`SqlExpr` is an abstract base class representing a SQL expression fragment. It replaces both `ExpressionSyntax`-based translation and `SyntacticExpression`-based deferred translation with a single IR that is dialect-agnostic until rendering.

**Namespace**: `Quarry.Generators.IR`

```csharp
internal abstract class SqlExpr : IEquatable<SqlExpr>
{
    public abstract SqlExprKind Kind { get; }
    public abstract bool Equals(SqlExpr other);
    public abstract override int GetHashCode();
}

internal enum SqlExprKind
{
    ColumnRef,          // Unresolved column: param.PropertyName
    ResolvedColumn,     // Resolved column: "table"."column"
    ParamSlot,          // Parameter placeholder: @p{index}
    Literal,            // SQL literal: 42, 'hello', NULL, TRUE
    BinaryOp,           // a AND b, a = b, a + b
    UnaryOp,            // NOT a, -a
    FunctionCall,       // LOWER(x), COALESCE(a, b), COUNT(*)
    InExpr,             // x IN (@p0, @p1, ...)
    IsNullCheck,        // x IS NULL, x IS NOT NULL
    LikeExpr,           // x LIKE pattern [ESCAPE char]
    BetweenExpr,        // x BETWEEN a AND b
    CaseExpr,           // CASE WHEN ... THEN ... ELSE ... END
    SubQuery,           // (SELECT ... FROM ...)
    CapturedValue,      // Runtime-evaluated captured variable
    SqlRaw,             // Pre-rendered SQL fragment (escape hatch)
    ExprList            // Comma-separated expression list
}
```

#### SqlExpr Node Types

Each node type is a sealed class inheriting from `SqlExpr`. Key nodes:

```csharp
/// Unresolved column reference. Created during discovery from syntax.
/// Resolved to ResolvedColumnExpr during binding.
internal sealed class ColumnRefExpr : SqlExpr
{
    public string ParameterName { get; }   // Lambda param: "u"
    public string PropertyName { get; }    // Entity property: "UserName"
    public string? NestedProperty { get; } // For Ref<T>.Id access: "Id"
}

/// Resolved column with quoted identifiers and optional table qualifier.
internal sealed class ResolvedColumnExpr : SqlExpr
{
    public string QuotedColumnName { get; }  // e.g., "\"user_name\""
    public string? TableQualifier { get; }   // e.g., "\"t0\"" or null
}

/// Parameter placeholder. Carries clause-local index and type metadata.
internal sealed class ParamSlotExpr : SqlExpr
{
    public int LocalIndex { get; }           // 0-based within this clause
    public string ClrType { get; }           // "string", "int", "decimal"
    public string ValueExpression { get; }   // C# expression to extract value
    public bool IsCaptured { get; }          // Captured variable from closure
    public string? ExpressionPath { get; }   // Path for direct extraction
    public bool IsCollection { get; }        // IN-clause collection parameter
    public string? ElementTypeName { get; }  // Collection element type
}

/// SQL literal value.
internal sealed class LiteralExpr : SqlExpr
{
    public string SqlText { get; }     // "42", "'hello'", "NULL", "TRUE"
    public string ClrType { get; }     // "int", "string", etc.
    public bool IsNull { get; }
}

/// Binary operation.
internal sealed class BinaryOpExpr : SqlExpr
{
    public SqlExpr Left { get; }
    public SqlBinaryOperator Operator { get; }
    public SqlExpr Right { get; }
}

/// Unary operation.
internal sealed class UnaryOpExpr : SqlExpr
{
    public SqlUnaryOperator Operator { get; }
    public SqlExpr Operand { get; }
}

/// SQL function call (LOWER, UPPER, COALESCE, COUNT, SUM, etc.).
internal sealed class FunctionCallExpr : SqlExpr
{
    public string FunctionName { get; }
    public IReadOnlyList<SqlExpr> Arguments { get; }
    public bool IsAggregate { get; }
}

/// IN expression with a list of values or a collection parameter.
internal sealed class InExpr : SqlExpr
{
    public SqlExpr Operand { get; }
    public IReadOnlyList<SqlExpr> Values { get; }  // Individual values or single ParamSlot (collection)
    public bool IsNegated { get; }
}

/// IS NULL / IS NOT NULL check.
internal sealed class IsNullCheckExpr : SqlExpr
{
    public SqlExpr Operand { get; }
    public bool IsNegated { get; }  // IS NOT NULL when true
}

/// LIKE expression with optional escape character.
internal sealed class LikeExpr : SqlExpr
{
    public SqlExpr Operand { get; }
    public SqlExpr Pattern { get; }
    public bool IsNegated { get; }
}

/// Embedded subquery (SELECT ... FROM ...).
/// Enables self-referencing: queries inside queries.
internal sealed class SubQueryExpr : SqlExpr
{
    public QueryPlan Plan { get; }
}

/// Captured runtime value that needs expression tree extraction.
internal sealed class CapturedValueExpr : SqlExpr
{
    public string VariableName { get; }
    public string SyntaxText { get; }
    public string? ExpressionPath { get; }
    public string ClrType { get; }
    public bool CanGenerateDirectPath { get; }
}

/// Pre-rendered SQL text (escape hatch for cases not modeled by the IR).
internal sealed class SqlRawExpr : SqlExpr
{
    public string SqlText { get; }
}
```

#### SqlExpr Operators

```csharp
internal enum SqlBinaryOperator
{
    Equal, NotEqual, LessThan, GreaterThan,
    LessThanOrEqual, GreaterThanOrEqual,
    And, Or, Add, Subtract, Multiply, Divide, Modulo,
    BitwiseAnd, BitwiseOr
}

internal enum SqlUnaryOperator
{
    Not, Negate
}
```

### 5.2 Layered Call Site Types

#### RawCallSite

Produced by Stage 1 (discovery). Contains only what can be determined from syntax without semantic analysis or entity metadata.

```csharp
/// Lightweight discovery result. No semantic model, no entity metadata.
internal sealed class RawCallSite : IEquatable<RawCallSite>
{
    public string MethodName { get; }
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string UniqueId { get; }
    public InterceptorKind Kind { get; }
    public BuilderKind BuilderKind { get; }
    public string EntityTypeName { get; }
    public string? ResultTypeName { get; }
    public bool IsAnalyzable { get; }
    public string? NonAnalyzableReason { get; }
    public string? InterceptableLocationData { get; }
    public int InterceptableLocationVersion { get; }

    // Expression data (parsed from syntax, before binding)
    public SqlExpr? Expression { get; }          // Clause expression as SqlExpr
    public ClauseKind? ClauseKind { get; }       // What kind of clause this is
    public bool IsDescending { get; }            // For OrderBy
    public ProjectionInfo? ProjectionInfo { get; } // For Select (analyzed from syntax)

    // Join-specific
    public string? JoinedEntityTypeName { get; }

    // Insert-specific
    public HashSet<string>? InitializedPropertyNames { get; }

    // Pagination
    public int? ConstantIntValue { get; }        // For Limit/Offset literals

    // Syntax reference (NOT stored — used transiently during discovery only)
    // SyntaxNode InvocationSyntax is consumed during discovery but not carried forward.
    // Instead, DiagnosticLocation is captured as a value type.
    public DiagnosticLocation Location { get; }
}
```

#### BoundCallSite

Produced by Stage 2 (binding). Adds entity metadata and context resolution.

```csharp
/// Call site with entity and context binding resolved.
internal sealed class BoundCallSite : IEquatable<BoundCallSite>
{
    public RawCallSite Raw { get; }              // Underlying raw site (composition, not copying)

    // Resolved bindings
    public string ContextClassName { get; }
    public string ContextNamespace { get; }
    public SqlDialect Dialect { get; }
    public string TableName { get; }
    public string? SchemaName { get; }

    // Entity metadata reference (for column resolution during translation)
    public EntityRef Entity { get; }

    // For joins: resolved joined entity metadata
    public EntityRef? JoinedEntity { get; }
    public IReadOnlyList<string>? JoinedEntityTypeNames { get; }

    // For insert/update: resolved column metadata
    public InsertInfo? InsertInfo { get; }
    public InsertInfo? UpdateInfo { get; }
    public RawSqlTypeInfo? RawSqlTypeInfo { get; }
}
```

`EntityRef` is a lightweight reference to entity metadata that avoids carrying the full `EntityInfo`:

```csharp
/// Lightweight reference to entity metadata for column resolution.
internal sealed class EntityRef : IEquatable<EntityRef>
{
    public string EntityName { get; }
    public string TableName { get; }
    public string? SchemaName { get; }
    public string SchemaNamespace { get; }
    public IReadOnlyList<ColumnInfo> Columns { get; }
    public IReadOnlyList<NavigationInfo> Navigations { get; }
    public string? CustomEntityReaderClass { get; }
}
```

#### TranslatedCallSite

Produced by Stage 3 (translation). Contains the fully translated SQL expression and extracted parameters.

```csharp
/// Fully translated call site with SQL expression and parameters.
internal sealed class TranslatedCallSite : IEquatable<TranslatedCallSite>
{
    public BoundCallSite Bound { get; }

    // Translated clause (null for non-clause sites like Limit, Distinct, ChainRoot)
    public TranslatedClause? Clause { get; }

    // Resolved type names for carrier optimization
    public string? KeyTypeName { get; }      // OrderBy/ThenBy/GroupBy key type
    public string? ValueTypeName { get; }    // Set value type
}

/// A fully translated clause with resolved SQL expression and parameters.
internal sealed class TranslatedClause : IEquatable<TranslatedClause>
{
    public ClauseKind Kind { get; }
    public SqlExpr ResolvedExpression { get; }       // Bound SqlExpr (all columns resolved)
    public IReadOnlyList<ParameterInfo> Parameters { get; }
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    // Clause-specific metadata
    public bool IsDescending { get; }                 // OrderBy direction
    public JoinClauseKind? JoinKind { get; }          // Join type
    public string? JoinedTableName { get; }           // Join target
    public string? JoinedSchemaName { get; }
    public string? TableAlias { get; }
    public IReadOnlyList<SetActionAssignment>? SetAssignments { get; }  // Set(Action<T>)
    public string? CustomTypeMappingClass { get; }    // Set value mapping
}
```

### 5.3 QueryPlan IR

`QueryPlan` is the compositional query representation. It describes the logical structure of a query independent of SQL dialect or parameter formatting.

```csharp
/// A complete query plan built from an analyzed chain.
/// Dialect-agnostic. Self-referencing via SubQueryExpr nodes.
internal sealed class QueryPlan : IEquatable<QueryPlan>
{
    public QueryKind Kind { get; }                    // Select, Delete, Update, Insert
    public TableRef PrimaryTable { get; }
    public IReadOnlyList<JoinPlan> Joins { get; }
    public IReadOnlyList<WhereTerm> WhereTerms { get; }   // AND-combined conditions
    public IReadOnlyList<OrderTerm> OrderTerms { get; }
    public IReadOnlyList<SqlExpr> GroupByExprs { get; }
    public IReadOnlyList<SqlExpr> HavingExprs { get; }
    public SelectProjection Projection { get; }
    public PaginationPlan? Pagination { get; }
    public bool IsDistinct { get; }

    // Update/Insert specific
    public IReadOnlyList<SetTerm> SetTerms { get; }       // SET col = value
    public IReadOnlyList<InsertColumn> InsertColumns { get; }

    // Conditional clause support (bitmask dispatch)
    public IReadOnlyList<ConditionalTerm> ConditionalTerms { get; }
    public IReadOnlyList<ulong> PossibleMasks { get; }

    // Parameter inventory (all parameters across all clauses, globally indexed)
    public IReadOnlyList<QueryParameter> Parameters { get; }

    // Chain metadata
    public OptimizationTier Tier { get; }
    public string? NotAnalyzableReason { get; }
    public IReadOnlyList<string>? UnmatchedMethodNames { get; }
}
```

#### QueryPlan Supporting Types

```csharp
/// Reference to a database table.
internal sealed class TableRef : IEquatable<TableRef>
{
    public string TableName { get; }
    public string? SchemaName { get; }
    public string? Alias { get; }           // "t0", "t1", etc.
}

/// A JOIN clause in a query plan.
internal sealed class JoinPlan : IEquatable<JoinPlan>
{
    public JoinClauseKind Kind { get; }     // Inner, Left, Right
    public TableRef Table { get; }
    public SqlExpr OnCondition { get; }     // Bound expression
    public bool IsNavigationJoin { get; }
}

/// A WHERE condition term. May be conditional (part of bitmask dispatch).
internal sealed class WhereTerm : IEquatable<WhereTerm>
{
    public SqlExpr Condition { get; }
    public int? BitIndex { get; }           // Non-null if conditional
    public BranchKind? BranchKind { get; }
}

/// An ORDER BY term with direction.
internal sealed class OrderTerm : IEquatable<OrderTerm>
{
    public SqlExpr Expression { get; }
    public bool IsDescending { get; }
    public int? BitIndex { get; }
}

/// A SET assignment for UPDATE operations.
internal sealed class SetTerm : IEquatable<SetTerm>
{
    public ResolvedColumnExpr Column { get; }
    public SqlExpr Value { get; }           // ParamSlot or Literal
    public string? CustomTypeMappingClass { get; }
    public int? BitIndex { get; }
}

/// Pagination with support for both literal and parameterized values.
internal sealed class PaginationPlan : IEquatable<PaginationPlan>
{
    public int? LiteralLimit { get; }       // Compile-time constant
    public int? LiteralOffset { get; }
    public int? LimitParamIndex { get; }    // Runtime parameter index
    public int? OffsetParamIndex { get; }
}

/// A conditional clause term with bitmask metadata.
internal sealed class ConditionalTerm : IEquatable<ConditionalTerm>
{
    public int BitIndex { get; }
    public ClauseRole Role { get; }
    public BranchKind BranchKind { get; }
}

/// A globally-indexed parameter in the query plan.
internal sealed class QueryParameter : IEquatable<QueryParameter>
{
    public int GlobalIndex { get; }
    public string ClrType { get; }
    public string ValueExpression { get; }
    public bool IsCaptured { get; }
    public string? ExpressionPath { get; }
    public bool IsCollection { get; }
    public string? ElementTypeName { get; }
    public string? TypeMappingClass { get; }
    public bool IsEnum { get; }
    public string? EnumUnderlyingType { get; }
    public bool IsSensitive { get; }
    public string? EntityPropertyExpression { get; }  // For SetPoco entity-sourced params
    public bool NeedsFieldInfoCache { get; }
    public bool IsDirectAccessible { get; }           // Collection direct-access
    public string? CollectionAccessExpression { get; }
}

/// SELECT projection plan.
internal sealed class SelectProjection : IEquatable<SelectProjection>
{
    public ProjectionKind Kind { get; }
    public string ResultTypeName { get; }
    public IReadOnlyList<ProjectedColumn> Columns { get; }
    public string? CustomEntityReaderClass { get; }
    public bool IsIdentity { get; }                    // SELECT * (no explicit Select clause)
}

/// A column in an INSERT statement.
internal sealed class InsertColumn : IEquatable<InsertColumn>
{
    public string QuotedColumnName { get; }
    public int ParameterIndex { get; }
    public bool IsIdentity { get; }                    // For RETURNING clause
}
```

### 5.4 AssembledPlan

Produced by Stage 5 (SQL assembly). Contains the materialized SQL strings for each bitmask variant.

```csharp
/// A query plan with materialized SQL strings and codegen metadata.
internal sealed class AssembledPlan : IEquatable<AssembledPlan>
{
    public QueryPlan Plan { get; }
    public Dictionary<ulong, AssembledSqlVariant> SqlVariants { get; }
    public string? ReaderDelegateCode { get; }       // For SELECT queries
    public int MaxParameterCount { get; }

    // Source chain metadata (for interceptor location attributes)
    public TranslatedCallSite ExecutionSite { get; }
    public IReadOnlyList<TranslatedCallSite> ClauseSites { get; }

    // Entity metadata
    public string EntityTypeName { get; }
    public string? ResultTypeName { get; }
    public SqlDialect Dialect { get; }
    public string? EntitySchemaNamespace { get; }
}

/// A single SQL variant for a specific bitmask value.
internal sealed class AssembledSqlVariant : IEquatable<AssembledSqlVariant>
{
    public string Sql { get; }
    public int ParameterCount { get; }
}
```

### 5.5 EntityRegistry

A collected, cached lookup structure built from all contexts. Changes rarely (only when schema classes change).

```csharp
/// Compiled entity metadata from all contexts. Used for binding.
internal sealed class EntityRegistry : IEquatable<EntityRegistry>
{
    /// Lookup by entity type name → (EntityInfo, ContextInfo) pairs.
    public IReadOnlyDictionary<string, IReadOnlyList<EntityRegistryEntry>> Entries { get; }

    /// Lookup by entity name → EntityInfo (for subquery resolution).
    public IReadOnlyDictionary<string, EntityInfo> ByName { get; }

    public EntityRegistryEntry? Resolve(string entityTypeName, string? contextClassName);
}

internal sealed class EntityRegistryEntry : IEquatable<EntityRegistryEntry>
{
    public EntityInfo Entity { get; }
    public ContextInfo Context { get; }
}
```

---

## 6. Phase 1: Unified SqlExpr IR

### Goal

Replace the dual expression translation system (`ExpressionSyntaxTranslator` + `SyntacticExpressionParser`/`SyntacticClauseTranslator`) with a single pipeline that produces, binds, and renders `SqlExpr` trees.

### Rationale

This is the highest-leverage change because it:
- Eliminates a class of "fixed in one translator but not the other" bugs
- Provides the foundation for `QueryPlan` (Phase 3) and subquery support
- Can be done without changing the incremental pipeline structure (Phase 4)
- Directly replaces ~3,600 lines of translator code with a unified system

### New Components

#### 6.1 `SqlExprParser` — Syntax to SqlExpr

Replaces `SyntacticExpressionParser`. Parses Roslyn `ExpressionSyntax` into `SqlExpr` nodes without requiring a `SemanticModel`. This always runs during discovery.

```csharp
/// Parses C# expression syntax into SqlExpr trees.
/// Works without SemanticModel — all column references remain unresolved.
internal static class SqlExprParser
{
    /// Parses a lambda body into an SqlExpr tree.
    public static SqlExpr Parse(
        ExpressionSyntax expression,
        HashSet<string> lambdaParameterNames);

    /// Parses with expression path tracking for captured variable extraction.
    public static SqlExpr ParseWithPathTracking(
        ExpressionSyntax expression,
        HashSet<string> lambdaParameterNames);
}
```

**Algorithm**: Recursive descent over the Roslyn syntax tree. For each syntax node kind:
- `MemberAccessExpressionSyntax` where the receiver is a lambda parameter → `ColumnRefExpr`
- `MemberAccessExpressionSyntax` where the receiver is NOT a lambda parameter → `CapturedValueExpr`
- `BinaryExpressionSyntax` → `BinaryOpExpr` with mapped operator
- `LiteralExpressionSyntax` → `LiteralExpr` with SQL-formatted value
- `InvocationExpressionSyntax` → analyze method name: `Contains` → `InExpr` or `LikeExpr`; `StartsWith`/`EndsWith` → `LikeExpr`; aggregate methods → `FunctionCallExpr`
- `IsPatternExpressionSyntax` → `IsNullCheckExpr`
- `IdentifierNameSyntax` matching lambda parameter → `ColumnRefExpr` (boolean column in WHERE context)
- `IdentifierNameSyntax` not matching lambda parameter → `CapturedValueExpr`

Path tracking: Each recursive call appends to a path string (e.g., `"Body.Left.Right"`). When a `CapturedValueExpr` is created, the current path is stored for direct extraction.

#### 6.2 `SqlExprAnnotator` — Semantic Enrichment

Optional pass that annotates `SqlExpr` nodes with type information from the `SemanticModel`. This runs during discovery when the semantic model is available and types can be resolved.

```csharp
/// Enriches SqlExpr trees with semantic type information.
/// Best-effort: gracefully returns the original tree if resolution fails.
internal static class SqlExprAnnotator
{
    /// Annotates an SqlExpr tree with type info from the semantic model.
    /// Returns the original tree if annotation fails (graceful degradation).
    public static SqlExpr Annotate(
        SqlExpr expr,
        ExpressionSyntax syntax,
        SemanticModel semanticModel);
}
```

**Algorithm**: Walks the `SqlExpr` tree in parallel with the `ExpressionSyntax` tree. For each node:
- `CapturedValueExpr` → resolve type via `SemanticModel.GetTypeInfo()`, update `ClrType`
- `ParamSlotExpr` → resolve CLR type from semantic model
- `FunctionCallExpr` → verify method resolution matches expected SQL function
- `LiteralExpr` → attempt constant folding for enum values via `GetConstantValue()`
- If any resolution fails, the node is left unchanged (syntactic form preserved)

#### 6.3 `SqlExprBinder` — Column Resolution

Resolves `ColumnRefExpr` nodes to `ResolvedColumnExpr` using entity metadata. Runs during the binding stage (Stage 2) when `EntityInfo` is available.

```csharp
/// Resolves column references in SqlExpr trees against entity metadata.
internal static class SqlExprBinder
{
    /// Resolves all ColumnRefExpr nodes to ResolvedColumnExpr.
    public static SqlExpr Bind(
        SqlExpr expr,
        EntityInfo primaryEntity,
        SqlDialect dialect,
        IReadOnlyDictionary<string, EntityInfo>? joinedEntities = null,
        IReadOnlyDictionary<string, string>? tableAliases = null);
}
```

**Algorithm**: Top-down recursive walk. For each `ColumnRefExpr`:
1. Look up `ParameterName` in the joined entities map (if present) or match against the primary entity's lambda parameter
2. Find the column by `PropertyName` in the resolved entity's `Columns` list
3. Quote the column name using the dialect's identifier quoting rules
4. If joins are present or in a subquery, prepend the table qualifier
5. Handle `NestedProperty` for `Ref<T>.Id` access (resolve to the foreign key column)
6. Return `ResolvedColumnExpr` with the quoted name and optional qualifier

If a column cannot be resolved, the binder returns a `SqlRawExpr` with an error marker, and the clause is marked as failed.

#### 6.4 `SqlExprRenderer` — SQL String Generation

Renders a bound `SqlExpr` tree to a SQL string with dialect-specific formatting. Runs during SQL assembly (Stage 5).

```csharp
/// Renders bound SqlExpr trees to dialect-specific SQL strings.
internal static class SqlExprRenderer
{
    /// Renders an expression to a SQL string.
    public static string Render(
        SqlExpr expr,
        SqlDialect dialect,
        int parameterBaseIndex);

    /// Renders an expression to a SqlFragmentTemplate for parameterized rendering.
    public static SqlFragmentTemplate RenderToTemplate(
        SqlExpr expr,
        SqlDialect dialect);
}
```

**Algorithm**: Post-order tree walk producing a `StringBuilder` output:
- `ResolvedColumnExpr` → append quoted column name (with qualifier if present)
- `ParamSlotExpr` → append dialect-specific parameter placeholder (`@p{base + localIndex}`, `${base + localIndex + 1}`, or `?`)
- `BinaryOpExpr` → render left, append operator SQL keyword, render right. For `And`/`Or`, wrap in parentheses if needed
- `FunctionCallExpr` → append function name, open paren, render args comma-separated, close paren
- `InExpr` → render operand, append `IN (`, render values comma-separated, append `)`
- `LikeExpr` → render operand, append `LIKE`, render pattern
- `IsNullCheckExpr` → render operand, append `IS NULL` or `IS NOT NULL`
- `SubQueryExpr` → append `(`, recursively render the embedded `QueryPlan` as a full SELECT, append `)`
- `LiteralExpr` → append SQL text directly
- `CapturedValueExpr` → render as a `ParamSlotExpr` (captured values become parameters)

### Files Affected

| File | Action |
|---|---|
| `Translation/ExpressionSyntaxTranslator.cs` (1,569 lines) | **Replace** with `SqlExprRenderer` |
| `Translation/SyntacticExpressionParser.cs` (503 lines) | **Replace** with `SqlExprParser` |
| `Translation/SyntacticClauseTranslator.cs` (538 lines) | **Delete** — functionality absorbed by `SqlExprBinder` + `SqlExprRenderer` |
| `Translation/ClauseTranslator.cs` (1,067 lines) | **Rewrite** — thin orchestrator calling `SqlExprParser` → `SqlExprBinder` → `SqlExprRenderer` |
| `Translation/ExpressionTranslationContext.cs` (386 lines) | **Delete** — context split into `SqlExprBinder` parameters and renderer state |
| `Translation/ExpressionTranslationResult.cs` (186 lines) | **Simplify** — `TranslatedClause` replaces this |
| `Models/SyntacticExpression.cs` (414 lines) | **Delete** — replaced by `SqlExpr` hierarchy |
| `Models/PendingClauseInfo.cs` (60 lines) | **Delete** — no more deferred translation; all expressions are `SqlExpr` from discovery |
| `Translation/SqlLikeHelpers.cs` (61 lines) | **Keep** — utility for LIKE pattern escaping, used by `SqlExprParser` |
| `Translation/SubqueryScope.cs` (31 lines) | **Delete** — subquery scoping is handled by `SubQueryExpr` containment |

New files:
- `IR/SqlExpr.cs` — base class and `SqlExprKind` enum
- `IR/SqlExprNodes.cs` — all node type definitions
- `IR/SqlExprParser.cs` — syntax-to-SqlExpr parser
- `IR/SqlExprAnnotator.cs` — semantic enrichment
- `IR/SqlExprBinder.cs` — column resolution
- `IR/SqlExprRenderer.cs` — SQL string generation
- `IR/SqlExprOperators.cs` — operator enums and mapping

### Validation

- All existing tests must pass (expression translation produces equivalent SQL)
- The `PendingClauseInfo` / `SyntacticClauseTranslator` path is eliminated — verify no test relies on this path specifically (tests should be input/output, not path-dependent)
- Subquery expressions (`.Where(u => u.Age > db.Users.Where(...).Select(...))`) should now work naturally through `SubQueryExpr`

---

## 7. Phase 2: Layered Call Site IR

### Goal

Replace `UsageSiteInfo` (25+ properties, one type for all stages) with `RawCallSite`, `BoundCallSite`, and `TranslatedCallSite`. Each type carries only what its pipeline stage needs.

### Rationale

- `RawCallSite.Equals()` compares ~12 fields instead of 25+ — faster cache checks
- `BoundCallSite` is produced per-site from `RawCallSite` + `EntityRegistry` — enables per-site caching
- `TranslatedCallSite` separates translation results from discovery data — the translation can be independently cached
- Eliminates the mutable `SyntaxNode InvocationSyntax` reference that prevents GC of syntax trees
- Makes it impossible to accidentally access enrichment-phase data during discovery

### New Components

#### 7.1 `CallSiteDiscovery` — Produces RawCallSite

Replaces `UsageSiteDiscovery.DiscoverUsageSite()`. Returns `RawCallSite` with `SqlExpr` (from Phase 1) instead of attempting full translation.

```csharp
/// Discovers call sites and produces lightweight RawCallSite values.
internal static class CallSiteDiscovery
{
    /// Quick syntactic predicate for the incremental generator.
    public static bool IsQuarryMethodCandidate(SyntaxNode node);

    /// Transforms a syntax node into a RawCallSite.
    /// Parses expressions into SqlExpr but does NOT resolve columns or produce SQL.
    public static RawCallSite? Discover(
        GeneratorSyntaxContext context,
        CancellationToken ct);
}
```

**Key change from current `UsageSiteDiscovery`**: The current `DiscoverUsageSite` calls `ClauseTranslator.TranslateWhere()` (and similar) during discovery, which requires entity resolution. The new `Discover` only calls `SqlExprParser.Parse()` on the lambda body, producing an unresolved `SqlExpr`. No entity lookup, no SQL generation, no semantic model for column resolution. This makes discovery pure-syntactic and very fast.

**SyntaxNode handling**: The current `UsageSiteInfo` stores `SyntaxNode InvocationSyntax` for later semantic analysis during enrichment. `RawCallSite` does NOT store the `SyntaxNode`. Instead, all information needed from the syntax is extracted during discovery:
- `DiagnosticLocation` (file path, line, column, span) — value type, no tree reference
- `SqlExpr` — our own IR, no Roslyn references
- `ProjectionInfo` — extracted from syntax during discovery
- `InterceptableLocationData` — opaque string for `[InterceptsLocation]`

Semantic model queries that currently happen during enrichment (e.g., resolving `ITypeSymbol` for captured variables) are moved to `SqlExprAnnotator` which runs during discovery when the semantic model is available.

#### 7.2 `CallSiteBinder` — Produces BoundCallSite

Binds a `RawCallSite` against the `EntityRegistry` to produce a `BoundCallSite`.

```csharp
/// Binds raw call sites to entity metadata.
internal static class CallSiteBinder
{
    /// Resolves entity and context bindings for a call site.
    public static BoundCallSite Bind(
        RawCallSite raw,
        EntityRegistry registry);
}
```

**Algorithm**:
1. Look up `raw.EntityTypeName` in the registry
2. If multiple contexts define the same entity, resolve by `raw.ContextClassName` or choose the first (emit QRY015 diagnostic)
3. Populate `ContextClassName`, `ContextNamespace`, `Dialect`, `TableName`, `SchemaName`
4. Build `EntityRef` from the resolved `EntityInfo`
5. For join sites, resolve joined entity and build `JoinedEntity`
6. For insert/update sites, build `InsertInfo`/`UpdateInfo` from entity columns and `InitializedPropertyNames`

#### 7.3 `ClauseTranslatorV2` — Produces TranslatedCallSite

Translates `SqlExpr` in a `BoundCallSite` by binding column references and extracting parameters.

```csharp
/// Translates bound call sites by resolving SqlExpr columns and extracting parameters.
internal static class ClauseTranslatorV2
{
    /// Translates a bound call site's SqlExpr into a fully resolved TranslatedCallSite.
    public static TranslatedCallSite Translate(BoundCallSite bound);
}
```

**Algorithm**:
1. If `bound.Raw.Expression` is null (non-clause site), return `TranslatedCallSite` with null `Clause`
2. Call `SqlExprBinder.Bind()` with the entity metadata from `bound.Entity`
3. Extract parameters from `ParamSlotExpr` and `CapturedValueExpr` nodes in the bound tree
4. Assign clause-local parameter indices
5. For OrderBy/ThenBy/GroupBy, resolve `KeyTypeName` from the expression's column type
6. For Set, resolve `ValueTypeName` and `CustomTypeMappingClass`
7. Build `TranslatedClause` with the bound `SqlExpr`, parameters, and metadata

### Migration from UsageSiteInfo

`UsageSiteInfo` is used extensively throughout the codebase. The migration is:

| Current Usage | New Usage |
|---|---|
| Discovery (`UsageSiteDiscovery`) | `CallSiteDiscovery` → `RawCallSite` |
| Enrichment (`EnrichUsageSiteWithEntityInfo`) | `CallSiteBinder.Bind()` → `BoundCallSite` |
| Clause translation (semantic + deferred) | `ClauseTranslatorV2.Translate()` → `TranslatedCallSite` |
| Chain analysis (`ChainAnalyzer`) | Operates on `TranslatedCallSite` instead of `UsageSiteInfo` |
| SQL building (`CompileTimeSqlBuilder`) | Receives `QueryPlan` instead of `ChainedClauseSite` |
| Code generation (`InterceptorCodeGenerator`) | Receives `AssembledPlan` + `TranslatedCallSite` |
| Diagnostics | `RawCallSite.Location` (value type) replaces `SyntaxNode` references |

### Files Affected

| File | Action |
|---|---|
| `Models/UsageSiteInfo.cs` (~300 lines) | **Delete** after all consumers migrated |
| `Parsing/UsageSiteDiscovery.cs` | **Rewrite** as `CallSiteDiscovery` |
| `Parsing/AnalyzabilityChecker.cs` | **Keep** — analyzability logic still needed, works on syntax |
| `QuarryGenerator.cs` lines 310-548 (`GroupByFileAndProcess`) | **Decompose** — enrichment extracted to `CallSiteBinder`, translation to `ClauseTranslatorV2` |
| `Models/FileInterceptorGroup.cs` | **Update** — stores `TranslatedCallSite[]` instead of `UsageSiteInfo[]` |

New files:
- `IR/RawCallSite.cs`
- `IR/BoundCallSite.cs`
- `IR/TranslatedCallSite.cs`
- `IR/EntityRef.cs`
- `Parsing/CallSiteDiscovery.cs`
- `Binding/CallSiteBinder.cs`
- `Binding/ClauseTranslatorV2.cs`

### Validation

- All existing tests pass (same interceptors generated from same input)
- `UsageSiteInfo` is fully removed — no references remain
- `RawCallSite.Equals()` is measurably faster (benchmark with ~100 sites comparing old vs new equality)
- No `SyntaxNode` references escape discovery — verify via code inspection

---

## 8. Phase 3: QueryPlan IR

### Goal

Introduce `QueryPlan` as the intermediate representation between chain analysis and SQL rendering. This decouples query semantics from SQL text and makes subqueries a natural composition.

### Rationale

Currently, `CompileTimeSqlBuilder` receives `IReadOnlyList<ChainedClauseSite>` and builds SQL strings directly by iterating clause sites and extracting SQL fragments from `ClauseInfo.SqlFragment`. This tight coupling means:
- SQL building must understand clause site ordering and bitmask semantics
- Subqueries require special-casing in the expression translator
- Adding new SQL features (e.g., window functions, CTEs) requires changes to both the expression translator and the SQL builder
- The SQL builder cannot optimize query structure (e.g., merging WHERE conditions, simplifying tautologies)

With `QueryPlan`, chain analysis produces a logical plan, and SQL rendering is a separate, dialect-specific operation over that plan.

### New Components

#### 8.1 `QueryPlanBuilder` — Builds QueryPlan from Chain Analysis

Converts a `ChainAnalysisResult` (with `TranslatedCallSite` clause members) into a `QueryPlan`.

```csharp
/// Builds a QueryPlan from an analyzed chain.
internal static class QueryPlanBuilder
{
    /// Builds a complete QueryPlan from chain analysis results.
    public static QueryPlan Build(
        ChainAnalysisResult analysis,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        TranslatedCallSite executionSite,
        EntityRegistry registry);
}
```

**Algorithm**:
1. Determine `QueryKind` from execution site's `InterceptorKind`
2. Build `PrimaryTable` from execution site's entity binding
3. Walk clause sites in chain order:
   - `ClauseRole.Join` → `JoinPlan` with resolved ON condition `SqlExpr`
   - `ClauseRole.Where` / `ClauseRole.DeleteWhere` / `ClauseRole.UpdateWhere` → `WhereTerm` with the clause's bound `SqlExpr`
   - `ClauseRole.OrderBy` / `ClauseRole.ThenBy` → `OrderTerm`
   - `ClauseRole.GroupBy` → `GroupByExprs`
   - `ClauseRole.Having` → `HavingExprs`
   - `ClauseRole.Select` → `SelectProjection` from `ProjectionInfo`
   - `ClauseRole.Set` / `ClauseRole.UpdateSet` → `SetTerm`
   - `ClauseRole.Limit` → `PaginationPlan.LiteralLimit` or parameter
   - `ClauseRole.Offset` → `PaginationPlan.LiteralOffset` or parameter
   - `ClauseRole.Distinct` → `IsDistinct = true`
4. For conditional clauses (where `ChainedClauseSite.IsConditional`), assign `BitIndex` to the corresponding term
5. Collect all parameters from all clause expressions into a globally-indexed `Parameters` list
6. Copy `PossibleMasks` and `Tier` from chain analysis
7. Handle identity projection (no Select clause): build `SelectProjection` from entity metadata

#### 8.2 `QueryPlanRenderer` — Renders QueryPlan to SQL

Replaces `CompileTimeSqlBuilder`. Renders a `QueryPlan` to SQL strings for each bitmask variant.

```csharp
/// Renders QueryPlan to dialect-specific SQL strings.
internal static class QueryPlanRenderer
{
    /// Renders a complete SQL string for a specific bitmask variant.
    public static AssembledSqlVariant RenderSelect(
        QueryPlan plan,
        ulong mask,
        SqlDialect dialect);

    public static AssembledSqlVariant RenderDelete(
        QueryPlan plan,
        ulong mask,
        SqlDialect dialect);

    public static AssembledSqlVariant RenderUpdate(
        QueryPlan plan,
        ulong mask,
        SqlDialect dialect);

    public static AssembledSqlVariant RenderInsert(
        QueryPlan plan,
        SqlDialect dialect);

    /// Renders all variants and produces an AssembledPlan.
    public static AssembledPlan RenderAll(
        QueryPlan plan,
        SqlDialect dialect,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites);
}
```

**Algorithm for `RenderSelect`**:
1. Determine active clauses by checking each conditional term's `BitIndex` against the mask
2. Emit `SELECT` keyword
3. If `IsDistinct`, emit `DISTINCT`
4. Emit projection columns: if `IsIdentity`, emit `*`; otherwise emit comma-separated resolved column names
5. Emit `FROM` with `PrimaryTable` (quoted, schema-qualified, with alias if present)
6. For each active `JoinPlan`: emit join keyword, table reference, `ON`, render join condition via `SqlExprRenderer.Render()`
7. Collect active `WhereTerm`s, emit `WHERE` with `AND`-combined rendered conditions
8. Emit `GROUP BY` with rendered expressions
9. Emit `HAVING` with `AND`-combined rendered conditions
10. Emit `ORDER BY` with rendered terms and `ASC`/`DESC`
11. Emit `LIMIT`/`OFFSET` from `PaginationPlan`
12. Count total parameters from active clauses, return `AssembledSqlVariant`

For `SubQueryExpr` nodes encountered during expression rendering, recursively call `RenderSelect` on the embedded `QueryPlan`. This is how self-referencing queries work naturally.

#### 8.3 `SqlFragmentTemplate` Integration

`SqlFragmentTemplate` (the existing parameter slot mechanism) is preserved but generated from `SqlExpr` trees instead of from pre-built SQL strings. `SqlExprRenderer.RenderToTemplate()` produces a `SqlFragmentTemplate` from any `SqlExpr` containing `ParamSlotExpr` nodes.

### Subquery Support

With `QueryPlan`, subqueries become a first-class composition:

A user writes:
```csharp
db.Users.Where(u => u.Age > db.Users.Where(u2 => u2.Department == "Sales").Select(u2 => Sql.Avg(u2.Age)).ExecuteScalar())
```

During parsing (`SqlExprParser`), the inner query chain is recognized and parsed into a `SubQueryExpr` containing its own `QueryPlan`:

```
BinaryOp(
    ColumnRef("u", "Age"),
    GreaterThan,
    SubQuery(QueryPlan(
        From: users,
        Where: [ColumnRef("u2", "Department") = Literal("Sales")],
        Select: FunctionCall("AVG", [ColumnRef("u2", "Age")])
    ))
)
```

During binding, both the outer and inner column references are resolved independently. During rendering, the inner `QueryPlan` is rendered as a parenthesized SELECT subquery.

### Files Affected

| File | Action |
|---|---|
| `Sql/CompileTimeSqlBuilder.cs` (976 lines) | **Replace** with `QueryPlanRenderer` |
| `Sql/SqlFragmentTemplate.cs` (217 lines) | **Keep** — still used for parameterized rendering, now generated from `SqlExpr` |
| `Models/ChainAnalysisResult.cs` | **Simplify** — `ChainedClauseSite` references `TranslatedCallSite` instead of `UsageSiteInfo` |
| `Models/PrebuiltChainInfo.cs` | **Replace** with `AssembledPlan` |
| `Parsing/ChainAnalyzer.cs` | **Update** — produces `ChainAnalysisResult` over `TranslatedCallSite`, feeds into `QueryPlanBuilder` |

New files:
- `IR/QueryPlan.cs` — `QueryPlan` and all supporting types
- `IR/QueryPlanBuilder.cs`
- `IR/QueryPlanRenderer.cs`

### Validation

- All existing tests pass (equivalent SQL produced)
- Subquery expressions that previously required special-casing now work through composition
- SQL output is byte-equivalent for non-subquery cases (or behaviorally equivalent per the "allow improvements" decision)
- `CompileTimeSqlBuilder` static methods are fully replaced — no references remain

---

## 9. Phase 4: Incremental Pipeline Restructure

### Goal

Restructure `QuarryGenerator.Initialize()` to move per-site enrichment and translation BEFORE the `.Collect()` call, enabling the incremental pipeline to cache individual site results.

### Rationale

The current pipeline structure:
```
usageSites.Collect() → GroupByFileAndProcess(allSites)
```

The target pipeline structure:
```
rawSites × entityRegistry → boundSites (per-site, cached)
boundSites → translatedSites (per-site, cached)
translatedSites.Collect() → chainAnalysis + grouping
```

The key insight: `EntityRegistry` changes only when schema classes change (adding/removing columns, changing table names). In normal development, most edits are to query code, not schema definitions. By moving binding and translation before the `.Collect()`, the expensive per-site work is individually cached and only re-runs when the site itself or the entity registry changes.

### New Pipeline Structure

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    // Phase 1: Context discovery (unchanged)
    var contextDeclarations = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (node, _) => IsContextCandidate(node),
            transform: static (ctx, ct) => GetContextInfo(ctx, ct))
        .Where(static info => info is not null)
        .Select(static (info, _) => info!);

    // Phase 1 output: entity/context code generation (unchanged)
    context.RegisterSourceOutput(contextDeclarations,
        static (spc, contextInfo) => GenerateEntityAndContextCode(contextInfo, spc));

    // NEW: Build EntityRegistry from all contexts (collected, but rarely changes)
    var entityRegistry = contextDeclarations.Collect()
        .Select(static (contexts, ct) => EntityRegistry.Build(contexts, ct));

    // Phase 2: Raw call site discovery (SqlExpr parsed, no binding)
    var rawCallSites = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (node, _) => CallSiteDiscovery.IsQuarryMethodCandidate(node),
            transform: static (ctx, ct) => CallSiteDiscovery.Discover(ctx, ct))
        .Where(static site => site is not null)
        .Select(static (site, _) => site!);

    // NEW: Per-site binding (cached individually)
    var boundCallSites = rawCallSites
        .Combine(entityRegistry)
        .Select(static (pair, ct) => CallSiteBinder.Bind(pair.Left, pair.Right, ct));

    // NEW: Per-site translation (cached individually)
    var translatedCallSites = boundCallSites
        .Select(static (bound, ct) => ClauseTranslatorV2.Translate(bound, ct));

    // Phase 4-6: Chain analysis + SQL assembly + codegen (requires Collect)
    var perFileGroups = context.CompilationProvider
        .Combine(entityRegistry)
        .Combine(translatedCallSites.Collect())
        .SelectMany(static (data, ct) =>
            PipelineOrchestrator.AnalyzeAndGroup(
                data.Left.Left,   // Compilation
                data.Left.Right,  // EntityRegistry
                data.Right,       // TranslatedCallSite[]
                ct));

    // Per-file output
    context.RegisterSourceOutput(perFileGroups,
        static (spc, group) => CodeEmitter.Emit(spc, group));

    // Phase 3: Migration discovery (unchanged)
    // ...
}
```

### What Changes Inside the Collected Transform

The `PipelineOrchestrator.AnalyzeAndGroup()` method replaces `GroupByFileAndProcess()` but does significantly less work:

**Removed from the collected transform**:
- `BuildEntityLookup()` — entity registry is pre-built
- `EnrichUsageSiteWithEntityInfo()` — binding happens per-site before Collect
- Deferred clause translation — translation happens per-site before Collect
- Navigation join chain member discovery — resolved during binding

**Remaining in the collected transform**:
- Chain analysis (`ChainAnalyzer`) — still needs all sites in the same method
- `QueryPlanBuilder` — builds plans from chain results
- `QueryPlanRenderer` — materializes SQL
- Diagnostic collection
- File grouping

```csharp
/// Orchestrates the collected pipeline stages.
internal static class PipelineOrchestrator
{
    /// Analyzes chains, builds plans, and groups by file.
    public static ImmutableArray<FileOutputGroup> AnalyzeAndGroup(
        Compilation compilation,
        EntityRegistry registry,
        ImmutableArray<TranslatedCallSite> sites,
        CancellationToken ct);
}
```

### Performance Impact

**Before**: Editing one `.Where()` lambda triggers:
1. Re-discovery of the changed site (fast, already cached per-site)
2. `.Collect()` produces a new array (all sites)
3. `GroupByFileAndProcess` runs for ALL sites: enrichment, translation, chain analysis, SQL building, grouping

**After**: Editing one `.Where()` lambda triggers:
1. Re-discovery of the changed site → new `RawCallSite`
2. Re-binding of the changed site → new `BoundCallSite` (fast: dictionary lookup)
3. Re-translation of the changed site → new `TranslatedCallSite` (one expression bind + render)
4. `.Collect()` produces a new array (all sites, but most are cached `TranslatedCallSite` objects)
5. `AnalyzeAndGroup` runs: chain analysis for affected methods, SQL assembly for affected chains, grouping

Steps 2-3 are O(1) work per changed site. Step 5 still processes all sites for grouping but chain analysis is only expensive for the affected method.

### Navigation Join Discovery

Currently, `DiscoverNavigationJoinChainMembers()` creates new `UsageSiteInfo` objects for implicit join sites that weren't discovered in the initial syntax scan. In the new architecture, navigation joins are resolved during binding:

When `CallSiteBinder.Bind()` processes a join site with `IsNavigationJoin = true`, it resolves the foreign key relationship from entity metadata and synthesizes the ON condition as a `SqlExpr`. No separate discovery phase is needed because the binding phase has access to the full `EntityRegistry` including navigation properties.

If navigation joins create additional chain members that need interceptors, `CallSiteBinder` produces `BoundCallSite` values for them, which are emitted as additional pipeline values. The incremental pipeline handles this via a `SelectMany` after binding that can produce zero or more bound sites per raw site.

### Files Affected

| File | Action |
|---|---|
| `QuarryGenerator.cs` | **Major rewrite** — `Initialize()` restructured, `GroupByFileAndProcess()` decomposed into `PipelineOrchestrator` |
| `Models/FileInterceptorGroup.cs` | **Replace** with `FileOutputGroup` containing `TranslatedCallSite[]` and `AssembledPlan[]` |

New files:
- `Pipeline/PipelineOrchestrator.cs`
- `Pipeline/EntityRegistry.cs`
- `Pipeline/FileOutputGroup.cs`

### Validation

- All existing tests pass
- Incremental caching verified: change one site, confirm only that site's binding/translation re-runs (can be verified with Roslyn's incremental generator driver test infrastructure)
- Memory usage: no `SyntaxNode` references persist in cached pipeline values

---

## 10. Phase 5: Carrier Redesign

### Goal

Redesign carrier class generation as a codegen strategy applied over `AssembledPlan`, replacing the current approach where carrier concerns are woven into chain analysis, parameter building, and SQL assembly.

### Rationale

Currently, carrier-related logic is spread across:
- `QuarryGenerator.BuildChainParameters()` (~150 lines) — decides carrier eligibility based on parameter types, collection support, type mappings
- `QuarryGenerator.BuildPrebuiltChainInfo()` (~100 lines) — checks carrier eligibility conditions (reader delegate, SQL validity, MySQL restrictions)
- `QuarryGenerator.TokenizeCollectionParameters()` (~55 lines) — modifies SQL strings for carrier collection expansion
- `CarrierClassBuilder.Build()` (~100 lines) — constructs the carrier class descriptor
- `CarrierClassBuilder.SelectBaseClass()` — determines inheritance
- `InterceptorCodeGenerator.Carrier.cs` (1,068 lines) — emits carrier class and carrier-path interceptor methods
- `InterceptorCodeGenerator.ResolveCarrierBaseClass()` — determines carrier base class
- `InterceptorCodeGenerator.WouldExecutionTerminalBeEmitted()` — pre-checks terminal emission

This spread means adding a new carrier feature (e.g., struct carriers, pooled carriers) requires changes in 6+ locations across 4 files.

### New Components

#### 10.1 `CarrierAnalyzer` — Determines Carrier Strategy

A single analysis pass that inspects an `AssembledPlan` and determines the carrier strategy.

```csharp
/// Analyzes an AssembledPlan and determines carrier optimization strategy.
internal static class CarrierAnalyzer
{
    /// Determines whether and how a plan should use carrier optimization.
    public static CarrierStrategy Analyze(AssembledPlan plan);
}

/// Describes the carrier optimization strategy for a plan.
internal sealed class CarrierStrategy : IEquatable<CarrierStrategy>
{
    public bool IsEligible { get; }
    public string? IneligibleReason { get; }
    public string BaseClassName { get; }
    public IReadOnlyList<CarrierField> Fields { get; }
    public IReadOnlyList<CarrierStaticField> StaticFields { get; }
    public IReadOnlyList<CarrierParameter> Parameters { get; }
}

/// A carrier parameter with full extraction metadata.
internal sealed class CarrierParameter : IEquatable<CarrierParameter>
{
    public int GlobalIndex { get; }
    public string FieldName { get; }       // "P0", "P1", ...
    public string FieldType { get; }       // Normalized CLR type
    public string? ExtractionCode { get; } // Code to extract value at the interceptor site
    public string? BindingCode { get; }    // Code to bind value to DbParameter at terminal
    public string? TypeMappingClass { get; }
    public bool IsCollection { get; }
    public bool IsSensitive { get; }
    public bool IsEntitySourced { get; }   // Read from Entity field, not P{n}
}
```

**Eligibility algorithm** (consolidated from current 6 locations):
1. Chain must be Tier 1 (`PrebuiltDispatch`)
2. No unmatched method names in the chain
3. All parameters must have resolved CLR types (not `object?` or `?`)
4. Collection parameters must have resolved element types
5. Set/OrderBy/GroupBy clauses must have resolved key/value types
6. For SELECT queries: result type must be resolvable, reader delegate must exist
7. For UPDATE/DELETE: SQL must be non-empty and well-formed
8. For INSERT: SQL must be non-empty; MySQL `ExecuteScalar` is ineligible
9. All captured parameters must have direct extraction paths (or use Action<T> delegate.Target pattern)

#### 10.2 `CarrierEmitter` — Generates Carrier Code

Replaces `InterceptorCodeGenerator.Carrier.cs` and `CarrierClassBuilder`. A focused emitter that takes `CarrierStrategy` and produces C# source.

```csharp
/// Emits carrier class source code and carrier-path interceptor bodies.
internal static class CarrierEmitter
{
    /// Emits the carrier class definition.
    public static void EmitCarrierClass(
        StringBuilder sb,
        CarrierStrategy strategy,
        AssembledPlan plan,
        int chainIndex);

    /// Emits a carrier-path interceptor method body for a clause site.
    public static void EmitCarrierClauseBody(
        StringBuilder sb,
        CarrierStrategy strategy,
        TranslatedCallSite site,
        int chainIndex);

    /// Emits a carrier-path terminal method body.
    public static void EmitCarrierTerminalBody(
        StringBuilder sb,
        CarrierStrategy strategy,
        AssembledPlan plan,
        int chainIndex);
}
```

#### 10.3 Collection Parameter Tokenization

Currently, `TokenizeCollectionParameters()` modifies SQL strings in `sqlMap` by replacing parameter placeholders with expansion tokens. In the new architecture, this is handled during rendering:

`QueryPlanRenderer` checks each `QueryParameter` for `IsCollection`. When rendering an `InExpr` whose value is a collection parameter, the renderer emits the expansion token `{__COL_P{index}__}` directly instead of a standard parameter placeholder. No post-processing needed.

### Files Affected

| File | Action |
|---|---|
| `Generation/CarrierClassBuilder.cs` (~200 lines) | **Delete** — replaced by `CarrierAnalyzer` + `CarrierEmitter` |
| `Generation/InterceptorCodeGenerator.Carrier.cs` (1,068 lines) | **Replace** with `CarrierEmitter` |
| `Models/CarrierClassInfo.cs` (~220 lines) | **Simplify** — `CarrierStrategy` replaces this |
| `Models/ChainParameterInfo.cs` (~140 lines) | **Replace** with `CarrierParameter` |
| `QuarryGenerator.cs` `BuildChainParameters()` | **Delete** — absorbed into `CarrierAnalyzer` |
| `QuarryGenerator.cs` `TokenizeCollectionParameters()` | **Delete** — handled in `QueryPlanRenderer` |

New files:
- `CodeGen/CarrierAnalyzer.cs`
- `CodeGen/CarrierEmitter.cs`
- `CodeGen/CarrierStrategy.cs`

### Validation

- All existing tests pass
- Carrier-optimized interceptors produce equivalent code
- Carrier eligibility decisions match current behavior (verified by comparing diagnostic output for QRY030/QRY031/QRY032)
- No carrier logic remains in `QuarryGenerator.cs`, `ChainAnalyzer.cs`, or `CompileTimeSqlBuilder` successors

---

## 11. Phase 6: Codegen Consolidation

### Goal

Consolidate the `InterceptorCodeGenerator` partial files (currently 7 files, ~5,900 lines) into a structured emitter system organized by concern rather than by method kind.

### Rationale

The current `InterceptorCodeGenerator` is organized into partial files by query type:
- `.cs` — file header, namespace, attribute definition, `GenerateInterceptorsFile()`
- `.Query.cs` — `GenerateInterceptorMethod()`, clause interceptors
- `.Execution.cs` — execution terminal interceptors (FetchAll, FetchFirst, etc.)
- `.Joins.cs` — join interceptor methods
- `.Modifications.cs` — delete/update interceptor methods
- `.Carrier.cs` — carrier class emission and carrier-path interceptors
- `.Utilities.cs` — shared helpers (type name resolution, parameter formatting)
- `.RawSql.cs` — RawSqlAsync/RawSqlScalarAsync interceptors

This organization leads to:
- Large `switch` statements dispatching by `InterceptorKind` across multiple files
- Carrier vs non-carrier branching duplicated in every interceptor method
- Shared state (`staticFields`, `chainLookup`, `carrierLookup`) threaded through as dictionary parameters

### New Components

#### 11.1 Emitter Architecture

Replace the monolithic `InterceptorCodeGenerator` with a set of focused emitters:

```csharp
/// Top-level code emitter for a file output group.
internal static class FileEmitter
{
    /// Emits the complete interceptors file for a (context, file) group.
    public static string Emit(FileOutputGroup group);
}

/// Emits interceptor method signatures and [InterceptsLocation] attributes.
internal static class InterceptorSignatureEmitter
{
    /// Emits the method signature with [InterceptsLocation] attribute.
    public static void EmitSignature(
        StringBuilder sb,
        TranslatedCallSite site,
        bool isCarrierPath);
}

/// Emits clause interceptor bodies (Where, OrderBy, GroupBy, Having, Set).
internal static class ClauseBodyEmitter
{
    /// Emits the body of a clause interceptor method.
    public static void EmitClauseBody(
        StringBuilder sb,
        TranslatedCallSite site,
        AssembledPlan? chainPlan);
}

/// Emits execution terminal interceptor bodies.
internal static class TerminalBodyEmitter
{
    /// Emits the body of an execution terminal method.
    public static void EmitTerminalBody(
        StringBuilder sb,
        AssembledPlan plan,
        TranslatedCallSite executionSite);
}

/// Emits join clause interceptor bodies.
internal static class JoinBodyEmitter
{
    /// Emits the body of a join clause interceptor method.
    public static void EmitJoinBody(
        StringBuilder sb,
        TranslatedCallSite site,
        AssembledPlan? chainPlan);
}

/// Emits raw SQL interceptor bodies.
internal static class RawSqlBodyEmitter
{
    public static void EmitRawSqlBody(StringBuilder sb, TranslatedCallSite site);
}
```

#### 11.2 Emitter Dispatch

Instead of the current approach where `GenerateInterceptorMethod()` is a 100+ line method with nested `switch` statements and carrier/non-carrier branching, dispatch is handled by a simple router:

```csharp
/// Routes each site to the appropriate emitter.
internal static class InterceptorRouter
{
    /// Emits a complete interceptor method for a site.
    public static void EmitInterceptor(
        StringBuilder sb,
        TranslatedCallSite site,
        AssembledPlan? plan,
        CarrierStrategy? carrier);
}
```

**Algorithm**:
1. If `carrier != null` and this site is a carrier member, delegate to `CarrierEmitter`
2. Else, based on `site.Raw.Kind`:
   - Clause kinds (Where, OrderBy, etc.) → `ClauseBodyEmitter`
   - Join kinds → `JoinBodyEmitter`
   - Execution kinds → `TerminalBodyEmitter`
   - RawSql kinds → `RawSqlBodyEmitter`
   - Transition kinds (Delete, Update, Insert, All) → skip on non-carrier path
   - Limit/Offset/Distinct/WithTimeout → skip on non-carrier path

#### 11.3 Reader Code Generation

`ReaderCodeGenerator` and `ProjectionAnalyzer` are largely unchanged. They operate on `ProjectionInfo` which is preserved in the new IR. The only change is that they receive `ProjectionInfo` from `AssembledPlan.Plan.Projection` instead of from `UsageSiteInfo.ProjectionInfo`.

### Files Affected

| File | Action |
|---|---|
| `Generation/InterceptorCodeGenerator.cs` (523 lines) | **Replace** with `FileEmitter` |
| `Generation/InterceptorCodeGenerator.Query.cs` (1,068 lines) | **Replace** with `InterceptorRouter` + `ClauseBodyEmitter` |
| `Generation/InterceptorCodeGenerator.Execution.cs` (981 lines) | **Replace** with `TerminalBodyEmitter` |
| `Generation/InterceptorCodeGenerator.Joins.cs` (744 lines) | **Replace** with `JoinBodyEmitter` |
| `Generation/InterceptorCodeGenerator.Modifications.cs` (760 lines) | **Merge** into `ClauseBodyEmitter` and `TerminalBodyEmitter` |
| `Generation/InterceptorCodeGenerator.RawSql.cs` (185 lines) | **Replace** with `RawSqlBodyEmitter` |
| `Generation/InterceptorCodeGenerator.Utilities.cs` (583 lines) | **Distribute** — type name helpers → `TypeNameHelpers.cs`, parameter formatting → `ParameterFormatting.cs` |
| `Generation/InterceptorCodeGenerator.Carrier.cs` (1,068 lines) | Already handled in Phase 5 |

New files:
- `CodeGen/FileEmitter.cs`
- `CodeGen/InterceptorRouter.cs`
- `CodeGen/InterceptorSignatureEmitter.cs`
- `CodeGen/ClauseBodyEmitter.cs`
- `CodeGen/TerminalBodyEmitter.cs`
- `CodeGen/JoinBodyEmitter.cs`
- `CodeGen/RawSqlBodyEmitter.cs`
- `CodeGen/TypeNameHelpers.cs`
- `CodeGen/ParameterFormatting.cs`

### Validation

- All existing tests pass
- Generated interceptor code is functionally equivalent (may differ in formatting)
- No references to old `InterceptorCodeGenerator` remain
- Each emitter file is under 500 lines (reduced from current 1,000+ line partials)

---

## 12. Cross-Cutting Concerns

### 12.1 Equality Implementation Strategy

All IR types must implement `IEquatable<T>` for the incremental pipeline. Given the netstandard2.0 constraint (no records), each type needs manual `Equals()` and `GetHashCode()`.

**Strategy**: Use a consistent pattern across all IR types:

```csharp
public bool Equals(T? other)
{
    if (other is null) return false;
    if (ReferenceEquals(this, other)) return true;
    // Compare fields in order of most likely to differ first
    // (cheap discriminators before expensive collections)
    return Field1 == other.Field1
        && Field2 == other.Field2
        && EqualityHelpers.SequenceEqual(Collection1, other.Collection1);
}

public override int GetHashCode()
{
    // Include only the most discriminating fields
    return HashCode.Combine(Field1, Field2, Collection1.Count);
}
```

For `SqlExpr` trees, equality is structural (recursive). The existing `SyntacticExpression.DeepEquals()` pattern is adapted.

### 12.2 SqlExpr Tree Equality

`SqlExpr.Equals()` must perform deep structural comparison. This is used heavily by the incremental pipeline to detect changes.

**Optimization**: Pre-compute a structural hash on construction. Each `SqlExpr` node computes its hash from its kind and children's hashes. This makes `GetHashCode()` O(1) after construction and `Equals()` O(1) for non-matching trees (different hashes).

```csharp
internal abstract class SqlExpr : IEquatable<SqlExpr>
{
    private readonly int _cachedHashCode;

    protected SqlExpr(int hashCode)
    {
        _cachedHashCode = hashCode;
    }

    public sealed override int GetHashCode() => _cachedHashCode;

    public bool Equals(SqlExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_cachedHashCode != other._cachedHashCode) return false;
        if (Kind != other.Kind) return false;
        return DeepEquals(other);
    }

    protected abstract bool DeepEquals(SqlExpr other);
}
```

### 12.3 Diagnostic Reporting

Currently, diagnostics are collected in `List<DiagnosticInfo>` during `GroupByFileAndProcess()` and reported in `EmitFileInterceptors()`. The new architecture splits diagnostic collection across stages:

- **Stage 1 (Discovery)**: QRY001 (not analyzable), QRY014 (anonymous type)
- **Stage 2 (Binding)**: QRY015 (ambiguous context)
- **Stage 3 (Translation)**: QRY016 (unbound parameter), QRY019 (clause not translatable)
- **Stage 4 (Chain Analysis)**: QRY030-032 (chain tier diagnostics), QRY033 (forked chain)

Diagnostics are stored as `DiagnosticInfo` values (file path, line, column, message args) on each IR type. They flow through the pipeline and are emitted at the final `RegisterSourceOutput`.

### 12.4 Error Recovery

Each pipeline stage must handle failures gracefully:
- `SqlExprParser`: Unknown expression kinds → `SqlRawExpr` with error marker
- `SqlExprBinder`: Unresolvable column → `SqlRawExpr` with error, clause marked as failed
- `CallSiteBinder`: Entity not found → `BoundCallSite` with null entity, site skipped
- `ClauseTranslatorV2`: Translation failure → `TranslatedClause.IsSuccess = false`
- `QueryPlanBuilder`: Chain with failed clauses → `QueryPlan.Tier = RuntimeBuild`

Failed sites fall through to the runtime path (existing `SqlBuilder`), exactly as they do today.

### 12.5 Namespace Organization

The new IR types and pipeline components are organized into sub-namespaces:

```
Quarry.Generators.IR/          SqlExpr, RawCallSite, BoundCallSite, TranslatedCallSite, QueryPlan
Quarry.Generators.Parsing/     CallSiteDiscovery, ChainAnalyzer (updated), AnalyzabilityChecker
Quarry.Generators.Binding/     CallSiteBinder, ClauseTranslatorV2, SqlExprBinder
Quarry.Generators.Pipeline/    PipelineOrchestrator, EntityRegistry, FileOutputGroup
Quarry.Generators.CodeGen/     FileEmitter, InterceptorRouter, CarrierAnalyzer, CarrierEmitter, ...
Quarry.Generators.Sql/         SqlExprRenderer, QueryPlanRenderer, SqlFragmentTemplate
Quarry.Generators.Models/      Existing models (EntityInfo, ContextInfo, ColumnInfo, etc.)
```

### 12.6 Performance Monitoring

To validate incremental improvements, add optional timing instrumentation:

```csharp
/// Conditional timing for pipeline stage performance measurement.
/// Compiled out in release builds via #if DEBUG.
internal static class PipelineMetrics
{
    [Conditional("DEBUG")]
    public static void RecordStageTime(string stage, long elapsedMs);

    [Conditional("DEBUG")]
    public static void RecordCacheHit(string stage);

    [Conditional("DEBUG")]
    public static void RecordCacheMiss(string stage);
}
```

This is not shipped in the NuGet package but aids development.

---

## 13. Migration Strategy

### Principle: One Phase at a Time, All Tests Green

Each phase is a self-contained refactoring step. At the end of each phase, all existing tests must pass. No phase depends on a future phase being complete — each is independently shippable.

### Phase 1 Migration Path

1. Create `IR/SqlExpr.cs` and all node types
2. Create `IR/SqlExprParser.cs` — port parsing logic from `SyntacticExpressionParser`
3. Create `IR/SqlExprAnnotator.cs` — extract semantic enrichment from `ExpressionSyntaxTranslator`
4. Create `IR/SqlExprBinder.cs` — extract column resolution from `ExpressionTranslationContext`
5. Create `IR/SqlExprRenderer.cs` — extract SQL rendering from `ExpressionSyntaxTranslator`
6. Update `ClauseTranslator` to use the new pipeline internally: `SqlExprParser.Parse()` → `SqlExprAnnotator.Annotate()` → `SqlExprBinder.Bind()` → `SqlExprRenderer.Render()`
7. Verify all tests pass
8. Update `SyntacticClauseTranslator` callers to use `SqlExprBinder` + `SqlExprRenderer` on existing `SyntacticExpression` (temporary adapter)
9. Verify all tests pass
10. Remove `SyntacticExpressionParser`, `SyntacticClauseTranslator`, `SyntacticExpression`, `PendingClauseInfo`
11. Verify all tests pass

**Temporary adapter** (step 8): During migration, `SyntacticExpression` trees can be converted to `SqlExpr` trees via a simple recursive adapter. This avoids big-bang replacement of the deferred translation path.

### Phase 2 Migration Path

1. Create `IR/RawCallSite.cs`, `IR/BoundCallSite.cs`, `IR/TranslatedCallSite.cs`
2. Create `Parsing/CallSiteDiscovery.cs` — port from `UsageSiteDiscovery`, return `RawCallSite`
3. Create `Binding/CallSiteBinder.cs` — port enrichment from `GroupByFileAndProcess`
4. Create `Binding/ClauseTranslatorV2.cs` — port clause translation
5. Add a temporary adapter: `TranslatedCallSite.ToLegacyUsageSiteInfo()` that constructs a `UsageSiteInfo` from the new types. This allows downstream code (chain analysis, codegen) to continue working unchanged while upstream is migrated.
6. Wire new discovery into `Initialize()`, with adapter at the boundary
7. Verify all tests pass
8. Migrate `ChainAnalyzer` to accept `TranslatedCallSite` instead of `UsageSiteInfo`
9. Migrate `InterceptorCodeGenerator` to accept `TranslatedCallSite`
10. Remove adapter and `UsageSiteInfo`
11. Verify all tests pass

### Phase 3 Migration Path

1. Create `IR/QueryPlan.cs` and supporting types
2. Create `IR/QueryPlanBuilder.cs` — build plans from chain analysis results
3. Create `IR/QueryPlanRenderer.cs` — port SQL rendering from `CompileTimeSqlBuilder`
4. Create `AssembledPlan` — replaces `PrebuiltChainInfo`
5. Add temporary adapter: `AssembledPlan.ToLegacyPrebuiltChainInfo()` for downstream codegen
6. Wire `QueryPlanBuilder` + `QueryPlanRenderer` into the pipeline, with adapter
7. Verify all tests pass
8. Migrate codegen to consume `AssembledPlan` directly
9. Remove adapter and `PrebuiltChainInfo`
10. Remove `CompileTimeSqlBuilder`
11. Verify all tests pass

### Phase 4 Migration Path

1. Create `Pipeline/EntityRegistry.cs`
2. Create `Pipeline/PipelineOrchestrator.cs`
3. Restructure `Initialize()` to use new pipeline (per-site binding/translation before Collect)
4. Verify all tests pass
5. Remove old `GroupByFileAndProcess()`
6. Verify incremental caching works via Roslyn's `GeneratorDriver` test API

### Phase 5 Migration Path

1. Create `CodeGen/CarrierAnalyzer.cs` — consolidate eligibility logic
2. Create `CodeGen/CarrierEmitter.cs` — port carrier code generation
3. Wire into codegen pipeline
4. Verify all tests pass
5. Remove `CarrierClassBuilder`, `ChainParameterInfo`, carrier-related code from `QuarryGenerator`

### Phase 6 Migration Path

1. Create new emitter files (`FileEmitter`, `InterceptorRouter`, `ClauseBodyEmitter`, etc.)
2. Port interceptor generation one kind at a time (clauses → terminals → joins → modifications → raw SQL)
3. Verify tests after each kind migration
4. Remove old `InterceptorCodeGenerator` partial files
5. Verify all tests pass

---

## 14. Risk Analysis

### High Risk

**Roslyn incremental pipeline semantics**: The pipeline restructure (Phase 4) changes how `Combine` and `Select` interact. Subtle bugs in equality implementations can cause:
- False cache hits (stale output) — if `Equals()` is too permissive
- Cache thrashing (no caching benefit) — if `Equals()` is too strict or `GetHashCode()` has poor distribution

**Mitigation**: Extensive equality testing. Write unit tests for every IR type's `Equals()` and `GetHashCode()`. Use Roslyn's `GeneratorDriver` to verify incremental behavior in integration tests.

**Expression translator parity**: The unified `SqlExpr` pipeline must produce SQL identical to both the semantic and syntactic translators for all supported expression forms. Any regression here means incorrect SQL at runtime.

**Mitigation**: The existing test suite covers expression translation extensively. Run all tests after each step of Phase 1. Consider temporarily running BOTH old and new translators in parallel and asserting output equality.

### Medium Risk

**Chain analysis compatibility**: `ChainAnalyzer` currently works with `UsageSiteInfo` and relies on `InvocationSyntax` for syntax tree walking. The migration to `TranslatedCallSite` removes syntax node access, which means chain analysis must use different signals.

**Mitigation**: `RawCallSite` captures all information needed for chain analysis (file path, line, column, method name, uniqueId). The `ChainAnalyzer` already uses these for matching. The syntax tree walk for variable flow analysis may need a compilation-based fallback, but this can be designed in Phase 2.

**Carrier regression**: The carrier system is highly tuned with many edge cases (collection expansion, enum casting, type mapping, entity-sourced parameters, FieldInfo caching). Consolidating this into `CarrierAnalyzer` + `CarrierEmitter` risks missing edge cases.

**Mitigation**: Preserve existing carrier test coverage. Add diagnostic output comparison tests that verify carrier eligibility decisions match before/after.

### Low Risk

**Performance regression in codegen**: The new emitter architecture processes the same information with more indirection (router → emitter → helpers). This is unlikely to be measurable since codegen is I/O-bound (StringBuilder operations).

**File organization churn**: Moving to a new namespace structure creates a large diff that is hard to review. Each phase should be a separate PR/commit.

---

## 15. Phase Dependency Graph

```
Phase 1 (SqlExpr IR)
    │
    ├───→ Phase 2 (Layered Call Sites)
    │         │
    │         ├───→ Phase 3 (QueryPlan IR)
    │         │         │
    │         │         ├───→ Phase 4 (Pipeline Restructure)
    │         │         │
    │         │         └───→ Phase 5 (Carrier Redesign)
    │         │                   │
    │         │                   └───→ Phase 6 (Codegen Consolidation)
    │         │
    │         └───→ Phase 6 (Codegen Consolidation) [partial — can start after Phase 2]
    │
    └───→ Phase 3 (QueryPlan IR) [Phase 1 is sufficient for QueryPlan's SqlExpr usage]
```

**Critical path**: Phase 1 → Phase 2 → Phase 3 → Phase 4

**Parallelizable**:
- Phase 5 can start after Phase 3 (needs QueryPlan but not pipeline restructure)
- Phase 6 can start partially after Phase 2 (non-carrier emitters don't need carrier redesign)

**Recommended order**: 1 → 2 → 3 → 4 → 5 → 6

Each phase is expected to be a separate branch/PR cycle, with all tests green at merge.
