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
public class SqlKataMigrationCodeFixTests
{
    private static readonly string SqlKataStub = @"
using System.Collections.Generic;

namespace SqlKata
{
    public class Query
    {
        public Query(string table) { }
        public Query Where(string column, string op, object value) => this;
        public Query Where(string column, object value) => this;
        public Query OrWhere(string column, string op, object value) => this;
        public Query WhereNull(string column) => this;
        public Query WhereNotNull(string column) => this;
        public Query WhereIn(string column, IEnumerable<object> values) => this;
        public Query WhereBetween(string column, object lower, object upper) => this;
        public Query WhereRaw(string sql, params object[] bindings) => this;
        public Query OrderBy(params string[] columns) => this;
        public Query OrderByDesc(params string[] columns) => this;
        public Query Select(params string[] columns) => this;
        public Query SelectRaw(string expression, params object[] bindings) => this;
        public Query Join(string table, string first, string second, string op = ""="") => this;
        public Query LeftJoin(string table, string first, string second, string op = ""="") => this;
        public Query Limit(int value) => this;
        public Query Offset(int value) => this;
        public Query Take(int value) => this;
        public Query Skip(int value) => this;
        public Query GroupBy(params string[] columns) => this;
        public Query Distinct() => this;
        public Query AsCount(params string[] columns) => this;
        public Query AsSum(string column) => this;
        public Query AsAvg(string column) => this;
        public Query AsMin(string column) => this;
        public Query AsMax(string column) => this;
        public Query ForPage(int page, int perPage = 15) => this;
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
        var sqlKataTree = CSharpSyntaxTree.ParseText(SqlKataStub);
        var quarryTree = CSharpSyntaxTree.ParseText(QuarryStub);

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, sqlKataTree, quarryTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new SqlKataMigrationAnalyzer();
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
        project = project.AddDocument("SqlKata.cs", SourceText.From(SqlKataStub)).Project;
        project = project.AddDocument("Quarry.cs", SourceText.From(QuarryStub)).Project;
        var document = project.AddDocument("Test.cs", SourceText.From(userCode));
        project = document.Project;

        workspace.TryApplyChanges(project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var fix = new SqlKataMigrationCodeFix();
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
        var fix = new SqlKataMigrationCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM031"));
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM032"));
    }

    [Test]
    public void HasFixAllProvider()
    {
        var fix = new SqlKataMigrationCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── Functional tests ──

    [Test]
    public async Task SimpleQuery_ReplacesWithChainApi()
    {
        // Note: the code fix uses AncestorsAndSelf() to find ObjectCreationExpressionSyntax,
        // which only works when the diagnostic location IS the creation node (bare queries).
        // Chained queries (e.g., new Query("t").Where(...)) have the creation as a descendant,
        // so the code fix correctly handles bare Query creation expressions.
        var (source, actions) = await ApplyCodeFixAsync(@"
using SqlKata;
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
        var query = new Query(""users"");
    }
}
");

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain(".Users()"));
    }

    [Test]
    public async Task UsingDirectivesAdded()
    {
        var (source, _) = await ApplyCodeFixAsync(@"
using SqlKata;
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
        var query = new Query(""users"");
    }
}
");

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("using Quarry;"));
        Assert.That(source, Does.Contain("using Quarry.Query;"));
    }

    [Test]
    public async Task NonFixableDiagnostic_QRM033_NoCodeAction()
    {
        // No schema match → QRM033 → code fix should not apply
        var (source, actions) = await ApplyCodeFixAsync(@"
using SqlKata;
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
        var query = new Query(""products"");
    }
}
");

        Assert.That(source, Is.Null);
        Assert.That(actions, Is.Empty);
    }
}
