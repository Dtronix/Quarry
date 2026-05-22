using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;

namespace Quarry.Tests.Generation;

/// <summary>
/// Phase 2 tests for the Patch nested struct emitted by
/// <see cref="EntityCodeGenerator"/>. Asserts the per-column field/property
/// layout, the mask-bit constant assignment, exclusions (Identity, Computed),
/// nullability handling, FK column shape, and the QRY045 cap behavior.
/// </summary>
[TestFixture]
public class EntityCodeGeneratorPatchTests
{
    [Test]
    public void Patch_StructIsEmitted_WithMaskAndBitConstants()
    {
        var source = Render(
            Col("Id", "int", identity: true, isValueType: true),
            Col("Name", "string"),
            Col("Email", "string"));

        Assert.That(source, Does.Contain("public struct Patch : Quarry.IPatchFor<Widget>"));
        Assert.That(source, Does.Contain("internal ulong __mask;"));
        Assert.That(source, Does.Contain("_Mask_Name = 0x1UL"));
        Assert.That(source, Does.Contain("_Mask_Email = 0x2UL"));
    }

    [Test]
    public void Patch_ExcludesIdentityAndComputedColumns()
    {
        var source = Render(
            Col("Id", "int", identity: true, isValueType: true),
            Col("Name", "string"),
            Col("FullName", "string", computed: true),
            Col("Email", "string"));

        Assert.That(source, Does.Contain("_Mask_Name"));
        Assert.That(source, Does.Contain("_Mask_Email"));
        Assert.That(source, Does.Not.Contain("_Mask_Id"), "Identity columns must not appear in Patch");
        Assert.That(source, Does.Not.Contain("_Mask_FullName"), "Computed columns must not appear in Patch");
    }

    [Test]
    public void Patch_MaskBitsFollowDeclarationOrder()
    {
        var source = Render(
            Col("A", "int", isValueType: true),
            Col("B", "int", isValueType: true),
            Col("C", "int", isValueType: true),
            Col("D", "int", isValueType: true));

        Assert.That(source, Does.Contain("_Mask_A = 0x1UL"));
        Assert.That(source, Does.Contain("_Mask_B = 0x2UL"));
        Assert.That(source, Does.Contain("_Mask_C = 0x4UL"));
        Assert.That(source, Does.Contain("_Mask_D = 0x8UL"));
    }

    [Test]
    public void Patch_PropertySetterAssignsBackingFieldAndFlipsMask()
    {
        var source = Render(
            Col("Id", "int", identity: true, isValueType: true),
            Col("Name", "string"));

        Assert.That(source, Does.Contain("private string? __Name;"),
            "non-nullable reference types use a nullable backing field to satisfy CS8618 without an initializer");
        Assert.That(source, Does.Contain("public string Name"));
        Assert.That(source, Does.Contain("set { __Name = value; __mask |= _Mask_Name; }"));
    }

    [Test]
    public void Patch_NonNullableReferenceProperty_GetterUsesNullForgiving()
    {
        var source = Render(Col("Name", "string"));
        Assert.That(source, Does.Contain("get => __Name!;"),
            "non-nullable reference property must suppress nullability on read");
    }

    [Test]
    public void Patch_ValueTypeProperty_UsesDirectBackingFieldWithoutNullForgiving()
    {
        var source = Render(Col("Age", "int", isValueType: true));

        Assert.That(source, Does.Contain("private int __Age;"));
        Assert.That(source, Does.Contain("get => __Age;"));
        Assert.That(source, Does.Not.Contain("__Age!"));
    }

    [Test]
    public void Patch_NullableReferenceProperty_DoesNotAddSuppression()
    {
        var source = Render(Col("Notes", "string", isNullable: true));

        Assert.That(source, Does.Contain("private string? __Notes;"));
        Assert.That(source, Does.Contain("public string? Notes"));
        Assert.That(source, Does.Contain("get => __Notes;"),
            "already-nullable reference doesn't need ! suppression on read");
    }

    [Test]
    public void Patch_ForeignKeyProperty_UsesEntityRefStructType()
    {
        var source = Render(
            Col("Id", "int", identity: true, isValueType: true),
            Col("OrganizationId", "int", kind: ColumnKind.ForeignKey, referencedEntityName: "Organization", isValueType: true));

        Assert.That(source, Does.Contain("public EntityRef<Organization, int> OrganizationId"));
        Assert.That(source, Does.Contain("private EntityRef<Organization, int> __OrganizationId;"),
            "EntityRef is a struct, so the backing field is non-nullable");
        Assert.That(source, Does.Not.Contain("__OrganizationId!"));
    }

    [Test]
    public void Patch_NotEmitted_WhenNoUpdatableColumns()
    {
        var source = Render(Col("Id", "int", identity: true, isValueType: true));
        Assert.That(source, Does.Not.Contain("public struct Patch"));
    }

    [Test]
    public void Patch_Emitted_AtSixtyFourColumns_ButNotSixtyFive()
    {
        var sourceA = EntityCodeGenerator.GenerateEntityClass(MakeWideEntity(64), "TestApp");
        Assert.That(sourceA, Does.Contain("public struct Patch"), "64-column entity must emit Patch");
        Assert.That(sourceA, Does.Contain($"_Mask_C63 = 0x{(1UL << 63):X}UL"), "highest bit at position 63");

        var sourceB = EntityCodeGenerator.GenerateEntityClass(MakeWideEntity(65), "TestApp");
        Assert.That(sourceB, Does.Not.Contain("public struct Patch"),
            "65-column entity must not emit Patch — would overflow the ulong mask");
    }

    [Test]
    public void CountUpdatableColumns_ExcludesIdentityAndComputed()
    {
        var entity = MakeEntity(
            Col("Id", "int", identity: true, isValueType: true),
            Col("Name", "string"),
            Col("FullName", "string", computed: true),
            Col("Email", "string"));

        Assert.That(EntityCodeGenerator.CountUpdatableColumns(entity), Is.EqualTo(2));
    }

    [Test]
    public void QRY045_ReportedWhenEntityExceedsSixtyFourUpdatableColumns()
    {
        var schema = WideSchemaSource(updatableColumnCount: 65);
        var (diags, _) = RunGenerator(schema);

        var qry045 = diags.Where(d => d.Id == "QRY045").ToList();
        Assert.That(qry045, Is.Not.Empty, "65 updatable columns should trigger QRY045");
        Assert.That(qry045[0].GetMessage(), Does.Contain("65"));
        Assert.That(qry045[0].GetMessage(), Does.Contain("Wide"));
    }

    [Test]
    public void QRY045_NotReportedAtExactlySixtyFour()
    {
        var schema = WideSchemaSource(updatableColumnCount: 64);
        var (diags, _) = RunGenerator(schema);

        Assert.That(diags.Where(d => d.Id == "QRY045"), Is.Empty,
            "64 updatable columns sits at the cap — no diagnostic");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static ColumnInfo Col(
        string name,
        string clrType,
        ColumnKind kind = ColumnKind.Standard,
        bool identity = false,
        bool computed = false,
        bool isNullable = false,
        bool isValueType = false,
        string? referencedEntityName = null)
    {
        var mods = new ColumnModifiers(
            isIdentity: identity,
            isComputed: computed,
            isForeignKey: kind == ColumnKind.ForeignKey);
        return new ColumnInfo(
            propertyName: name,
            columnName: name.ToLowerInvariant(),
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            kind: kind,
            referencedEntityName: referencedEntityName,
            modifiers: mods,
            isValueType: isValueType);
    }

    private static EntityInfo MakeEntity(params ColumnInfo[] columns)
    {
        return new EntityInfo(
            entityName: "Widget",
            schemaClassName: "WidgetSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "widgets",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: columns,
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);
    }

    private static string Render(params ColumnInfo[] columns)
        => EntityCodeGenerator.GenerateEntityClass(MakeEntity(columns), "TestApp");

    private static EntityInfo MakeWideEntity(int updatableCount)
    {
        var cols = new List<ColumnInfo>
        {
            Col("Id", "int", identity: true, isValueType: true),
        };
        for (int i = 0; i < updatableCount; i++)
            cols.Add(Col($"C{i}", "int", isValueType: true));

        return new EntityInfo(
            entityName: "Wide",
            schemaClassName: "WideSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "wides",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: cols,
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);
    }

    private static string WideSchemaSource(int updatableColumnCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using Quarry;");
        sb.AppendLine("namespace TestApp;");
        sb.AppendLine("public class WideSchema : Schema");
        sb.AppendLine("{");
        sb.AppendLine("    public static string Table => \"wides\";");
        sb.AppendLine("    public Key<int> Id => Identity();");
        for (int i = 0; i < updatableColumnCount; i++)
            sb.AppendLine($"    public Col<int> C{i} => default!;");
        sb.AppendLine("}");
        sb.AppendLine("[QuarryContext(Dialect = SqlDialect.SQLite)]");
        sb.AppendLine("public partial class TestDb : QuarryContext");
        sb.AppendLine("{");
        sb.AppendLine("    public partial IEntityAccessor<Wide> Wides();");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, IReadOnlyList<string> GeneratedFiles) RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions) };

        var quarryCorePath = typeof(Schema).Assembly.Location;
        var sysRuntimePath = typeof(object).Assembly.Location;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(quarryCorePath),
            MetadataReference.CreateFromFile(sysRuntimePath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        var run = driver.GetRunResult();
        var files = run.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        return (diagnostics, files);
    }
}
