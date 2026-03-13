using System;
using System.Collections.Generic;
using System.Text;

#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql;
#else
namespace Quarry.Shared.Sql;
#endif

/// <summary>
/// Shared logic for joining SQL conditions with AND and paren-wrapping.
/// Used by both the runtime SqlBuilder and the compile-time CompileTimeSqlBuilder.
/// </summary>
internal static class SqlClauseJoining
{
    /// <summary>
    /// Appends WHERE keyword + conditions joined by AND with paren-wrapping for multiple conditions.
    /// Single condition: " WHERE cond"
    /// Multiple conditions: " WHERE (cond1) AND (cond2)"
    /// </summary>
    public static void AppendWhereClause(StringBuilder sb, IReadOnlyList<string> conditions)
    {
        if (conditions.Count == 0) return;

        sb.Append(" WHERE ");
        AppendAndJoinedConditions(sb, conditions);
    }

    /// <summary>
    /// Appends HAVING keyword + conditions joined by AND with paren-wrapping for multiple conditions.
    /// </summary>
    public static void AppendHavingClause(StringBuilder sb, IReadOnlyList<string> conditions)
    {
        if (conditions.Count == 0) return;

        sb.Append(" HAVING ");
        AppendAndJoinedConditions(sb, conditions);
    }

    /// <summary>
    /// Joins conditions with AND + paren wrapping.
    /// Single: "cond"
    /// Multiple: "(cond1) AND (cond2)"
    /// Does not include the keyword.
    /// </summary>
    public static void AppendAndJoinedConditions(StringBuilder sb, IReadOnlyList<string> conditions)
    {
        if (conditions.Count == 1)
        {
            sb.Append(conditions[0]);
            return;
        }

        sb.Append('(');
        sb.Append(string.Join(") AND (", (IEnumerable<string>)conditions));
        sb.Append(')');
    }

    /// <summary>
    /// Callback-based overload for joining conditions.
    /// Allows callers (like CompileTimeSqlBuilder) to render each condition via a callback
    /// while the joining structure (paren wrapping, AND separators) is handled here.
    /// </summary>
    public static void AppendAndJoinedConditions(
        StringBuilder sb,
        int count,
        Action<StringBuilder, int> appendCondition)
    {
        if (count == 0) return;

        if (count == 1)
        {
            appendCondition(sb, 0);
            return;
        }

        sb.Append('(');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(") AND (");
            appendCondition(sb, i);
        }
        sb.Append(')');
    }
}
