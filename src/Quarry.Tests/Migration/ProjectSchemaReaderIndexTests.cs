using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Shared.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

[TestFixture]
public class ProjectSchemaReaderIndexTests
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

    private static SchemaSnapshot ExtractSnapshot(string source, int version = 1, string name = "test")
    {
        var compilation = CreateCompilation(source);
        return ProjectSchemaReader.ExtractSchemaSnapshot(compilation, version, name, null);
    }

    private static TableDef ExtractSingleTable(string source)
    {
        var snapshot = ExtractSnapshot(source);
        Assert.That(snapshot.Tables, Has.Count.EqualTo(1), "Expected exactly one table");
        return snapshot.Tables[0];
    }

    #region Category A: Schema & Column Detection

    [Test]
    public void ExtractSchema_BasicTable_ExtractsColumnsAndTable()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
    public Col<string?> Email { get; }
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.TableName, Is.EqualTo("users"));
        Assert.That(table.Columns, Has.Count.EqualTo(3));
        Assert.That(table.Columns[0].Name, Is.EqualTo("UserId"));
        Assert.That(table.Columns[0].Kind, Is.EqualTo(ColumnKind.PrimaryKey));
        Assert.That(table.Columns[1].Name, Is.EqualTo("UserName"));
        Assert.That(table.Columns[1].Kind, Is.EqualTo(ColumnKind.Standard));
        Assert.That(table.Columns[2].Name, Is.EqualTo("Email"));
        Assert.That(table.Columns[2].IsNullable, Is.True);
    }

    [Test]
    public void ExtractSchema_NamingStyleSnakeCase_AppliedToColumnNames()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public NamingStyle Naming => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.NamingStyle, Is.EqualTo(NamingStyleKind.SnakeCase));
        Assert.That(table.Columns[0].Name, Is.EqualTo("user_id"));
        Assert.That(table.Columns[1].Name, Is.EqualTo("user_name"));
    }

    #endregion

    #region Category B: Basic Index Extraction

    [Test]
    public void ExtractIndex_SingleColumn_CreatesIndexDef()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns[0], Is.EqualTo("Email"));
    }

    [Test]
    public void ExtractIndex_MultiColumn_PreservesOrder()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
    public Col<string> Email { get; }

    public Index IX_Name_Email => Index(UserName, Email);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "UserName", "Email" }));
    }

    [Test]
    public void ExtractIndex_PropertyNameUsedAsIndexName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].Name, Is.EqualTo("IX_Email"));
    }

    #endregion

    #region Category C: Direction Specifiers

    [Test]
    public void ExtractIndex_DescDirection_ExtractsColumnName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<DateTime> CreatedAt { get; }

    public Index IX_Created => Index(CreatedAt.Desc());
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "CreatedAt" }));
    }

    [Test]
    public void ExtractIndex_AscDirection_ExtractsColumnName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email.Asc());
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "Email" }));
    }

    [Test]
    public void ExtractIndex_MixedDirections_ExtractsAllColumns()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
    public Col<DateTime> CreatedAt { get; }

    public Index IX_Multi => Index(Email.Asc(), CreatedAt.Desc());
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "Email", "CreatedAt" }));
    }

    #endregion

    #region Category D: Fluent Modifiers

    [Test]
    public void ExtractIndex_Unique_SetsIsUnique()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email).Unique();
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].IsUnique, Is.True);
        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "Email" }));
    }

    [Test]
    public void ExtractIndex_WhereRawSql_SetsFilter()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email).Where(""email IS NOT NULL"");
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].Filter, Is.EqualTo("email IS NOT NULL"));
    }

    [Test]
    public void ExtractIndex_WhereBoolColumn_SetsFilterWithColumnName()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
    public Col<bool> IsActive { get; }

    public Index IX_Email => Index(Email).Where(IsActive);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].Filter, Is.EqualTo("IsActive = TRUE"));
    }

    [Test]
    public void ExtractIndex_Using_SetsMethod()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email).Using(IndexType.Hash);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].Method, Is.EqualTo("Hash"));
    }

    [Test]
    public void ExtractIndex_FullChain_AllPropertiesSet()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email).Unique().Where(""email IS NOT NULL"").Using(IndexType.Hash);
}";
        var table = ExtractSingleTable(source);
        var idx = table.Indexes[0];

        Assert.That(idx.Name, Is.EqualTo("IX_Email"));
        Assert.That(idx.Columns, Is.EqualTo(new[] { "Email" }));
        Assert.That(idx.IsUnique, Is.True);
        Assert.That(idx.Filter, Is.EqualTo("email IS NOT NULL"));
        Assert.That(idx.Method, Is.EqualTo("Hash"));
    }

    #endregion

    #region Category E: Include (ignored)

    [Test]
    public void ExtractIndex_Include_IgnoredInIndexDef()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
    public Col<string> UserName { get; }
    public Col<DateTime> CreatedAt { get; }

    public Index IX_Covering => Index(Email).Include(UserName, CreatedAt);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(1));
        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "Email" }));
        Assert.That(table.Indexes[0].Name, Is.EqualTo("IX_Covering"));
    }

    #endregion

    #region Category F: Naming Convention

    [Test]
    public void ExtractIndex_SnakeCaseNaming_ConvertsColumnNames()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public NamingStyle Naming => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }

    public Index IX_UserName => Index(UserName);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].Columns, Is.EqualTo(new[] { "user_name" }));
    }

    [Test]
    public void ExtractIndex_SnakeCaseNaming_WhereBoolColumnConverted()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public NamingStyle Naming => NamingStyle.SnakeCase;
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
    public Col<bool> IsActive { get; }

    public Index IX_Email => Index(Email).Where(IsActive);
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes[0].Filter, Is.EqualTo("is_active = TRUE"));
    }

    #endregion

    #region Category G: No Index / Edge Cases

    [Test]
    public void ExtractSchema_NoIndexes_EmptyIndexList()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Is.Empty);
    }

    [Test]
    public void ExtractIndex_MultipleIndexes_AllExtracted()
    {
        var source = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> UserName { get; }
    public Col<string> Email { get; }
    public Col<DateTime> CreatedAt { get; }

    public Index IX_Email => Index(Email);
    public Index IX_UserName => Index(UserName);
    public Index IX_Created => Index(CreatedAt.Desc());
}";
        var table = ExtractSingleTable(source);

        Assert.That(table.Indexes, Has.Count.EqualTo(3));
        var names = table.Indexes.Select(i => i.Name).ToList();
        Assert.That(names, Does.Contain("IX_Email"));
        Assert.That(names, Does.Contain("IX_UserName"));
        Assert.That(names, Does.Contain("IX_Created"));
    }

    #endregion

    #region Category H: End-to-End Diff Integration

    [Test]
    public void Diff_IndexAdded_ViaExtraction_EmitsAddIndex()
    {
        var sourceBefore = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
}";
        var sourceAfter = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email);
}";
        var before = ExtractSnapshot(sourceBefore, version: 1);
        var after = ExtractSnapshot(sourceAfter, version: 2);
        var steps = SchemaDiffer.Diff(before, after);

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].StepType, Is.EqualTo(MigrationStepType.AddIndex));
        Assert.That(steps[0].TableName, Is.EqualTo("users"));
        var indexDef = steps[0].NewValue as IndexDef;
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.Name, Is.EqualTo("IX_Email"));
    }

    [Test]
    public void Diff_IndexRemoved_ViaExtraction_EmitsDropIndex()
    {
        var sourceBefore = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email);
}";
        var sourceAfter = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }
}";
        var before = ExtractSnapshot(sourceBefore, version: 1);
        var after = ExtractSnapshot(sourceAfter, version: 2);
        var steps = SchemaDiffer.Diff(before, after);

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].StepType, Is.EqualTo(MigrationStepType.DropIndex));
        Assert.That(steps[0].TableName, Is.EqualTo("users"));
        var indexDef = steps[0].OldValue as IndexDef;
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.Name, Is.EqualTo("IX_Email"));
    }

    [Test]
    public void Diff_IndexModified_ViaExtraction_EmitsDropAndAdd()
    {
        var sourceBefore = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email);
}";
        var sourceAfter = @"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId { get; }
    public Col<string> Email { get; }

    public Index IX_Email => Index(Email).Unique();
}";
        var before = ExtractSnapshot(sourceBefore, version: 1);
        var after = ExtractSnapshot(sourceAfter, version: 2);
        var steps = SchemaDiffer.Diff(before, after);

        Assert.That(steps, Has.Count.EqualTo(2));
        Assert.That(steps[0].StepType, Is.EqualTo(MigrationStepType.DropIndex));
        Assert.That(steps[1].StepType, Is.EqualTo(MigrationStepType.AddIndex));
        var newIndex = steps[1].NewValue as IndexDef;
        Assert.That(newIndex, Is.Not.Null);
        Assert.That(newIndex!.IsUnique, Is.True);
    }

    #endregion
}
