using System;
using System.IO;
using System.Linq;
using Quarry.Shared.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for the <c>--rename-map</c> parser and forced-rename pre-transform (step 3).
/// </summary>
public class RenameMapTests
{
    [Test]
    public void Parse_Inline_QualifiedAndBare()
    {
        var map = RenameMap.Parse("users.user_name=UserName,qty=Quantity");

        Assert.That(map.Resolve("users", "user_name"), Is.EqualTo("UserName"));
        Assert.That(map.Resolve("orders", "qty"), Is.EqualTo("Quantity"));      // bare applies to any table
        Assert.That(map.Resolve("users", "unmapped"), Is.Null);
    }

    [Test]
    public void Parse_Inline_IsCaseInsensitiveOnNames_PreservesTargetCase()
    {
        var map = RenameMap.Parse("Users.User_Name=UserName");
        Assert.That(map.Resolve("users", "user_name"), Is.EqualTo("UserName"));
    }

    [Test]
    public void Parse_QualifiedTakesPrecedenceOverBare()
    {
        var map = RenameMap.Parse("code=GlobalCode,products.code=Sku");

        Assert.That(map.Resolve("products", "code"), Is.EqualTo("Sku"));   // qualified wins
        Assert.That(map.Resolve("orders", "code"), Is.EqualTo("GlobalCode")); // falls back to bare
    }

    [Test]
    public void Parse_Empty_IsEmpty()
    {
        Assert.That(RenameMap.Parse(null).IsEmpty, Is.True);
        Assert.That(RenameMap.Parse("   ").IsEmpty, Is.True);
    }

    [Test]
    public void Parse_Malformed_Throws()
    {
        Assert.Throws<FormatException>(() => RenameMap.Parse("no_equals_here"));
        Assert.Throws<FormatException>(() => RenameMap.Parse("=NoLeft"));
    }

    [Test]
    public void Parse_File_ThreeAndTwoColumnRows_SkipsHeaderAndComments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"renames_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "table,from,to\n# a comment\nusers,user_name,UserName\n\nqty,Quantity\n");
        try
        {
            var map = RenameMap.Parse("@" + path);
            Assert.That(map.Resolve("users", "user_name"), Is.EqualTo("UserName"));
            Assert.That(map.Resolve("anything", "qty"), Is.EqualTo("Quantity"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Parse_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => RenameMap.Parse("@does_not_exist_12345.csv"));
    }

    [Test]
    public void ApplyForcedRenames_RenamesColumnAndUpdatesReferences()
    {
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("orders", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
                    new ColumnDef("qty", "int", false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(),
                new[] { new IndexDef("ix_qty", new[] { "qty" }, false) })
        });

        var map = RenameMap.Parse("orders.qty=Quantity");
        var (patched, applied) = map.ApplyForcedRenames(from);

        Assert.That(applied.Count, Is.EqualTo(1));
        Assert.That(applied[0].OldName, Is.EqualTo("qty"));
        Assert.That(applied[0].NewName, Is.EqualTo("Quantity"));

        var orders = patched.Tables.Single();
        Assert.That(orders.Columns.Any(c => c.Name == "Quantity"), Is.True);
        Assert.That(orders.Columns.Any(c => c.Name == "qty"), Is.False);
        Assert.That(orders.Indexes.Single().Columns, Does.Contain("Quantity")); // index reference rewritten
    }

    [Test]
    public void ApplyForcedRenames_SubThresholdRename_ProducesNoDropOrAddInDiff()
    {
        // "qty" -> "Quantity" is canonically different ("qty" vs "quantity") and would score
        // low, so the heuristic differ would drop+add it. With a forced rename, the pre-transform
        // makes the column match by name, so the diff yields neither DropColumn nor AddColumn —
        // and the caller emits an explicit RenameColumn from the returned Applied list.
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("orders", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
                    new ColumnDef("qty", "int", false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("orders", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
                    new ColumnDef("Quantity", "int", false, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var map = RenameMap.Parse("orders.qty=Quantity");
        var (patched, applied) = map.ApplyForcedRenames(from);

        // Reject heuristic renames to prove the forced pre-transform (not scoring) is what works.
        var steps = SchemaDiffer.Diff(patched, to, _ => false);

        Assert.That(applied.Single().NewName, Is.EqualTo("Quantity"));
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn), Is.False);
    }

    // --- Validation (F6/F7): reject invalid maps BEFORE the adopt baseline is written. ---

    private static SchemaSnapshot Snap(int version, string table, params string[] columns) =>
        new(version, $"v{version}", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef(table, null, NamingStyleKind.Exact,
                columns.Select(c => new ColumnDef(c, "string", false, ColumnKind.Standard)).ToArray(),
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

    [Test]
    public void Validate_ValidMap_NoErrorsNoWarnings()
    {
        var from = Snap(1, "users", "id", "user_name");
        var to = Snap(2, "users", "id", "UserName");
        var result = RenameMap.Parse("users.user_name=UserName").Validate(from, to);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Validate_TargetNotInProjectSchema_IsError()
    {
        // Map renames usr_name -> UserName, but the project column is FullName. Left unchecked this
        // would drop the renamed column (data loss) and crash the guard querying a missing column.
        var from = Snap(1, "users", "id", "usr_name");
        var to = Snap(2, "users", "id", "FullName");
        var result = RenameMap.Parse("users.usr_name=UserName").Validate(from, to);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(result.Errors.Any(e => e.Contains("UserName")), Is.True);
    }

    [Test]
    public void Validate_DuplicateTargetsInTable_IsError()
    {
        var from = Snap(1, "users", "id", "a", "b");
        var to = Snap(2, "users", "id", "X");
        var result = RenameMap.Parse("users.a=X,users.b=X").Validate(from, to);

        Assert.That(result.Errors.Any(e => e.Contains("multiple columns to 'X'")), Is.True);
    }

    [Test]
    public void Validate_TargetCollidesWithKeptColumn_IsError()
    {
        // Renaming 'old' -> 'keep' while 'keep' also exists and is not renamed away = duplicate column.
        var from = Snap(1, "users", "id", "old", "keep");
        var to = Snap(2, "users", "id", "keep");
        var result = RenameMap.Parse("users.old=keep").Validate(from, to);

        Assert.That(result.Errors.Any(e => e.Contains("collides")), Is.True);
    }

    [Test]
    public void Validate_UnmatchedEntry_IsWarningNotError()
    {
        var from = Snap(1, "users", "id", "user_name");
        var to = Snap(2, "users", "id", "UserName");
        // 'nonexistent' matches no live column -> a warning (silent no-op), not a hard error.
        var result = RenameMap.Parse("users.nonexistent=Foo").Validate(from, to);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.Warnings.Any(w => w.Contains("nonexistent")), Is.True);
    }
}
