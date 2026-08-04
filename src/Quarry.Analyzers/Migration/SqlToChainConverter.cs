using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql.Parser;
using Quarry.Shared.Migration;

namespace Quarry.Analyzers.Migration;

/// <summary>
/// Converts a parsed SQL SELECT statement to an equivalent Quarry chain query C# expression.
/// </summary>
internal sealed class SqlToChainConverter
{
    private static readonly HashSet<string> SupportedAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "MIN", "MAX", "AVG"
    };

    /// <summary>Table name (case-insensitive) → (EntityInfo, EntityMapping).</summary>
    private readonly Dictionary<string, (EntityInfo Entity, EntityMapping Mapping)> _tableToEntity;

    public SqlToChainConverter(ContextInfo context)
    {
        _tableToEntity = new Dictionary<string, (EntityInfo, EntityMapping)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in context.EntityMappings)
        {
            var tableName = mapping.Entity.TableName;
            if (!string.IsNullOrEmpty(tableName) && !_tableToEntity.ContainsKey(tableName))
                _tableToEntity[tableName] = (mapping.Entity, mapping);
        }
    }

    /// <summary>
    /// Checks whether the given SQL statement can be fully converted to a chain query.
    /// Returns null on success, or an error reason string on failure.
    /// </summary>
    public string? CheckConvertibility(SqlSelectStatement stmt)
    {
        return CheckConvertibility(stmt, out _);
    }

    /// <summary>
    /// Checks convertibility and, on success, hands back the CTE bindings the emitter needs.
    /// Returns null when convertible, or an error reason string.
    /// </summary>
    public string? CheckConvertibility(SqlSelectStatement stmt, out IReadOnlyList<CteBinding> cteBindings)
    {
        cteBindings = Array.Empty<CteBinding>();

        if (stmt.Ctes != null)
        {
            var cteError = TryBindCtes(stmt, out cteBindings);
            if (cteError != null) return cteError;
        }

        // Must have a FROM clause
        if (stmt.From == null)
            return "No FROM clause";

        // FROM table must resolve to a known entity, or to a CTE declared by this statement
        var fromIsCte = TryFindBinding(cteBindings, stmt.From.TableName) != null;
        if (!fromIsCte && !_tableToEntity.ContainsKey(stmt.From.TableName))
            return $"Unknown table '{stmt.From.TableName}'";

        if (cteBindings.Count > 0)
        {
            // The outer query must read from a CTE, so the chain can start with
            // FromCte<T>(). Starting from an entity accessor after With<>() instead would
            // require the context to derive from QuarryContext<TSelf>, which is not
            // verifiable here — emitting it would risk a NotSupportedException at runtime.
            if (!fromIsCte)
                return $"Outer query reads from '{stmt.From.TableName}' rather than a CTE";

            // Joining a CTE resolves against the underlying table rather than the CTE name
            // in the current runtime, so a converted join would emit different SQL.
            if (stmt.Joins.Count > 0)
                return "Joins are not supported alongside a CTE";
        }

        // Max 4 tables (1 primary + 3 joins) — chain query limit
        if (stmt.Joins.Count > 3)
            return $"Too many joins ({stmt.Joins.Count}); chain queries support up to 3";

        // All joined tables must resolve
        foreach (var join in stmt.Joins)
        {
            if (!_tableToEntity.ContainsKey(join.Table.TableName))
                return $"Unknown table '{join.Table.TableName}'";
        }

        // Build alias-to-entity map for column resolution
        var aliasMap = BuildAliasMap(stmt, cteBindings);

        // Walk the AST to check all nodes are convertible
        foreach (var col in stmt.Columns)
        {
            var err = CheckNode(col, aliasMap);
            if (err != null) return err;
        }

        if (stmt.Where != null)
        {
            var err = CheckExpr(stmt.Where, aliasMap);
            if (err != null) return err;
        }

        foreach (var join in stmt.Joins)
        {
            if (join.Condition != null)
            {
                var err = CheckExpr(join.Condition, aliasMap);
                if (err != null) return err;
            }
        }

        if (stmt.GroupBy != null)
        {
            foreach (var expr in stmt.GroupBy)
            {
                var err = CheckExpr(expr, aliasMap);
                if (err != null) return err;
            }
        }

        if (stmt.Having != null)
        {
            var err = CheckExpr(stmt.Having, aliasMap);
            if (err != null) return err;
        }

        if (stmt.OrderBy != null)
        {
            foreach (var term in stmt.OrderBy)
            {
                var err = CheckExpr(term.Expression, aliasMap);
                if (err != null) return err;
            }
        }

        if (stmt.Limit != null)
        {
            var err = CheckExpr(stmt.Limit, aliasMap);
            if (err != null) return err;
        }

        if (stmt.Offset != null)
        {
            var err = CheckExpr(stmt.Offset, aliasMap);
            if (err != null) return err;
        }

        return null; // convertible
    }

    /// <summary>
    /// Builds a mapping from table alias (or table name if no alias) to EntityInfo.
    /// </summary>
    internal Dictionary<string, EntityInfo> BuildAliasMap(SqlSelectStatement stmt)
        => BuildAliasMap(stmt, Array.Empty<CteBinding>());

    internal Dictionary<string, EntityInfo> BuildAliasMap(
        SqlSelectStatement stmt,
        IReadOnlyList<CteBinding> cteBindings)
    {
        var map = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);

        // A CTE in the FROM resolves against the shape the CTE exposes, not the raw entity.
        var fromBinding = stmt.From != null ? TryFindBinding(cteBindings, stmt.From.TableName) : null;
        if (fromBinding != null)
        {
            map[stmt.From!.Alias ?? stmt.From.TableName] = fromBinding.ExposedEntity;
            return map;
        }

        if (stmt.From != null && _tableToEntity.TryGetValue(stmt.From.TableName, out var fromEntry))
        {
            var alias = stmt.From.Alias ?? stmt.From.TableName;
            map[alias] = fromEntry.Entity;
        }

        foreach (var join in stmt.Joins)
        {
            if (_tableToEntity.TryGetValue(join.Table.TableName, out var joinEntry))
            {
                var alias = join.Table.Alias ?? join.Table.TableName;
                map[alias] = joinEntry.Entity;
            }
        }

        return map;
    }

    // ─── CTE binding (#331) ──────────────────────────────

    internal static CteBinding? TryFindBinding(IReadOnlyList<CteBinding> bindings, string name)
    {
        foreach (var b in bindings)
        {
            if (string.Equals(b.CteName, name, StringComparison.OrdinalIgnoreCase))
                return b;
        }
        return null;
    }

    /// <summary>
    /// Validates every CTE on the statement and builds the bindings the emitter needs.
    /// Returns null when all CTEs are convertible, or an error reason string.
    /// </summary>
    private string? TryBindCtes(SqlSelectStatement stmt, out IReadOnlyList<CteBinding> bindings)
    {
        bindings = Array.Empty<CteBinding>();

        // A recursive CTE has no chain equivalent — the runtime has no recursive With<>.
        if (stmt.IsRecursive)
            return "Recursive CTEs are not convertible to a chain query";

        var result = new List<CteBinding>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cte in stmt.Ctes!)
        {
            if (!seenNames.Add(cte.Name))
                return $"Duplicate CTE name '{cte.Name}'";

            // A set-operation body is only produced for recursive CTEs, but guard anyway.
            if (cte.Query is not SqlSelectStatement body)
                return $"CTE '{cte.Name}' uses a set operation, which is not convertible";

            var error = BindCte(cte, body, out var binding);
            if (error != null) return error;

            result.Add(binding!);
        }

        bindings = result;
        return null;
    }

    private string? BindCte(SqlCommonTableExpression cte, SqlSelectStatement body, out CteBinding? binding)
    {
        binding = null;

        if (body.Ctes != null)
            return $"CTE '{cte.Name}' nests a further WITH clause";

        if (body.From == null)
            return $"CTE '{cte.Name}' has no FROM clause";

        if (!_tableToEntity.TryGetValue(body.From.TableName, out var source))
            return $"CTE '{cte.Name}' reads from unknown table '{body.From.TableName}'";

        if (body.Joins.Count > 0)
            return $"CTE '{cte.Name}' contains a join";

        // With<>'s inner builder is a Where + projection chain. Anything else in the body
        // would be dropped, so reject rather than emit a query that differs from the SQL.
        if (body.IsDistinct)
            return $"CTE '{cte.Name}' uses DISTINCT";
        if (body.GroupBy != null || body.Having != null)
            return $"CTE '{cte.Name}' uses GROUP BY or HAVING";
        if (body.OrderBy != null || body.Limit != null || body.Offset != null)
            return $"CTE '{cte.Name}' uses ORDER BY, LIMIT or OFFSET";

        var bodyAliasMap = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [body.From.Alias ?? body.From.TableName] = source.Entity
        };

        if (body.Where != null)
        {
            var whereError = CheckExpr(body.Where, bodyAliasMap);
            if (whereError != null) return $"CTE '{cte.Name}': {whereError}";
        }

        // SELECT * maps onto With<TEntity>, which needs no synthesized type.
        var isWholeEntity = body.Columns.Count == 1 && body.Columns[0] is SqlStarColumn;

        if (isWholeEntity)
        {
            if (cte.ColumnNames != null)
                return $"CTE '{cte.Name}' declares a column list over SELECT *";

            binding = new CteBinding(
                cte.Name, source.Entity.EntityName, source.Entity, source.Mapping,
                isWholeEntity: true, Array.Empty<CteProjection>(), source.Entity, body);
            return null;
        }

        var dtoTypeName = ToPascalCase(cte.Name);
        if (dtoTypeName.Length == 0)
            return $"CTE '{cte.Name}' has no usable type name";

        // A synthesized DTO must not shadow an existing entity type.
        foreach (var entry in _tableToEntity.Values)
        {
            if (string.Equals(entry.Entity.EntityName, dtoTypeName, StringComparison.Ordinal))
                return $"CTE '{cte.Name}' would need a DTO named '{dtoTypeName}', which already exists";
        }

        if (cte.ColumnNames != null && cte.ColumnNames.Count != body.Columns.Count)
            return $"CTE '{cte.Name}' column list does not match its SELECT list";

        var projections = new List<CteProjection>(body.Columns.Count);
        var exposedColumns = new List<ColumnInfo>(body.Columns.Count);

        for (var i = 0; i < body.Columns.Count; i++)
        {
            if (body.Columns[i] is not SqlSelectColumn selectCol)
                return $"CTE '{cte.Name}' selects a star column alongside other columns";

            if (selectCol.Expression is not SqlColumnRef colRef)
                return $"CTE '{cte.Name}' projects an expression, which is not convertible";

            if (colRef.ColumnName == "*")
                return $"CTE '{cte.Name}' selects a star column alongside other columns";

            var sourceColumn = FindColumn(source.Entity, colRef.ColumnName);
            if (sourceColumn == null)
                return $"CTE '{cte.Name}' references unknown column '{colRef.ColumnName}'";

            // Foreign-key columns surface as wrapper types; synthesizing an equivalent DTO
            // property is not something this converter can do reliably.
            if (sourceColumn.Kind == ColumnKind.ForeignKey)
                return $"CTE '{cte.Name}' projects reference column '{colRef.ColumnName}'";

            // The name the CTE exposes: explicit column list, then alias, then source column.
            var exposedName = cte.ColumnNames != null
                ? cte.ColumnNames[i]
                : selectCol.Alias ?? sourceColumn.ColumnName;

            var dtoPropertyName = cte.ColumnNames != null || selectCol.Alias != null
                ? ToPascalCase(exposedName)
                : sourceColumn.PropertyName;

            if (dtoPropertyName.Length == 0)
                return $"CTE '{cte.Name}' exposes a column with no usable property name";

            projections.Add(new CteProjection(sourceColumn.PropertyName, dtoPropertyName, sourceColumn));
            exposedColumns.Add(CloneColumnAs(sourceColumn, dtoPropertyName, exposedName));
        }

        if (projections.Count == 0)
            return $"CTE '{cte.Name}' projects no columns";

        var exposedEntity = new EntityInfo(
            entityName: dtoTypeName,
            schemaClassName: dtoTypeName,
            schemaNamespace: source.Entity.SchemaNamespace,
            tableName: dtoTypeName,
            namingStyle: source.Entity.NamingStyle,
            columns: exposedColumns,
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: source.Entity.Location);

        binding = new CteBinding(
            cte.Name, dtoTypeName, source.Entity, source.Mapping,
            isWholeEntity: false, projections, exposedEntity, body);
        return null;
    }

    private static ColumnInfo? FindColumn(EntityInfo entity, string columnName)
    {
        foreach (var col in entity.Columns)
        {
            if (string.Equals(col.ColumnName, columnName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(col.PropertyName, columnName, StringComparison.OrdinalIgnoreCase))
                return col;
        }
        return null;
    }

    /// <summary>
    /// Copies a column, re-pointing it at the name the CTE exposes it under.
    /// </summary>
    private static ColumnInfo CloneColumnAs(ColumnInfo source, string propertyName, string columnName)
        => new ColumnInfo(
            propertyName, columnName, source.ClrType, source.FullClrType,
            source.IsNullable, ColumnKind.Standard, referencedEntityName: null, source.Modifiers,
            source.IsValueType, source.ReaderMethodName, source.IsEnum);

    /// <summary>
    /// Converts a SQL identifier (snake_case, kebab, spaces) to a PascalCase C# name.
    /// </summary>
    internal static string ToPascalCase(string name)
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

    /// <summary>
    /// Resolves a SQL column name to a C# property name for the given entity.
    /// Returns null if not found.
    /// </summary>
    internal static string? ResolveColumnToProperty(EntityInfo entity, string columnName)
    {
        foreach (var col in entity.Columns)
        {
            if (string.Equals(col.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                return col.PropertyName;
        }

        return null;
    }

    /// <summary>
    /// Looks up the EntityMapping for a given table name.
    /// </summary>
    internal (EntityInfo Entity, EntityMapping Mapping)? ResolveTable(string tableName)
    {
        return _tableToEntity.TryGetValue(tableName, out var entry) ? entry : null;
    }

    /// <summary>
    /// Converts a SQL SELECT statement to a C# chain query expression string.
    /// Call <see cref="CheckConvertibility"/> first to ensure the SQL is convertible.
    /// </summary>
    /// <param name="stmt">The parsed SQL statement.</param>
    /// <param name="contextVarName">The variable name of the QuarryContext (e.g., "db").</param>
    /// <param name="parameterArgs">The argument expressions from the RawSqlAsync call site (positional).</param>
    /// <param name="useExecuteFetchAll">If true, ends with ExecuteFetchAllAsync() instead of ToAsyncEnumerable().</param>
    public string Convert(SqlSelectStatement stmt, string contextVarName, IReadOnlyList<string> parameterArgs, bool useExecuteFetchAll)
        => Convert(stmt, contextVarName, parameterArgs, useExecuteFetchAll, Array.Empty<CteBinding>());

    /// <param name="cteBindings">
    /// The bindings produced by <see cref="CheckConvertibility(SqlSelectStatement, out IReadOnlyList{CteBinding})"/>.
    /// Must be supplied whenever the statement declares CTEs, or the WITH clause is dropped.
    /// </param>
    /// <inheritdoc cref="Convert(SqlSelectStatement, string, IReadOnlyList{string}, bool)"/>
    public string Convert(
        SqlSelectStatement stmt,
        string contextVarName,
        IReadOnlyList<string> parameterArgs,
        bool useExecuteFetchAll,
        IReadOnlyList<CteBinding> cteBindings)
    {
        var aliasMap = BuildAliasMap(stmt, cteBindings);

        // Build ordered alias list: FROM table first, then JOINs in order
        var orderedAliases = new List<string>();
        if (stmt.From != null)
            orderedAliases.Add(stmt.From.Alias ?? stmt.From.TableName);
        foreach (var join in stmt.Joins)
            orderedAliases.Add(join.Table.Alias ?? join.Table.TableName);

        // Generate lambda parameter names (single letter based on entity name)
        var lambdaParams = GenerateLambdaParams(stmt, aliasMap);

        // Build alias → lambda param mapping
        var aliasToParam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < orderedAliases.Count && i < lambdaParams.Count; i++)
            aliasToParam[orderedAliases[i]] = lambdaParams[i];

        var sb = new StringBuilder();

        var fromBinding = stmt.From != null ? TryFindBinding(cteBindings, stmt.From.TableName) : null;
        if (fromBinding != null)
        {
            // WITH ... → ctx.With<...>(...) ... .FromCte<T>()
            AppendCteHeader(sb, contextVarName, cteBindings, fromBinding, parameterArgs);
        }
        else
        {
            // FROM → ctx.Accessor()
            var fromEntry = _tableToEntity[stmt.From!.TableName];
            sb.Append(contextVarName);
            sb.Append('.');
            sb.Append(fromEntry.Mapping.PropertyName);
            sb.Append("()");
        }

        // WHERE (pre-join: only if no joins, or WHERE only references primary table)
        if (stmt.Where != null && stmt.Joins.Count == 0)
        {
            sb.Append("\n    .Where(");
            sb.Append(lambdaParams[0]);
            sb.Append(" => ");
            sb.Append(TranslateExpr(stmt.Where, aliasMap, aliasToParam, parameterArgs));
            sb.Append(')');
        }

        // JOINs
        for (var i = 0; i < stmt.Joins.Count; i++)
        {
            var join = stmt.Joins[i];
            var joinEntry = _tableToEntity[join.Table.TableName];

            sb.Append("\n    .");
            sb.Append(JoinMethodName(join.JoinKind));
            sb.Append('<');
            sb.Append(joinEntry.Entity.EntityName);
            sb.Append(">(");

            if (join.JoinKind != SqlJoinKind.Cross)
            {
                // Lambda params: all entities up to and including this join
                sb.Append('(');
                for (var j = 0; j <= i + 1; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append(lambdaParams[j]);
                }
                sb.Append(") => ");
                sb.Append(TranslateExpr(join.Condition!, aliasMap, aliasToParam, parameterArgs));
            }

            sb.Append(')');
        }

        // WHERE (post-join: if joins exist)
        if (stmt.Where != null && stmt.Joins.Count > 0)
        {
            sb.Append("\n    .Where(");
            AppendLambdaSignature(sb, lambdaParams, stmt.Joins.Count + 1);
            sb.Append(" => ");
            sb.Append(TranslateExpr(stmt.Where, aliasMap, aliasToParam, parameterArgs));
            sb.Append(')');
        }

        // GROUP BY
        if (stmt.GroupBy != null && stmt.GroupBy.Count > 0)
        {
            sb.Append("\n    .GroupBy(");
            AppendLambdaSignature(sb, lambdaParams, stmt.Joins.Count + 1);
            sb.Append(" => ");
            if (stmt.GroupBy.Count == 1)
            {
                sb.Append(TranslateExpr(stmt.GroupBy[0], aliasMap, aliasToParam, parameterArgs));
            }
            else
            {
                sb.Append('(');
                for (var i = 0; i < stmt.GroupBy.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(TranslateExpr(stmt.GroupBy[i], aliasMap, aliasToParam, parameterArgs));
                }
                sb.Append(')');
            }
            sb.Append(')');
        }

        // HAVING
        if (stmt.Having != null)
        {
            sb.Append("\n    .Having(");
            AppendLambdaSignature(sb, lambdaParams, stmt.Joins.Count + 1);
            sb.Append(" => ");
            sb.Append(TranslateExpr(stmt.Having, aliasMap, aliasToParam, parameterArgs));
            sb.Append(')');
        }

        // ORDER BY
        if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
        {
            for (var i = 0; i < stmt.OrderBy.Count; i++)
            {
                var term = stmt.OrderBy[i];
                sb.Append("\n    .");
                sb.Append(i == 0 ? "OrderBy" : "ThenBy");
                sb.Append('(');
                AppendLambdaSignature(sb, lambdaParams, stmt.Joins.Count + 1);
                sb.Append(" => ");
                sb.Append(TranslateExpr(term.Expression, aliasMap, aliasToParam, parameterArgs));
                if (term.IsDescending)
                    sb.Append(", Direction.Descending");
                sb.Append(')');
            }
        }

        // DISTINCT
        if (stmt.IsDistinct)
            sb.Append("\n    .Distinct()");

        // SELECT
        sb.Append("\n    .Select(");
        AppendLambdaSignature(sb, lambdaParams, stmt.Joins.Count + 1);
        sb.Append(" => ");
        AppendSelectProjection(sb, stmt, aliasMap, aliasToParam, parameterArgs, lambdaParams);
        sb.Append(')');

        // LIMIT
        if (stmt.Limit != null)
        {
            sb.Append("\n    .Limit(");
            sb.Append(TranslateExpr(stmt.Limit, aliasMap, aliasToParam, parameterArgs));
            sb.Append(')');
        }

        // OFFSET
        if (stmt.Offset != null)
        {
            sb.Append("\n    .Offset(");
            sb.Append(TranslateExpr(stmt.Offset, aliasMap, aliasToParam, parameterArgs));
            sb.Append(')');
        }

        // Terminal
        if (useExecuteFetchAll)
            sb.Append("\n    .ExecuteFetchAllAsync()");
        else
            sb.Append("\n    .ToAsyncEnumerable()");

        return sb.ToString();
    }

    /// <summary>
    /// Emits <c>ctx.With&lt;…&gt;(…)</c> for every declared CTE, then <c>.FromCte&lt;T&gt;()</c> for
    /// the one the outer query reads from.
    /// </summary>
    private void AppendCteHeader(
        StringBuilder sb,
        string contextVarName,
        IReadOnlyList<CteBinding> cteBindings,
        CteBinding fromBinding,
        IReadOnlyList<string> parameterArgs)
    {
        sb.Append(contextVarName);

        foreach (var binding in cteBindings)
        {
            // The body's own lambda scope: accessor param, then the entity param the Where
            // and Select expressions bind to.
            var used = new HashSet<string>(StringComparer.Ordinal);
            var entityParam = PickParamName(binding.SourceEntity.EntityName, used);
            used.Add(entityParam);
            var accessorParam = PickParamName(binding.SourceMapping.PropertyName, used);

            var bodyAlias = binding.Body.From!.Alias ?? binding.Body.From.TableName;
            var bodyAliasMap = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [bodyAlias] = binding.SourceEntity
            };
            var bodyAliasToParam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [bodyAlias] = entityParam
            };

            sb.Append("\n    .With<");
            sb.Append(binding.SourceEntity.EntityName);
            if (!binding.IsWholeEntity)
            {
                sb.Append(", ");
                sb.Append(binding.DtoTypeName);
            }
            sb.Append(">(");
            sb.Append(accessorParam);
            sb.Append(" => ");
            sb.Append(accessorParam);

            if (binding.Body.Where != null)
            {
                sb.Append(".Where(");
                sb.Append(entityParam);
                sb.Append(" => ");
                sb.Append(TranslateExpr(binding.Body.Where, bodyAliasMap, bodyAliasToParam, parameterArgs));
                sb.Append(')');
            }

            if (!binding.IsWholeEntity)
            {
                sb.Append("\n        .Select(");
                sb.Append(entityParam);
                sb.Append(" => new ");
                sb.Append(binding.DtoTypeName);
                sb.Append(" { ");
                for (var i = 0; i < binding.Projections.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p = binding.Projections[i];
                    sb.Append(p.DtoPropertyName);
                    sb.Append(" = ");
                    sb.Append(entityParam);
                    sb.Append('.');
                    sb.Append(p.SourcePropertyName);
                }
                sb.Append(" })");
            }

            sb.Append(')');
        }

        sb.Append("\n    .FromCte<");
        sb.Append(fromBinding.DtoTypeName);
        sb.Append(">()");
    }

    /// <summary>
    /// Renders the C# class declaration for every CTE that needs a synthesized DTO.
    /// Whole-entity CTEs reuse their entity type and contribute nothing.
    /// </summary>
    public static IReadOnlyList<string> BuildDtoDeclarations(IReadOnlyList<CteBinding> cteBindings)
    {
        var declarations = new List<string>();

        foreach (var binding in cteBindings)
        {
            if (binding.IsWholeEntity) continue;

            var sb = new StringBuilder();
            sb.Append("public class ").Append(binding.DtoTypeName).Append('\n').Append("{\n");
            foreach (var p in binding.Projections)
            {
                sb.Append("    public ").Append(p.SourceColumn.FullClrType);
                if (p.SourceColumn.IsNullable && !p.SourceColumn.FullClrType.EndsWith("?", StringComparison.Ordinal))
                    sb.Append('?');
                sb.Append(' ').Append(p.DtoPropertyName).Append(" { get; set; }\n");
            }
            sb.Append('}');

            declarations.Add(sb.ToString());
        }

        return declarations;
    }

    private static List<string> GenerateLambdaParams(SqlSelectStatement stmt, Dictionary<string, EntityInfo> aliasMap)
    {
        var params_ = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        if (stmt.From != null)
        {
            var alias = stmt.From.Alias ?? stmt.From.TableName;
            if (aliasMap.TryGetValue(alias, out var entity))
            {
                var p = PickParamName(entity.EntityName, used);
                params_.Add(p);
                used.Add(p);
            }
        }

        foreach (var join in stmt.Joins)
        {
            var alias = join.Table.Alias ?? join.Table.TableName;
            if (aliasMap.TryGetValue(alias, out var entity))
            {
                var p = PickParamName(entity.EntityName, used);
                params_.Add(p);
                used.Add(p);
            }
        }

        return params_;
    }

    private static string PickParamName(string entityName, HashSet<string> used)
    {
        // Use first letter lowercase
        var candidate = entityName.Substring(0, 1).ToLowerInvariant();
        if (!used.Contains(candidate))
            return candidate;

        // Try two letters
        if (entityName.Length > 1)
        {
            candidate = entityName.Substring(0, 2).ToLowerInvariant();
            if (!used.Contains(candidate))
                return candidate;
        }

        // Append number
        for (var i = 2; ; i++)
        {
            var numbered = entityName.Substring(0, 1).ToLowerInvariant() + i;
            if (!used.Contains(numbered))
                return numbered;
        }
    }

    private static void AppendLambdaSignature(StringBuilder sb, List<string> lambdaParams, int count)
    {
        if (count == 1)
        {
            sb.Append(lambdaParams[0]);
        }
        else
        {
            sb.Append('(');
            for (var i = 0; i < count && i < lambdaParams.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(lambdaParams[i]);
            }
            sb.Append(')');
        }
    }

    private void AppendSelectProjection(
        StringBuilder sb,
        SqlSelectStatement stmt,
        Dictionary<string, EntityInfo> aliasMap,
        Dictionary<string, string> aliasToParam,
        IReadOnlyList<string> parameterArgs,
        List<string> lambdaParams)
    {
        // Check for SELECT *
        if (stmt.Columns.Count == 1 && stmt.Columns[0] is SqlStarColumn star && star.TableAlias == null)
        {
            sb.Append(lambdaParams[0]);
            return;
        }

        // Check for table.*
        if (stmt.Columns.Count == 1 && stmt.Columns[0] is SqlStarColumn tableStar && tableStar.TableAlias != null)
        {
            if (aliasToParam.TryGetValue(tableStar.TableAlias, out var param))
                sb.Append(param);
            else
                sb.Append(lambdaParams[0]);
            return;
        }

        // Multiple columns or expressions → tuple
        if (stmt.Columns.Count == 1)
        {
            // Single expression (e.g., COUNT(*))
            var col = stmt.Columns[0];
            if (col is SqlSelectColumn sc)
                sb.Append(TranslateExpr(sc.Expression, aliasMap, aliasToParam, parameterArgs));
            else
                sb.Append(lambdaParams[0]);
            return;
        }

        sb.Append('(');
        for (var i = 0; i < stmt.Columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var col = stmt.Columns[i];
            if (col is SqlSelectColumn sc)
                sb.Append(TranslateExpr(sc.Expression, aliasMap, aliasToParam, parameterArgs));
            else if (col is SqlStarColumn s)
            {
                if (s.TableAlias != null && aliasToParam.TryGetValue(s.TableAlias, out var p))
                    sb.Append(p);
                else
                    sb.Append(lambdaParams[0]);
            }
        }
        sb.Append(')');
    }

    private string TranslateExpr(
        SqlExpr expr,
        Dictionary<string, EntityInfo> aliasMap,
        Dictionary<string, string> aliasToParam,
        IReadOnlyList<string> parameterArgs)
    {
        switch (expr)
        {
            case SqlColumnRef colRef:
                return TranslateColumnRef(colRef, aliasMap, aliasToParam);

            case SqlParameter param:
                return TranslateParameter(param, parameterArgs);

            case SqlLiteral literal:
                return TranslateLiteral(literal);

            case SqlBinaryExpr binary:
                var left = TranslateExpr(binary.Left, aliasMap, aliasToParam, parameterArgs);
                var right = TranslateExpr(binary.Right, aliasMap, aliasToParam, parameterArgs);
                var op = TranslateBinaryOp(binary.Operator);
                return $"{left} {op} {right}";

            case SqlUnaryExpr unary:
                var operand = TranslateExpr(unary.Operand, aliasMap, aliasToParam, parameterArgs);
                return unary.Operator == SqlUnaryOp.Not ? $"!{operand}" : $"-{operand}";

            case SqlParenExpr paren:
                var inner = TranslateExpr(paren.Inner, aliasMap, aliasToParam, parameterArgs);
                return $"({inner})";

            case SqlIsNullExpr isNull:
                var nullExpr = TranslateExpr(isNull.Expression, aliasMap, aliasToParam, parameterArgs);
                return isNull.IsNegated ? $"{nullExpr} != null" : $"{nullExpr} == null";

            case SqlInExpr inExpr:
                return TranslateInExpr(inExpr, aliasMap, aliasToParam, parameterArgs);

            case SqlBetweenExpr between:
                return TranslateBetweenExpr(between, aliasMap, aliasToParam, parameterArgs);

            case SqlFunctionCall func:
                return TranslateFunctionCall(func, aliasMap, aliasToParam, parameterArgs);

            default:
                return "/* unsupported */";
        }
    }

    private string TranslateColumnRef(
        SqlColumnRef colRef,
        Dictionary<string, EntityInfo> aliasMap,
        Dictionary<string, string> aliasToParam)
    {
        string paramName = aliasToParam.Values.FirstOrDefault() ?? "x";
        EntityInfo? entity = null;

        if (colRef.TableAlias != null)
        {
            if (aliasToParam.TryGetValue(colRef.TableAlias, out var resolved))
                paramName = resolved;
            aliasMap.TryGetValue(colRef.TableAlias, out entity);
        }
        else
        {
            // Find the first entity that has this column
            foreach (var kvp in aliasMap)
            {
                if (ResolveColumnToProperty(kvp.Value, colRef.ColumnName) != null)
                {
                    entity = kvp.Value;
                    if (aliasToParam.TryGetValue(kvp.Key, out var resolvedParam))
                        paramName = resolvedParam;
                    break;
                }
            }
        }

        var propName = entity != null
            ? ResolveColumnToProperty(entity, colRef.ColumnName) ?? colRef.ColumnName
            : colRef.ColumnName;

        return $"{paramName}.{propName}";
    }

    private static string TranslateParameter(SqlParameter param, IReadOnlyList<string> parameterArgs)
    {
        // Extract index from @p0, @p1, etc.
        var raw = param.RawText;
        if (raw.StartsWith("@p", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raw.Substring(2), out var index) &&
            index >= 0 && index < parameterArgs.Count)
        {
            return parameterArgs[index];
        }

        // Fallback: return as-is
        return raw;
    }

    private static string TranslateLiteral(SqlLiteral literal)
    {
        switch (literal.LiteralKind)
        {
            case SqlLiteralKind.String:
                var escaped = literal.Value
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");
                return $"\"{escaped}\"";
            case SqlLiteralKind.Number:
                return literal.Value;
            case SqlLiteralKind.Boolean:
                return literal.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            case SqlLiteralKind.Null:
                return "null";
            default:
                return literal.Value;
        }
    }

    private static string TranslateBinaryOp(SqlBinaryOp op)
    {
        switch (op)
        {
            case SqlBinaryOp.Equal: return "==";
            case SqlBinaryOp.NotEqual: return "!=";
            case SqlBinaryOp.LessThan: return "<";
            case SqlBinaryOp.GreaterThan: return ">";
            case SqlBinaryOp.LessThanOrEqual: return "<=";
            case SqlBinaryOp.GreaterThanOrEqual: return ">=";
            case SqlBinaryOp.And: return "&&";
            case SqlBinaryOp.Or: return "||";
            case SqlBinaryOp.Add: return "+";
            case SqlBinaryOp.Subtract: return "-";
            case SqlBinaryOp.Multiply: return "*";
            case SqlBinaryOp.Divide: return "/";
            case SqlBinaryOp.Modulo: return "%";
            case SqlBinaryOp.Like: return "/* LIKE */";
            default: return "/* unknown op */";
        }
    }

    private string TranslateInExpr(
        SqlInExpr inExpr,
        Dictionary<string, EntityInfo> aliasMap,
        Dictionary<string, string> aliasToParam,
        IReadOnlyList<string> parameterArgs)
    {
        var target = TranslateExpr(inExpr.Expression, aliasMap, aliasToParam, parameterArgs);
        var sb = new StringBuilder();

        sb.Append("new[] { ");
        for (var i = 0; i < inExpr.Values.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(TranslateExpr(inExpr.Values[i], aliasMap, aliasToParam, parameterArgs));
        }
        sb.Append(" }.Contains(");
        sb.Append(target);
        sb.Append(')');

        if (inExpr.IsNegated)
            return $"!{sb}";

        return sb.ToString();
    }

    private string TranslateBetweenExpr(
        SqlBetweenExpr between,
        Dictionary<string, EntityInfo> aliasMap,
        Dictionary<string, string> aliasToParam,
        IReadOnlyList<string> parameterArgs)
    {
        var expr = TranslateExpr(between.Expression, aliasMap, aliasToParam, parameterArgs);
        var low = TranslateExpr(between.Low, aliasMap, aliasToParam, parameterArgs);
        var high = TranslateExpr(between.High, aliasMap, aliasToParam, parameterArgs);

        var result = $"{expr} >= {low} && {expr} <= {high}";
        return between.IsNegated ? $"!({result})" : result;
    }

    private string TranslateFunctionCall(
        SqlFunctionCall func,
        Dictionary<string, EntityInfo> aliasMap,
        Dictionary<string, string> aliasToParam,
        IReadOnlyList<string> parameterArgs)
    {
        var name = func.FunctionName.ToUpperInvariant();

        switch (name)
        {
            case "COUNT":
                if (func.Arguments.Count == 0)
                    return "Sql.Count()";
                // COUNT(*) — the star is parsed as SqlColumnRef(null, "*")
                if (func.Arguments[0] is SqlColumnRef starRef && starRef.ColumnName == "*")
                    return "Sql.Count()";
                return $"Sql.Count({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "SUM":
                if (func.Arguments.Count == 0) return "Sql.Sum(0)";
                return $"Sql.Sum({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "AVG":
                if (func.Arguments.Count == 0) return "Sql.Avg(0)";
                return $"Sql.Avg({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "MIN":
                if (func.Arguments.Count == 0) return "Sql.Min(0)";
                return $"Sql.Min({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "MAX":
                if (func.Arguments.Count == 0) return "Sql.Max(0)";
                return $"Sql.Max({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            default:
                return $"/* {func.FunctionName}(...) */";
        }
    }

    private static string JoinMethodName(SqlJoinKind kind)
    {
        switch (kind)
        {
            case SqlJoinKind.Inner: return "Join";
            case SqlJoinKind.Left: return "LeftJoin";
            case SqlJoinKind.Right: return "RightJoin";
            case SqlJoinKind.Cross: return "CrossJoin";
            case SqlJoinKind.FullOuter: return "FullOuterJoin";
            default: return "Join";
        }
    }

    private string? CheckNode(SqlNode node, Dictionary<string, EntityInfo> aliasMap)
    {
        switch (node)
        {
            case SqlSelectColumn selectCol:
                return CheckExpr(selectCol.Expression, aliasMap);

            case SqlStarColumn:
                return null; // SELECT * is always convertible

            default:
                return $"Unsupported SELECT column node: {node.NodeKind}";
        }
    }

    private string? CheckExpr(SqlExpr expr, Dictionary<string, EntityInfo> aliasMap)
    {
        switch (expr)
        {
            case SqlBinaryExpr binary:
                if (binary.Operator == SqlBinaryOp.Like)
                    return "LIKE expressions are not supported";
                return CheckExpr(binary.Left, aliasMap) ?? CheckExpr(binary.Right, aliasMap);

            case SqlUnaryExpr unary:
                return CheckExpr(unary.Operand, aliasMap);

            case SqlColumnRef colRef:
                return CheckColumnRef(colRef, aliasMap);

            case SqlLiteral:
            case SqlParameter:
                return null;

            case SqlFunctionCall func:
                if (!SupportedAggregates.Contains(func.FunctionName))
                    return $"Unsupported function '{func.FunctionName}'";
                foreach (var arg in func.Arguments)
                {
                    var err = CheckExpr(arg, aliasMap);
                    if (err != null) return err;
                }
                return null;

            case SqlInExpr inExpr:
                var inErr = CheckExpr(inExpr.Expression, aliasMap);
                if (inErr != null) return inErr;
                foreach (var val in inExpr.Values)
                {
                    var err = CheckExpr(val, aliasMap);
                    if (err != null) return err;
                }
                return null;

            case SqlBetweenExpr between:
                return CheckExpr(between.Expression, aliasMap)
                    ?? CheckExpr(between.Low, aliasMap)
                    ?? CheckExpr(between.High, aliasMap);

            case SqlIsNullExpr isNull:
                return CheckExpr(isNull.Expression, aliasMap);

            case SqlParenExpr paren:
                return CheckExpr(paren.Inner, aliasMap);

            // Unconvertible nodes
            case SqlCaseExpr:
                return "CASE expressions are not supported";

            case SqlCastExpr:
                return "CAST expressions are not supported";

            case SqlExistsExpr:
                return "EXISTS subqueries are not supported";

            case SqlUnsupported unsupported:
                return $"Unsupported SQL construct: {unsupported.RawText}";

            default:
                return $"Unrecognized expression node: {expr.NodeKind}";
        }
    }

    private string? CheckColumnRef(SqlColumnRef colRef, Dictionary<string, EntityInfo> aliasMap)
    {
        // Star in expression context (e.g., COUNT(*))
        if (colRef.ColumnName == "*")
            return null;

        if (colRef.TableAlias != null)
        {
            if (!aliasMap.TryGetValue(colRef.TableAlias, out var entity))
                return $"Unknown table alias '{colRef.TableAlias}'";

            if (ResolveColumnToProperty(entity, colRef.ColumnName) == null)
                return $"Unknown column '{colRef.ColumnName}' on entity '{entity.EntityName}'";
        }
        else
        {
            // No table alias — try to resolve against all entities in scope
            var found = false;
            foreach (var kvp in aliasMap)
            {
                if (ResolveColumnToProperty(kvp.Value, colRef.ColumnName) != null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return $"Unknown column '{colRef.ColumnName}'";
        }

        return null;
    }
}
