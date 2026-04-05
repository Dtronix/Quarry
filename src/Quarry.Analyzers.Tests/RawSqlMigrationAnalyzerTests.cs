using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Analyzers;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class RawSqlMigrationAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetMigrationDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRef = System.IO.Path.Combine(runtimeDir, "System.Runtime.dll");
        if (System.IO.File.Exists(runtimeRef))
            references.Add(MetadataReference.CreateFromFile(runtimeRef));

        // Add netstandard for analyzer assembly compatibility
        var netstdRef = System.IO.Path.Combine(runtimeDir, "netstandard.dll");
        if (System.IO.File.Exists(netstdRef))
            references.Add(MetadataReference.CreateFromFile(netstdRef));

        var quarryAssembly = typeof(Quarry.QuarryContext).Assembly.Location;
        if (!string.IsNullOrEmpty(quarryAssembly))
            references.Add(MetadataReference.CreateFromFile(quarryAssembly));

        // Add System.Data.Common for DbConnection
        var dataCommonAssembly = typeof(System.Data.Common.DbConnection).Assembly.Location;
        if (!string.IsNullOrEmpty(dataCommonAssembly))
            references.Add(MetadataReference.CreateFromFile(dataCommonAssembly));

        // Add System.ComponentModel for IDbConnection
        var componentModelAssembly = typeof(System.Data.IDbConnection).Assembly.Location;
        if (!string.IsNullOrEmpty(componentModelAssembly))
            references.Add(MetadataReference.CreateFromFile(componentModelAssembly));

        // Add Threading for IAsyncEnumerable
        var threadingRef = System.IO.Path.Combine(runtimeDir, "System.Threading.dll");
        if (System.IO.File.Exists(threadingRef))
            references.Add(MetadataReference.CreateFromFile(threadingRef));

        // Add System.Runtime.InteropServices
        var interopRef = System.IO.Path.Combine(runtimeDir, "System.Runtime.InteropServices.dll");
        if (System.IO.File.Exists(interopRef))
            references.Add(MetadataReference.CreateFromFile(interopRef));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new RawSqlMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics
            .Where(d => d.Id.StartsWith("QRY"))
            .ToImmutableArray();
    }

    // ── Source templates ──

    private const string ContextAndSchemaPrefix = @"
using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Threading;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
    public Col<string> Status { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public TestDbContext(IDbConnection connection) : base(connection) { }
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
}

public class Order
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
}
";

    // ── Detection tests ──

    [Test]
    public async Task QRY042_StringLiteralRawSql_ReportsDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRY042"));
    }

    [Test]
    public async Task QRY042_VariableSqlArgument_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        string sql = ""SELECT * FROM users"";
        var results = db.RawSqlAsync<User>(sql);
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task QRY042_NonQuarryContextReceiver_NoDiagnostic()
    {
        // Calling RawSqlAsync on something that isn't a QuarryContext should not trigger
        var source = @"
using System;
using System.Collections.Generic;

public class FakeContext
{
    public IAsyncEnumerable<T> RawSqlAsync<T>(string sql, params object?[] parameters) => throw new NotSupportedException();
}

public class User { public int UserId { get; set; } }

public class TestService
{
    public void Run(FakeContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task QRY042_WithParameters_ReportsDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users WHERE UserId = @p0"", 1);
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRY042"));
    }

    [Test]
    public async Task QRY042_DiagnosticProperties_ContainSql()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Properties.ContainsKey("Sql"), Is.True);
        Assert.That(diagnostics[0].Properties["Sql"], Is.EqualTo("SELECT * FROM users"));
    }

    // ── Convertibility tests: should report ──

    [Test]
    public async Task QRY042_SelectWithWhereAndOrderBy_ReportsDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT UserId, UserName FROM users WHERE IsActive = @p0 ORDER BY UserName"", true);
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task QRY042_SelectDistinct_ReportsDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT DISTINCT UserName FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task QRY042_SelectWithLimitOffset_ReportsDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users LIMIT 10 OFFSET 5"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
    }

    // ── Convertibility tests: should NOT report ──

    [Test]
    public async Task QRY042_UnknownTable_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM unknown_table"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task QRY042_UnknownColumn_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT NonExistent FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task QRY042_CaseExpression_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT CASE WHEN IsActive = 1 THEN 'Yes' ELSE 'No' END FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task QRY042_InvalidSql_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""not valid sql at all"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task QRY042_UnsupportedFunction_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT COALESCE(Email, 'none') FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    // ── Chain code generation tests ──

    [Test]
    public async Task QRY042_ChainCode_SimpleSelectStar()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".Users()"));
        Assert.That(chainCode, Does.Contain(".Select(u => u)"));
        Assert.That(chainCode, Does.Contain(".ToAsyncEnumerable()"));
    }

    [Test]
    public async Task QRY042_ChainCode_WhereWithParameter()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db, int userId)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users WHERE UserId = @p0"", userId);
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".Where(u => u.UserId == userId)"));
    }

    [Test]
    public async Task QRY042_ChainCode_OrderByDescending()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users ORDER BY UserName DESC"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".OrderBy(u => u.UserName, Direction.Descending)"));
    }

    [Test]
    public async Task QRY042_ChainCode_SelectSpecificColumns()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT UserId, UserName FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".Select(u => (u.UserId, u.UserName))"));
    }

    [Test]
    public async Task QRY042_ChainCode_LimitAndOffset()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users LIMIT 10 OFFSET 5"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".Limit(10)"));
        Assert.That(chainCode, Does.Contain(".Offset(5)"));
    }

    [Test]
    public async Task QRY042_ChainCode_Distinct()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT DISTINCT UserName FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".Distinct()"));
    }

    [Test]
    public async Task QRY042_ChainCode_CountAggregate()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT COUNT(*) FROM users"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain("Sql.Count()"));
    }

    // ── JOIN tests ──

    [Test]
    public async Task QRY042_ChainCode_InnerJoin()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT u.UserName, o.Total FROM users u INNER JOIN orders o ON u.UserId = o.UserId"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".Join<Order>("));
        Assert.That(chainCode, Does.Contain("u.UserId == o.UserId"));
        Assert.That(chainCode, Does.Contain(".Select((u, o) => (u.UserName, o.Total))"));
    }

    [Test]
    public async Task QRY042_ChainCode_LeftJoin()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT u.UserName FROM users u LEFT JOIN orders o ON u.UserId = o.UserId"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".LeftJoin<Order>("));
    }

    // ── GROUP BY / HAVING tests ──

    [Test]
    public async Task QRY042_ChainCode_GroupByWithHaving()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT UserName, COUNT(*) FROM users GROUP BY UserName HAVING COUNT(*) > 1"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain(".GroupBy(u => u.UserName)"));
        Assert.That(chainCode, Does.Contain(".Having(u => Sql.Count() > 1)"));
    }

    // ── LIKE rejection test ──

    [Test]
    public async Task QRY042_LikeExpression_NoDiagnostic()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(
            ""SELECT * FROM users WHERE UserName LIKE '%test%'"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }

    // ── IN / IS NULL / BETWEEN expression tests ──

    [Test]
    public async Task QRY042_ChainCode_InExpression()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users WHERE UserId IN (@p0, @p1)"", 1, 2);
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain("new[] { 1, 2 }.Contains(u.UserId)"));
    }

    [Test]
    public async Task QRY042_ChainCode_IsNullExpression()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users WHERE Email IS NULL"");
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain("u.Email == null"));
    }

    [Test]
    public async Task QRY042_ChainCode_BetweenExpression()
    {
        var source = ContextAndSchemaPrefix + @"
public class TestService
{
    public void Run(TestDbContext db)
    {
        var results = db.RawSqlAsync<User>(""SELECT * FROM users WHERE UserId BETWEEN @p0 AND @p1"", 1, 10);
    }
}";
        var diagnostics = await GetMigrationDiagnosticsAsync(source);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var chainCode = diagnostics[0].Properties["ChainCode"];
        Assert.That(chainCode, Does.Contain("u.UserId >= 1 && u.UserId <= 10"));
    }
}
