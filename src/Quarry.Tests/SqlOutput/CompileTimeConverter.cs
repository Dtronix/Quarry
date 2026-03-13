using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;
using Quarry.Internal;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Converts runtime query state objects into the compile-time builder inputs
/// (ChainedClauseSite lists) so that CompileTimeSqlBuilder can produce SQL
/// from the same state and the outputs can be compared.
/// </summary>
internal static class CompileTimeConverter
{
    private static readonly IReadOnlyList<ParameterInfo> EmptyParams = System.Array.Empty<ParameterInfo>();

    public static (List<ChainedClauseSite> Clauses, GenSqlDialect Dialect, string Table, string? Schema, string? Alias)
        ConvertSelectState(QueryState state)
    {
        var clauses = new List<ChainedClauseSite>();
        int idx = 0;

        // DISTINCT
        if (state.IsDistinct)
            clauses.Add(MakeDistinctClause(idx++));

        // SELECT columns
        if (state.HasSelect)
            clauses.Add(MakeSelectClause(state.SelectColumns, state.Dialect, idx++));

        // JOIN (must appear before WHERE in clause order to match runtime SQL structure)
        foreach (var join in state.JoinClauses)
            clauses.Add(MakeJoinClause(join, idx++));

        // WHERE
        foreach (var condition in state.WhereConditions)
            clauses.Add(MakeStaticClause(ClauseRole.Where, InterceptorKind.Where, condition, idx++));

        // GROUP BY
        foreach (var col in state.GroupByColumns)
            clauses.Add(MakeStaticClause(ClauseRole.GroupBy, InterceptorKind.GroupBy, col, idx++));

        // HAVING
        foreach (var condition in state.HavingConditions)
            clauses.Add(MakeStaticClause(ClauseRole.Having, InterceptorKind.Having, condition, idx++));

        // ORDER BY
        for (int i = 0; i < state.OrderByClauses.Length; i++)
        {
            var ob = state.OrderByClauses[i];
            bool isDesc = ob.Direction == Direction.Descending;
            if (i == 0)
                clauses.Add(MakeOrderByClause(ob.Column, isDesc, ClauseRole.OrderBy, InterceptorKind.OrderBy, idx++));
            else
                clauses.Add(MakeOrderByClause(ob.Column, isDesc, ClauseRole.ThenBy, InterceptorKind.ThenBy, idx++));
        }

        // LIMIT / OFFSET (parameterized in compile-time path)
        if (state.Limit.HasValue)
            clauses.Add(MakePaginationClause(ClauseRole.Limit, idx++));
        if (state.Offset.HasValue)
            clauses.Add(MakePaginationClause(ClauseRole.Offset, idx++));

        return (clauses, (GenSqlDialect)(int)state.Dialect, state.TableName, state.SchemaName, state.FromTableAlias);
    }

    public static (List<ChainedClauseSite> Clauses, GenSqlDialect Dialect, string Table, string? Schema)
        ConvertUpdateState(UpdateState state)
    {
        var clauses = new List<ChainedClauseSite>();
        int idx = 0;

        foreach (var set in state.SetClauses)
            clauses.Add(MakeSetClause(set.ColumnSql, set.ParameterIndex, idx++));

        foreach (var condition in state.WhereConditions)
            clauses.Add(MakeStaticClause(ClauseRole.UpdateWhere, InterceptorKind.UpdateWhere, condition, idx++));

        return (clauses, (GenSqlDialect)(int)state.Dialect, state.TableName, state.SchemaName);
    }

    public static (List<ChainedClauseSite> Clauses, GenSqlDialect Dialect, string Table, string? Schema)
        ConvertDeleteState(DeleteState state)
    {
        var clauses = new List<ChainedClauseSite>();
        int idx = 0;

        foreach (var condition in state.WhereConditions)
            clauses.Add(MakeStaticClause(ClauseRole.DeleteWhere, InterceptorKind.DeleteWhere, condition, idx++));

        return (clauses, (GenSqlDialect)(int)state.Dialect, state.TableName, state.SchemaName);
    }

    // ───────────────────────────────────────────────────────────────
    // Clause factory helpers
    // ───────────────────────────────────────────────────────────────

    private static ChainedClauseSite MakeStaticClause(
        ClauseRole role, InterceptorKind kind, string sqlFragment, int uniqueIndex)
    {
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, sqlFragment, EmptyParams);
        var site = new UsageSiteInfo(
            methodName: role.ToString(),
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: kind,
            invocationSyntax: null!,
            uniqueId: $"conv_{role}_{uniqueIndex}",
            clauseInfo: clauseInfo);
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, role);
    }

    private static ChainedClauseSite MakeDistinctClause(int uniqueIndex)
    {
        var site = new UsageSiteInfo(
            methodName: "Distinct",
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: null!,
            uniqueId: $"conv_distinct_{uniqueIndex}");
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, ClauseRole.Distinct);
    }

    private static ChainedClauseSite MakeSelectClause(
        System.Collections.Immutable.ImmutableArray<string> selectColumns, SqlDialect dialect, int uniqueIndex)
    {
        // Map QueryState.SelectColumns to ProjectedColumn objects.
        // Simple identifiers → ColumnName (CompileTimeSqlBuilder quotes them).
        // Pre-formatted expressions → SqlExpression (passed through literally).
        var projectedColumns = new List<ProjectedColumn>(selectColumns.Length);
        for (int i = 0; i < selectColumns.Length; i++)
        {
            var col = selectColumns[i];
            if (IsSimpleIdentifier(col))
            {
                projectedColumns.Add(new ProjectedColumn(
                    propertyName: col, columnName: col,
                    clrType: "object", fullClrType: "System.Object",
                    isNullable: false, ordinal: i));
            }
            else
            {
                projectedColumns.Add(new ProjectedColumn(
                    propertyName: $"col{i}", columnName: $"col{i}",
                    clrType: "object", fullClrType: "System.Object",
                    isNullable: false, ordinal: i,
                    sqlExpression: col));
            }
        }

        var projectionInfo = new ProjectionInfo(
            ProjectionKind.Tuple,
            "System.Object",
            projectedColumns);

        var site = new UsageSiteInfo(
            methodName: "Select",
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: InterceptorKind.Select,
            invocationSyntax: null!,
            uniqueId: $"conv_select_{uniqueIndex}",
            projectionInfo: projectionInfo);
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, ClauseRole.Select);
    }

    private static ChainedClauseSite MakeOrderByClause(
        string columnSql, bool isDescending, ClauseRole role, InterceptorKind kind, int uniqueIndex)
    {
        var clauseInfo = new OrderByClauseInfo(columnSql, isDescending, EmptyParams);
        var site = new UsageSiteInfo(
            methodName: role == ClauseRole.OrderBy ? "OrderBy" : "ThenBy",
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: kind,
            invocationSyntax: null!,
            uniqueId: $"conv_{role}_{uniqueIndex}",
            clauseInfo: clauseInfo);
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, role);
    }

    private static ChainedClauseSite MakeJoinClause(Quarry.Internal.JoinClause join, int uniqueIndex)
    {
        var joinKind = join.Kind switch
        {
            Quarry.Internal.JoinKind.Inner => JoinClauseKind.Inner,
            Quarry.Internal.JoinKind.Left => JoinClauseKind.Left,
            Quarry.Internal.JoinKind.Right => JoinClauseKind.Right,
            _ => JoinClauseKind.Inner
        };

        var clauseInfo = new JoinClauseInfo(
            joinKind,
            joinedEntityName: join.JoinedTableName,
            joinedTableName: join.JoinedTableName,
            onConditionSql: join.OnConditionSql,
            parameters: EmptyParams,
            joinedSchemaName: join.JoinedSchemaName,
            tableAlias: join.TableAlias);

        var site = new UsageSiteInfo(
            methodName: "Join",
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: InterceptorKind.Join,
            invocationSyntax: null!,
            uniqueId: $"conv_join_{uniqueIndex}",
            clauseInfo: clauseInfo);
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, ClauseRole.Join);
    }

    private static ChainedClauseSite MakeSetClause(string columnSql, int parameterIndex, int uniqueIndex)
    {
        var parameters = new[] { new ParameterInfo(parameterIndex, $"@p{parameterIndex}", "object", "value") };
        var clauseInfo = new SetClauseInfo(columnSql, parameterIndex, parameters);
        var site = new UsageSiteInfo(
            methodName: "Set",
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.ExecutableUpdateBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: InterceptorKind.UpdateSet,
            invocationSyntax: null!,
            uniqueId: $"conv_set_{uniqueIndex}",
            clauseInfo: clauseInfo);
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, ClauseRole.UpdateSet);
    }

    private static ChainedClauseSite MakePaginationClause(ClauseRole role, int uniqueIndex)
    {
        var parameters = new[] { new ParameterInfo(0, "@p0", "int", "0") };
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, "@p0", parameters);
        var site = new UsageSiteInfo(
            methodName: role == ClauseRole.Limit ? "Limit" : "Offset",
            filePath: "converter.cs",
            line: 1, column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "Entity",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: null!,
            uniqueId: $"conv_{role}_{uniqueIndex}",
            clauseInfo: clauseInfo);
        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, role);
    }

    /// <summary>
    /// Replicates SqlBuilder.IsSimpleIdentifier to determine how SELECT columns are handled.
    /// </summary>
    private static bool IsSimpleIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Already quoted identifiers (double quotes, backticks, brackets)
        if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
            (value.StartsWith("`") && value.EndsWith("`")) ||
            (value.StartsWith("[") && value.EndsWith("]")))
            return false;

        // SQL expressions contain non-identifier characters
        foreach (var c in value)
        {
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }
}
