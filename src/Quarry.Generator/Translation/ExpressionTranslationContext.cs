using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;

namespace Quarry.Generators.Translation;

/// <summary>
/// Provides context for expression translation, including entity metadata,
/// parameter tracking, and dialect information.
/// </summary>
internal sealed class ExpressionTranslationContext
{
    private readonly List<ParameterInfo> _parameters = new();
    private int _parameterIndex;
    private readonly List<SubqueryScope> _subqueryScopes = new();
    private readonly int[] _subqueryAliasCounter = new int[] { 0 };

    public ExpressionTranslationContext(
        SemanticModel semanticModel,
        EntityInfo entityInfo,
        SqlDialect dialect,
        string lambdaParameterName,
        IReadOnlyDictionary<string, EntityInfo>? entityRegistry = null)
    {
        SemanticModel = semanticModel;
        EntityInfo = entityInfo;
        Dialect = dialect;
        LambdaParameterName = lambdaParameterName;
        EntityRegistry = entityRegistry;
        JoinedEntities = new Dictionary<string, EntityInfo>();

        // Build property-to-column lookup
        ColumnLookup = new Dictionary<string, ColumnInfo>();
        foreach (var column in entityInfo.Columns)
        {
            ColumnLookup[column.PropertyName] = column;
        }
    }

    /// <summary>
    /// Private constructor for creating a copy with additional joined entities.
    /// </summary>
    private ExpressionTranslationContext(
        SemanticModel semanticModel,
        EntityInfo entityInfo,
        SqlDialect dialect,
        string lambdaParameterName,
        Dictionary<string, ColumnInfo> columnLookup,
        Dictionary<string, EntityInfo> joinedEntities,
        List<ParameterInfo> parameters,
        int parameterIndex,
        IReadOnlyDictionary<string, EntityInfo>? entityRegistry,
        List<SubqueryScope> subqueryScopes,
        int[] subqueryAliasCounter)
    {
        SemanticModel = semanticModel;
        EntityInfo = entityInfo;
        Dialect = dialect;
        LambdaParameterName = lambdaParameterName;
        ColumnLookup = columnLookup;
        JoinedEntities = joinedEntities;
        _parameters = parameters;
        _parameterIndex = parameterIndex;
        EntityRegistry = entityRegistry;
        _subqueryScopes = subqueryScopes;
        _subqueryAliasCounter = subqueryAliasCounter;
    }

    /// <summary>
    /// Gets the semantic model for symbol resolution.
    /// </summary>
    public SemanticModel SemanticModel { get; }

    /// <summary>
    /// Gets the entity metadata for column lookups.
    /// </summary>
    public EntityInfo EntityInfo { get; }

    /// <summary>
    /// Gets the SQL dialect for syntax variations.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the lambda parameter name (e.g., "u" in "u => u.Name").
    /// </summary>
    public string LambdaParameterName { get; }

    /// <summary>
    /// Gets the column lookup dictionary (property name → column info).
    /// </summary>
    public Dictionary<string, ColumnInfo> ColumnLookup { get; }

    /// <summary>
    /// Gets the joined entity lookups (parameter name → entity info).
    /// </summary>
    public Dictionary<string, EntityInfo> JoinedEntities { get; }

    /// <summary>
    /// Gets the table alias mapping (lambda parameter name → positional alias like "t0", "t1").
    /// Null when no aliases are configured.
    /// </summary>
    public Dictionary<string, string>? TableAliases { get; private set; }

    /// <summary>
    /// Gets the collected parameters.
    /// </summary>
    public IReadOnlyList<ParameterInfo> Parameters => _parameters;

    /// <summary>
    /// Gets the current parameter index for the next parameter.
    /// </summary>
    public int CurrentParameterIndex => _parameterIndex;

    /// <summary>
    /// Gets the entity registry for resolving related entities during subquery translation.
    /// Null when not available (backward compat with ITypeSymbol-based contexts).
    /// </summary>
    public IReadOnlyDictionary<string, EntityInfo>? EntityRegistry { get; }

    /// <summary>
    /// Gets the current subquery nesting depth (0 = no subquery context).
    /// </summary>
    public int SubqueryDepth => _subqueryScopes.Count;

    /// <summary>
    /// Pushes a new subquery scope for the given inner entity.
    /// Returns the generated table alias (e.g., "sq0").
    /// </summary>
    public string PushSubqueryScope(string parameterName, EntityInfo innerEntity)
    {
        var alias = $"sq{_subqueryAliasCounter[0]++}";
        _subqueryScopes.Add(new SubqueryScope(parameterName, innerEntity, alias));
        return alias;
    }

    /// <summary>
    /// Pops the innermost subquery scope.
    /// </summary>
    public void PopSubqueryScope()
    {
        if (_subqueryScopes.Count > 0)
            _subqueryScopes.RemoveAt(_subqueryScopes.Count - 1);
    }

    /// <summary>
    /// Adds a parameter and returns its placeholder.
    /// </summary>
    /// <param name="clrType">The CLR type of the parameter.</param>
    /// <param name="valueExpression">The C# expression producing the value.</param>
    /// <param name="isCollection">Whether this is a collection parameter.</param>
    /// <param name="isCaptured">Whether this parameter is a captured variable from the enclosing scope.</param>
    /// <returns>The parameter placeholder (e.g., "@p0").</returns>
    public string AddParameter(string clrType, string valueExpression, bool isCollection = false, bool isCaptured = false, ITypeSymbol? typeSymbol = null)
    {
        var name = $"@p{_parameterIndex}";
        var param = new ParameterInfo(_parameterIndex, name, clrType, valueExpression, isCollection, isCaptured);

        // Detect enum types when the type symbol is available.
        // This enables inline cast codegen in the carrier terminal.
        if (typeSymbol != null)
        {
            var unwrapped = typeSymbol;
            if (typeSymbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                unwrapped = nullable.TypeArguments[0];

            if (unwrapped.TypeKind == TypeKind.Enum && unwrapped is INamedTypeSymbol enumType)
            {
                param.IsEnum = true;
                param.EnumUnderlyingType = enumType.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "int";
            }
        }

        _parameters.Add(param);
        _parameterIndex++;
        return FormatParameterPlaceholder(_parameterIndex - 1);
    }

    /// <summary>
    /// Formats a parameter placeholder according to the dialect.
    /// </summary>
    public string FormatParameterPlaceholder(int index)
    {
        // For now we use @p0 format; the runtime converts to dialect-specific format
        return $"@p{index}";
    }

    /// <summary>
    /// Quotes an identifier according to the dialect.
    /// </summary>
    public string QuoteIdentifier(string identifier)
    {
        return Dialect switch
        {
            SqlDialect.SQLite => $"\"{identifier}\"",
            SqlDialect.PostgreSQL => $"\"{identifier}\"",
            SqlDialect.MySQL => $"`{identifier}`",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => $"\"{identifier}\""
        };
    }

    /// <summary>
    /// Formats a boolean literal according to the dialect.
    /// </summary>
    public string FormatBooleanLiteral(bool value)
    {
        return Dialect switch
        {
            SqlDialect.PostgreSQL => value ? "TRUE" : "FALSE",
            _ => value ? "1" : "0"
        };
    }

    /// <summary>
    /// Gets the column info for a property name.
    /// </summary>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>The column info, or null if not found.</returns>
    public ColumnInfo? GetColumnInfo(string propertyName)
    {
        return ColumnLookup.TryGetValue(propertyName, out var info) ? info : null;
    }

    /// <summary>
    /// Gets the quoted column name for a property.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The quoted column name, or null if property not found.</returns>
    public string? GetQuotedColumnName(string propertyName)
    {
        var column = GetColumnInfo(propertyName);
        return column != null ? QuoteIdentifier(column.ColumnName) : null;
    }

    /// <summary>
    /// Creates a new context with an additional joined entity.
    /// </summary>
    /// <param name="parameterName">The parameter name for the joined entity.</param>
    /// <param name="entityInfo">The entity info for the joined entity.</param>
    /// <returns>A new context with the joined entity added.</returns>
    public ExpressionTranslationContext WithJoinedEntity(string parameterName, EntityInfo entityInfo)
    {
        var newJoinedEntities = new Dictionary<string, EntityInfo>(JoinedEntities)
        {
            [parameterName] = entityInfo
        };

        var ctx = new ExpressionTranslationContext(
            SemanticModel,
            EntityInfo,
            Dialect,
            LambdaParameterName,
            ColumnLookup,
            newJoinedEntities,
            _parameters,
            _parameterIndex,
            EntityRegistry,
            _subqueryScopes,
            _subqueryAliasCounter);
        ctx.TableAliases = TableAliases;
        return ctx;
    }

    /// <summary>
    /// Gets the entity info for a lambda parameter name.
    /// Checks subquery scopes (innermost-first), then primary entity, then joined entities.
    /// </summary>
    /// <param name="parameterName">The lambda parameter name.</param>
    /// <returns>The entity info, or null if not found.</returns>
    public EntityInfo? GetEntityInfo(string parameterName)
    {
        // Check subquery scopes innermost-first
        for (int i = _subqueryScopes.Count - 1; i >= 0; i--)
        {
            if (_subqueryScopes[i].ParameterName == parameterName)
                return _subqueryScopes[i].EntityInfo;
        }

        if (parameterName == LambdaParameterName)
            return EntityInfo;

        return JoinedEntities.TryGetValue(parameterName, out var entityInfo) ? entityInfo : null;
    }

    /// <summary>
    /// Gets the column info for a property on a specific entity.
    /// Checks subquery scopes first (innermost-first), then primary/joined entities.
    /// </summary>
    /// <param name="parameterName">The lambda parameter name for the entity.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The column info, or null if not found.</returns>
    public ColumnInfo? GetColumnInfo(string parameterName, string propertyName)
    {
        // Check subquery scopes innermost-first
        for (int i = _subqueryScopes.Count - 1; i >= 0; i--)
        {
            if (_subqueryScopes[i].ParameterName == parameterName)
            {
                return _subqueryScopes[i].ColumnLookup.TryGetValue(propertyName, out var col) ? col : null;
            }
        }

        var entityInfo = GetEntityInfo(parameterName);
        if (entityInfo == null)
            return null;

        foreach (var column in entityInfo.Columns)
        {
            if (column.PropertyName == propertyName)
                return column;
        }

        return null;
    }

    /// <summary>
    /// Gets the quoted column name for a property on a specific entity, with table alias.
    /// Checks subquery scopes for alias resolution before falling back to table aliases and table name.
    /// </summary>
    /// <param name="parameterName">The lambda parameter name for the entity.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The qualified quoted column name, or null if not found.</returns>
    public string? GetQualifiedColumnName(string parameterName, string propertyName)
    {
        var entityInfo = GetEntityInfo(parameterName);
        var column = GetColumnInfo(parameterName, propertyName);
        if (entityInfo == null || column == null)
            return null;

        // Check subquery scopes for alias (innermost-first)
        for (int i = _subqueryScopes.Count - 1; i >= 0; i--)
        {
            if (_subqueryScopes[i].ParameterName == parameterName)
            {
                var sqAlias = QuoteIdentifier(_subqueryScopes[i].TableAlias);
                return $"{sqAlias}.{QuoteIdentifier(column.ColumnName)}";
            }
        }

        // Use positional alias if available, otherwise fall back to table name
        string qualifier;
        if (TableAliases != null && TableAliases.TryGetValue(parameterName, out var alias))
        {
            qualifier = QuoteIdentifier(alias);
        }
        else
        {
            qualifier = QuoteIdentifier(entityInfo.TableName);
        }

        var columnName = QuoteIdentifier(column.ColumnName);
        return $"{qualifier}.{columnName}";
    }

    /// <summary>
    /// Creates a new context with the specified table aliases.
    /// </summary>
    public ExpressionTranslationContext WithTableAliases(Dictionary<string, string> aliases)
    {
        var ctx = new ExpressionTranslationContext(
            SemanticModel,
            EntityInfo,
            Dialect,
            LambdaParameterName,
            ColumnLookup,
            JoinedEntities,
            _parameters,
            _parameterIndex,
            EntityRegistry,
            _subqueryScopes,
            _subqueryAliasCounter);
        ctx.TableAliases = aliases;
        return ctx;
    }
}
