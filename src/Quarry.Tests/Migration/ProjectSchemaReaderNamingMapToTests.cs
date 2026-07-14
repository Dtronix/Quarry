using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Shared.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Verifies that <see cref="ProjectSchemaReader"/> honors the real <c>NamingStyle</c> override and
/// per-column <c>MapTo("physical")</c> — the two column-naming mechanisms the runtime source generator
/// honors — so migration snapshots/DDL use the same physical names the runtime queries (issue #324).
/// </summary>
[TestFixture]
public class ProjectSchemaReaderNamingMapToTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static TableDef ExtractSingleTable(string source)
    {
        var compilation = CreateCompilation(source);
        var snapshot = ProjectSchemaReader.ExtractSchemaSnapshot(compilation, 1, "test", null);
        Assert.That(snapshot.Tables, Has.Count.EqualTo(1), "Expected exactly one table");
        return snapshot.Tables[0];
    }

    private static ColumnDef Column(TableDef table, string name) =>
        table.Columns.Single(c => c.Name == name);

    private static SchemaSnapshot ExtractSnapshot(string source, int version) =>
        ProjectSchemaReader.ExtractSchemaSnapshot(CreateCompilation(source), version, "test", null);

    // A mapping change must surface as either a single RenameColumn or a drop+add pair —
    // never zero steps. Threshold-dependent rename scoring means we accept either shape.
    private static void AssertColumnTransition(
        IReadOnlyList<MigrationStep> steps, string oldName, string newName)
    {
        Assert.That(steps, Is.Not.Empty, "Expected a diff step for the mapping change, got a no-op");

        var asRename = steps.Any(s =>
            s.StepType == MigrationStepType.RenameColumn
            && (s.OldValue as string) == oldName
            && (s.NewValue as string) == newName);

        var asDropAdd =
            steps.Any(s => s.StepType == MigrationStepType.DropColumn && s.ColumnName == oldName)
            && steps.Any(s => s.StepType == MigrationStepType.AddColumn && s.ColumnName == newName);

        Assert.That(asRename || asDropAdd, Is.True,
            $"Expected a rename {oldName}->{newName} or a drop+add, but got: "
            + string.Join(", ", steps.Select(s => $"{s.StepType}({s.ColumnName})")));
    }

    [Test]
    public void RemovingMapTo_ProducesDiffStep_NotNoOp()
    {
        const string withMapTo = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName => MapTo<string>(""user_name"");
}";
        const string withoutMapTo = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        var v1 = ExtractSnapshot(withMapTo, 1);
        var v2 = ExtractSnapshot(withoutMapTo, 2);

        var steps = SchemaDiffer.Diff(v1, v2);

        AssertColumnTransition(steps, "user_name", "UserName");
    }

    [Test]
    public void AddingMapTo_ProducesDiffStep_NotNoOp()
    {
        const string withoutMapTo = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        const string withMapTo = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName => MapTo<string>(""user_name"");
}";
        var v1 = ExtractSnapshot(withoutMapTo, 1);
        var v2 = ExtractSnapshot(withMapTo, 2);

        var steps = SchemaDiffer.Diff(v1, v2);

        AssertColumnTransition(steps, "UserName", "user_name");
    }

    [Test]
    public void NamingStyle_SnakeCase_StylesColumnNames_AndLeavesMappedNameNull()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.NamingStyle, Is.EqualTo(NamingStyleKind.SnakeCase));
        Assert.That(Column(table, "user_id").MappedName, Is.Null);
        Assert.That(Column(table, "user_name").MappedName, Is.Null);
    }

    [Test]
    public void MapTo_StandaloneGeneric_SetsPhysicalNameAndMappedName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName => MapTo<string>(""user_name"");
}";
        var table = ExtractSingleTable(source);

        var col = Column(table, "user_name");
        Assert.That(col.Name, Is.EqualTo("user_name"));
        Assert.That(col.MappedName, Is.EqualTo("user_name"));
        // The un-mapped property name must not survive as a column.
        Assert.That(table.Columns.Any(c => c.Name == "UserName"), Is.False);
    }

    [Test]
    public void MapTo_ChainedAfterModifier_SetsPhysicalNameAndMappedName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> AccountName => Length(100).MapTo(""account_name"");
}";
        var table = ExtractSingleTable(source);

        var col = Column(table, "account_name");
        Assert.That(col.Name, Is.EqualTo("account_name"));
        Assert.That(col.MappedName, Is.EqualTo("account_name"));
    }

    [Test]
    public void MapTo_GenericNotOutermostInChain_StillDetected()
    {
        // MapTo<T>(...) is the inner call; a trailing modifier makes it non-outermost.
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName => MapTo<string>(""user_name"").Unique();
}";
        var table = ExtractSingleTable(source);

        var col = Column(table, "user_name");
        Assert.That(col.MappedName, Is.EqualTo("user_name"));
    }

    [Test]
    public void MapTo_OverridesNamingStyle_ForThatColumnOnly()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> FirstName { get; }
    public Col<string> UserName => MapTo<string>(""explicit_name"");
}";
        var table = ExtractSingleTable(source);

        // Sibling without MapTo is styled by the naming convention.
        Assert.That(Column(table, "first_name").MappedName, Is.Null);
        // MapTo wins over the naming style for its column.
        var mapped = Column(table, "explicit_name");
        Assert.That(mapped.MappedName, Is.EqualTo("explicit_name"));
        Assert.That(table.Columns.Any(c => c.Name == "user_name"), Is.False);
    }

    [Test]
    public void NoMapTo_ExactNaming_UsesPropertyNameAndNullMappedName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
    public Col<string> Email => Length(100);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.NamingStyle, Is.EqualTo(NamingStyleKind.Exact));
        Assert.That(Column(table, "UserName").MappedName, Is.Null);
        // A modifier chain without MapTo must not fabricate a mapped name.
        Assert.That(Column(table, "Email").MappedName, Is.Null);
    }

    [Test]
    public void NamingStyle_SnakeCase_MigrationCodeUsesStyledColumnNames()
    {
        // Symmetry with the MapTo end-to-end guard: drive a snake_case schema through the DDL-bound
        // MigrationCodeGenerator (which emits col.Name) and confirm the styled physical name lands there.
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        var snapshot = ExtractSnapshot(source, 1);
        var steps = SchemaDiffer.Diff(null, snapshot);
        var migration = MigrationCodeGenerator.GenerateMigrationClass(
            1, "UsersInit", steps, null, snapshot, "Test");

        Assert.That(migration, Does.Contain("\"user_name\""));
        Assert.That(migration, Does.Not.Contain("\"UserName\""));
    }

    [Test]
    public void NamingStyle_GetterArrowBody_IgnoredExactly_LikeRuntime()
    {
        // The runtime SchemaParser honors ONLY an expression-bodied override; a getter-arrow body
        // yields Exact. The tool must match, or the migration would style names the runtime does not.
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    protected override NamingStyle NamingStyle { get => NamingStyle.SnakeCase; }
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.NamingStyle, Is.EqualTo(NamingStyleKind.Exact));
        Assert.That(table.Columns.Any(c => c.Name == "user_name"), Is.False);
    }

    [Test]
    public void NamingStyle_NonOverrideProperty_Ignored_LikeRuntime()
    {
        // A same-named property that is not an override must not be treated as the naming style
        // (the runtime requires IsOverride).
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public new NamingStyle NamingStyle => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.NamingStyle, Is.EqualTo(NamingStyleKind.Exact));
        Assert.That(table.Columns.Any(c => c.Name == "user_name"), Is.False);
    }

    [Test]
    public void MapTo_NonLiteralArgument_NotExtracted_LikeRuntime()
    {
        // Only string literals are extracted (matching runtime); a non-literal MapTo argument leaves
        // the column on its property/styled name, so tool and runtime still agree.
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    private const string ColName = ""user_name"";
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName => MapTo<string>(ColName);
}";
        var table = ExtractSingleTable(source);

        Assert.That(Column(table, "UserName").MappedName, Is.Null);
        Assert.That(table.Columns.Any(c => c.Name == "user_name"), Is.False);
    }

    // Loads a committed sample source file (sibling ../Samples of this test file) so the guard
    // tracks the REAL AccountSchema, not a copy that could silently drift.
    private static string ReadSampleSource(string fileName, [CallerFilePath] string thisFilePath = "")
    {
        var dir = Path.GetDirectoryName(thisFilePath)!;
        return File.ReadAllText(Path.Combine(dir, "..", "Samples", fileName));
    }

    [Test]
    public void AccountSchema_MapTo_YieldsPhysicalCreditLimit_MatchingRuntime()
    {
        // Real AccountSchema + real Money/MoneyMapping; UserSchema is stubbed to the minimum the
        // Ref needs (the real one drags in the whole sample graph). AccountSchema itself is verbatim.
        var accountSchema = ReadSampleSource("AccountSchema.cs");
        var money = ReadSampleSource("Money.cs");
        const string userSchemaStub = @"
using Quarry;
namespace Quarry.Tests.Samples;
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}";
        var compilation = CreateCompilation(accountSchema, money, userSchemaStub);
        var snapshot = ProjectSchemaReader.ExtractSchemaSnapshot(compilation, 1, "AccountsInit", null);

        var accounts = snapshot.Tables.Single(t => t.TableName == "accounts");

        // CreditLimit => Mapped<Money, MoneyMapping>().MapTo("credit_limit")
        var creditLimit = accounts.Columns.Single(c => c.Name == "credit_limit");
        Assert.That(creditLimit.MappedName, Is.EqualTo("credit_limit"));
        Assert.That(accounts.Columns.Any(c => c.Name == "CreditLimit"), Is.False,
            "The property name must not survive; the runtime queries the physical 'credit_limit'.");

        // Balance => Mapped<Money, MoneyMapping>() with no MapTo keeps its Exact property name.
        Assert.That(accounts.Columns.Single(c => c.Name == "Balance").MappedName, Is.Null);

        // The migration DDL is generated from col.Name, so it must carry the physical name —
        // matching the runtime SQL asserted by CrossDialectSchemaTests.Select_MapToColumn_CreditLimit.
        var steps = SchemaDiffer.Diff(null, snapshot);
        var migration = MigrationCodeGenerator.GenerateMigrationClass(
            1, "AccountsInit", steps, null, snapshot, "Test");
        Assert.That(migration, Does.Contain("\"credit_limit\""));
        Assert.That(migration, Does.Not.Contain("\"CreditLimit\""));
    }
}
