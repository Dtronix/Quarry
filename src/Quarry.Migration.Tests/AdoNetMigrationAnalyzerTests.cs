using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class AdoNetMigrationAnalyzerTests
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

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string userCode)
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

        return diagnostics.Where(d => d.Id.StartsWith("QRM")).ToImmutableArray();
    }

    [Test]
    public async Task ConvertibleSelect_ReportsQRM021()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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

        Assert.That(diagnostics.Any(d => d.Id == "QRM021"), Is.True,
            $"Expected QRM021. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task InsertStatement_ReportsQRM023()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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
        cmd.CommandText = ""INSERT INTO users (user_name) VALUES (@name)"";
        cmd.Parameters.AddWithValue(""@name"", ""John"");
        cmd.ExecuteNonQuery();
    }
}
");

        Assert.That(diagnostics.Any(d => d.Id == "QRM023"), Is.True,
            $"Expected QRM023. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task NoDbCommandReference_NoDiagnostics()
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

        var analyzer = new AdoNetMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics.Where(d => d.Id.StartsWith("QRM")), Is.Empty);
    }
}
