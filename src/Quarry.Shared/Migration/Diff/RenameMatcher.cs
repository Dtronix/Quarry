using System;
using System.Collections.Generic;

namespace Quarry.Shared.Migration;

/// <summary>
/// Detects potential renames between added and removed tables or columns.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class RenameMatcher
{
    /// <summary>
    /// Result of a rename match attempt.
    /// </summary>
#if QUARRY_GENERATOR
    internal
#else
    public
#endif
    sealed class RenameCandidate
    {
        public string OldName { get; }
        public string NewName { get; }
        public double Score { get; }

        public RenameCandidate(string oldName, string newName, double score)
        {
            OldName = oldName;
            NewName = newName;
            Score = score;
        }
    }

    /// <summary>
    /// Attempts to match a single added table with a single dropped table as a rename.
    /// Returns null if no good match found.
    /// </summary>
    public static RenameCandidate? MatchTable(TableDef added, TableDef dropped)
    {
        var score = 0.0;

        // Column count similarity
        if (added.Columns.Count == dropped.Columns.Count)
            score += 0.25;
        else if (Math.Abs(added.Columns.Count - dropped.Columns.Count) <= 2)
            score += 0.1;

        // Name similarity (primary signal)
        score += LevenshteinDistance.Similarity(dropped.TableName, added.TableName) * 0.55;

        // Column name overlap bonus
        var addedColNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < added.Columns.Count; j++)
            addedColNames.Add(added.Columns[j].Name);
        var matchedCols = 0;
        for (var i = 0; i < dropped.Columns.Count; i++)
        {
            if (addedColNames.Contains(dropped.Columns[i].Name))
                matchedCols++;
        }
        if (dropped.Columns.Count > 0)
            score += 0.2 * matchedCols / dropped.Columns.Count;

        if (score >= 0.6)
            return new RenameCandidate(dropped.TableName, added.TableName, score);

        return null;
    }

    /// <summary>
    /// Attempts to match a single added column with a single dropped column as a rename.
    /// Returns null if no good match found.
    /// </summary>
    public static RenameCandidate? MatchColumn(ColumnDef added, ColumnDef dropped)
    {
        var score = 0.0;

        // Type match (strong signal — different types are unlikely to be renames)
        if (added.ClrType == dropped.ClrType)
            score += 0.35;

        // Name similarity (primary signal)
        score += LevenshteinDistance.Similarity(dropped.Name, added.Name) * 0.45;

        // Nullability and kind match
        if (added.IsNullable == dropped.IsNullable && added.Kind == dropped.Kind)
            score += 0.1;

        // MapTo consistency: matching mappings increase confidence, differing ones decrease it
        if (added.MappedName == dropped.MappedName)
            score += 0.1;
        else if (added.MappedName != null && dropped.MappedName != null)
            score -= 0.05;

        if (score >= 0.6)
            return new RenameCandidate(dropped.Name, added.Name, score);

        return null;
    }

    /// <summary>
    /// Whether a rename candidate should be auto-accepted in non-interactive mode.
    /// </summary>
    public static bool ShouldAutoAccept(RenameCandidate candidate)
    {
        return candidate.Score >= 0.8;
    }
}
