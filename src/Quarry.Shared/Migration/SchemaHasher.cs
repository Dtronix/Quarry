using System.Collections.Generic;
using System.Text;

namespace Quarry.Shared.Migration;

/// <summary>
/// Computes a lightweight hash of a schema for drift detection.
/// Uses table count + column count + sorted table/column names.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class SchemaHasher
{
    /// <summary>
    /// Computes a deterministic hash string from a collection of table definitions.
    /// </summary>
    public static string ComputeHash(IReadOnlyList<TableDef> tables)
    {
        // Build a deterministic string: sorted table names with sorted column signatures.
        // Column signature format must match ComputeHashFromEntities exactly.
        var entries = new List<string>(tables.Count);
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            var cols = new List<string>(table.Columns.Count);
            for (var j = 0; j < table.Columns.Count; j++)
            {
                var c = table.Columns[j];
                // Use \0 as delimiter to prevent collisions when names contain ':' or ','
                cols.Add(c.Name + "\0" + c.ClrType + "\0" + (c.IsNullable ? "1" : "0") + "\0" + (int)c.Kind
                    + "\0" + (c.IsIdentity ? "1" : "0") + "\0" + (c.IsComputed ? "1" : "0")
                    + "\0" + (c.MaxLength?.ToString() ?? "") + "\0" + (c.Precision?.ToString() ?? "")
                    + "\0" + (c.Scale?.ToString() ?? "") + "\0" + (c.HasDefault ? "1" : "0")
                    + "\0" + (c.MappedName ?? "") + "\0" + (c.ComputedExpression ?? "")
                    + "\0" + (c.Collation ?? ""));
            }
            cols.Sort(System.StringComparer.Ordinal);
            entries.Add(table.TableName + "\0" + string.Join("\0", cols));
        }
        entries.Sort(System.StringComparer.Ordinal);

        var input = string.Join("\0\0", entries);
        return ComputeFnv1aHash(input);
    }

    /// <summary>
    /// Computes a schema hash from entity info available in the generator context.
    /// Uses table name + column names + types for lightweight comparison.
    /// </summary>
    public static string ComputeHashFromEntities(
        IReadOnlyList<string> tableNames,
        IReadOnlyList<IReadOnlyList<string>> columnSignatures)
    {
        var entries = new List<string>(tableNames.Count);
        for (var i = 0; i < tableNames.Count; i++)
        {
            var cols = new List<string>(columnSignatures[i]);
            cols.Sort(System.StringComparer.Ordinal);
            entries.Add(tableNames[i] + "\0" + string.Join("\0", cols));
        }
        entries.Sort(System.StringComparer.Ordinal);

        var input = string.Join("\0\0", entries);
        return ComputeFnv1aHash(input);
    }

    private static string ComputeFnv1aHash(string input)
    {
        // FNV-1a 64-bit hash
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        var hash = fnvOffset;
        for (var i = 0; i < input.Length; i++)
        {
            hash ^= input[i];
            hash *= fnvPrime;
        }

        return hash.ToString("x16");
    }
}
