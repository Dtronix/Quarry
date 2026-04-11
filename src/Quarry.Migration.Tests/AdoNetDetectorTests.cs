using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class AdoNetDetectorTests
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

    private static (SemanticModel model, SyntaxNode root) CreateCompilationForDetection(string userCode)
    {
        var userTree = CSharpSyntaxTree.ParseText(userCode);
        var adoNetTree = CSharpSyntaxTree.ParseText(AdoNetStub);

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
            new[] { userTree, adoNetTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(userTree);
        return (model, userTree.GetRoot());
    }

    [Test]
    public void Detect_ExecuteReader_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

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

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("ExecuteReader"));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM users WHERE user_id = @id"));
        Assert.That(sites[0].ParameterNames, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames[0], Is.EqualTo("id"));
    }

    [Test]
    public void Detect_ExecuteNonQuery_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

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

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("ExecuteNonQuery"));
        Assert.That(sites[0].Sql, Does.Contain("DELETE FROM users"));
    }

    [Test]
    public void Detect_ExecuteScalar_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

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

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("ExecuteScalar"));
        Assert.That(sites[0].ParameterNames, Is.Empty);
    }

    [Test]
    public void Detect_MultipleParameters_CollectsAll()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users WHERE name = @name AND age > @age"";
        cmd.Parameters.AddWithValue(""@name"", ""John"");
        cmd.Parameters.AddWithValue(""@age"", 18);
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames, Has.Count.EqualTo(2));
        Assert.That(sites[0].ParameterNames[0], Is.EqualTo("name"));
        Assert.That(sites[0].ParameterNames[1], Is.EqualTo("age"));
    }

    [Test]
    public void Detect_ParametersAdd_WithSqlParameter()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users WHERE user_id = @id"";
        cmd.Parameters.Add(new SqlParameter(""@id"", 1));
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames[0], Is.EqualTo("id"));
    }

    [Test]
    public void Detect_ConstantSql_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    private const string Sql = ""SELECT * FROM users"";

    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = Sql;
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM users"));
    }

    [Test]
    public void Detect_NonDbCommandType_NotDetected()
    {
        var (model, root) = CreateCompilationForDetection(@"
public class MyCommand
{
    public string CommandText { get; set; } = """";
    public int ExecuteNonQuery() => 0;
}

public class Example
{
    public void Run()
    {
        var cmd = new MyCommand();
        cmd.CommandText = ""DELETE FROM foo"";
        cmd.ExecuteNonQuery();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Is.Empty);
    }

    [Test]
    public void Detect_NoCommandText_NotDetected()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        // No CommandText assignment
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Is.Empty);
    }

    [Test]
    public void Detect_ReassignedCommandText_UsesLastBeforeExecute()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT 1"";
        cmd.CommandText = ""SELECT * FROM orders"";
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM orders"));
    }

    [Test]
    public void Detect_CommandTextAfterExecute_Ignored()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users"";
        cmd.ExecuteReader();
        cmd.CommandText = ""SELECT * FROM orders"";
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(2));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM users"));
        Assert.That(sites[1].Sql, Is.EqualTo("SELECT * FROM orders"));
    }

    [Test]
    public void Detect_ParametersAfterExecute_NotCollected()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;

public class Example
{
    public void Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users WHERE name = @name"";
        cmd.Parameters.AddWithValue(""@name"", ""John"");
        cmd.ExecuteReader();
        cmd.CommandText = ""SELECT * FROM orders WHERE id = @id"";
        cmd.Parameters.AddWithValue(""@id"", 42);
        cmd.ExecuteReader();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(2));
        Assert.That(sites[0].ParameterNames, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames[0], Is.EqualTo("name"));
        Assert.That(sites[1].ParameterNames, Has.Count.EqualTo(1));
        Assert.That(sites[1].ParameterNames[0], Is.EqualTo("id"));
    }

    [Test]
    public void Detect_AsyncMethods_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data.SqlClient;
using System.Threading.Tasks;

public class Example
{
    public async Task Run()
    {
        var cmd = new SqlCommand();
        cmd.CommandText = ""SELECT * FROM users"";
        var reader = await cmd.ExecuteReaderAsync();
    }
}
");

        var detector = new AdoNetDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("ExecuteReaderAsync"));
    }
}
