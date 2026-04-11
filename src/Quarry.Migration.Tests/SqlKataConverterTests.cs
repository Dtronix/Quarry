using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class SqlKataConverterTests
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

    private static IReadOnlyList<SqlKataConversionEntry> Convert(string userCode)
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

        var converter = new SqlKataConverter();
        return converter.ConvertAll(compilation);
    }

    [Test]
    public void SimpleQuery_Converts()
    {
        var entries = Convert(@"
using SqlKata;
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
        var query = new Query(""users"");
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].ChainCode, Does.Contain(".Users()"));
    }

    [Test]
    public void WhereWithOperator_Converts()
    {
        var entries = Convert(@"
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

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Where(u => u.UserId > 5)"));
    }

    [Test]
    public void WhereDefaultEquals_Converts()
    {
        var entries = Convert(@"
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
        var query = new Query(""users"").Where(""user_id"", 1);
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Where(u => u.UserId == 1)"));
    }

    [Test]
    public void OrderByDesc_MapsToDirectionDescending()
    {
        var entries = Convert(@"
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
        var query = new Query(""users"").OrderByDesc(""user_id"");
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain("Direction.Descending"));
    }

    [Test]
    public void SelectMultipleColumns_Converts()
    {
        var entries = Convert(@"
using SqlKata;
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
        var query = new Query(""users"").Select(""user_id"", ""user_name"");
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Select(u => new {"));
        Assert.That(entries[0].ChainCode, Does.Contain("u.UserId"));
        Assert.That(entries[0].ChainCode, Does.Contain("u.UserName"));
    }

    [Test]
    public void LimitOffset_Converts()
    {
        var entries = Convert(@"
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
        var query = new Query(""users"").Offset(20).Limit(10);
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Offset(20)"));
        Assert.That(entries[0].ChainCode, Does.Contain(".Limit(10)"));
    }

    [Test]
    public void Distinct_Converts()
    {
        var entries = Convert(@"
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
        var query = new Query(""users"").Distinct();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Distinct()"));
    }

    [Test]
    public void WhereNull_Converts()
    {
        var entries = Convert(@"
using SqlKata;
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
        var query = new Query(""users"").WhereNull(""user_name"");
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain("== null"));
    }

    [Test]
    public void UnsupportedWhereRaw_ProducesWarning()
    {
        var entries = Convert(@"
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

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].HasWarnings, Is.True);
        Assert.That(entries[0].Diagnostics.Any(d => d.Message.Contains("WhereRaw")), Is.True);
    }

    [Test]
    public void NoSchemaMatch_NotConvertible()
    {
        var entries = Convert(@"
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

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.False);
    }

    [Test]
    public void AsCount_MapsToScalar()
    {
        var entries = Convert(@"
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
        var query = new Query(""users"").AsCount();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain("Sql.Count()"));
        Assert.That(entries[0].ChainCode, Does.Contain("ExecuteScalarAsync<int>()"));
    }
}
