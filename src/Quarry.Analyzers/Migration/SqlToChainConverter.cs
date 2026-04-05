using System;
using System.Collections.Generic;
using System.Linq;
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
