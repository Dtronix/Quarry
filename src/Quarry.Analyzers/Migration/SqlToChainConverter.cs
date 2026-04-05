using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql.Parser;

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

    private readonly ContextInfo _context;

    /// <summary>Table name (case-insensitive) → (EntityInfo, EntityMapping).</summary>
    private readonly Dictionary<string, (EntityInfo Entity, EntityMapping Mapping)> _tableToEntity;

    public SqlToChainConverter(ContextInfo context)
    {
        _context = context;

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
        // Must have a FROM clause
        if (stmt.From == null)
            return "No FROM clause";

        // FROM table must resolve to a known entity
        if (!_tableToEntity.ContainsKey(stmt.From.TableName))
            return $"Unknown table '{stmt.From.TableName}'";

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
        var aliasMap = BuildAliasMap(stmt);

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
    {
        var map = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);

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
    {
        var aliasMap = BuildAliasMap(stmt);

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

        // FROM → ctx.Accessor()
        var fromEntry = _tableToEntity[stmt.From!.TableName];
        sb.Append(contextVarName);
        sb.Append('.');
        sb.Append(fromEntry.Mapping.PropertyName);
        sb.Append("()");

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
        string paramName;
        EntityInfo? entity = null;

        if (colRef.TableAlias != null)
        {
            aliasToParam.TryGetValue(colRef.TableAlias, out paramName!);
            aliasMap.TryGetValue(colRef.TableAlias, out entity);
        }
        else
        {
            // Find the first entity that has this column
            paramName = aliasToParam.Values.FirstOrDefault() ?? "x";
            foreach (var kvp in aliasMap)
            {
                if (ResolveColumnToProperty(kvp.Value, colRef.ColumnName) != null)
                {
                    entity = kvp.Value;
                    aliasToParam.TryGetValue(kvp.Key, out paramName!);
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
                return $"\"{literal.Value}\"";
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
                return $"Sql.Sum({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "AVG":
                return $"Sql.Avg({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "MIN":
                return $"Sql.Min({TranslateExpr(func.Arguments[0], aliasMap, aliasToParam, parameterArgs)})";

            case "MAX":
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
