using System.Collections.Generic;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Projection;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;

namespace Quarry.Tests.Testing;

/// <summary>
/// Fluent builder for constructing TranslatedCallSite instances in tests.
/// Replaces direct UsageSiteInfo construction for unit tests that exercise emitters.
/// </summary>
internal class TestCallSiteBuilder
{
    private string _methodName = "TestMethod";
    private string _filePath = "TestFile.cs";
    private int _line = 10;
    private int _column = 10;
    private string _uniqueId = "test123";
    private InterceptorKind _kind = InterceptorKind.Where;
    private BuilderKind _builderKind = BuilderKind.Query;
    private string _entityTypeName = "User";
    private string? _resultTypeName;
    private bool _isAnalyzable = true;
    private string? _interceptableLocationData = "dGVzdGRhdGE=";
    private int _interceptableLocationVersion = 1;
    private ProjectionInfo? _projectionInfo;
    private string? _builderTypeName;
    private string _contextClassName = "TestDb";
    private string? _contextNamespace = "TestApp";
    private GenSqlDialect _dialect = GenSqlDialect.SQLite;
    private string _tableName = "users";
    private string? _schemaName;
    private TranslatedClause? _clause;
    private string? _keyTypeName;
    private string? _valueTypeName;
    private string? _joinedEntityTypeName;
    private IReadOnlyList<string>? _joinedEntityTypeNames;
    private RawSqlTypeInfo? _rawSqlTypeInfo;
    private string? _customEntityReaderClass;
    private IReadOnlyList<ColumnInfo>? _columns;
    private NestingContext? _nestingContext;

    public TestCallSiteBuilder WithMethodName(string name) { _methodName = name; return this; }
    public TestCallSiteBuilder WithFilePath(string path) { _filePath = path; return this; }
    public TestCallSiteBuilder WithLine(int line) { _line = line; return this; }
    public TestCallSiteBuilder WithColumn(int col) { _column = col; return this; }
    public TestCallSiteBuilder WithUniqueId(string id) { _uniqueId = id; return this; }
    public TestCallSiteBuilder WithKind(InterceptorKind kind) { _kind = kind; return this; }
    public TestCallSiteBuilder WithBuilderKind(BuilderKind kind) { _builderKind = kind; return this; }
    public TestCallSiteBuilder WithEntityType(string type) { _entityTypeName = type; return this; }
    public TestCallSiteBuilder WithResultType(string? type) { _resultTypeName = type; return this; }
    public TestCallSiteBuilder WithAnalyzable(bool value) { _isAnalyzable = value; return this; }
    public TestCallSiteBuilder WithLocationData(string? data) { _interceptableLocationData = data; return this; }
    public TestCallSiteBuilder WithLocationVersion(int ver) { _interceptableLocationVersion = ver; return this; }
    public TestCallSiteBuilder WithProjection(ProjectionInfo? proj) { _projectionInfo = proj; return this; }
    public TestCallSiteBuilder WithBuilderTypeName(string? type) { _builderTypeName = type; return this; }
    public TestCallSiteBuilder WithContext(string className, string? ns = "TestApp")
    {
        _contextClassName = className;
        _contextNamespace = ns;
        return this;
    }
    public TestCallSiteBuilder WithDialect(GenSqlDialect dialect) { _dialect = dialect; return this; }
    public TestCallSiteBuilder WithTable(string name, string? schema = null)
    {
        _tableName = name;
        _schemaName = schema;
        return this;
    }
    public TestCallSiteBuilder WithClause(TranslatedClause? clause) { _clause = clause; return this; }
    public TestCallSiteBuilder WithKeyType(string? type) { _keyTypeName = type; return this; }
    public TestCallSiteBuilder WithValueType(string? type) { _valueTypeName = type; return this; }
    public TestCallSiteBuilder WithJoinedEntityType(string? type) { _joinedEntityTypeName = type; return this; }
    public TestCallSiteBuilder WithJoinedEntityTypeNames(IReadOnlyList<string>? types) { _joinedEntityTypeNames = types; return this; }
    public TestCallSiteBuilder WithRawSqlTypeInfo(RawSqlTypeInfo? info) { _rawSqlTypeInfo = info; return this; }
    public TestCallSiteBuilder WithCustomEntityReader(string? readerClass) { _customEntityReaderClass = readerClass; return this; }
    public TestCallSiteBuilder WithColumns(IReadOnlyList<ColumnInfo>? cols) { _columns = cols; return this; }
    public TestCallSiteBuilder WithNestingContext(string conditionText, int nestingDepth, BranchKind branchKind = BranchKind.Independent)
    {
        _nestingContext = new NestingContext(conditionText, nestingDepth, branchKind);
        return this;
    }
    public TestCallSiteBuilder WithInsertInfo(InsertInfo? info) { _insertInfo = info; return this; }
    public TestCallSiteBuilder WithUpdateInfo(InsertInfo? info) { _updateInfo = info; return this; }
    private InsertInfo? _insertInfo;
    private InsertInfo? _updateInfo;

    public TranslatedCallSite Build()
    {
        var builderTypeName = _builderTypeName ?? InferBuilderTypeName();

        var raw = new RawCallSite(
            methodName: _methodName,
            filePath: _filePath,
            line: _line,
            column: _column,
            uniqueId: _uniqueId,
            kind: _kind,
            builderKind: _builderKind,
            entityTypeName: _entityTypeName,
            resultTypeName: _resultTypeName,
            isAnalyzable: _isAnalyzable,
            nonAnalyzableReason: null,
            interceptableLocationData: _interceptableLocationData,
            interceptableLocationVersion: _interceptableLocationVersion,
            location: default,
            projectionInfo: _projectionInfo,
            joinedEntityTypeName: _joinedEntityTypeName,
            builderTypeName: builderTypeName,
            joinedEntityTypeNames: _joinedEntityTypeNames,
            nestingContext: _nestingContext,
            contextClassName: _contextClassName,
            contextNamespace: _contextNamespace);

        // RawSqlTypeInfo is a mutable enrichment property (set by DisplayClassEnricher in the real pipeline)
        raw.RawSqlTypeInfo = _rawSqlTypeInfo;

        var columns = _columns ?? new List<ColumnInfo>();
        var entityRef = new EntityRef(
            entityName: _entityTypeName,
            tableName: _tableName,
            schemaName: _schemaName,
            schemaNamespace: _contextNamespace ?? "",
            columns: columns,
            navigations: new List<NavigationInfo>(),
            customEntityReaderClass: _customEntityReaderClass);

        var bound = new BoundCallSite(
            raw: raw,
            contextClassName: _contextClassName,
            contextNamespace: _contextNamespace ?? "",
            dialectConfig: new GenSqlDialectConfig(_dialect),
            tableName: _tableName,
            schemaName: _schemaName,
            entity: entityRef,
            joinedEntityTypeNames: _joinedEntityTypeNames,
            rawSqlTypeInfo: _rawSqlTypeInfo,
            insertInfo: _insertInfo,
            updateInfo: _updateInfo);

        return new TranslatedCallSite(bound, _clause, _keyTypeName, _valueTypeName);
    }

    private string InferBuilderTypeName()
    {
        var prefix = _builderKind switch
        {
            BuilderKind.Query => _resultTypeName != null
                ? $"Quarry.Query.IQueryBuilder<{_entityTypeName}, {_resultTypeName}>"
                : $"Quarry.Query.IEntityAccessor<{_entityTypeName}>",
            BuilderKind.Delete => $"Quarry.Query.IDeleteBuilder<{_entityTypeName}>",
            BuilderKind.Update => $"Quarry.Query.IUpdateBuilder<{_entityTypeName}>",
            BuilderKind.ExecutableUpdate => $"Quarry.Query.IExecutableUpdateBuilder<{_entityTypeName}>",
            // No BuilderKind.Insert — insert operations use Query builder kind
            _ => $"Quarry.Query.IQueryBuilder<{_entityTypeName}>"
        };
        return prefix;
    }

    /// <summary>
    /// Creates a TranslatedClause for join tests with the given table and ON condition.
    /// </summary>
    public static TranslatedClause CreateJoinClause(
        string joinedTableName,
        string onConditionSql = "\"t0\".\"Id\" = \"t1\".\"Id\"",
        JoinClauseKind joinKind = JoinClauseKind.Inner,
        string? joinedSchemaName = null,
        string? tableAlias = null)
    {
        return new TranslatedClause(
            ClauseKind.Join,
            new ResolvedColumnExpr(onConditionSql),
            new List<Generators.Translation.ParameterInfo>(),
            isSuccess: true,
            joinKind: joinKind,
            joinedTableName: joinedTableName,
            joinedSchemaName: joinedSchemaName,
            tableAlias: tableAlias);
    }

    /// <summary>
    /// Creates a TranslatedClause for Where/OrderBy tests.
    /// </summary>
    public static TranslatedClause CreateSimpleClause(
        ClauseKind kind,
        string sql = "\"Col\" = 1",
        bool isDescending = false)
    {
        return new TranslatedClause(
            kind,
            new ResolvedColumnExpr(sql),
            new List<Generators.Translation.ParameterInfo>(),
            isSuccess: true,
            isDescending: isDescending);
    }

    /// <summary>
    /// Creates a simple Select clause site with the given entity and result type.
    /// </summary>
    public static TranslatedCallSite CreateSelectSite(
        string entityType,
        string resultType,
        ProjectionInfo? projection = null,
        string uniqueId = "Select_0",
        GenSqlDialect dialect = GenSqlDialect.SQLite,
        string? customEntityReaderClass = null)
    {
        return new TestCallSiteBuilder()
            .WithMethodName("Select")
            .WithKind(InterceptorKind.Select)
            .WithEntityType(entityType)
            .WithResultType(resultType)
            .WithProjection(projection)
            .WithUniqueId(uniqueId)
            .WithDialect(dialect)
            .WithCustomEntityReader(customEntityReaderClass)
            .Build();
    }

    /// <summary>
    /// Creates a simple execution terminal site.
    /// </summary>
    public static TranslatedCallSite CreateExecutionSite(
        InterceptorKind kind,
        string entityType,
        string? resultType = null,
        ProjectionInfo? projection = null,
        string uniqueId = "test123")
    {
        var methodName = kind switch
        {
            InterceptorKind.ExecuteFetchAll => "ExecuteFetchAllAsync",
            InterceptorKind.ExecuteFetchFirst => "ExecuteFetchFirstAsync",
            InterceptorKind.ExecuteFetchFirstOrDefault => "ExecuteFetchFirstOrDefaultAsync",
            InterceptorKind.ExecuteFetchSingle => "ExecuteFetchSingleAsync",
            InterceptorKind.ExecuteScalar => "ExecuteScalarAsync",
            InterceptorKind.ExecuteNonQuery => "ExecuteNonQueryAsync",
            InterceptorKind.ToAsyncEnumerable => "ToAsyncEnumerable",
            _ => "Unknown"
        };

        return new TestCallSiteBuilder()
            .WithMethodName(methodName)
            .WithKind(kind)
            .WithEntityType(entityType)
            .WithResultType(resultType ?? entityType)
            .WithProjection(projection)
            .WithUniqueId(uniqueId)
            .Build();
    }

    /// <summary>
    /// Creates a Where clause site.
    /// </summary>
    public static TranslatedCallSite CreateWhereSite(
        string entityType,
        string? resultType = null,
        string uniqueId = "Where_0",
        TranslatedClause? clause = null)
    {
        return new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType(entityType)
            .WithResultType(resultType)
            .WithUniqueId(uniqueId)
            .WithClause(clause)
            .Build();
    }
}
