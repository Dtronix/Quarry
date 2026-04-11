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
public class DapperMigrationCodeFixTests
{
    private static readonly string DapperStub = @"
using System.Data;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Dapper
{
    public static class SqlMapper
    {
        public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult<IEnumerable<T>>(null!);
        public static Task<T> QueryFirstAsync<T>(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult<T>(default!);
        public static Task<int> ExecuteAsync(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult(0);
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
        var dapperTree = CSharpSyntaxTree.ParseText(DapperStub);
        var quarryTree = CSharpSyntaxTree.ParseText(QuarryStub);

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, dapperTree, quarryTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new DapperMigrationAnalyzer();
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
        project = project.AddDocument("Dapper.cs", SourceText.From(DapperStub)).Project;
        project = project.AddDocument("Quarry.cs", SourceText.From(QuarryStub)).Project;
        var document = project.AddDocument("Test.cs", SourceText.From(userCode));
        project = document.Project;

        workspace.TryApplyChanges(project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var fix = new DapperMigrationCodeFix();
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
        var fix = new DapperMigrationCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM001"));
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM002"));
    }

    [Test]
    public void HasFixAllProvider()
    {
        var fix = new DapperMigrationCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── Functional tests ──

    [Test]
    public async Task SimpleQueryAsync_ReplacesWithChainApi()
    {
        var (source, actions) = await ApplyCodeFixAsync(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class User { public int UserId { get; set; } }

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM users"");
    }
}
");

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain(".Users()"));
    }

    [Test]
    public async Task AwaitPreserved_WhenOriginalIsAwaited()
    {
        var (source, _) = await ApplyCodeFixAsync(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM users"");
    }
}
");

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("await "));
    }

    [Test]
    public async Task UsingDirectivesAdded()
    {
        var (source, _) = await ApplyCodeFixAsync(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM users"");
    }
}
");

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("using Quarry;"));
        Assert.That(source, Does.Contain("using Quarry.Query;"));
    }

    [Test]
    public async Task NonFixableDiagnostic_QRM003_NoCodeAction()
    {
        // INSERT emits IsSuggestionOnly=true → routed to QRM003 → not in FixableDiagnosticIds
        var (source, actions) = await ApplyCodeFixAsync(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        await connection.ExecuteAsync(""INSERT INTO users (user_name) VALUES (@name)"", new { name = ""x"" });
    }
}
");

        Assert.That(source, Is.Null);
        Assert.That(actions, Is.Empty);
    }
}
