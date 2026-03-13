using System;
using System.Collections.Generic;
using System.Linq;

namespace Quarry.Shared.Migration;

/// <summary>
/// Core diff engine comparing two SchemaSnapshot instances.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class SchemaDiffer
{
    /// <summary>
    /// Computes the diff between two snapshots.
    /// </summary>
    /// <param name="from">The old snapshot (null for initial migration).</param>
    /// <param name="to">The new snapshot.</param>
    /// <param name="acceptRename">
    /// Callback to confirm renames interactively. If null, auto-accept at >= 0.8, else drop+add.
    /// </param>
    public static IReadOnlyList<MigrationStep> Diff(
        SchemaSnapshot? from,
        SchemaSnapshot to,
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename = null)
    {
        var steps = new List<MigrationStep>();

        var oldTables = new Dictionary<string, TableDef>(StringComparer.OrdinalIgnoreCase);
        var newTables = new Dictionary<string, TableDef>(StringComparer.OrdinalIgnoreCase);

        if (from != null)
        {
            for (var i = 0; i < from.Tables.Count; i++)
                oldTables[from.Tables[i].TableName] = from.Tables[i];
        }

        for (var i = 0; i < to.Tables.Count; i++)
            newTables[to.Tables[i].TableName] = to.Tables[i];

        // Find added and dropped tables
        var addedTables = new List<TableDef>();
        var droppedTables = new List<TableDef>();
        var matchedTables = new List<(TableDef Old, TableDef New)>();

        foreach (var kvp in newTables)
        {
            if (oldTables.ContainsKey(kvp.Key))
                matchedTables.Add((oldTables[kvp.Key], kvp.Value));
            else
                addedTables.Add(kvp.Value);
        }

        foreach (var kvp in oldTables)
        {
            if (!newTables.ContainsKey(kvp.Key))
                droppedTables.Add(kvp.Value);
        }

        // Rename detection for tables — greedy matching across all add/drop pairs
        if (addedTables.Count > 0 && droppedTables.Count > 0)
        {
            DetectTableRenames(addedTables, droppedTables, steps, acceptRename);
        }

        // Drop foreign keys before dropping tables
        foreach (var table in droppedTables)
        {
            for (var i = 0; i < table.ForeignKeys.Count; i++)
            {
                var fk = table.ForeignKeys[i];
                steps.Add(new MigrationStep(
                    MigrationStepType.DropForeignKey,
                    StepClassification.Destructive,
                    table.TableName,
                    table.SchemaName,
                    null,
                    fk,
                    null,
                    $"Drop foreign key '{fk.ConstraintName}' from '{table.TableName}'"));
            }
        }

        // Create tables
        foreach (var table in addedTables)
        {
            steps.Add(new MigrationStep(
                MigrationStepType.CreateTable,
                StepClassification.Safe,
                table.TableName,
                table.SchemaName,
                null,
                null,
                table,
                $"Create table '{table.TableName}'"));
        }

        // Drop tables
        foreach (var table in droppedTables)
        {
            steps.Add(new MigrationStep(
                MigrationStepType.DropTable,
                StepClassification.Destructive,
                table.TableName,
                table.SchemaName,
                null,
                table,
                null,
                $"Drop table '{table.TableName}'"));
        }

        // Diff matched tables
        foreach (var (oldTable, newTable) in matchedTables)
        {
            DiffTables(oldTable, newTable, steps, acceptRename);
        }

        // Add foreign keys for new tables (after all creates)
        foreach (var table in addedTables)
        {
            for (var i = 0; i < table.ForeignKeys.Count; i++)
            {
                var fk = table.ForeignKeys[i];
                steps.Add(new MigrationStep(
                    MigrationStepType.AddForeignKey,
                    StepClassification.Safe,
                    table.TableName,
                    table.SchemaName,
                    null,
                    null,
                    fk,
                    $"Add foreign key '{fk.ConstraintName}' on '{table.TableName}'"));
            }
        }

        return steps;
    }

    /// <summary>
    /// Diffs two matched tables (same name).
    /// </summary>
    public static IReadOnlyList<MigrationStep> DiffTables(
        TableDef? from,
        TableDef to,
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename = null)
    {
        var steps = new List<MigrationStep>();
        if (from == null)
        {
            steps.Add(new MigrationStep(
                MigrationStepType.CreateTable,
                StepClassification.Safe,
                to.TableName,
                to.SchemaName,
                null, null, to,
                $"Create table '{to.TableName}'"));
            return steps;
        }
        DiffTables(from, to, steps, acceptRename);
        return steps;
    }

    private static void DiffTables(
        TableDef from, TableDef to,
        List<MigrationStep> steps,
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename)
    {
        DiffColumns(from, to, to.TableName, to.SchemaName, steps, acceptRename);
        DiffForeignKeys(from, to, to.TableName, to.SchemaName, steps);
        DiffIndexes(from, to, to.TableName, to.SchemaName, steps);
    }

    private static void DiffColumns(
        TableDef from, TableDef to,
        string tableName, string? schemaName,
        List<MigrationStep> steps,
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename)
    {
        var oldCols = new Dictionary<string, ColumnDef>(StringComparer.OrdinalIgnoreCase);
        var newCols = new Dictionary<string, ColumnDef>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < from.Columns.Count; i++)
            oldCols[from.Columns[i].Name] = from.Columns[i];
        for (var i = 0; i < to.Columns.Count; i++)
            newCols[to.Columns[i].Name] = to.Columns[i];

        var addedCols = new List<ColumnDef>();
        var droppedCols = new List<ColumnDef>();

        foreach (var kvp in newCols)
        {
            if (oldCols.TryGetValue(kvp.Key, out var oldCol))
            {
                if (!oldCol.Equals(kvp.Value))
                {
                    steps.Add(new MigrationStep(
                        MigrationStepType.AlterColumn,
                        StepClassification.Cautious,
                        tableName, schemaName, kvp.Key,
                        oldCol, kvp.Value,
                        $"Alter column '{kvp.Key}' in '{tableName}'"));
                }
            }
            else
            {
                addedCols.Add(kvp.Value);
            }
        }

        foreach (var kvp in oldCols)
        {
            if (!newCols.ContainsKey(kvp.Key))
                droppedCols.Add(kvp.Value);
        }

        // Rename detection for columns — greedy matching across all add/drop pairs
        if (addedCols.Count > 0 && droppedCols.Count > 0)
        {
            DetectColumnRenames(addedCols, droppedCols, tableName, schemaName, steps, acceptRename);
        }

        // Emit remaining adds and drops
        foreach (var col in addedCols)
        {
            var classification = MigrationStep.Classify(MigrationStepType.AddColumn, col);
            steps.Add(new MigrationStep(
                MigrationStepType.AddColumn,
                classification,
                tableName, schemaName, col.Name,
                null, col,
                $"Add column '{col.Name}' to '{tableName}'"));
        }

        foreach (var col in droppedCols)
        {
            steps.Add(new MigrationStep(
                MigrationStepType.DropColumn,
                StepClassification.Destructive,
                tableName, schemaName, col.Name,
                col, null,
                $"Drop column '{col.Name}' from '{tableName}'"));
        }
    }

    /// <summary>
    /// Greedy bipartite matching for table renames: score all pairs, accept best matches first.
    /// </summary>
    private static void DetectTableRenames(
        List<TableDef> addedTables, List<TableDef> droppedTables,
        List<MigrationStep> steps,
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename)
    {
        // Score all candidate pairs
        var candidates = new List<(int AddIdx, int DropIdx, RenameMatcher.RenameCandidate Candidate)>();
        for (var a = 0; a < addedTables.Count; a++)
        {
            for (var d = 0; d < droppedTables.Count; d++)
            {
                var candidate = RenameMatcher.MatchTable(addedTables[a], droppedTables[d]);
                if (candidate != null)
                    candidates.Add((a, d, candidate));
            }
        }

        // Sort by score descending — greedily pick best matches
        candidates.Sort((x, y) => y.Candidate.Score.CompareTo(x.Candidate.Score));

        var usedAdded = new HashSet<int>();
        var usedDropped = new HashSet<int>();

        foreach (var (addIdx, dropIdx, candidate) in candidates)
        {
            if (usedAdded.Contains(addIdx) || usedDropped.Contains(dropIdx))
                continue;

            var accepted = acceptRename != null
                ? acceptRename(candidate)
                : RenameMatcher.ShouldAutoAccept(candidate);

            if (!accepted) continue;

            usedAdded.Add(addIdx);
            usedDropped.Add(dropIdx);

            var dropped = droppedTables[dropIdx];
            var added = addedTables[addIdx];

            steps.Add(new MigrationStep(
                MigrationStepType.RenameTable,
                StepClassification.Cautious,
                candidate.OldName,
                dropped.SchemaName,
                null,
                candidate.OldName,
                candidate.NewName,
                $"Rename table '{candidate.OldName}' to '{candidate.NewName}'"));

            DiffColumns(dropped, added, added.TableName, added.SchemaName, steps, acceptRename);
            DiffForeignKeys(dropped, added, added.TableName, added.SchemaName, steps);
            DiffIndexes(dropped, added, added.TableName, added.SchemaName, steps);
        }

        // Remove matched entries in reverse order to preserve indices
        foreach (var idx in usedAdded.OrderByDescending(i => i))
            addedTables.RemoveAt(idx);
        foreach (var idx in usedDropped.OrderByDescending(i => i))
            droppedTables.RemoveAt(idx);
    }

    /// <summary>
    /// Greedy bipartite matching for column renames within a table.
    /// </summary>
    private static void DetectColumnRenames(
        List<ColumnDef> addedCols, List<ColumnDef> droppedCols,
        string tableName, string? schemaName,
        List<MigrationStep> steps,
        Func<RenameMatcher.RenameCandidate, bool>? acceptRename)
    {
        var candidates = new List<(int AddIdx, int DropIdx, RenameMatcher.RenameCandidate Candidate)>();
        for (var a = 0; a < addedCols.Count; a++)
        {
            for (var d = 0; d < droppedCols.Count; d++)
            {
                var candidate = RenameMatcher.MatchColumn(addedCols[a], droppedCols[d]);
                if (candidate != null)
                    candidates.Add((a, d, candidate));
            }
        }

        candidates.Sort((x, y) => y.Candidate.Score.CompareTo(x.Candidate.Score));

        var usedAdded = new HashSet<int>();
        var usedDropped = new HashSet<int>();

        foreach (var (addIdx, dropIdx, candidate) in candidates)
        {
            if (usedAdded.Contains(addIdx) || usedDropped.Contains(dropIdx))
                continue;

            var accepted = acceptRename != null
                ? acceptRename(candidate)
                : RenameMatcher.ShouldAutoAccept(candidate);

            if (!accepted) continue;

            usedAdded.Add(addIdx);
            usedDropped.Add(dropIdx);

            var added = addedCols[addIdx];
            var dropped = droppedCols[dropIdx];

            steps.Add(new MigrationStep(
                MigrationStepType.RenameColumn,
                StepClassification.Cautious,
                tableName, schemaName, candidate.OldName,
                candidate.OldName, candidate.NewName,
                $"Rename column '{candidate.OldName}' to '{candidate.NewName}' in '{tableName}'"));

            // Check if other properties changed too
            if (!added.Equals(dropped))
            {
                var oldWithNewName = new ColumnDef(
                    candidate.NewName, dropped.ClrType, dropped.IsNullable, dropped.Kind,
                    dropped.IsIdentity, dropped.IsClientGenerated, dropped.IsComputed,
                    dropped.MaxLength, dropped.Precision, dropped.Scale,
                    dropped.HasDefault, dropped.DefaultExpression, dropped.MappedName,
                    dropped.ReferencedEntityName, dropped.CustomTypeMapping);

                if (!oldWithNewName.Equals(added))
                {
                    steps.Add(new MigrationStep(
                        MigrationStepType.AlterColumn,
                        StepClassification.Cautious,
                        tableName, schemaName, candidate.NewName,
                        dropped, added,
                        $"Alter column '{candidate.NewName}' in '{tableName}'"));
                }
            }
        }

        foreach (var idx in usedAdded.OrderByDescending(i => i))
            addedCols.RemoveAt(idx);
        foreach (var idx in usedDropped.OrderByDescending(i => i))
            droppedCols.RemoveAt(idx);
    }

    private static void DiffForeignKeys(
        TableDef from, TableDef to,
        string tableName, string? schemaName,
        List<MigrationStep> steps)
    {
        var oldFks = new Dictionary<string, ForeignKeyDef>(StringComparer.OrdinalIgnoreCase);
        var newFks = new Dictionary<string, ForeignKeyDef>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < from.ForeignKeys.Count; i++)
            oldFks[from.ForeignKeys[i].ConstraintName] = from.ForeignKeys[i];
        for (var i = 0; i < to.ForeignKeys.Count; i++)
            newFks[to.ForeignKeys[i].ConstraintName] = to.ForeignKeys[i];

        // Drop removed FKs first
        foreach (var kvp in oldFks)
        {
            if (!newFks.ContainsKey(kvp.Key))
            {
                steps.Add(new MigrationStep(
                    MigrationStepType.DropForeignKey,
                    StepClassification.Destructive,
                    tableName, schemaName, null,
                    kvp.Value, null,
                    $"Drop foreign key '{kvp.Key}' from '{tableName}'"));
            }
        }

        // Add new FKs
        foreach (var kvp in newFks)
        {
            if (!oldFks.ContainsKey(kvp.Key))
            {
                steps.Add(new MigrationStep(
                    MigrationStepType.AddForeignKey,
                    StepClassification.Safe,
                    tableName, schemaName, null,
                    null, kvp.Value,
                    $"Add foreign key '{kvp.Key}' on '{tableName}'"));
            }
            else if (!oldFks[kvp.Key].Equals(kvp.Value))
            {
                // FK changed — drop and re-add
                steps.Add(new MigrationStep(
                    MigrationStepType.DropForeignKey,
                    StepClassification.Destructive,
                    tableName, schemaName, null,
                    oldFks[kvp.Key], null,
                    $"Drop foreign key '{kvp.Key}' from '{tableName}'"));
                steps.Add(new MigrationStep(
                    MigrationStepType.AddForeignKey,
                    StepClassification.Safe,
                    tableName, schemaName, null,
                    null, kvp.Value,
                    $"Add foreign key '{kvp.Key}' on '{tableName}'"));
            }
        }
    }

    private static void DiffIndexes(
        TableDef from, TableDef to,
        string tableName, string? schemaName,
        List<MigrationStep> steps)
    {
        var oldIdx = new Dictionary<string, IndexDef>(StringComparer.OrdinalIgnoreCase);
        var newIdx = new Dictionary<string, IndexDef>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < from.Indexes.Count; i++)
            oldIdx[from.Indexes[i].Name] = from.Indexes[i];
        for (var i = 0; i < to.Indexes.Count; i++)
            newIdx[to.Indexes[i].Name] = to.Indexes[i];

        foreach (var kvp in oldIdx)
        {
            if (!newIdx.ContainsKey(kvp.Key))
            {
                steps.Add(new MigrationStep(
                    MigrationStepType.DropIndex,
                    StepClassification.Destructive,
                    tableName, schemaName, null,
                    kvp.Value, null,
                    $"Drop index '{kvp.Key}' from '{tableName}'"));
            }
        }

        foreach (var kvp in newIdx)
        {
            if (!oldIdx.ContainsKey(kvp.Key))
            {
                steps.Add(new MigrationStep(
                    MigrationStepType.AddIndex,
                    StepClassification.Safe,
                    tableName, schemaName, null,
                    null, kvp.Value,
                    $"Add index '{kvp.Key}' on '{tableName}'"));
            }
            else if (!oldIdx[kvp.Key].Equals(kvp.Value))
            {
                steps.Add(new MigrationStep(
                    MigrationStepType.DropIndex,
                    StepClassification.Destructive,
                    tableName, schemaName, null,
                    oldIdx[kvp.Key], null,
                    $"Drop index '{kvp.Key}' from '{tableName}'"));
                steps.Add(new MigrationStep(
                    MigrationStepType.AddIndex,
                    StepClassification.Safe,
                    tableName, schemaName, null,
                    null, kvp.Value,
                    $"Add index '{kvp.Key}' on '{tableName}'"));
            }
        }
    }
}
