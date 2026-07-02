using System;
using System.Collections.Generic;
using System.Text;

namespace Quarry.Generators.IR;

/// <summary>
/// Generation-time bind-order markers for MySQL's opaque positional <c>?</c> placeholders.
///
/// MySqlConnector binds the Nth <c>?</c> in the SQL text to the Nth parameter added to the
/// command, while the carrier bind loop adds parameters in chain-call (<c>GlobalIndex</c>)
/// order. Renderers that emit placeholders out of chain order (e.g. the DISTINCT + ORDER BY
/// derived-table wrap, which hoists ORDER BY expressions textually before WHERE) silently
/// swap values on MySQL (#303). To track SQL-text order without hand-mirroring every
/// renderer's traversal, MySQL variant rendering emits <c>{__Q{globalIndex}__}</c> markers
/// instead of bare <c>?</c>; after assembly, <see cref="RewriteAndExtract"/> scans each
/// variant once, records the marker sequence (= the text-order bind ranking consumed by
/// CarrierEmitter), and rewrites markers to <c>?</c> / collection-expansion tokens.
///
/// Markers exist only inside the generator between rendering and the assembly post-pass —
/// never in generated source, manifests, diagnostics, or runtime SQL. Marker emission is
/// opt-in via <c>SqlDialectConfig.EmitMySqlBindMarkers</c> (set only by SqlAssembler's
/// variant rendering) so render paths that bake SQL into runtime strings or comparison keys
/// keep producing bare <c>?</c>.
/// </summary>
internal static class MySqlBindMarkers
{
    private const string Prefix = "{__Q";
    private const string Suffix = "__}";

    /// <summary>Formats the marker token for a global parameter slot.</summary>
    internal static string Format(int globalIndex)
        => Prefix + globalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + Suffix;

    /// <summary>Appends the marker token for a global parameter slot.</summary>
    internal static void AppendTo(StringBuilder sb, int globalIndex)
        => sb.Append(Prefix).Append(globalIndex).Append(Suffix);

    /// <summary>
    /// Single pass over a rendered MySQL variant: rewrites each <c>{__Q{n}__}</c> marker to
    /// <c>?</c> — or to the carrier collection-expansion token <c>{__COL_P{n}__}</c> when
    /// <paramref name="isCollectionSlot"/> returns true for <c>n</c> — and appends each
    /// <c>n</c> to <paramref name="textOrder"/> at its text position. The output is built
    /// once from the inter-marker segments (no <c>string.Replace</c>); when the input
    /// contains no markers the original string instance is returned unchanged.
    /// </summary>
    /// <param name="sql">The rendered SQL variant (possibly marker-free).</param>
    /// <param name="isCollectionSlot">
    /// Predicate for slots that must become collection-expansion tokens instead of <c>?</c>;
    /// null when no collection tokenization applies (non-carrier-eligible chains).
    /// </param>
    /// <param name="textOrder">
    /// Receives the global slot indices in SQL-text order; null when the caller only needs
    /// the rewrite (e.g. stripping markers from trace output).
    /// </param>
    internal static string RewriteAndExtract(string sql, Func<int, bool>? isCollectionSlot, List<int>? textOrder)
    {
        var i = sql.IndexOf(Prefix, StringComparison.Ordinal);
        if (i < 0)
            return sql;

        var sb = new StringBuilder(sql.Length);
        var segStart = 0;
        while (i >= 0)
        {
            var numStart = i + Prefix.Length;
            var numEnd = numStart;
            var slot = 0;
            while (numEnd < sql.Length && sql[numEnd] >= '0' && sql[numEnd] <= '9')
            {
                slot = slot * 10 + (sql[numEnd] - '0');
                numEnd++;
            }
            var isMarker = numEnd > numStart
                && numEnd + Suffix.Length <= sql.Length
                && string.CompareOrdinal(sql, numEnd, Suffix, 0, Suffix.Length) == 0;
            if (!isMarker)
            {
                // Not a marker (e.g. "{__COL_P0__}" or user text); keep scanning past it.
                i = sql.IndexOf(Prefix, i + 1, StringComparison.Ordinal);
                continue;
            }

            sb.Append(sql, segStart, i - segStart);
            if (isCollectionSlot != null && isCollectionSlot(slot))
                sb.Append("{__COL_P").Append(slot).Append("__}");
            else
                sb.Append('?');
            textOrder?.Add(slot);

            segStart = numEnd + Suffix.Length;
            i = sql.IndexOf(Prefix, segStart, StringComparison.Ordinal);
        }
        sb.Append(sql, segStart, sql.Length - segStart);
        return sb.ToString();
    }
}
