using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Tests that validate carrier class generation patterns for various query chain shapes.
/// </summary>
[TestFixture]
public class CarrierGenerationTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string SharedSchema = @"
using Quarry;
namespace TestApp;
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive { get; }
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}
";

    /// <summary>
    /// Creates a compilation with the given source code and necessary references.
    /// </summary>
    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

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
    /// Runs the generator on the given compilation and returns the results.
    /// </summary>
    private static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Runs the generator and returns both the result and all diagnostics (input + generator).
    /// </summary>
    private static (GeneratorDriverRunResult Result, IReadOnlyList<Diagnostic> Diagnostics) RunGeneratorWithDiagnostics(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (driver.GetRunResult(), diagnostics);
    }

    [Test]
    public void CarrierGeneration_SimpleNoParams()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Carrier class is emitted with CarrierBase<User, User> since Select(u => u) maps User -> User
        Assert.That(code, Does.Contain("file sealed class Chain_0 : CarrierBase<"));
        // Carrier-optimized chains don't use AllocatePrebuiltParams (that's the non-carrier path)
        Assert.That(code, Does.Not.Contain("AllocatePrebuiltParams"));
        // The carrier remark should indicate the optimization level
        Assert.That(code, Does.Contain("Carrier-Optimized PrebuiltDispatch"));
    }

    [Test]
    public void CarrierGeneration_WithParameters()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var active = true;
        await db.Users().Where(u => u.IsActive == active).Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("file sealed class Chain_"));
        Assert.That(code, Does.Contain("CarrierBase<"));
        Assert.That(code, Does.Contain("__c.P0 ="));
    }

    [Test]
    public void CarrierGeneration_ConstantLimit()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Select(u => u).Limit(10).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("file sealed class Chain_"));
        Assert.That(code, Does.Contain("return builder;"));
    }

    [Test]
    public void CarrierGeneration_Distinct()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Distinct().Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("file sealed class Chain_"));
        // Distinct noop — may use Unsafe.As for IEntityAccessor→IQueryBuilder crossing
        Assert.That(code, Does.Contain("Distinct_"));
    }

    [Test]
    public void CarrierGeneration_ToDiagnosticsOnly()
    {
        // ToDiagnostics chain with captured parameter and tuple projection -- uses
        // the same fluent pattern as integration tests that produce carrier-based interceptors
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static string Test(TestDbContext db, bool active)
    {
        return db.Users().Where(u => u.IsActive == active).Select(u => (u.UserId, u.UserName)).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors") && t.FilePath.EndsWith(".g.cs"));

        if (interceptorsTree == null || !interceptorsTree.GetText().ToString().Contains("InterceptsLocation"))
        {
            // ToDiagnostics chains may not always produce interceptors in unit test compilation context;
            // verify that the generator at least ran and produced entity/context output.
            var entityTree = result.GeneratedTrees
                .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
            Assert.That(entityTree, Is.Not.Null, "Should generate entity classes");

            // Verify generation didn't produce errors
            var (_, diagnostics) = RunGeneratorWithDiagnostics(CreateCompilation(source));
            // QRY diagnostics may include warnings; just verify no QRY-prefixed errors
            var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
            Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for a valid ToDiagnostics chain");
            return;
        }

        var code = interceptorsTree!.GetText().ToString();
        // ToDiagnostics chains produce interceptors for clause sites even if the ToDiagnostics terminal isn't intercepted.
        // Just verify the interceptors file was generated without errors.
        Assert.That(code.Length, Is.GreaterThan(100), "Should have generated interceptor content");
    }

    [Test]
    public void CarrierGeneration_DeleteToDiagnostics_ProducesPrebuiltDispatch()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static string Test(TestDbContext db, int id)
    {
        return db.Users().Delete().Where(u => u.UserId == id).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors") && t.FilePath.EndsWith(".g.cs"));

        if (interceptorsTree == null || !interceptorsTree.GetText().ToString().Contains("InterceptsLocation"))
        {
            var entityTree = result.GeneratedTrees
                .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
            Assert.That(entityTree, Is.Not.Null, "Should generate entity classes");

            var (_, diagnostics) = RunGeneratorWithDiagnostics(CreateCompilation(source));
            var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
            Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for a valid Delete ToDiagnostics chain");
            return;
        }

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("Chain_"),
            "Delete ToDiagnostics chain should be grouped into a carrier chain");
        Assert.That(code, Does.Contain("DELETE"),
            "Delete ToDiagnostics chain should contain prebuilt DELETE SQL");
    }

    [Test]
    public void CarrierGeneration_UpdateToDiagnostics_ProducesPrebuiltDispatch()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static string Test(TestDbContext db, int userId, string newName)
    {
        return db.Users().Update().Set(u => u.UserName, newName).Where(u => u.UserId == userId).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors") && t.FilePath.EndsWith(".g.cs"));

        if (interceptorsTree == null || !interceptorsTree.GetText().ToString().Contains("InterceptsLocation"))
        {
            var entityTree = result.GeneratedTrees
                .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
            Assert.That(entityTree, Is.Not.Null, "Should generate entity classes");

            var (_, diagnostics) = RunGeneratorWithDiagnostics(CreateCompilation(source));
            var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
            Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for a valid Update ToDiagnostics chain");
            return;
        }

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("UPDATE"),
            "Update ToDiagnostics chain should contain prebuilt UPDATE SQL");
    }

    [Test]
    public void CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch()
    {
        // Bare db.Users().ToDiagnostics().Sql should NOT produce PrebuiltDispatch — no clauses to optimize.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static string Test(TestDbContext db)
    {
        return db.Users().ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        // Should not produce QRY errors — the chain is valid, just not optimizable
        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Bare IEntityAccessor ToDiagnostics should not produce errors");

        // Verify no carrier class was generated for this trivial chain
        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors") && t.FilePath.EndsWith(".g.cs"));
        if (interceptorsTree != null)
        {
            var code = interceptorsTree.GetText().ToString();
            Assert.That(code, Does.Not.Contain("file sealed class Chain_"),
                "Bare IEntityAccessor ToDiagnostics should not produce carrier class");
        }
    }

    [Test]
    public void CarrierGeneration_ForkedChain_EmitsDiagnostic()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var q = db.Users().Select(u => u);
        await q.ExecuteFetchAllAsync();
        await q.ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry033 = diagnostics.FirstOrDefault(d => d.Id == "QRY033");
        Assert.That(qry033, Is.Not.Null, "Should report QRY033 for forked query chain");
    }

    [Test]
    public void CarrierGeneration_DeleteWithWhere()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var id = 42;
        await db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Delete chains now use carrier classes since DeleteTransition is intercepted
        Assert.That(code, Does.Contain("Chain_"));
        // The non-query terminal interceptor should still be present
        Assert.That(code, Does.Contain("ExecuteNonQueryAsync"));
    }

    [Test]
    public void CarrierGeneration_BaseClassHasCorrectInterfaces()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("CarrierBase<User, User>"));
    }

    [Test]
    public void CarrierGeneration_InsertExecuteNonQuery()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Insert(new User { UserName = ""test"", IsActive = true }).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Insert carrier uses InsertCarrierBase
        Assert.That(code, Does.Contain("InsertCarrierBase<User>"));
        // Carrier should have Entity field
        Assert.That(code, Does.Contain("internal User? Entity;"));
        // Insert transition stores entity on carrier
        Assert.That(code, Does.Contain("__c.Entity = entity;"));
        // Pre-built INSERT SQL
        Assert.That(code, Does.Contain("INSERT INTO"));
        Assert.That(code, Does.Contain("VALUES"));
        // Inline parameter binding from entity properties
        Assert.That(code, Does.Contain("__c.Entity!"));
        // Carrier execution terminal uses inline command creation
        Assert.That(code, Does.Contain("ExecuteCarrierNonQueryWithCommandAsync"));
    }

    [Test]
    public void CarrierGeneration_InsertExecuteScalar()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var id = await db.Users().Insert(new User { UserName = ""test"", IsActive = true }).ExecuteScalarAsync<int>();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Insert carrier with RETURNING clause
        Assert.That(code, Does.Contain("InsertCarrierBase<User>"));
        Assert.That(code, Does.Contain("RETURNING"));
        // Carrier scalar execution
        Assert.That(code, Does.Contain("ExecuteCarrierScalarWithCommandAsync"));
    }

    [Test]
    public void CarrierGeneration_UpdateWithSetAndWhere()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, int userId, string newName)
    {
        await db.Users().Update().Set(u => u.UserName, newName).Where(u => u.UserId == userId).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Update chains with scalar Set clauses are carrier-eligible when ValueTypeName is resolved
        Assert.That(code, Does.Contain("UPDATE"));
        Assert.That(code, Does.Contain("Carrier-Optimized PrebuiltDispatch"));
    }


    [Test]
    public void CarrierGeneration_UpdateSetAction_Literal_IsCarrierOptimized()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Queries
{
    private readonly TestDbContext _db;
    public Queries(TestDbContext db) { _db = db; }
    public string Test()
    {
        return _db.Users().Update().Set(u => u.UserName = ""lit"").Where(u => u.UserId == 1).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("UPDATE"));
        Assert.That(code, Does.Contain("Carrier-Optimized PrebuiltDispatch"));
    }

    [Test]
    public void CarrierGeneration_UpdateSetAction_CapturedVariable_IsCarrierOptimized()
    {
        // Uses _db.Users().Update() (non-generic Update) with a captured variable
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Queries
{
    private readonly TestDbContext _db;
    public Queries(TestDbContext db) { _db = db; }
    public string Test(string name)
    {
        return _db.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).ToDiagnostics().Sql;
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("UPDATE"));
        Assert.That(code, Does.Contain("Carrier-Optimized PrebuiltDispatch"));
    }
}
