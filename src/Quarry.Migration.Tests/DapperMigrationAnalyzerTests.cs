using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Migration;
using Quarry.Migration.Analyzers;

namespace Quarry.Migration.Tests;

[TestFixture]
public class DapperMigrationAnalyzerTests
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

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string userCode)
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

        return diagnostics.Where(d => d.Id.StartsWith("QRM")).ToImmutableArray();
    }

    [Test]
    public async Task ConvertibleDapperCall_ReportsQRM001()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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

        Assert.That(diagnostics.Any(d => d.Id == "QRM001"), Is.True,
            $"Expected QRM001. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task NoDapperReference_NoDiagnostics()
    {
        // This test uses a compilation without Dapper references
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

        var analyzer = new DapperMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics.Where(d => d.Id.StartsWith("QRM")), Is.Empty);
    }

    [Test]
    public async Task UnmappableTable_ReportsQRM003()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class Log { }

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<Log>(""SELECT * FROM logs"");
    }
}
");

        Assert.That(diagnostics.Any(d => d.Id == "QRM003"), Is.True,
            $"Expected QRM003. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }
}
