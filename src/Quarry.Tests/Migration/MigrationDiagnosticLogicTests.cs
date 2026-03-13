using Quarry.Generators.Models;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for migration diagnostic detection logic (QRY050-055).
/// These test the MigrationInfo properties that drive diagnostic reporting.
/// </summary>
public class MigrationDiagnosticLogicTests
{
    #region QRY052: Duplicate migration versions

    [Test]
    public void DuplicateVersions_DetectedByHashSet()
    {
        var migrations = new[]
        {
            new MigrationInfo(1, "Init", "M1", "NS"),
            new MigrationInfo(1, "Duplicate", "M2", "NS"),
            new MigrationInfo(2, "Second", "M3", "NS"),
        };

        var versions = new HashSet<int>();
        var duplicates = new List<int>();
        foreach (var m in migrations)
        {
            if (!versions.Add(m.Version))
                duplicates.Add(m.Version);
        }

        Assert.That(duplicates, Has.Count.EqualTo(1));
        Assert.That(duplicates[0], Is.EqualTo(1));
    }

    [Test]
    public void NoDuplicateVersions_NoDiagnostic()
    {
        var migrations = new[]
        {
            new MigrationInfo(1, "Init", "M1", "NS"),
            new MigrationInfo(2, "Second", "M2", "NS"),
        };

        var versions = new HashSet<int>();
        var hasDuplicate = false;
        foreach (var m in migrations)
        {
            if (!versions.Add(m.Version))
                hasDuplicate = true;
        }

        Assert.That(hasDuplicate, Is.False);
    }

    #endregion

    #region QRY053: Pending migrations

    [Test]
    public void PendingMigrations_CountedCorrectly()
    {
        var migrations = new[]
        {
            new MigrationInfo(1, "Init", "M1", "NS"),
            new MigrationInfo(2, "Second", "M2", "NS"),
            new MigrationInfo(3, "Third", "M3", "NS"),
        };
        var latestSnapshotVersion = 1;

        var pendingCount = migrations.Count(m => m.Version > latestSnapshotVersion);
        Assert.That(pendingCount, Is.EqualTo(2));
    }

    [Test]
    public void NoPendingMigrations_CountIsZero()
    {
        var migrations = new[]
        {
            new MigrationInfo(1, "Init", "M1", "NS"),
        };
        var latestSnapshotVersion = 1;

        var pendingCount = migrations.Count(m => m.Version > latestSnapshotVersion);
        Assert.That(pendingCount, Is.EqualTo(0));
    }

    #endregion

    #region QRY054: Destructive without backup

    [Test]
    public void DestructiveWithoutBackup_Flagged()
    {
        var migration = new MigrationInfo(1, "DropUsers", "M1", "NS",
            hasDestructiveSteps: true, hasBackup: false);

        Assert.That(migration.HasDestructiveSteps && !migration.HasBackup, Is.True);
    }

    [Test]
    public void DestructiveWithBackup_NotFlagged()
    {
        var migration = new MigrationInfo(1, "DropUsers", "M1", "NS",
            hasDestructiveSteps: true, hasBackup: true);

        Assert.That(migration.HasDestructiveSteps && !migration.HasBackup, Is.False);
    }

    [Test]
    public void NonDestructive_NotFlagged()
    {
        var migration = new MigrationInfo(1, "AddColumn", "M1", "NS",
            hasDestructiveSteps: false, hasBackup: false);

        Assert.That(migration.HasDestructiveSteps && !migration.HasBackup, Is.False);
    }

    #endregion

    #region QRY051: Unknown table/column references

    [Test]
    public void UnknownTableReference_Detected()
    {
        var migration = new MigrationInfo(1, "M1", "M1", "NS",
            referencedTableNames: new[] { "users", "nonexistent" });

        var knownTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "users", "posts" };
        var unknowns = migration.ReferencedTableNames.Where(t => !knownTables.Contains(t)).ToList();

        Assert.That(unknowns, Has.Count.EqualTo(1));
        Assert.That(unknowns[0], Is.EqualTo("nonexistent"));
    }

    [Test]
    public void UnknownColumnReference_Detected()
    {
        var migration = new MigrationInfo(1, "M1", "M1", "NS",
            referencedColumnNames: new[] { "users.id", "users.ghost" });

        var knownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "users.id", "users.name" };
        var unknowns = migration.ReferencedColumnNames.Where(c => !knownColumns.Contains(c)).ToList();

        Assert.That(unknowns, Has.Count.EqualTo(1));
        Assert.That(unknowns[0], Is.EqualTo("users.ghost"));
    }

    [Test]
    public void AllReferencesKnown_NoDiagnostic()
    {
        var migration = new MigrationInfo(1, "M1", "M1", "NS",
            referencedTableNames: new[] { "users" },
            referencedColumnNames: new[] { "users.id" });

        var knownTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "users" };
        var knownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "users.id" };

        var hasUnknown = migration.ReferencedTableNames.Any(t => !knownTables.Contains(t))
            || migration.ReferencedColumnNames.Any(c => !knownColumns.Contains(c));

        Assert.That(hasUnknown, Is.False);
    }

    #endregion

    #region QRY055: Nullable to non-null without data migration

    [Test]
    public void NullableToNonNull_WithoutSql_Flagged()
    {
        var migration = new MigrationInfo(1, "M1", "M1", "NS",
            hasAlterColumnNotNull: true, hasSqlStep: false);

        Assert.That(migration.HasAlterColumnNotNull && !migration.HasSqlStep, Is.True);
    }

    [Test]
    public void NullableToNonNull_WithSql_NotFlagged()
    {
        var migration = new MigrationInfo(1, "M1", "M1", "NS",
            hasAlterColumnNotNull: true, hasSqlStep: true);

        Assert.That(migration.HasAlterColumnNotNull && !migration.HasSqlStep, Is.False);
    }

    [Test]
    public void NoAlterColumnNotNull_NotFlagged()
    {
        var migration = new MigrationInfo(1, "M1", "M1", "NS",
            hasAlterColumnNotNull: false, hasSqlStep: false);

        Assert.That(migration.HasAlterColumnNotNull && !migration.HasSqlStep, Is.False);
    }

    #endregion

    #region MigrationInfo defaults

    [Test]
    public void MigrationInfo_DefaultValues_AreCorrect()
    {
        var migration = new MigrationInfo(5, "Test", "M0005_Test", "MyApp");

        Assert.That(migration.Version, Is.EqualTo(5));
        Assert.That(migration.Name, Is.EqualTo("Test"));
        Assert.That(migration.ClassName, Is.EqualTo("M0005_Test"));
        Assert.That(migration.Namespace, Is.EqualTo("MyApp"));
        Assert.That(migration.HasDestructiveSteps, Is.False);
        Assert.That(migration.HasBackup, Is.False);
        Assert.That(migration.HasSqlStep, Is.False);
        Assert.That(migration.HasAlterColumnNotNull, Is.False);
        Assert.That(migration.ReferencedTableNames, Is.Empty);
        Assert.That(migration.ReferencedColumnNames, Is.Empty);
    }

    #endregion
}
