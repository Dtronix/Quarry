using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class AdoNetMigrationCodeFixTests
{
    private static readonly string AdoNetStub = @"
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace System.Data.Common
{
    public abstract class DbCommand : IDisposable
    {
        public abstract string CommandText { get; set; }
        public DbParameterCollection Parameters { get; } = null!;
        public abstract DbDataReader ExecuteReader();
        public abstract int ExecuteNonQuery();
        public abstract object ExecuteScalar();
        public virtual Task<DbDataReader> ExecuteReaderAsync(CancellationToken ct = default) => null!;
        public virtual Task<int> ExecuteNonQueryAsync(CancellationToken ct = default) => null!;
        public virtual Task<object?> ExecuteScalarAsync(CancellationToken ct = default) => null!;
        public void Dispose() { }
    }

    public abstract class DbDataReader : IDisposable
    {
        public abstract bool Read();
        public void Dispose() { }
    }

    public abstract class DbParameterCollection
    {
        public void AddWithValue(string parameterName, object value) { }
        public int Add(object value) => 0;
    }

    public class DbParameter
    {
        public string ParameterName { get; set; }
        public object Value { get; set; }
        public DbParameter(string name, object value) { ParameterName = name; Value = value; }
    }
}

namespace System.Data.SqlClient
{
    public class SqlCommand : System.Data.Common.DbCommand
    {
        public override string CommandText { get; set; } = """";
        public override System.Data.Common.DbDataReader ExecuteReader() => null!;
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => null!;
    }

    public class SqlParameter : System.Data.Common.DbParameter
    {
        public SqlParameter(string name, object value) : base(name, value) { }
    }
}
";

    private static readonly string QuarryStub = @"
namespace Quarry
{
    public abstract class Schema
    {
        protected virtual NamingStyle NamingStyle => NamingStyle.Exact;
        protected static ColumnBuilder<T> Identity<T>() => default;
        protected static ColumnBuilder<T> Length<T>(int maxLength) => default;
    }
    public enum NamingStyle { Exact = 0, SnakeCase = 1 }
    public readonly struct Col<T>
    {
        public static implicit operator Col<T>(ColumnBuilder<T> builder) => default;
    }
    public readonly struct Key<T>
    {
        public static implicit operator Key<T>(ColumnBuilder<T> builder) => default;
    }
    public readonly struct ColumnBuilder<T>
    {
        public ColumnBuilder<T> Identity() => default;
        public ColumnBuilder<T> Length(int maxLength) => default;
    }
}
";

    private static async Task<(string? transformedSource, List<CodeAction> actions)> ApplyCodeFixAsync(string userCode)
    {
        var userTree = CSharpSyntaxTree.ParseText(userCode);
        var adoNetTree = CSharpSyntaxTree.ParseText(AdoNetStub);
        var quarryTree = CSharpSyntaxTree.ParseText(QuarryStub);

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, adoNetTree, quarryTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new AdoNetMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var migrationDiagnostics = diagnostics.Where(d => d.Id.StartsWith("QRM")).ToList();

        if (migrationDiagnostics.Count == 0)
            return (null, new List<CodeAction>());

        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "TestProject",
            "TestProject",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: references);
        var project = workspace.AddProject(projectInfo);
        project = project.AddDocument("AdoNet.cs", SourceText.From(AdoNetStub)).Project;
        project = project.AddDocument("Quarry.cs", SourceText.From(QuarryStub)).Project;
        var document = project.AddDocument("Test.cs", SourceText.From(userCode));
        project = document.Project;

        workspace.TryApplyChanges(project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var fix = new AdoNetMigrationCodeFix();
        var fixableDiagnostic = migrationDiagnostics
            .FirstOrDefault(d => fix.FixableDiagnosticIds.Contains(d.Id));

        if (fixableDiagnostic == null)
            return (null, new List<CodeAction>());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            fixableDiagnostic,
            (action, _) => actions.Add(action),
            default);

        await fix.RegisterCodeFixesAsync(context);

        if (actions.Count == 0)
            return (null, actions);

        var operations = await actions[0].GetOperationsAsync(default);
        var changedSolution = operations.OfType<ApplyChangesOperation>().First().ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var newText = await changedDocument.GetTextAsync();

        return (newText.ToString(), actions);
    }

    // ── Registration tests ──

    [Test]
    public void FixableDiagnosticIds_ContainsExpectedIds()
    {
        var fix = new AdoNetMigrationCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM021"));
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM022"));
    }

    [Test]
    public void HasFixAllProvider()
    {
        var fix = new AdoNetMigrationCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── Functional tests ──

    [Test]
    public async Task SimpleExecuteReader_ReplacesWithChainApi()
    {
        var (source, actions) = await ApplyCodeFixAsync(@"
using System.Data.SqlClient;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users"";
        var reader = cmd.ExecuteReader();
    }
}
");

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain(".Users()"));
    }

    [Test]
    public async Task TodoCommentInserted()
    {
        var (source, _) = await ApplyCodeFixAsync(@"
using System.Data.SqlClient;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users"";
        var reader = cmd.ExecuteReader();
    }
}
");

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("// TODO: Remove DbCommand setup above"));
    }

    [Test]
    public async Task UsingDirectivesAdded()
    {
        var (source, _) = await ApplyCodeFixAsync(@"
using System.Data.SqlClient;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users"";
        var reader = cmd.ExecuteReader();
    }
}
");

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("using Quarry;"));
        Assert.That(source, Does.Contain("using Quarry.Query;"));
    }

    [Test]
    public async Task WarningTierDiagnostic_QRM022_StillAppliesCodeFix()
    {
        // DELETE without WHERE → QRM022 (conversion with warnings) → code fix still applies
        var (source, actions) = await ApplyCodeFixAsync(@"
using System.Data.SqlClient;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""DELETE FROM users"";
        cmd.ExecuteNonQuery();
    }
}
");

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain(".Users()"));
        Assert.That(source, Does.Contain(".All()"));
    }

    [Test]
    public async Task NonFixableDiagnostic_QRM023_NoCodeAction()
    {
        var (source, actions) = await ApplyCodeFixAsync(@"
using System.Data.SqlClient;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""INSERT INTO users (user_name) VALUES ('John')"";
        cmd.ExecuteNonQuery();
    }
}
");

        Assert.That(source, Is.Null);
        Assert.That(actions, Is.Empty);
    }
}
