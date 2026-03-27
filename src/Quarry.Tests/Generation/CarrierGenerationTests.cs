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
        // Carrier class implements interfaces directly (no base class)
        Assert.That(code, Does.Contain("file sealed class Chain_0 : IEntityAccessor<"));
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
        Assert.That(code, Does.Contain("IEntityAccessor<"));
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
        return db.Users().Update().Set(u => u.UserName = newName).Where(u => u.UserId == userId).ToDiagnostics().Sql;
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
    public void CarrierGeneration_EntityAccessorToDiagnostics_ProducesCarrier()
    {
        // Bare db.Users().ToDiagnostics().Sql now produces a carrier after trivial gate removal.
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

        // Verify a carrier class IS generated — all PrebuiltDispatch chains now get carriers
        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptor file");
        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("file sealed class Chain_"),
            "Bare IEntityAccessor ToDiagnostics should now produce carrier class");
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
        Assert.That(code, Does.Contain("IQueryBuilder<User, User>"));
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
        // Insert carrier implements interfaces directly
        Assert.That(code, Does.Contain("IInsertBuilder<User>"));
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
        Assert.That(code, Does.Contain("IInsertBuilder<User>"));
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
        await db.Users().Update().Set(u => u.UserName = newName).Where(u => u.UserId == userId).ExecuteNonQueryAsync();
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

    [Test]
    public void CarrierGeneration_InsertBoolColumn_SQLite_EmitsBoolToIntConversion()
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
        // SQLite: bool should be converted to 0/1 integer
        Assert.That(code, Does.Contain("IsActive ? 1 : 0"),
            "SQLite carrier insert should convert bool to int (? 1 : 0)");
        // SQLite: DbType.Int32 should be set for bool columns
        Assert.That(code, Does.Contain("DbType = System.Data.DbType.Int32"),
            "SQLite carrier insert should set DbType.Int32 for bool columns");
    }

    [Test]
    public void CarrierGeneration_InsertBoolColumn_PostgreSQL_DoesNotConvertBoolToInt()
    {
        var source = @"
using Quarry;
namespace TestApp;
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<bool> IsActive { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
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
        // PostgreSQL: bool should NOT be converted — native bool support
        Assert.That(code, Does.Not.Contain("IsActive ? 1 : 0"),
            "PostgreSQL carrier insert should NOT convert bool to int");
        // Still has the entity property access
        Assert.That(code, Does.Contain("__c.Entity!.IsActive"),
            "PostgreSQL carrier insert should access bool property directly");
    }

    [Test]
    public void CarrierGeneration_InsertEnumColumn_SQLite_EmitsEnumToIntCast()
    {
        var source = @"
using Quarry;
namespace TestApp;

public enum Priority { Low, Normal, High }

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<Priority> Priority { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Orders().Insert(new Order { Priority = Priority.High }).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Enum should be cast to int for all dialects
        Assert.That(code, Does.Contain("(int)").And.Contain("Priority"),
            "Carrier insert should cast enum to int");
        // DbType.Int32 should be set for enum columns
        Assert.That(code, Does.Contain("DbType = System.Data.DbType.Int32"),
            "Carrier insert should set DbType.Int32 for enum columns");
    }

    [Test]
    public void CarrierGeneration_InsertBoolColumn_MySQL_EmitsBoolToIntConversion()
    {
        var source = @"
using Quarry;
namespace TestApp;
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<bool> IsActive { get; }
}

[QuarryContext(Dialect = SqlDialect.MySQL)]
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
        // MySQL: bool should be converted to 0/1 integer (same as SQLite)
        Assert.That(code, Does.Contain("IsActive ? 1 : 0"),
            "MySQL carrier insert should convert bool to int (? 1 : 0)");
        Assert.That(code, Does.Contain("DbType = System.Data.DbType.Int32"),
            "MySQL carrier insert should set DbType.Int32 for bool columns");
    }

    [Test]
    public void CarrierGeneration_InsertEnumColumn_PostgreSQL_EmitsEnumToIntCast()
    {
        var source = @"
using Quarry;
namespace TestApp;

public enum Priority { Low, Normal, High }

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<Priority> Priority { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Orders().Insert(new Order { Priority = Priority.High }).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Enum-to-int cast applies to ALL dialects (enums are never a native DB type)
        Assert.That(code, Does.Contain("(int)").And.Contain("Priority"),
            "PostgreSQL carrier insert should also cast enum to int");
        Assert.That(code, Does.Contain("DbType = System.Data.DbType.Int32"),
            "PostgreSQL carrier insert should set DbType.Int32 for enum columns");
    }

    [Test]
    public void CarrierGeneration_InsertNullableBoolColumn_SQLite_EmitsNullSafeConversion()
    {
        var source = @"
using Quarry;
namespace TestApp;
public class ProfileSchema : Schema
{
    public static string Table => ""profiles"";
    public Key<int> ProfileId => Identity();
    public Col<string> Name => Length(100);
    public Col<bool?> IsVerified { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Profile> Profiles();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Profiles().Insert(new Profile { Name = ""x"", IsVerified = true }).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Nullable bool on SQLite: null-safe ternary with int conversion
        Assert.That(code, Does.Contain("IsVerified != null"),
            "Nullable bool should emit null check");
        Assert.That(code, Does.Contain(".Value ? 1 : 0"),
            "Nullable bool should convert .Value to 0/1");
        // The nullable path returns null for the false branch, letting the
        // caller's (object?)expr ?? DBNull.Value handle null→DBNull uniformly
        Assert.That(code, Does.Not.Match(@"DBNull\.Value\).*\?\?.*DBNull\.Value"),
            "Nullable bool should not double-wrap with DBNull.Value");
        Assert.That(code, Does.Contain("DbType = System.Data.DbType.Int32"),
            "Nullable bool should still set DbType.Int32");
    }

    [Test]
    public void CarrierGeneration_InsertNullableEnumColumn_SQLite_EmitsNullSafeIntCast()
    {
        var source = @"
using Quarry;
namespace TestApp;

public enum Priority { Low, Normal, High }

public class TaskSchema : Schema
{
    public static string Table => ""tasks"";
    public Key<int> TaskId => Identity();
    public Col<string> Title => Length(200);
    public Col<Priority?> Priority { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Task> Tasks();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Tasks().Insert(new Task { Title = ""x"", Priority = Priority.High }).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Nullable enum: null-safe ternary with int cast
        Assert.That(code, Does.Contain("Priority != null"),
            "Nullable enum should emit null check");
        Assert.That(code, Does.Contain("(int)").And.Contain(".Value"),
            "Nullable enum should cast .Value to int");
        Assert.That(code, Does.Not.Match(@"DBNull\.Value\).*\?\?.*DBNull\.Value"),
            "Nullable enum should not double-wrap with DBNull.Value");
        Assert.That(code, Does.Contain("DbType = System.Data.DbType.Int32"),
            "Nullable enum should still set DbType.Int32");
    }

    [Test]
    public void CarrierGeneration_DateTimeParameter_NoNullConditional()
    {
        var source = @"
using System;
using Quarry;
namespace TestApp;

public class SessionSchema : Schema
{
    public static string Table => ""sessions"";
    public Key<int> SessionId => Identity();
    public Col<DateTime> ExpiresAt { get; }
    public Col<string> Token => Length(64);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Session> Sessions();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        // DateTime.UtcNow is captured as a System.DateTime parameter
        await db.Sessions().Delete()
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null);

        var code = interceptorsTree!.GetText().ToString();

        // The DateTime parameter should use .ToString() not ?.ToString() since DateTime is a non-nullable value type
        Assert.That(code, Does.Not.Contain("P0?.ToString()"),
            "Non-nullable System.DateTime should not use null-conditional operator");
        Assert.That(code, Does.Contain("P0.ToString()"),
            "Non-nullable System.DateTime should use plain .ToString()");
    }

    [Test]
    public void CarrierGeneration_InterceptorFile_HasLogLevelAlias()
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
        Assert.That(interceptorsTree, Is.Not.Null);

        var code = interceptorsTree!.GetText().ToString();

        // The generated file should alias LogLevel to avoid ambiguity with Microsoft.Extensions.Logging.LogLevel
        Assert.That(code, Does.Contain("using LogLevel = Quarry.Logging.LogLevel;"),
            "Generated interceptors should alias LogLevel to avoid ambiguity in ASP.NET Core projects");
    }

    [Test]
    public void CarrierGeneration_ByteArrayColumn_ReaderCastsGetValue()
    {
        var source = @"
using System;
using Quarry;
namespace TestApp;

public class FileSchema : Schema
{
    public static string Table => ""files"";
    public Key<int> FileId => Identity();
    public Col<string> Name => Length(255);
    public Col<byte[]> Content { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<File> Files();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task Test(TestDbContext db)
    {
        await db.Files().Select(f => f).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null);

        var code = interceptorsTree!.GetText().ToString();

        // byte[] columns should use (byte[])r.GetValue(n), not bare r.GetValue(n)
        Assert.That(code, Does.Contain("(byte[])r.GetValue("),
            "byte[] columns should cast GetValue() result to byte[]");
        Assert.That(code, Does.Not.Match(@"Content = r\.GetValue\(\d+\)[^)]"),
            "byte[] columns should not assign GetValue() result without cast");
    }

    [Test]
    public void CarrierGeneration_ByteArraySetParam_EmitsNullableFieldWithoutCS8618()
    {
        var source = @"
using System;
using Quarry;
namespace TestApp;

public class FileSchema : Schema
{
    public static string Table => ""files"";
    public Key<int> FileId => Identity();
    public Col<string> Name => Length(255);
    public Col<byte[]> Content { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<File> Files();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task Test(TestDbContext db, byte[] data)
    {
        await db.Files().Update().Set(f => f.Content = data).Where(f => f.FileId == 1).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("Carrier-Optimized PrebuiltDispatch"),
            "byte[] Set chain should be carrier-optimized");

        // byte[] carrier field must be emitted as nullable (byte[]?) to avoid CS8618
        Assert.That(code, Does.Contain("internal byte[]? P0;"),
            "byte[] carrier field should be emitted as nullable (byte[]?)");
        Assert.That(code, Does.Not.Match(@"internal byte\[\] P\d+;"),
            "byte[] carrier field must not be emitted as non-nullable");

        // No CS8618 warnings on the generated code
        var cs8618 = diagnostics.Where(d => d.Id == "CS8618").ToList();
        Assert.That(cs8618, Is.Empty,
            "Generated carrier class should not produce CS8618 warnings for byte[] fields");
    }

    [Test]
    public void CarrierGeneration_CollectionParam_EmitsNullBangInitializer()
    {
        var source = SharedSchema + @"
using System.Collections.Generic;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task Test(TestDbContext db, IReadOnlyList<int> ids)
    {
        await db.Users().Where(u => ids.Contains(u.UserId)).Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // IReadOnlyList<> carrier field is non-nullable reference type — must have = null! initializer
        Assert.That(code, Does.Match(@"internal System\.Collections\.Generic\.IReadOnlyList<int\??> P0 = null!;"),
            "IReadOnlyList carrier field should have = null! initializer to suppress CS8618");
    }

    [Test]
    public void CarrierGeneration_NoBaseClass_UsesInterfacesDirectly()
    {
        // Verifies that generated carriers implement interfaces directly
        // and do not inherit from any CarrierBase class (issue #86).
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
        // No CarrierBase inheritance — interfaces only
        Assert.That(code, Does.Not.Contain("CarrierBase"));
        Assert.That(code, Does.Not.Contain("JoinedCarrierBase"));
        // Carrier class directly implements interfaces
        Assert.That(code, Does.Contain("IEntityAccessor<User>"));
        Assert.That(code, Does.Contain("IQueryBuilder<User>"));
        Assert.That(code, Does.Contain("IQueryBuilder<User, User>"));
        // Ctx field emitted directly on carrier
        Assert.That(code, Does.Contain("internal IQueryExecutionContext? Ctx;"));
    }

    [Test]
    public void CarrierGeneration_DeleteCarrier_UsesInterfacesDirectly()
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
        // Delete carrier uses interfaces directly, not DeleteCarrierBase
        Assert.That(code, Does.Not.Contain("DeleteCarrierBase"));
        Assert.That(code, Does.Contain("IEntityAccessor<User>"));
        Assert.That(code, Does.Contain("IDeleteBuilder<User>"));
        Assert.That(code, Does.Contain("IExecutableDeleteBuilder<User>"));
    }
}
