using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class SqlKataMigrationAnalyzerTests
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
        public Query WhereRaw(string sql, params object[] bindings) => this;
        public Query OrderBy(params string[] columns) => this;
        public Query Select(params string[] columns) => this;
        public Query Limit(int value) => this;
        public Query Distinct() => this;
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

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string userCode)
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

        return diagnostics.Where(d => d.Id.StartsWith("QRM")).ToImmutableArray();
    }

    [Test]
    public async Task ConvertibleQuery_ReportsQRM031()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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
        var query = new Query(""users"").Where(""user_id"", "">"", 5);
    }
}
");

        Assert.That(diagnostics.Any(d => d.Id == "QRM031"), Is.True,
            $"Expected QRM031. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task UnsupportedWhereRaw_ReportsQRM032()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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
        var query = new Query(""users"").WhereRaw(""age > ?"", 18);
    }
}
");

        Assert.That(diagnostics.Any(d => d.Id == "QRM032"), Is.True,
            $"Expected QRM032. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task NoSchemaMatch_ReportsQRM033()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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

        Assert.That(diagnostics.Any(d => d.Id == "QRM033"), Is.True,
            $"Expected QRM033. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task NoSqlKataReference_NoDiagnostics()
    {
        var userTree = CSharpSyntaxTree.ParseText(@"
public class Example
{
    public void Run() { }
}
");

        var quarryTree = CSharpSyntaxTree.ParseText(QuarryStub);
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { userTree, quarryTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new SqlKataMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics.Where(d => d.Id.StartsWith("QRM")), Is.Empty);
    }
}
