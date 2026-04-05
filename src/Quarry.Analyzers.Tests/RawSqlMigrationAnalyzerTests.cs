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

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public TestDbContext(IDbConnection connection) : base(connection) { }
    public partial IEntityAccessor<User> Users();
}

public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
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
}
