using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quarry.Shared.Migration;

namespace Quarry.Tool.Schema;

/// <summary>
/// An explicit set of column renames supplied via <c>--rename-map</c>. Renames are
/// trusted verbatim and bypass heuristic scoring, so a rename the differ would score
/// below its acceptance floor still happens (and never degrades to drop+add).
///
/// Accepted spec forms:
/// <list type="bullet">
///   <item>Inline: <c>users.user_name=UserName,orders.qty=Quantity,legacy=Legacy</c></item>
///   <item>File: <c>@renames.csv</c> with rows <c>table,from,to</c> or <c>from,to</c></item>
/// </list>
/// A table-qualified entry (<c>table.col=new</c>) applies only to that table; a bare
/// entry (<c>col=new</c>) applies to a column of that name in any table. Qualified
/// entries take precedence over bare ones. Table and column matching is
/// case-insensitive; the target name is preserved verbatim.
/// </summary>
internal sealed class RenameMap
{
    // Keys are lowercased for case-insensitive matching; values (target names) are verbatim.
    private readonly Dictionary<(string Table, string From), string> _qualified;
    private readonly Dictionary<string, string> _bare;

    private RenameMap(
        Dictionary<(string, string), string> qualified,
        Dictionary<string, string> bare)
    {
        _qualified = qualified;
        _bare = bare;
    }

    public bool IsEmpty => _qualified.Count == 0 && _bare.Count == 0;

    /// <summary>
    /// Resolves the target name for <paramref name="column"/> in <paramref name="table"/>,
    /// or null if no rename applies. Table-qualified entries win over bare entries.
    /// </summary>
    public string? Resolve(string table, string column)
    {
        if (_qualified.TryGetValue((table.ToLowerInvariant(), column.ToLowerInvariant()), out var q))
            return q;
        if (_bare.TryGetValue(column.ToLowerInvariant(), out var b))
            return b;
        return null;
    }

    /// <summary>
    /// Parses a <c>--rename-map</c> spec (inline or <c>@file</c>).
    /// </summary>
    public static RenameMap Parse(string? spec)
    {
        var qualified = new Dictionary<(string, string), string>();
        var bare = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(spec))
            return new RenameMap(qualified, bare);

        spec = spec.Trim();

        if (spec.StartsWith("@", StringComparison.Ordinal))
        {
            var path = spec.Substring(1).Trim();
            if (!File.Exists(path))
                throw new FileNotFoundException($"Rename-map file not found: {path}");
            ParseFile(File.ReadAllLines(path), qualified, bare);
        }
        else
        {
            foreach (var token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
                ParseInlineEntry(token.Trim(), qualified, bare);
        }

        return new RenameMap(qualified, bare);
    }

    private static void ParseInlineEntry(
        string token,
        Dictionary<(string, string), string> qualified,
        Dictionary<string, string> bare)
    {
        if (token.Length == 0)
            return;

        var eq = token.IndexOf('=');
        if (eq <= 0 || eq == token.Length - 1)
            throw new FormatException($"Invalid rename-map entry '{token}'. Expected 'table.col=NewName' or 'col=NewName'.");

        var left = token.Substring(0, eq).Trim();
        var to = token.Substring(eq + 1).Trim();

        var dot = left.IndexOf('.');
        if (dot > 0 && dot < left.Length - 1)
        {
            var table = left.Substring(0, dot).Trim();
            var from = left.Substring(dot + 1).Trim();
            qualified[(table.ToLowerInvariant(), from.ToLowerInvariant())] = to;
        }
        else
        {
            bare[left.ToLowerInvariant()] = to;
        }
    }

    private static void ParseFile(
        string[] lines,
        Dictionary<(string, string), string> qualified,
        Dictionary<string, string> bare)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            var fields = line.Split(',').Select(f => f.Trim()).ToArray();

            // Skip an optional header row.
            if (fields.Length is 2 or 3 &&
                fields[^1].Equals("to", StringComparison.OrdinalIgnoreCase) &&
                fields[^2].Equals("from", StringComparison.OrdinalIgnoreCase))
                continue;

            if (fields.Length == 3 && fields.All(f => f.Length > 0))
                qualified[(fields[0].ToLowerInvariant(), fields[1].ToLowerInvariant())] = fields[2];
            else if (fields.Length == 2 && fields.All(f => f.Length > 0))
                bare[fields[0].ToLowerInvariant()] = fields[1];
            else
                throw new FormatException($"Invalid rename-map row '{line}'. Expected 'table,from,to' or 'from,to'.");
        }
    }

    /// <summary>
    /// The outcome of <see cref="Validate"/>: fatal <see cref="Errors"/> (the caller must abort)
    /// and non-fatal <see cref="Warnings"/> (surfaced but processing continues).
    /// </summary>
    public sealed record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// Validates this map against the live-database snapshot (<paramref name="from"/>) and the
    /// desired project snapshot (<paramref name="to"/>) BEFORE any forced rename is applied or any
    /// baseline is written. Catches user mistakes that would otherwise cause a spurious drop, a
    /// data-loss guard crash (querying a column that no longer exists under its live name), or an
    /// invalid snapshot with duplicate columns:
    /// <list type="bullet">
    ///   <item>Two source columns in one table mapping to the same target (duplicate column) — error.</item>
    ///   <item>A target colliding with an existing column that is not itself renamed away — error.</item>
    ///   <item>A target absent from the project schema for that table — error (it would be dropped,
    ///     losing the renamed data and breaking the drop guard).</item>
    ///   <item>An entry that matches no column in any live table — warning (likely a typo; ignored).</item>
    /// </list>
    /// Tables are paired between the two snapshots by canonical name (case/separator-insensitive),
    /// matching the differ's own rename detection.
    /// </summary>
    public ValidationResult Validate(SchemaSnapshot from, SchemaSnapshot to)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (IsEmpty)
            return new ValidationResult(errors, warnings);

        // Index desired (project) tables by canonical name for target-existence lookup.
        var toByCanon = new Dictionary<string, TableDef>(StringComparer.Ordinal);
        foreach (var t in to.Tables)
            toByCanon[NamingConventions.Canonicalize(t.TableName)] = t;

        var usedQualified = new HashSet<(string, string)>();
        var usedBare = new HashSet<string>();

        foreach (var table in from.Tables)
        {
            // Collect the renames that would apply to this table (old -> target).
            var applied = new List<(string Old, string Target)>();
            foreach (var col in table.Columns)
            {
                var target = Resolve(table.TableName, col.Name);
                if (target == null || string.Equals(target, col.Name, StringComparison.Ordinal))
                    continue;
                applied.Add((col.Name, target));

                var key = (table.TableName.ToLowerInvariant(), col.Name.ToLowerInvariant());
                if (_qualified.ContainsKey(key))
                    usedQualified.Add(key);
                else
                    usedBare.Add(col.Name.ToLowerInvariant());
            }

            if (applied.Count == 0)
                continue;

            var renamedAway = new HashSet<string>(applied.Select(a => a.Old), StringComparer.OrdinalIgnoreCase);
            var existing = new HashSet<string>(table.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            // Duplicate targets within the table -> would produce two columns of the same name.
            foreach (var g in applied.GroupBy(a => a.Target, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                errors.Add($"rename-map: table '{table.TableName}' maps multiple columns to '{g.Key}' ({string.Join(", ", g.Select(a => a.Old))}).");

            foreach (var (old, target) in applied)
            {
                // Target collides with a column that stays (not renamed away) -> duplicate column.
                if (existing.Contains(target) && !renamedAway.Contains(target))
                    errors.Add($"rename-map: table '{table.TableName}' rename '{old}'->'{target}' collides with the existing column '{target}'.");
            }

            // Every target must exist in the project schema for this table; otherwise the alignment
            // diff would DROP the renamed column (losing its data) and the guard would query a
            // column the live database no longer has under that name.
            if (toByCanon.TryGetValue(NamingConventions.Canonicalize(table.TableName), out var toTable))
            {
                var toCols = new HashSet<string>(toTable.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var (old, target) in applied)
                {
                    if (!toCols.Contains(target))
                        errors.Add($"rename-map: table '{table.TableName}' target '{target}' (from '{old}') is not a column in the project schema.");
                }
            }
        }

        // Entries that matched no live column at all are almost always typos; they are silent
        // no-ops, so surface them as warnings.
        foreach (var key in _qualified.Keys)
        {
            if (!usedQualified.Contains(key))
                warnings.Add($"rename-map: entry '{key.Table}.{key.From}=…' matched no column in the live database (ignored).");
        }
        foreach (var from2 in _bare.Keys)
        {
            if (!usedBare.Contains(from2))
                warnings.Add($"rename-map: entry '{from2}=…' matched no column in any live table (ignored).");
        }

        return new ValidationResult(errors, warnings);
    }

    /// <summary>
    /// A single forced column rename applied to a snapshot.
    /// </summary>
    public sealed record ForcedRename(string Table, string? Schema, string OldName, string NewName);

    /// <summary>
    /// Returns a copy of <paramref name="from"/> with every mapped column renamed to its
    /// target name (updating composite-key, foreign-key, and index references too), plus
    /// the list of renames applied. Callers diff the returned snapshot against the desired
    /// schema and prepend an explicit RenameColumn step for each <see cref="ForcedRename"/>,
    /// so the rename is emitted even when scoring would not have detected it.
    /// </summary>
    public (SchemaSnapshot PatchedFrom, IReadOnlyList<ForcedRename> Applied) ApplyForcedRenames(SchemaSnapshot from)
    {
        if (IsEmpty)
            return (from, Array.Empty<ForcedRename>());

        var applied = new List<ForcedRename>();
        var newTables = new List<TableDef>(from.Tables.Count);

        foreach (var table in from.Tables)
        {
            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in table.Columns)
            {
                var target = Resolve(table.TableName, col.Name);
                if (target != null && !string.Equals(target, col.Name, StringComparison.Ordinal))
                    renames[col.Name] = target;
            }

            if (renames.Count == 0)
            {
                newTables.Add(table);
                continue;
            }

            var newColumns = new List<ColumnDef>(table.Columns.Count);
            foreach (var col in table.Columns)
            {
                if (renames.TryGetValue(col.Name, out var newName))
                {
                    applied.Add(new ForcedRename(table.TableName, table.SchemaName, col.Name, newName));
                    newColumns.Add(WithName(col, newName));
                }
                else
                {
                    newColumns.Add(col);
                }
            }

            var newFks = table.ForeignKeys
                .Select(fk => renames.TryGetValue(fk.ColumnName, out var n)
                    ? new ForeignKeyDef(fk.ConstraintName, n, fk.ReferencedTable, fk.ReferencedColumn, fk.OnDelete, fk.OnUpdate)
                    : fk)
                .ToList();

            var newIndexes = table.Indexes
                .Select(idx => idx.Columns.Any(c => renames.ContainsKey(c))
                    ? new IndexDef(idx.Name, idx.Columns.Select(c => renames.GetValueOrDefault(c, c)).ToList(), idx.IsUnique, idx.Filter, idx.Method, idx.DescendingColumns)
                    : idx)
                .ToList();

            var newComposite = table.CompositeKeyColumns?
                .Select(c => renames.GetValueOrDefault(c, c))
                .ToList();

            newTables.Add(new TableDef(
                table.TableName, table.SchemaName, table.NamingStyle,
                newColumns, newFks, newIndexes, newComposite, table.CharacterSet));
        }

        var patched = new SchemaSnapshot(from.Version, from.Name, from.Timestamp, from.ParentVersion, newTables);
        return (patched, applied);
    }

    private static ColumnDef WithName(ColumnDef col, string newName) => new(
        newName, col.ClrType, col.IsNullable, col.Kind,
        col.IsIdentity, col.IsClientGenerated, col.IsComputed,
        col.MaxLength, col.Precision, col.Scale,
        col.HasDefault, col.DefaultExpression, col.MappedName,
        col.ReferencedEntityName, col.CustomTypeMapping,
        col.ComputedExpression, col.Collation);
}
