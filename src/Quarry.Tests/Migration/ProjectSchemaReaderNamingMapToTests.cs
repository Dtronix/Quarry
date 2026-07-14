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
}
