using System;
using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;
using Quarry.Generators.Sql;
using Quarry;

namespace Quarry.Generators.Sql;

/// <summary>
/// A structured representation of a SQL fragment that separates static SQL text
/// from parameter slot positions. This allows rendering the fragment with correct
/// parameter indices and dialect-specific formatting without any string manipulation
/// or regex replacement.
/// </summary>
/// <remarks>
/// <para>
/// A SQL fragment like <c>("Name" = @p0 AND "Age" > @p1)</c> is decomposed into:
/// </para>
/// <list type="bullet">
/// <item>TextSegments: ["(\"Name\" = ", " AND \"Age\" > ", ")"]</item>
/// <item>ParameterSlots: [0, 1] (clause-local indices)</item>
/// </list>
/// <para>
/// There is always exactly one more text segment than parameter slots.
/// TextSegments[0] is the text before the first parameter,
/// TextSegments[N] is the text after the last parameter.
/// </para>
/// </remarks>
internal sealed class SqlFragmentTemplate
{
    /// <summary>
    /// Creates a new fragment template from pre-split text segments and parameter slots.
    /// </summary>
    /// <param name="textSegments">
    /// The static SQL text segments, interleaved around parameter slots.
    /// Length must be exactly <paramref name="parameterSlots"/>.Length + 1.
    /// </param>
    /// <param name="parameterSlots">
    /// Clause-local parameter indices (0-based within this clause).
    /// These are mapped to global indices during rendering.
    /// </param>
    public SqlFragmentTemplate(string[] textSegments, int[] parameterSlots)
    {
        TextSegments = textSegments;
        ParameterSlots = parameterSlots;
    }

    /// <summary>
    /// Gets the static SQL text segments, interleaved around parameter slots.
    /// </summary>
    public string[] TextSegments { get; }

    /// <summary>
    /// Gets the clause-local parameter indices.
    /// </summary>
    public int[] ParameterSlots { get; }

    /// <summary>
    /// Gets whether this template contains any parameter slots.
    /// </summary>
    public bool HasParameters => ParameterSlots.Length > 0;

    /// <summary>
    /// Gets the number of distinct parameter slots in this template.
    /// </summary>
    public int ParameterCount => ParameterSlots.Length;

    /// <summary>
    /// Renders the fragment with the correct global parameter indices and dialect formatting.
    /// This produces the final SQL text with no post-processing needed.
    /// </summary>
    /// <param name="dialect">The SQL dialect for parameter formatting.</param>
    /// <param name="parameterBaseIndex">
    /// The global starting parameter index for this clause's parameters.
    /// Clause-local slot 0 maps to this index, slot 1 maps to baseIndex + 1, etc.
    /// </param>
    /// <returns>The rendered SQL fragment with correct parameter placeholders.</returns>
    public string Render(SqlDialect dialect, int parameterBaseIndex)
    {
        if (ParameterSlots.Length == 0)
        {
            // No parameters — return the single text segment directly
            return TextSegments[0];
        }

        var sb = new StringBuilder();
        for (int i = 0; i < ParameterSlots.Length; i++)
        {
            sb.Append(TextSegments[i]);
            sb.Append(SqlFormatting.FormatParameter(dialect, parameterBaseIndex + ParameterSlots[i]));
        }
        // Append the trailing text segment after the last parameter
        sb.Append(TextSegments[ParameterSlots.Length]);

        return sb.ToString();
    }

    /// <summary>
    /// Renders the fragment into an existing StringBuilder.
    /// </summary>
    public void RenderTo(StringBuilder sb, SqlDialect dialect, int parameterBaseIndex)
    {
        if (ParameterSlots.Length == 0)
        {
            sb.Append(TextSegments[0]);
            return;
        }

        for (int i = 0; i < ParameterSlots.Length; i++)
        {
            sb.Append(TextSegments[i]);
            sb.Append(SqlFormatting.FormatParameter(dialect, parameterBaseIndex + ParameterSlots[i]));
        }
        sb.Append(TextSegments[ParameterSlots.Length]);
    }

    /// <summary>
    /// Creates a template with no parameters — just a static SQL string.
    /// </summary>
    public static SqlFragmentTemplate Static(string sql)
    {
        return new SqlFragmentTemplate(new[] { sql }, Array.Empty<int>());
    }

    /// <summary>
    /// Builds a <see cref="SqlFragmentTemplate"/> from a <see cref="ClauseInfo"/>'s SQL fragment
    /// and its known parameter list. Uses exact string matching on known parameter names
    /// to split the fragment — no regex.
    /// </summary>
    /// <param name="clauseInfo">The clause info containing the SQL fragment and parameter metadata.</param>
    /// <returns>
    /// A structured template that can be rendered with any base parameter index and dialect.
    /// </returns>
    public static SqlFragmentTemplate FromClauseInfo(ClauseInfo clauseInfo)
    {
        if (clauseInfo.Parameters.Count == 0)
        {
            return Static(clauseInfo.SqlFragment);
        }

        return SplitFragment(clauseInfo.SqlFragment, clauseInfo.Parameters);
    }

    /// <summary>
    /// Splits a SQL fragment around known parameter placeholders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expression translator produces parameter names like <c>@p0</c>, <c>@p1</c>, etc.
    /// These are the <see cref="ParameterInfo.Name"/> values. This method finds each parameter
    /// name in the fragment using exact string matching (not regex), taking care to match the
    /// full parameter name (not a prefix of a longer name like <c>@p1</c> inside <c>@p10</c>).
    /// </para>
    /// <para>
    /// Parameters are found left-to-right in the fragment. Each occurrence is replaced by a
    /// clause-local slot index (0 for the first parameter found, 1 for the second, etc.).
    /// </para>
    /// </remarks>
    private static SqlFragmentTemplate SplitFragment(string fragment, IReadOnlyList<ParameterInfo> parameters)
    {
        // Build a sorted list of (position, paramName, clauseLocalIndex) for all parameter
        // occurrences in the fragment, found by exact string matching.
        var occurrences = new List<(int Position, int Length, int ClauseLocalIndex)>();

        for (int paramLocalIdx = 0; paramLocalIdx < parameters.Count; paramLocalIdx++)
        {
            var paramName = parameters[paramLocalIdx].Name; // e.g., "@p0"
            var searchStart = 0;
            while (searchStart < fragment.Length)
            {
                var pos = fragment.IndexOf(paramName, searchStart, StringComparison.Ordinal);
                if (pos < 0) break;

                // Ensure this is a complete match — the character after the parameter name
                // must not be a digit (to avoid @p1 matching inside @p10)
                var endPos = pos + paramName.Length;
                if (endPos < fragment.Length && char.IsDigit(fragment[endPos]))
                {
                    // Partial match — skip past this occurrence
                    searchStart = endPos;
                    continue;
                }

                occurrences.Add((pos, paramName.Length, paramLocalIdx));
                searchStart = endPos;
            }
        }

        if (occurrences.Count == 0)
        {
            // No parameter references found in fragment — treat as static
            return Static(fragment);
        }

        // Sort by position (left-to-right in the fragment)
        occurrences.Sort((a, b) => a.Position.CompareTo(b.Position));

        // Split the fragment into text segments and parameter slots
        var textSegments = new string[occurrences.Count + 1];
        var parameterSlots = new int[occurrences.Count];

        int textStart = 0;
        for (int i = 0; i < occurrences.Count; i++)
        {
            var (pos, len, localIdx) = occurrences[i];
            textSegments[i] = fragment.Substring(textStart, pos - textStart);
            parameterSlots[i] = localIdx;
            textStart = pos + len;
        }
        // Trailing text after the last parameter
        textSegments[occurrences.Count] = fragment.Substring(textStart);

        return new SqlFragmentTemplate(textSegments, parameterSlots);
    }
}
