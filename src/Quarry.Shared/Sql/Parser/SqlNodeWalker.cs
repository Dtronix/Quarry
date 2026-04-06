using System;
using System.Collections.Generic;

#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql.Parser;
#else
namespace Quarry.Shared.Sql.Parser;
#endif

/// <summary>
/// Walks an SQL AST tree, visiting every node depth-first.
/// </summary>
internal static class SqlNodeWalker
{
    /// <summary>
    /// Visits every node in the tree depth-first, calling <paramref name="visitor"/> on each.
    /// </summary>
    public static void Walk(SqlNode? node, Action<SqlNode> visitor)
    {
        if (node == null) return;

        visitor(node);

        switch (node)
        {
            case SqlSelectStatement s:
                foreach (var col in s.Columns) Walk(col, visitor);
                Walk(s.From, visitor);
                foreach (var join in s.Joins) Walk(join, visitor);
                Walk(s.Where, visitor);
                if (s.GroupBy != null)
                    foreach (var g in s.GroupBy) Walk(g, visitor);
                Walk(s.Having, visitor);
                if (s.OrderBy != null)
                    foreach (var o in s.OrderBy) Walk(o, visitor);
                Walk(s.Limit, visitor);
                Walk(s.Offset, visitor);
                break;

            case SqlDeleteStatement d:
                Walk(d.Table, visitor);
                Walk(d.Where, visitor);
                break;

            case SqlUpdateStatement up:
                Walk(up.Table, visitor);
                foreach (var a in up.Assignments) Walk(a, visitor);
                Walk(up.Where, visitor);
                break;

            case SqlInsertStatement ins:
                Walk(ins.Table, visitor);
                if (ins.Columns != null)
                    foreach (var col in ins.Columns) Walk(col, visitor);
                foreach (var row in ins.ValueRows)
                    foreach (var v in row) Walk(v, visitor);
                break;

            case SqlAssignment asn:
                Walk(asn.Column, visitor);
                Walk(asn.Value, visitor);
                break;

            case SqlSelectColumn c:
                Walk(c.Expression, visitor);
                break;

            case SqlJoin j:
                Walk(j.Table, visitor);
                Walk(j.Condition, visitor);
                break;

            case SqlBinaryExpr b:
                Walk(b.Left, visitor);
                Walk(b.Right, visitor);
                break;

            case SqlUnaryExpr u:
                Walk(u.Operand, visitor);
                break;

            case SqlFunctionCall f:
                foreach (var arg in f.Arguments) Walk(arg, visitor);
                break;

            case SqlInExpr i:
                Walk(i.Expression, visitor);
                foreach (var v in i.Values) Walk(v, visitor);
                break;

            case SqlBetweenExpr bt:
                Walk(bt.Expression, visitor);
                Walk(bt.Low, visitor);
                Walk(bt.High, visitor);
                break;

            case SqlIsNullExpr isn:
                Walk(isn.Expression, visitor);
                break;

            case SqlParenExpr p:
                Walk(p.Inner, visitor);
                break;

            case SqlCaseExpr cs:
                Walk(cs.Operand, visitor);
                foreach (var wc in cs.WhenClauses) Walk(wc, visitor);
                Walk(cs.ElseResult, visitor);
                break;

            case SqlWhenClause wc:
                Walk(wc.Condition, visitor);
                Walk(wc.Result, visitor);
                break;

            case SqlCastExpr ct:
                Walk(ct.Expression, visitor);
                break;

            case SqlExistsExpr ex:
                Walk(ex.Subquery, visitor);
                break;

            case SqlOrderTerm ot:
                Walk(ot.Expression, visitor);
                break;

            // Leaf nodes: SqlColumnRef, SqlLiteral, SqlParameter, SqlUnsupported,
            // SqlStarColumn, SqlTableSource — no children to walk.
        }
    }

    /// <summary>
    /// Finds all nodes of type <typeparamref name="T"/> in the tree.
    /// </summary>
    public static List<T> FindAll<T>(SqlNode? root) where T : SqlNode
    {
        var results = new List<T>();
        Walk(root, n => { if (n is T match) results.Add(match); });
        return results;
    }
}
