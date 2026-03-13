using System;
using System.Collections.Generic;
using System.Linq;

namespace Quarry.Shared.Scaffold;

internal static class JunctionTableDetector
{
    public sealed class JunctionTableResult
    {
        public string TableName { get; }
        public string? Schema { get; }
        public ForeignKeyMetadata LeftFk { get; }
        public ForeignKeyMetadata RightFk { get; }
        public List<ColumnMetadata> ExtraColumns { get; }

        public JunctionTableResult(
            string tableName,
            string? schema,
            ForeignKeyMetadata leftFk,
            ForeignKeyMetadata rightFk,
            List<ColumnMetadata> extraColumns)
        {
            TableName = tableName;
            Schema = schema;
            LeftFk = leftFk;
            RightFk = rightFk;
            ExtraColumns = extraColumns;
        }
    }

    /// <summary>
    /// Detects if a table is a junction/bridge table for many-to-many relationships.
    /// A junction table has exactly 2 FK columns that together form the composite PK
    /// (or a unique composite index), and at most 2 additional non-FK, non-PK columns.
    /// </summary>
    public static JunctionTableResult? Detect(
        string tableName,
        string? schema,
        List<ColumnMetadata> columns,
        PrimaryKeyMetadata? primaryKey,
        List<ForeignKeyMetadata> foreignKeys,
        List<IndexMetadata> indexes)
    {
        // Must have exactly 2 FKs
        if (foreignKeys.Count != 2)
            return null;

        var fkColumns = new HashSet<string>(foreignKeys.Select(fk => fk.ColumnName), StringComparer.OrdinalIgnoreCase);

        // The 2 FK columns must form the composite PK, or be covered by a unique composite index
        var isCompositePk = false;
        if (primaryKey != null && primaryKey.Columns.Count == 2)
        {
            isCompositePk = primaryKey.Columns.All(c => fkColumns.Contains(c));
        }

        if (!isCompositePk)
        {
            // Check for unique composite index covering both FK columns
            var hasUniqueIndex = indexes.Any(idx =>
                idx.IsUnique &&
                idx.Columns.Count == 2 &&
                idx.Columns.All(c => fkColumns.Contains(c)));

            if (!hasUniqueIndex)
                return null;
        }

        // At most 2 additional non-FK columns (e.g., created_at, sort_order)
        var extraColumns = columns
            .Where(c => !fkColumns.Contains(c.Name) &&
                        (primaryKey == null || !primaryKey.Columns.Any(pc => pc.Equals(c.Name, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (extraColumns.Count > 2)
            return null;

        return new JunctionTableResult(
            tableName,
            schema,
            foreignKeys[0],
            foreignKeys[1],
            extraColumns);
    }
}
