using System;
using System.Collections.Generic;
using System.Linq;

namespace Quarry.Shared.Scaffold;

internal static class ImplicitForeignKeyDetector
{
    public sealed class ImplicitFkCandidate
    {
        public string SourceTable { get; }
        public string SourceColumn { get; }
        public string TargetTable { get; }
        public string TargetColumn { get; }
        public int Score { get; }
        public double Confidence => Math.Min(Score / 100.0, 1.0);

        public ImplicitFkCandidate(string sourceTable, string sourceColumn, string targetTable, string targetColumn, int score)
        {
            SourceTable = sourceTable;
            SourceColumn = sourceColumn;
            TargetTable = targetTable;
            TargetColumn = targetColumn;
            Score = score;
        }
    }

    /// <summary>
    /// Detects implicit foreign keys by convention-based matching.
    /// Only returns candidates scoring >= 50.
    /// </summary>
    public static List<ImplicitFkCandidate> Detect(
        string sourceTable,
        List<ColumnMetadata> sourceColumns,
        List<ForeignKeyMetadata> existingFks,
        Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)> allTables,
        List<IndexMetadata> sourceIndexes)
    {
        var existingFkColumns = new HashSet<string>(existingFks.Select(fk => fk.ColumnName), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ImplicitFkCandidate>();

        foreach (var col in sourceColumns)
        {
            // Skip columns that already have explicit FKs
            if (existingFkColumns.Contains(col.Name))
                continue;

            // Skip PK columns and boolean-like columns
            if (col.IsIdentity)
                continue;

            // Try to find a matching table
            var match = FindBestMatch(col, sourceTable, allTables, sourceIndexes);
            if (match != null && match.Score >= 50)
                candidates.Add(match);
        }

        return candidates;
    }

    private static ImplicitFkCandidate? FindBestMatch(
        ColumnMetadata column,
        string sourceTable,
        Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)> allTables,
        List<IndexMetadata> sourceIndexes)
    {
        var colName = column.Name;
        var colType = column.DataType;

        // Extract potential table references from column name
        var tableRef = ExtractTableReference(colName);
        if (tableRef == null)
            return null;

        ImplicitFkCandidate? best = null;
        var matchCount = 0;

        foreach (var (tableName, (pk, tableCols)) in allTables)
        {
            if (tableName.Equals(sourceTable, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pk == null || pk.Columns.Count != 1)
                continue;

            var pkCol = tableCols.FirstOrDefault(c => c.Name.Equals(pk.Columns[0], StringComparison.OrdinalIgnoreCase));
            if (pkCol == null)
                continue;

            var score = ScoreMatch(colName, tableRef, tableName, colType, pkCol.DataType, sourceIndexes);
            if (score > 0)
            {
                matchCount++;
                if (best == null || score > best.Score)
                {
                    best = new ImplicitFkCandidate(sourceTable, colName, tableName, pk.Columns[0], score);
                }
            }
        }

        // Penalize ambiguous matches
        if (best != null && matchCount > 1)
        {
            best = new ImplicitFkCandidate(best.SourceTable, best.SourceColumn, best.TargetTable, best.TargetColumn, best.Score - 20);
        }

        return best;
    }

    private static int ScoreMatch(
        string columnName,
        string tableRef,
        string candidateTable,
        string sourceType,
        string targetType,
        List<IndexMetadata> sourceIndexes)
    {
        var score = 0;
        var singular = Singularizer.Singularize(candidateTable);

        // +40: Column name exactly matches "{table}_id" or "{table}Id"
        if (tableRef.Equals(candidateTable, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        // +30: Column name matches "{singular(table)}_id"
        else if (tableRef.Equals(singular, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }
        // +10: Column name contains table name as substring
        else if (columnName.Contains(candidateTable, StringComparison.OrdinalIgnoreCase) ||
                 columnName.Contains(singular, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        else
        {
            return 0; // No name match at all
        }

        // +20: Type matches target PK type
        if (sourceType.Equals(targetType, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        // -30: Column has a unique index (less likely to be FK)
        var hasUniqueIndex = sourceIndexes.Any(idx =>
            idx.IsUnique && idx.Columns.Count == 1 &&
            idx.Columns[0].Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (hasUniqueIndex)
        {
            score -= 30;
        }

        return score;
    }

    private static string? ExtractTableReference(string columnName)
    {
        // Try pattern: {TableName}_id, {TableName}Id, {TableName}_fk, {TableName}_key
        foreach (var suffix in new[] { "_id", "Id", "_fk", "_key" })
        {
            if (columnName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && columnName.Length > suffix.Length)
            {
                return columnName.Substring(0, columnName.Length - suffix.Length);
            }
        }

        return null;
    }
}
