using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class DapperDetectorTests
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

        public static Task<T> QueryFirstOrDefaultAsync<T>(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult<T>(default!);

        public static Task<T> QuerySingleAsync<T>(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult<T>(default!);

        public static Task<T> QuerySingleOrDefaultAsync<T>(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult<T>(default!);

        public static Task<int> ExecuteAsync(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult(0);

        public static Task<T> ExecuteScalarAsync<T>(this IDbConnection connection, string sql, object? param = null)
            => Task.FromResult<T>(default!);

        public static IEnumerable<T> Query<T>(this IDbConnection connection, string sql, object? param = null)
            => null!;

        public static T QueryFirst<T>(this IDbConnection connection, string sql, object? param = null)
            => default!;

        public static T QueryFirstOrDefault<T>(this IDbConnection connection, string sql, object? param = null)
            => default!;
    }
}
";

    private static (SemanticModel model, SyntaxNode root) CreateCompilationForDetection(string userCode)
    {
        var userTree = CSharpSyntaxTree.ParseText(userCode);
        var dapperTree = CSharpSyntaxTree.ParseText(DapperStub);

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
            new[] { userTree, dapperTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(userTree);
        return (model, userTree.GetRoot());
    }

    [Test]
    public void Detect_QueryAsync_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM users"");
    }
}

public class User { public int UserId { get; set; } }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("QueryAsync"));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM users"));
        Assert.That(sites[0].ResultTypeName, Is.EqualTo("User"));
        Assert.That(sites[0].ParameterNames, Is.Empty);
    }

    [Test]
    public void Detect_QueryAsyncWithParams_ExtractsParameterNames()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection, int userId, string name)
    {
        var results = await connection.QueryAsync<User>(
            ""SELECT * FROM users WHERE user_id = @userId AND name = @name"",
            new { userId, name });
    }
}

public class User { public int UserId { get; set; } }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames, Is.EqualTo(new[] { "userId", "name" }));
    }

    [Test]
    public void Detect_QueryFirstAsync_DetectsMethodName()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var user = await connection.QueryFirstAsync<User>(""SELECT * FROM users WHERE user_id = 1"");
    }
}

public class User { public int UserId { get; set; } }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("QueryFirstAsync"));
    }

    [Test]
    public void Detect_ExecuteAsync_NoResultType()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection, int userId)
    {
        await connection.ExecuteAsync(""DELETE FROM users WHERE user_id = @userId"", new { userId });
    }
}
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("ExecuteAsync"));
        Assert.That(sites[0].ResultTypeName, Is.Null);
        Assert.That(sites[0].Sql, Is.EqualTo("DELETE FROM users WHERE user_id = @userId"));
        Assert.That(sites[0].ParameterNames, Is.EqualTo(new[] { "userId" }));
    }

    [Test]
    public void Detect_MultipleCalls_DetectsAll()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var users = await connection.QueryAsync<User>(""SELECT * FROM users"");
        var orders = await connection.QueryAsync<Order>(""SELECT * FROM orders"");
        await connection.ExecuteAsync(""DELETE FROM logs"");
    }
}

public class User { }
public class Order { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(3));
    }

    [Test]
    public void Detect_NonDapperMethod_Ignored()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using System.Threading.Tasks;

public class Example
{
    public async Task Run()
    {
        var result = QueryAsync<int>(""not dapper"");
    }

    private T QueryAsync<T>(string sql) => default!;
}
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Is.Empty);
    }

    [Test]
    public void Detect_NonLiteralSql_ReturnsNull()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection, string dynamicSql)
    {
        var results = await connection.QueryAsync<User>(dynamicSql);
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Is.Empty);
    }

    [Test]
    public void Detect_ConstantSql_ExtractsValue()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    private const string Sql = ""SELECT * FROM users"";

    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(Sql);
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM users"));
    }

    [Test]
    public void Detect_NamedParamArgument_ExtractsNames()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection, int id)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM users WHERE id = @id"", param: new { id });
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames, Is.EqualTo(new[] { "id" }));
    }

    [Test]
    public void Detect_NamedAnonymousMembers_ExtractsNames()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection, int userId)
    {
        var results = await connection.QueryAsync<User>(
            ""SELECT * FROM users WHERE id = @Id"",
            new { Id = userId });
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].ParameterNames, Is.EqualTo(new[] { "Id" }));
    }

    [Test]
    public void Detect_SyncQuery_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;

public class Example
{
    public void Run(IDbConnection connection)
    {
        var users = connection.Query<User>(""SELECT * FROM users"");
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("Query"));
    }

    [Test]
    public void Detect_VerbatimString_ExtractsSql()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(@""SELECT *
FROM users
WHERE is_active = 1"");
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Sql, Does.Contain("SELECT *"));
        Assert.That(sites[0].Sql, Does.Contain("FROM users"));
    }

    [Test]
    public void Detect_ExecuteScalarAsync_DetectsMethodAndType()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var count = await connection.ExecuteScalarAsync<int>(""SELECT COUNT(*) FROM users"");
    }
}
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].MethodName, Is.EqualTo("ExecuteScalarAsync"));
        Assert.That(sites[0].ResultTypeName, Is.EqualTo("int"));
    }

    [Test]
    public void Detect_ConcatenatedConstantSql_ExtractsValue()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Data;
using Dapper;
using System.Threading.Tasks;

public class Example
{
    public async Task Run(IDbConnection connection)
    {
        var results = await connection.QueryAsync<User>(""SELECT * FROM "" + ""users"");
    }
}

public class User { }
");

        var detector = new DapperDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Sql, Is.EqualTo("SELECT * FROM users"));
    }
}
