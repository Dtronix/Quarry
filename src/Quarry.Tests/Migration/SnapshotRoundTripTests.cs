using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RuntimeMig = Quarry.Migration;
using SharedMig = Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

/// <summary>
/// Round-trip regression tests for #313: SnapshotCodeGenerator output for a schema exercising
/// every builder method must compile against the runtime builders in Quarry.dll (the builders
/// user projects and the CLI's SnapshotCompiler consume — single-sourced since #313), rebuild
/// an equivalent snapshot, and diff as a no-op against the original.
/// </summary>
public class SnapshotRoundTripTests
{
    /// <summary>
    /// A snapshot exercising every method SnapshotCodeGenerator can emit: PK, FK column,
    /// AddForeignKey with non-default actions, identity, client-generated, computed (with and
    /// without expression), nullable, length, precision/scale, DefaultValue, HasDefault, MapTo,
    /// CustomTypeMapping, Collation, table Schema/NamingStyle/CharacterSet, AddIndex with
    /// unique/filter/method/descendingColumns, and CompositeKey.
    /// </summary>
    internal static SharedMig.SchemaSnapshot CreateFullFeaturedSnapshot()
    {
        var users = new SharedMig.TableDef(
            "users", null, SharedMig.NamingStyleKind.SnakeCase,
            new[]
            {
                new SharedMig.ColumnDef("user_id", "int", false, SharedMig.ColumnKind.PrimaryKey, isIdentity: true),
                new SharedMig.ColumnDef("user_name", "string", false, SharedMig.ColumnKind.Standard, maxLength: 100,
                    collation: "nocase"),
                new SharedMig.ColumnDef("status", "string", false, SharedMig.ColumnKind.Standard,
                    hasDefault: true, defaultExpression: "'active'"),
                new SharedMig.ColumnDef("token", "Guid", false, SharedMig.ColumnKind.Standard,
                    isClientGenerated: true, hasDefault: true),
                new SharedMig.ColumnDef("balance", "decimal", false, SharedMig.ColumnKind.Standard,
                    precision: 18, scale: 2),
                new SharedMig.ColumnDef("display_name", "string", true, SharedMig.ColumnKind.Standard,
                    isComputed: true, computedExpression: "upper(user_name)"),
                new SharedMig.ColumnDef("shadow", "string", true, SharedMig.ColumnKind.Standard,
                    isComputed: true),
                new SharedMig.ColumnDef("legacy_email", "string", true, SharedMig.ColumnKind.Standard,
                    mappedName: "email_address", customTypeMapping: "citext"),
            },
            Array.Empty<SharedMig.ForeignKeyDef>(),
            new[]
            {
                new SharedMig.IndexDef("ix_users_name", new[] { "user_name", "status" },
                    isUnique: true, filter: "status = 'active'", method: "btree",
                    descendingColumns: new[] { true, false }),
            },
            characterSet: "utf8mb4");

        var orders = new SharedMig.TableDef(
            "orders", "sales", SharedMig.NamingStyleKind.Exact,
            new[]
            {
                new SharedMig.ColumnDef("order_id", "int", false, SharedMig.ColumnKind.PrimaryKey, isIdentity: true),
                new SharedMig.ColumnDef("user_id", "int", false, SharedMig.ColumnKind.ForeignKey,
                    referencedEntityName: "User"),
                new SharedMig.ColumnDef("note", "string", true, SharedMig.ColumnKind.Standard),
            },
            new[]
            {
                new SharedMig.ForeignKeyDef("fk_orders_users", "user_id", "users", "user_id",
                    SharedMig.ForeignKeyAction.Cascade, SharedMig.ForeignKeyAction.SetNull),
            },
            Array.Empty<SharedMig.IndexDef>());

        var junction = new SharedMig.TableDef(
            "user_roles", null, SharedMig.NamingStyleKind.Exact,
            new[]
            {
                new SharedMig.ColumnDef("user_id", "int", false, SharedMig.ColumnKind.Standard),
                new SharedMig.ColumnDef("role_id", "int", false, SharedMig.ColumnKind.Standard),
            },
            Array.Empty<SharedMig.ForeignKeyDef>(),
            Array.Empty<SharedMig.IndexDef>(),
            compositeKeyColumns: new[] { "user_id", "role_id" });

        return new SharedMig.SchemaSnapshot(
            7, "Full", DateTimeOffset.Parse("2026-07-14T00:00:00Z"), 6,
            new[] { users, orders, junction });
    }

    [Test]
    public void GeneratedSnapshot_CompilesAgainstRuntimeBuilders_AndRoundTripsAsNoOp()
    {
        var original = CreateFullFeaturedSnapshot();
        var code = SharedMig.SnapshotCodeGenerator.GenerateSnapshotClass(original, "TestApp.Migrations");

        // Compile the generated source exactly as emitted (using Quarry.Migration;) against
        // Quarry.dll — the same builders user projects compile snapshots against.
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RuntimeMig.SchemaSnapshotBuilder).Assembly.Location),
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[] { "System.Runtime.dll", "netstandard.dll", "System.Collections.dll" })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            "SnapshotRoundTrip",
            new[] { CSharpSyntaxTree.ParseText(code) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        var errors = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();
        Assert.That(errors, Is.Empty,
            $"Generated snapshot must compile against the runtime builders. Generated code:\n{code}");

        // Load and invoke Build() to rebuild the snapshot through the runtime builder API.
        ms.Seek(0, SeekOrigin.Begin);
        var context = new AssemblyLoadContext("SnapshotRoundTrip", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(ms);
            var snapshotType = assembly.GetTypes().Single(t => t.Name.StartsWith("S0007_"));
            var buildMethod = snapshotType.GetMethod(
                "Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var rebuilt = (RuntimeMig.SchemaSnapshot)buildMethod.Invoke(null, null)!;

            var roundTripped = ToShared(rebuilt);

            Assert.Multiple(() =>
            {
                Assert.That(SharedMig.SchemaDiffer.Diff(roundTripped, original), Is.Empty,
                    "diff(roundTripped -> original) must be a no-op");
                Assert.That(SharedMig.SchemaDiffer.Diff(original, roundTripped), Is.Empty,
                    "diff(original -> roundTripped) must be a no-op");
                Assert.That(SharedMig.SchemaHasher.ComputeHash(roundTripped.Tables),
                    Is.EqualTo(SharedMig.SchemaHasher.ComputeHash(original.Tables)),
                    "persisted schema hash must survive the round trip");
            });
        }
        finally
        {
            context.Unload();
        }
    }

    // The rebuilt snapshot comes back as runtime (Quarry.Migration) types; SchemaDiffer and
    // SchemaHasher in this test project operate on the shared (Quarry.Shared.Migration) types.
    // Both compile from the same single-sourced files, so this mapping is property-by-property.
    private static SharedMig.SchemaSnapshot ToShared(RuntimeMig.SchemaSnapshot s) =>
        new(s.Version, s.Name, s.Timestamp, s.ParentVersion, s.Tables.Select(ToShared).ToList());

    private static SharedMig.TableDef ToShared(RuntimeMig.TableDef t) =>
        new(t.TableName, t.SchemaName, (SharedMig.NamingStyleKind)(int)t.NamingStyle,
            t.Columns.Select(ToShared).ToList(),
            t.ForeignKeys.Select(ToShared).ToList(),
            t.Indexes.Select(ToShared).ToList(),
            t.CompositeKeyColumns, t.CharacterSet);

    private static SharedMig.ColumnDef ToShared(RuntimeMig.ColumnDef c) =>
        new(c.Name, c.ClrType, c.IsNullable, (SharedMig.ColumnKind)(int)c.Kind,
            c.IsIdentity, c.IsClientGenerated, c.IsComputed,
            c.MaxLength, c.Precision, c.Scale,
            c.HasDefault, c.DefaultExpression, c.MappedName,
            c.ReferencedEntityName, c.CustomTypeMapping,
            c.ComputedExpression, c.Collation);

    private static SharedMig.ForeignKeyDef ToShared(RuntimeMig.ForeignKeyDef fk) =>
        new(fk.ConstraintName, fk.ColumnName, fk.ReferencedTable, fk.ReferencedColumn,
            (SharedMig.ForeignKeyAction)(int)fk.OnDelete, (SharedMig.ForeignKeyAction)(int)fk.OnUpdate);

    private static SharedMig.IndexDef ToShared(RuntimeMig.IndexDef ix) =>
        new(ix.Name, ix.Columns, ix.IsUnique, ix.Filter, ix.Method, ix.DescendingColumns);
}
