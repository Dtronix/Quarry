using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class DapperConverterTests
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

    private static Compilation CreateCompilation(string userCode)
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

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, dapperTree, quarryTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Test]
    public void ConvertAll_FindsAndConverts()
    {
        var compilation = CreateCompilation(@"
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

        var converter = new DapperConverter();
        var results = converter.ConvertAll(compilation);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].IsConvertible, Is.True);
        Assert.That(results[0].ChainCode, Does.Contain("db.Users()"));
        Assert.That(results[0].DapperMethod, Is.EqualTo("QueryAsync"));
        Assert.That(results[0].OriginalSql, Is.EqualTo("SELECT * FROM users"));
    }

    [Test]
    public void ConvertAll_UnconvertibleQuery_ReportsNotConvertible()
    {
        var compilation = CreateCompilation(@"
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

        var converter = new DapperConverter();
        var results = converter.ConvertAll(compilation);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].IsConvertible, Is.False);
        Assert.That(results[0].Diagnostics, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ConvertAll_WithDialect_UsesSpecifiedDialect()
    {
        var compilation = CreateCompilation(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { }

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM users LIMIT 10"");
    }
}
");

        var converter = new DapperConverter();
        var results = converter.ConvertAll(compilation, "sqlite");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].IsConvertible, Is.True);
    }

    [Test]
    public void CountSchemaEntities_ReturnsCorrectCount()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity<int>();
}
");

        var converter = new DapperConverter();
        var count = converter.CountSchemaEntities(compilation);

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void ConvertAll_InsertExecuteAsync_IsSuggestionOnly()
    {
        var compilation = CreateCompilation(@"
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

        var converter = new DapperConverter();
        var results = converter.ConvertAll(compilation);

        Assert.That(results, Has.Count.EqualTo(1));
        var entry = results[0];

        // INSERT must surface as a manual-conversion suggestion, NOT as an
        // auto-convertible result. The CLI consumer relies on IsSuggestionOnly
        // to print the comment block as-is rather than flattening it onto one line.
        Assert.That(entry.IsSuggestionOnly, Is.True,
            "INSERT should be marked IsSuggestionOnly so the CLI doesn't pretend it's chain code.");
        Assert.That(entry.IsConvertible, Is.False,
            "IsConvertible must exclude suggestion-only outputs to prevent mechanical application of comment text.");
        Assert.That(entry.ChainCode, Does.Contain("// TODO:"),
            "INSERT chain code should be the comment template, not a substitutable expression.");
        Assert.That(entry.ChainCode, Does.Contain("Insert(entity).ExecuteNonQueryAsync()"));
        Assert.That(entry.HasWarnings, Is.True,
            "INSERT should still report a warning explaining the manual-conversion requirement.");
    }

    [Test]
    public void ConvertAll_DeleteExecuteAsync_IsConvertible()
    {
        // Counterpart to the INSERT test: DELETE produces real chain code (not a comment),
        // so IsSuggestionOnly stays false and IsConvertible stays true.
        var compilation = CreateCompilation(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        await connection.ExecuteAsync(""DELETE FROM users WHERE user_id = @id"", new { id = 1 });
    }
}
");

        var converter = new DapperConverter();
        var results = converter.ConvertAll(compilation);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].IsSuggestionOnly, Is.False);
        Assert.That(results[0].IsConvertible, Is.True);
        Assert.That(results[0].ChainCode, Does.Contain(".Delete()"));
        Assert.That(results[0].ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
    }

    [Test]
    public void ConvertAll_NoDapperCalls_ReturnsEmpty()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class Example
{
    public void Run() { }
}
");

        var converter = new DapperConverter();
        var results = converter.ConvertAll(compilation);

        Assert.That(results, Is.Empty);
    }
}
