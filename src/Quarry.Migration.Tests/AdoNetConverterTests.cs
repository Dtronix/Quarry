using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class AdoNetConverterTests
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

    private static IReadOnlyList<AdoNetConversionEntry> Convert(string userCode, string? dialect = null)
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

        var converter = new AdoNetConverter();
        return converter.ConvertAll(compilation, dialect);
    }

    [Test]
    public void SelectWithParameter_Converts()
    {
        var entries = Convert(@"
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
        cmd.CommandText = ""SELECT * FROM users WHERE user_id = @id"";
        cmd.Parameters.AddWithValue(""@id"", 1);
        var reader = cmd.ExecuteReader();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].ChainCode, Does.Contain(".Users()"));
        Assert.That(entries[0].ChainCode, Does.Contain(".Where("));
        Assert.That(entries[0].ChainCode, Does.Contain("id"));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteFetchAllAsync()"));
    }

    [Test]
    public void Delete_Converts()
    {
        var entries = Convert(@"
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
        cmd.CommandText = ""DELETE FROM users WHERE user_id = @id"";
        cmd.Parameters.AddWithValue(""@id"", 42);
        cmd.ExecuteNonQuery();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].ChainCode, Does.Contain(".Delete()"));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
    }

    [Test]
    public void Insert_IsSuggestionOnly()
    {
        var entries = Convert(@"
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

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsSuggestionOnly, Is.True);
        Assert.That(entries[0].IsConvertible, Is.False);
    }

    [Test]
    public void Update_Converts()
    {
        var entries = Convert(@"
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
        cmd.CommandText = ""UPDATE users SET user_name = @name WHERE user_id = @id"";
        cmd.Parameters.AddWithValue(""@name"", ""Jane"");
        cmd.Parameters.AddWithValue(""@id"", 1);
        cmd.ExecuteNonQuery();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].ChainCode, Does.Contain(".Update()"));
        Assert.That(entries[0].ChainCode, Does.Contain(".Set("));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
    }

    [Test]
    public void NoSchemaMatch_ReturnsNotConvertible()
    {
        var entries = Convert(@"
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
        cmd.CommandText = ""SELECT * FROM products"";
        cmd.ExecuteReader();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        // Schema miss produces a warning, but no chain code since the table isn't mapped
        Assert.That(entries[0].HasWarnings, Is.True);
    }

    [Test]
    public void ExecuteScalar_MapsCorrectly()
    {
        var entries = Convert(@"
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
        cmd.CommandText = ""SELECT COUNT(*) FROM users"";
        var count = cmd.ExecuteScalar();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteScalarAsync()"));
    }
}
