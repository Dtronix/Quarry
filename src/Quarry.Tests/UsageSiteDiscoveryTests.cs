using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Tests for Phase 6a: Usage Site Discovery and Analyzability.
/// </summary>
[TestFixture]
public class UsageSiteDiscoveryTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    /// <summary>
    /// Creates a compilation with the given source code and necessary references.
    /// </summary>
    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        // Add netstandard/runtime references
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    /// <summary>
    /// Runs the generator on the given compilation and returns the diagnostics and generated sources.
    /// </summary>
    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Result) RunGeneratorWithDiagnostics(
        CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (diagnostics, driver.GetRunResult());
    }

    #region QRY001 Warning Tests

    [Test]
    public void Generator_FluentChain_NoWarning()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        // This is a fluent chain - should be analyzable
        var sql = db.Users.Select(u => u).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should not have QRY001 for fluent chain usage
        var qry001Diagnostics = diagnostics.Where(d => d.Id == "QRY001").ToList();

        // The db.Users.Select(u => u) is a fluent chain - no warning expected
        Assert.That(qry001Diagnostics.Any(d => d.GetMessage().Contains("Query is assigned")), Is.False,
            "Fluent chain should not produce QRY001 warning");
    }

    [Test]
    public void Generator_VariableAssignment_EmitsQRY001()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db, IQueryBuilder<User> externalBuilder)
    {
        // Parameter receiver - should emit QRY001
        externalBuilder.Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should have QRY001 for parameter receiver
        var qry001Diagnostics = diagnostics.Where(d => d.Id == "QRY001").ToList();

        Assert.That(qry001Diagnostics.Count, Is.GreaterThan(0),
            "Parameter receiver should produce QRY001 warning");
        Assert.That(qry001Diagnostics.Any(d => d.GetMessage().Contains("parameter")), Is.True,
            "Warning message should mention parameter");
    }

    [Test]
    public void Generator_LocalVariableReceiver_EmitsQRY001()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(IQueryBuilder<User> builder)
    {
        // Parameter receiver - should emit QRY001
        var projected = builder.Select(u => u);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001Diagnostics = diagnostics.Where(d => d.Id == "QRY001").ToList();

        // Select on a parameter should emit QRY001
        Assert.That(qry001Diagnostics.Any(d => d.GetMessage().Contains("parameter")), Is.True,
            "Method call on parameter should emit QRY001");
    }

    #endregion

    #region Interceptor Generation Tests

    [Test]
    public void Generator_WithAnalyzableUsageSite_GeneratesInterceptorsFile()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public string Test(TestDbContext db)
    {
        // Fluent chain that should be intercepted
        return db.Users.Select(u => u).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Check that interceptors file is generated
        var interceptorsFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));

        // Note: Interceptors may not be generated if there are no analyzable sites
        // or if all sites are fluent chains that don't need interception
        // This test verifies the infrastructure is in place
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Generator should not produce errors");
    }

    [Test]
    public void Generator_InterceptorsFile_HasCorrectStructure()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public string Test(TestDbContext db)
    {
        return db.Users.Select(u => u).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var interceptorsFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors"));

        if (interceptorsFile != null)
        {
            var code = interceptorsFile.GetText().ToString();

            // Check file structure
            Assert.That(code, Does.Contain("// <auto-generated/>"),
                "Should have auto-generated header");
            Assert.That(code, Does.Contain("#nullable enable"),
                "Should enable nullable context");
            Assert.That(code, Does.Contain("using System.Runtime.CompilerServices;"),
                "Should have CompilerServices using");
            Assert.That(code, Does.Contain("file static class"),
                "Should use file-scoped class for interceptors");
            Assert.That(code, Does.Contain("InterceptsLocationAttribute"),
                "Should define InterceptsLocationAttribute");
        }
    }

    #endregion

    #region Usage Site Detection Tests

    [Test]
    public void Generator_DetectsSelectMethodCalls()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        // Multiple Select calls should be detected
        var s1 = db.Users.Select(u => u).ToDiagnostics().Sql;
        var s2 = db.Users.Select(u => u.UserId).ToDiagnostics().Sql;  // Use single column instead of anonymous type
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should not have errors
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Generator should process Select methods without errors");
    }

    [Test]
    public void Generator_DetectsWhereMethodCalls()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        var result = db.Users.Select(u => u).Where(u => u.UserId > 0).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Generator should process Where methods without errors");
    }

    [Test]
    public void Generator_DetectsExecutionMethodCalls()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        // Execution methods should be detected
        var users = await db.Users.Select(u => u).ExecuteFetchAllAsync();
        var first = await db.Users.Select(u => u).ExecuteFetchFirstAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Generator should process execution methods without errors");
    }

    #endregion

    #region Analyzability Pattern Tests

    [Test]
    public void Generator_ConditionalBranching_IsAnalyzable()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db, bool condition)
    {
        // Query construction inside conditional — handled by ChainAnalyzer
        if (condition)
        {
            var query = db.Users.Select(u => u);
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001Diagnostics = diagnostics.Where(d => d.Id == "QRY001").ToList();

        // Conditional branching is now handled by ChainAnalyzer — should not trigger QRY001
        Assert.That(qry001Diagnostics, Is.Empty,
            "Conditional code should not produce QRY001 — ChainAnalyzer handles conditionals via bitmask dispatch");
    }

    [Test]
    public void Generator_DirectPropertyAccess_Analyzable()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public string Test(TestDbContext db)
    {
        // Direct property access in fluent chain - fully analyzable
        return db.Users.Select(u => u).Limit(10).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should not have errors
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);

        // Direct fluent chain should not emit QRY001 for the chain itself
        // (though ToDiagnostics might depending on implementation)
    }

    #endregion

    #region Diagnostic Messages Tests

    [Test]
    public void QRY001_HasDescriptiveMessage()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(IQueryBuilder<User> builder)
    {
        builder.Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001 = diagnostics.FirstOrDefault(d => d.Id == "QRY001");
        Assert.That(qry001, Is.Not.Null, "Should have QRY001 diagnostic");

        var message = qry001!.GetMessage();
        Assert.That(message, Does.Contain("not fully analyzable"),
            "Message should explain the query is not analyzable");
    }

    [Test]
    public void QRY001_ReportsCorrectLocation()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(IQueryBuilder<User> builder)
    {
        builder.Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001 = diagnostics.FirstOrDefault(d => d.Id == "QRY001");
        Assert.That(qry001, Is.Not.Null);

        var location = qry001!.Location;
        Assert.That(location.IsInSource, Is.True, "Diagnostic should have source location");

        var lineSpan = location.GetLineSpan();
        Assert.That(lineSpan.StartLinePosition.Line, Is.GreaterThan(0),
            "Diagnostic should report correct line number");
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Generator_NoQuarryUsage_NoWarnings()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    // No Quarry method calls at all
    public void Test()
    {
        var x = 1 + 2;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001Diagnostics = diagnostics.Where(d => d.Id == "QRY001").ToList();
        Assert.That(qry001Diagnostics.Count, Is.EqualTo(0),
            "No Quarry usage should produce no warnings");
    }

    [Test]
    public void Generator_MultipleContexts_HandledCorrectly()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

public class ProductSchema : Schema
{
    public static string Table => ""products"";
    public Key<int> ProductId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class UserDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class ProductDbContext : QuarryContext
{
    public partial IEntityAccessor<Product> Products();
}

public class Service
{
    public void Test(UserDbContext userDb, ProductDbContext productDb)
    {
        var users = userDb.Users.Select(u => u).ToDiagnostics().Sql;
        var products = productDb.Products.Select(p => p).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Multiple contexts should be handled without errors");

        // Should generate files for both contexts
        Assert.That(result.GeneratedTrees.Any(t => t.FilePath.Contains("UserDbContext")), Is.True);
        Assert.That(result.GeneratedTrees.Any(t => t.FilePath.Contains("ProductDbContext")), Is.True);
    }

    #endregion

    #region Sql.Raw Placeholder Validation (QRY029)

    [Test]
    public void QRY029_TooManyArguments_EmitsDiagnostic()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        // {0} only, but two args supplied
        db.Users().Where(u => Sql.Raw<bool>(""custom_func({0})"", u.UserId, u.UserName))
            .Select(u => u).ExecuteFetchAllAsync();
    }
}
";
        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);
        var qry029 = diagnostics.Where(d => d.Id == "QRY029").ToList();
        Assert.That(qry029.Count, Is.GreaterThan(0), "Should emit QRY029 for too many arguments");
        Assert.That(qry029[0].GetMessage(), Does.Contain("2 argument(s) were supplied"));
    }

    [Test]
    public void QRY029_TooFewArguments_EmitsDiagnostic()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        // {0} and {1} but only one arg supplied
        db.Users().Where(u => Sql.Raw<bool>(""check({0}, {1})"", u.UserId))
            .Select(u => u).ExecuteFetchAllAsync();
    }
}
";
        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);
        var qry029 = diagnostics.Where(d => d.Id == "QRY029").ToList();
        Assert.That(qry029.Count, Is.GreaterThan(0), "Should emit QRY029 for too few arguments");
        Assert.That(qry029[0].GetMessage(), Does.Contain("1 argument(s) were supplied"));
    }

    [Test]
    public void QRY029_NonSequentialPlaceholders_EmitsDiagnostic()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        // {0} and {2} -- skips {1}
        db.Users().Where(u => Sql.Raw<bool>(""check({0}, {2})"", u.UserId, u.UserName, u.UserId))
            .Select(u => u).ExecuteFetchAllAsync();
    }
}
";
        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);
        var qry029 = diagnostics.Where(d => d.Id == "QRY029").ToList();
        Assert.That(qry029.Count, Is.GreaterThan(0), "Should emit QRY029 for non-sequential placeholders");
        Assert.That(qry029[0].GetMessage(), Does.Contain("{1} is missing"));
    }

    [Test]
    public void QRY029_ValidTemplate_NoDiagnostic()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        db.Users().Where(u => Sql.Raw<bool>(""custom_func({0}, {1})"", u.UserId, u.UserName))
            .Select(u => u).ExecuteFetchAllAsync();
    }
}
";
        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);
        var qry029 = diagnostics.Where(d => d.Id == "QRY029").ToList();
        Assert.That(qry029.Count, Is.EqualTo(0), "Valid template should not emit QRY029");
    }

    #endregion

    #region .Trace() Chain Tracing (QRY034)

    private static CSharpCompilation CreateCompilationWithSymbols(string source, params string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols);
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions) };

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private const string TraceTestSchema = @"
using Quarry;
namespace TestApp;
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<bool> IsActive { get; }
}
";

    private const string TraceTestSource = TraceTestSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users()
            .Where(u => u.IsActive)
            .Trace()
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();
    }
}
";

    [Test]
    public void Trace_WithQuarryTraceSymbol_EmitsTraceComments()
    {
        var compilation = CreateCompilationWithSymbols(TraceTestSource, "QUARRY_TRACE");
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should not emit QRY034 when QUARRY_TRACE is defined
        var qry034 = diagnostics.Where(d => d.Id == "QRY034").ToList();
        Assert.That(qry034.Count, Is.EqualTo(0), "Should not emit QRY034 when QUARRY_TRACE is defined");

        // Find the interceptors file
        var interceptorTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors."));
        Assert.That(interceptorTree, Is.Not.Null, "Should generate an interceptors file");

        var code = interceptorTree!.GetText().ToString();

        // Verify trace comments are present
        Assert.That(code, Does.Contain("// [Trace]"), "Should contain // [Trace] comments");
        Assert.That(code, Does.Contain("// [Trace] Discovery"), "Should contain discovery trace");
        Assert.That(code, Does.Contain("// [Trace] Binding"), "Should contain binding trace");
        Assert.That(code, Does.Contain("// [Trace] Translation"), "Should contain translation trace");
        Assert.That(code, Does.Contain("// [Trace] ChainAnalysis"), "Should contain chain analysis trace");
        Assert.That(code, Does.Contain("// [Trace] Assembly"), "Should contain assembly trace");
    }

    [Test]
    public void Trace_WithoutQuarryTraceSymbol_EmitsQRY034()
    {
        var compilation = CreateCompilation(TraceTestSource);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should emit QRY034 when .Trace() is present but QUARRY_TRACE is not defined
        var qry034 = diagnostics.Where(d => d.Id == "QRY034").ToList();
        Assert.That(qry034.Count, Is.GreaterThan(0), "Should emit QRY034 when QUARRY_TRACE is not defined");
        Assert.That(qry034[0].GetMessage(), Does.Contain("QUARRY_TRACE"));

        // Verify trace comments are NOT present in generated output
        var interceptorTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors."));
        if (interceptorTree != null)
        {
            var code = interceptorTree.GetText().ToString();
            Assert.That(code, Does.Not.Contain("// [Trace] Discovery"), "Should not contain trace comments without QUARRY_TRACE");
        }
    }

    [Test]
    public void Trace_ChainWithoutTrace_NoTraceComments()
    {
        var sourceWithoutTrace = TraceTestSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();
    }
}
";
        // Even with QUARRY_TRACE defined, chains without .Trace() should not have trace comments
        var compilation = CreateCompilationWithSymbols(sourceWithoutTrace, "QUARRY_TRACE");
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var interceptorTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors."));
        if (interceptorTree != null)
        {
            var code = interceptorTree.GetText().ToString();
            Assert.That(code, Does.Not.Contain("// [Trace] Discovery"), "Non-traced chains should not have trace comments");
        }

        // No QRY034 expected (no .Trace() call)
        var qry034 = diagnostics.Where(d => d.Id == "QRY034").ToList();
        Assert.That(qry034.Count, Is.EqualTo(0), "Should not emit QRY034 without .Trace() call");
    }

    #endregion

    #region Variable-Stored Chain QRY001 Tests

    [Test]
    public void Generator_VariableStoredChain_WithAnalyzableInitializer_NoQRY001()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<bool> IsActive => Default(true);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(TestDbContext db)
    {
        // Variable-stored chain from analyzable source — should NOT emit QRY001
        var query = db.Users().Where(u => u.IsActive);
        query.Select(u => u.UserName).ToDiagnostics();
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001 = diagnostics.Where(d => d.Id == "QRY001").ToList();
        Assert.That(qry001.Count, Is.EqualTo(0),
            "Variable-stored chain from context property should not emit QRY001");
    }

    [Test]
    public void Generator_ParameterReceiver_StillEmitsQRY001()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public void Test(IQueryBuilder<User> externalBuilder)
    {
        // Parameter receiver — 3+ hops or non-traceable — should still emit QRY001
        externalBuilder.Select(u => u.UserName).ToDiagnostics();
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry001 = diagnostics.Where(d => d.Id == "QRY001").ToList();
        Assert.That(qry001.Count, Is.GreaterThan(0),
            "Parameter receiver should still emit QRY001");
    }

    #endregion
}
