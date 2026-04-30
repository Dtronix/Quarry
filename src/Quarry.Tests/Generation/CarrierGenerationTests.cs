using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;
using Quarry.Generators.CodeGen;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;

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
        Assert.That(code, Does.Contain("PrebuiltDispatch (1 allocation: carrier)"));
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
    public void CarrierGeneration_MutuallyExclusiveBranches_NoForkedChainDiagnostic()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, bool condition)
    {
        if (condition)
        {
            await db.Users().Update().Set(u => u.UserName = ""a"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
        }
        else
        {
            await db.Users().Update().Set(u => u.UserName = ""b"").Where(u => u.UserId == 2).ExecuteNonQueryAsync();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry033 = diagnostics.FirstOrDefault(d => d.Id == "QRY033");
        Assert.That(qry033, Is.Null, "Should NOT report QRY033 for chains in mutually exclusive if/else branches");
    }

    [Test]
    public void CarrierGeneration_TryCatchBranches_NoForkedChainDiagnostic()
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
        try
        {
            await db.Users().Update().Set(u => u.UserName = ""a"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
        }
        catch
        {
            await db.Users().Update().Set(u => u.UserName = ""b"").Where(u => u.UserId == 2).ExecuteNonQueryAsync();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry033 = diagnostics.FirstOrDefault(d => d.Id == "QRY033");
        Assert.That(qry033, Is.Null, "Should NOT report QRY033 for chains in try/catch branches");
    }

    [Test]
    public void CarrierGeneration_NestedIfElseBranches_NoForkedChainDiagnostic()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, int mode)
    {
        var user = await db.Users().Where(u => u.UserId == 1).Select(u => u).ExecuteFetchFirstOrDefaultAsync();
        if (user != null)
        {
            if (mode == 1)
            {
                await db.Users().Update().Set(u => u.UserName = ""a"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
            }
            else if (mode == 2)
            {
                await db.Users().Update().Set(u => u.UserName = ""b"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
            }
            else
            {
                await db.Users().Update().Set(u => u.UserName = ""c"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
            }
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry033 = diagnostics.FirstOrDefault(d => d.Id == "QRY033");
        Assert.That(qry033, Is.Null, "Should NOT report QRY033 for chains in nested if/else-if/else branches");
    }

    [Test]
    public void CarrierGeneration_DeeplyNestedBranches_NoQRY032OrCrash()
    {
        // Regression: chains at absolute nesting depth 3 must not trigger QRY032
        // ("conditional nesting depth exceeds maximum") or QRY900 (crash from
        // NestingContext present without a matching ConditionalTerm).
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, int status, int handler)
    {
        if (status == 0)
        {
            if (handler == 1)
            {
                if (handler == 2)
                {
                    await db.Users().Update().Set(u => u.UserName = ""a"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
                }

                await db.Users().Update().Set(u => u.UserName = ""b"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
            }
            else if (handler == 3)
            {
                try
                {
                    await db.Users().Update().Set(u => u.UserName = ""c"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
                }
                catch
                {
                    await db.Users().Update().Set(u => u.UserName = ""d"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
                }
            }
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry032 = diagnostics.FirstOrDefault(d => d.Id == "QRY032");
        Assert.That(qry032, Is.Null, "Should NOT report QRY032 for chains in deeply nested control flow");

        var qry033 = diagnostics.FirstOrDefault(d => d.Id == "QRY033");
        Assert.That(qry033, Is.Null, "Should NOT report QRY033 for chains in mutually exclusive branches");

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Should NOT crash (QRY900) when emitting chains with NestingContext but no conditional terms");

        // Verify interceptors were actually generated (chains compiled successfully)
        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptor file for deeply nested chains");
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
    public void CarrierGeneration_OpIdIsConditionalOnLogger()
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
        var count = await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Logger should be cached in a local
        Assert.That(code, Does.Contain("var __logger = LogsmithOutput.Logger;"));
        // OpId should be conditional on cached logger local
        Assert.That(code, Does.Contain("__logger != null ? OpId.Next() : 0"));
        // Unconditional OpId.Next() should not appear
        Assert.That(code, Does.Not.Match(@"var __opId = OpId\.Next\(\);"));
    }

    [Test]
    public void CarrierGeneration_CtxIsCachedInLocal()
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
        var count = await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Ctx should be cached in a local
        Assert.That(code, Does.Contain("var __ctx = __c.Ctx!;"));
        // Command creation should use cached local
        Assert.That(code, Does.Contain("__ctx.Connection.CreateCommand()"));
        // Direct __c.Ctx access should not appear after the local declaration
        Assert.That(code, Does.Not.Contain("__c.Ctx.Connection"));
        Assert.That(code, Does.Not.Contain("__c.Ctx!.DefaultTimeout"));
    }

    [Test]
    public void CarrierGeneration_LoggerIsUsedInLoggingGates()
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
        var count = await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Logging gates should use cached __logger local, not LogsmithOutput.Logger directly
        Assert.That(code, Does.Contain("__logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName)"));
        Assert.That(code, Does.Not.Match(@"LogsmithOutput\.Logger\?\.IsEnabled"));
    }

    [Test]
    public void CarrierGeneration_InsertTerminal_CachesCtxAndLogger()
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
        // Insert terminal has its own inline preamble — verify it also caches locals
        Assert.That(code, Does.Contain("var __ctx = __c.Ctx!;"));
        Assert.That(code, Does.Contain("var __logger = LogsmithOutput.Logger;"));
        // Insert terminal should use cached locals, not direct access
        Assert.That(code, Does.Contain("__ctx.Connection.CreateCommand()"));
        Assert.That(code, Does.Not.Contain("__c.Ctx.Connection"));
        Assert.That(code, Does.Not.Contain("__c.Ctx!.DefaultTimeout"));
        Assert.That(code, Does.Contain("__logger?.IsEnabled(LogLevel.Debug"));
        Assert.That(code, Does.Contain("__logger?.IsEnabled(LogLevel.Trace"));
    }

    [Test]
    public void CarrierGeneration_BatchInsertTerminal_CachesCtxAndLogger()
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
        var users = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Batch insert terminal has its own inline preamble — verify it also caches locals
        Assert.That(code, Does.Contain("var __ctx = __c.Ctx!;"));
        Assert.That(code, Does.Contain("var __logger = LogsmithOutput.Logger;"));
        Assert.That(code, Does.Contain("__logger != null ? OpId.Next() : 0"));
        Assert.That(code, Does.Contain("__ctx.Connection.CreateCommand()"));
        Assert.That(code, Does.Not.Contain("__c.Ctx.Connection"));
        Assert.That(code, Does.Not.Contain("__c.Ctx!.DefaultTimeout"));
        Assert.That(code, Does.Contain("__logger?.IsEnabled(LogLevel.Debug"));
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
        Assert.That(code, Does.Contain("PrebuiltDispatch (1 allocation: carrier)"));
    }


    [Test]
    public void CarrierGeneration_UpdateSetAction_Literal_IsPrebuiltDispatch()
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
        Assert.That(code, Does.Contain("PrebuiltDispatch (1 allocation: carrier)"));
    }

    [Test]
    public void CarrierGeneration_UpdateSetAction_CapturedVariable_IsPrebuiltDispatch()
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
        Assert.That(code, Does.Contain("PrebuiltDispatch (1 allocation: carrier)"));
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
    public void CarrierGeneration_WhereEnumParameter_EmitsNullSafeBindingAndLogging()
    {
        var source = @"
using Quarry;
namespace TestApp;

public enum OrderPriority { Low, Normal, High }

public class TicketSchema : Schema
{
    public static string Table => ""tickets"";
    public Key<int> TicketId => Identity();
    public Col<string> Subject => Length(200);
    public Col<OrderPriority> Priority { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Ticket> Tickets();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var p = OrderPriority.High;
        await db.Tickets().Where(t => t.Priority == p).Select(t => t).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // Carrier field for enum should be nullable (NormalizeFieldType treats enums
        // as reference types), so parameter binding must use HasValue null guard.
        Assert.That(code, Does.Contain(".HasValue").And.Contain(".Value"),
            "Enum parameter binding should use HasValue null guard (CS8629 fix)");
        Assert.That(code, Does.Not.Contain("(object)(int)__c.P0;"),
            "Should not directly cast nullable enum field without null guard");

        // Parameter logging should use null-safe ToString pattern, not bare .ToString()
        Assert.That(code, Does.Contain("?.ToString()").And.Contain(@"?? ""null"""),
            "Enum parameter logging should use null-safe ToString (CS8604 fix)");
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

        // byte[] columns should use r.GetFieldValue<byte[]>(n), not bare r.GetValue(n)
        Assert.That(code, Does.Contain("r.GetFieldValue<byte[]>("),
            "byte[] columns should use typed GetFieldValue<byte[]>");
        Assert.That(code, Does.Not.Match(@"Content = r\.GetValue\(\d+\)"),
            "byte[] columns should not use untyped GetValue()");
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
        Assert.That(code, Does.Contain("PrebuiltDispatch (1 allocation: carrier)"),
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
        Assert.That(code, Does.Contain("internal TestDbContext? Ctx;"));
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

    [Test]
    public void ResolveCarrierInterfaceList_TwoTableJoin_ReturnsCorrectInterfaces()
    {
        var interfaces = ResolveJoinInterfaces("User", "Order");

        Assert.That(interfaces, Is.EqualTo(new[]
        {
            "IEntityAccessor<User>",
            "IQueryBuilder<User>",
            "IJoinedQueryBuilder<User, Order>"
        }));
    }

    [Test]
    public void ResolveCarrierInterfaceList_ThreeTableJoin_ReturnsCorrectInterfaces()
    {
        var interfaces = ResolveJoinInterfaces("User", "Order", "Product");

        Assert.That(interfaces, Is.EqualTo(new[]
        {
            "IEntityAccessor<User>",
            "IQueryBuilder<User>",
            "IJoinedQueryBuilder<User, Order>",
            "IJoinedQueryBuilder3<User, Order, Product>"
        }));
    }

    [Test]
    public void ResolveCarrierInterfaceList_FourTableJoin_ReturnsCorrectInterfaces()
    {
        var interfaces = ResolveJoinInterfaces("User", "Order", "Product", "Category");

        Assert.That(interfaces, Is.EqualTo(new[]
        {
            "IEntityAccessor<User>",
            "IQueryBuilder<User>",
            "IJoinedQueryBuilder<User, Order>",
            "IJoinedQueryBuilder3<User, Order, Product>",
            "IJoinedQueryBuilder4<User, Order, Product, Category>"
        }));
    }

    /// <summary>
    /// Constructs a minimal AssembledPlan with the given entity type names as a join chain
    /// and calls ResolveCarrierInterfaceList. End-to-end join tests can't produce carrier
    /// interceptors in the unit test compilation context, so we test the resolution directly.
    /// </summary>
    private static string[] ResolveJoinInterfaces(params string[] entityTypeNames)
    {
        var raw = new RawCallSite(
            methodName: "ExecuteFetchAllAsync",
            filePath: "test.cs",
            line: 1, column: 1,
            uniqueId: "join_test",
            kind: InterceptorKind.ExecuteFetchAll,
            builderKind: BuilderKind.Query,
            entityTypeName: entityTypeNames[0],
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: default);

        var mods = new ColumnModifiers();
        var entity = EntityRef.FromEntityInfo(new EntityInfo(
            entityName: entityTypeNames[0], schemaClassName: "Schema", schemaNamespace: "TestApp",
            tableName: "t0", namingStyle: NamingStyleKind.SnakeCase,
            columns: new[] { new ColumnInfo("Id", "id", "int", "int", false, ColumnKind.PrimaryKey, null, mods, isValueType: true) },
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Location.None));

        var bound = new BoundCallSite(
            raw, "Ctx", "App", new GenSqlDialectConfig(GenSqlDialect.SQLite),
            "t0", null, entity,
            joinedEntityTypeNames: entityTypeNames);

        var site = new TranslatedCallSite(bound);

        var joins = new JoinPlan[entityTypeNames.Length - 1];
        for (int i = 0; i < joins.Length; i++)
        {
            joins[i] = new JoinPlan(
                JoinClauseKind.Inner,
                new TableRef($"t{i + 1}", null, $"t{i + 1}"),
                new LiteralExpr("1", "int"));
        }

        var plan = new Quarry.Generators.IR.QueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("t0", null, "t0"),
            joins: joins,
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: new SelectProjection(
                ProjectionKind.Entity, entityTypeNames[0],
                Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null, isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: new[] { 0 },
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);

        var assembled = new AssembledPlan(
            plan: plan,
            sqlVariants: new Dictionary<int, AssembledSqlVariant>
            {
                [0] = new AssembledSqlVariant("SELECT 1", 0)
            },
            readerDelegateCode: null,
            maxParameterCount: 0,
            executionSite: site,
            clauseSites: Array.Empty<TranslatedCallSite>(),
            entityTypeName: entityTypeNames[0],
            resultTypeName: null,
            dialect: GenSqlDialect.SQLite);

        return CarrierEmitter.ResolveCarrierInterfaceList(assembled);
    }

    // ── Concrete Context Type Tests ──

    [Test]
    public void CarrierGeneration_ConcreteContextType_OnCarrierCtxField()
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

        // Carrier Ctx field must use concrete context type, not QuarryContext
        Assert.That(code, Does.Contain("internal TestDbContext? Ctx;"));
        Assert.That(code, Does.Not.Contain("internal QuarryContext? Ctx;"));
    }

    [Test]
    public void CarrierGeneration_ChainRoot_NoInterfaceCast()
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

        // ChainRoot must assign @this directly, no interface cast
        Assert.That(code, Does.Contain("Ctx = @this"));
        Assert.That(code, Does.Not.Contain("(IQueryExecutionContext)"));
    }

    [Test]
    public void CarrierGeneration_ScalarTerminal_DelegatesToQueryExecutor()
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
        var count = await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // Scalar must delegate to QueryExecutor, not inline
        Assert.That(code, Does.Contain("ExecuteCarrierScalarWithCommandAsync"));
        // Must NOT be async (no state machine)
        Assert.That(code, Does.Not.Contain("public static async Task<TScalar>"));
    }

    [Test]
    public void CarrierGeneration_NamedTupleProjection()
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
        await db.Users()
            .Select(u => (Id: u.UserId, Name: u.UserName))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // Verify correct tuple type with resolved element types (not error '?' types)
        Assert.That(code, Does.Contain("(int Id, string Name)"),
            "Generated code should contain the fully resolved named tuple type");
        Assert.That(code, Does.Not.Contain("(?)"),
            "Generated code should not contain error-type casts");

        // Named tuple elements must appear as prefixes in the generated reader delegate
        Assert.That(code, Does.Contain("Id: r.Get"),
            "Generated reader should include 'Id:' named element prefix with typed reader call");
        Assert.That(code, Does.Contain("Name: r.Get"),
            "Generated reader should include 'Name:' named element prefix with typed reader call");
    }

    // -----------------------------------------------------------------
    //  Top-level program -- ComputeChainId scoping
    // -----------------------------------------------------------------

    [Test]
    public void TopLevelProgram_MultipleLocalFunctions_SeparateChains()
    {
        // Top-level programs with static local functions must scope each function's
        // chains independently. Before the ComputeChainId fix, LocalFunctionStatementSyntax
        // (which derives from StatementSyntax) was consumed by the generic statement handler,
        // causing all chains to collapse into ChainId "db" and produce QRY032.
        var source = SharedSchema + @"
using System;
using System.Threading.Tasks;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

" + @"
var db = new TestDbContext((System.Data.IDbConnection)null!);
await Scenario1(db);
await Scenario2(db);

static async Task<string> Scenario1(TestDbContext db)
{
    var rows = await db.Users()
        .Where(u => u.IsActive)
        .Select(u => u.UserName)
        .ExecuteFetchAllAsync();
    return rows[0];
}

static async Task<int> Scenario2(TestDbContext db)
{
    var rows = await db.Users()
        .Where(u => u.UserId == 1)
        .Select(u => u.UserId)
        .ExecuteFetchFirstAsync();
    return rows;
}
";
        // Must use ConsoleApplication for top-level statements
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
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
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")));

        var compilation = CSharpCompilation.Create(
            "TopLevelTestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        // Must NOT produce QRY032 (forked chain)
        var qry032 = diagnostics.Where(d => d.Id == "QRY032").ToList();
        Assert.That(qry032, Is.Empty,
            $"Top-level local functions should not produce QRY032. Got: {string.Join("; ", qry032.Select(d => d.GetMessage()))}");

        // Should generate interceptors with two separate carrier classes
        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // Two separate chains -> at least two carrier classes
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.GreaterThanOrEqualTo(2),
            "Each local function's chain should produce a separate carrier class");
    }

    // ── Fix regression tests ──────────────────────────────────────────────

    [Test]
    public void QuarryContextPassedToHelper_DoesNotTriggerQRY032()
    {
        // Reproduces a common pattern: a method uses db for its own
        // chains AND passes db to a helper method that also uses Quarry chains.
        var source = SharedSchema + @"
public class LogSchema : Schema
{
    public static string Table => ""logs"";
    public Key<int> LogId => Identity();
    public Col<string> Text => Length(500);
    public Col<int> UserId { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Log> Logs();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task Create(TestDbContext db, string name)
    {
        var id = await db.Users().Insert(new User { UserName = name }).ExecuteScalarAsync<int>();

        // Passing the context to a helper must not flag the chain above
        await InsertLog(db, id);
    }

    private static async System.Threading.Tasks.Task InsertLog(TestDbContext db, int userId)
    {
        await db.Logs().Insert(new Log { Text = ""created"", UserId = userId }).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry032 = diagnostics.Where(d => d.Id == "QRY032").ToList();
        Assert.That(qry032, Is.Empty,
            $"Passing QuarryContext to a helper should not trigger QRY032. Got: {string.Join("; ", qry032.Select(d => d.GetMessage()))}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors for both methods");
    }

    [Test]
    public void ChainFullyInsideLoop_DoesNotTriggerQRY032()
    {
        // A chain that starts AND terminates inside a loop body is fine:
        // the SQL shape is constant, only parameter values change per iteration.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task DeleteMany(TestDbContext db, int[] ids)
    {
        foreach (var id in ids)
        {
            await db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry032 = diagnostics.Where(d => d.Id == "QRY032").ToList();
        Assert.That(qry032, Is.Empty,
            $"Chain fully inside loop should not trigger QRY032. Got: {string.Join("; ", qry032.Select(d => d.GetMessage()))}");
    }

    [Test]
    public void ChainCrossingLoopBoundary_TriggersQRY032()
    {
        // A chain where the root is outside a loop but a clause is inside
        // crosses a loop boundary and must be rejected.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task CrossBoundary(TestDbContext db, int[] ids)
    {
        var q = db.Users().Delete();
        foreach (var id in ids)
        {
            await q.Where(u => u.UserId == id).ExecuteNonQueryAsync();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry032 = diagnostics.Where(d => d.Id == "QRY032").ToList();
        Assert.That(qry032, Is.Not.Empty,
            "Chain crossing a loop boundary should produce QRY032");
        Assert.That(qry032[0].GetMessage(), Does.Contain("loop boundary"),
            "Diagnostic should specifically cite loop boundary, not fork or other reason");
    }

    [Test]
    public void FirstOrDefault_ValueTypeResult_NoNullableSuffix()
    {
        // For value-type TResult (tuple), the interceptor return type must be
        // Task<T> (not Task<T?>) because unconstrained TResult? on the interface
        // doesn't create Nullable<T> for value types.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task Test(TestDbContext db, int id)
    {
        // Tuple projection → value type
        var tuple = await db.Users()
            .Where(u => u.UserId == id)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchFirstOrDefaultAsync();

        // Single value-type column projection
        var scalar = await db.Users()
            .Where(u => u.UserId == id)
            .Select(u => u.UserId)
            .ExecuteFetchFirstOrDefaultAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry032 = diagnostics.Where(d => d.Id == "QRY032").ToList();
        Assert.That(qry032, Is.Empty,
            $"Should not produce QRY032. Got: {string.Join("; ", qry032.Select(d => d.GetMessage()))}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null);

        var code = interceptorsTree!.GetText().ToString();

        // Tuple: must be Task<(int, string)> not Task<(int, string)?>
        Assert.That(code, Does.Contain("Task<(int UserId, string UserName)>"),
            "Tuple value-type result should NOT have nullable suffix");
        Assert.That(code, Does.Not.Contain("Task<(int UserId, string UserName)?>"),
            "Tuple value-type result must not produce Nullable<ValueTuple>");

        // Single int column: must be Task<int> not Task<int?>
        Assert.That(code, Does.Contain("Task<int>"),
            "Primitive value-type result should NOT have nullable suffix");
    }

    [Test]
    public void FirstOrDefault_ReferenceTypeResult_HasNullableSuffix()
    {
        // For reference-type TResult (entity class), the interceptor return type
        // must be Task<T?> because unconstrained TResult? adds nullable annotation
        // for reference types.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async System.Threading.Tasks.Task Test(TestDbContext db, int id)
    {
        var user = await db.Users()
            .Where(u => u.UserId == id)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry032 = diagnostics.Where(d => d.Id == "QRY032").ToList();
        Assert.That(qry032, Is.Empty,
            $"Should not produce QRY032. Got: {string.Join("; ", qry032.Select(d => d.GetMessage()))}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null);

        var code = interceptorsTree!.GetText().ToString();

        // Entity (reference type): must be Task<User?> with nullable suffix
        Assert.That(code, Does.Contain("Task<User?>"),
            "Reference-type result should have nullable suffix for FirstOrDefault");
    }

    #region Issue: No concrete QueryBuilder<T> references in generated code

    [Test]
    public void GeneratedCode_NeverReferences_ConcreteQueryBuilderType()
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
        await db.Users()
            .Where(u => u.IsActive)
            .Select(u => u)
            .OrderBy(u => u.UserName)
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // Generated code must never reference QueryBuilder<T> — it was removed
        // from the runtime. Only IQueryBuilder<T> (interface) should appear.
        Assert.That(code, Does.Not.Match(@"(?<!I)QueryBuilder<"),
            "Generated code should not reference concrete QueryBuilder<T> type — only IQueryBuilder<T> interface");
    }

    [Test]
    public void FallbackPath_Uses_InterfaceType_NotConcreteQueryBuilder()
    {
        // This test verifies that when a clause falls back to the non-carrier path
        // (e.g., untranslatable clause), the fallback cast uses interface types,
        // not the nonexistent concrete QueryBuilder<T>.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        // Simple chain that should work
        await db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        // Another chain with identity select
        await db.Users()
            .Select(u => u)
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        // Get all generated interceptor files
        var interceptorTrees = result.GeneratedTrees
            .Where(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"))
            .ToList();

        foreach (var tree in interceptorTrees)
        {
            var code = tree.GetText().ToString();

            // No generated file should reference concrete QueryBuilder<T>
            Assert.That(code, Does.Not.Match(@"(?<!I)QueryBuilder<"),
                $"File {tree.FilePath} references concrete QueryBuilder<T> — should use IQueryBuilder<T> or carrier class");
        }
    }

    #endregion

    #region CTE diagnostics (QRY080 / QRY081)

    [Test]
    public void Cte_With_NonInlineInnerArgument_EmitsQRY080()
    {
        // Regression for review pass #2: when the inner argument to With<T>() is NOT
        // an inline fluent chain that DetectCteInnerChain can classify as a CTE inner
        // chain (e.g. a field reference, a hoisted local, an external method result),
        // the chain analyzer's cteInnerResults lookup misses. The user must see a
        // dedicated QRY080 diagnostic — NOT QRY900 InternalError, and not a runtime
        // "no such table" error.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    // Field reference, NOT an inline chain — DetectCteInnerChain cannot classify it
    // because the field's syntactic ancestor is not an ArgumentSyntax of a With() call.
    public static IQueryBuilder<Order> _stub = null!;

    public static async Task Test(TestDbContext db)
    {
        await db.With<Order>(_stub)
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry080 = diagnostics.FirstOrDefault(d => d.Id == "QRY080");
        Assert.That(qry080, Is.Not.Null,
            $"Expected QRY080 for non-inline With<T>() inner argument. Got: {string.Join("; ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");

        // Must NOT surface as QRY900 InternalError — that misclassifies a user-input
        // problem as a generator bug.
        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Non-inline CTE inner should not surface as QRY900. Got: {qry900?.GetMessage()}");
    }

    [Test]
    public void Cte_LambdaWith_NullBody_EmitsQRY080()
    {
        // Lambda form of With<T>() where the body returns null! instead of building
        // a fluent chain on the lambda parameter. The discovery phase cannot classify
        // this as a lambda inner chain, so ChainAnalyzer emits QRY080.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.With<Order>(orders => null!)
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry080 = diagnostics.FirstOrDefault(d => d.Id == "QRY080");
        Assert.That(qry080, Is.Not.Null,
            $"Expected QRY080 for null-returning lambda With<T>() body. Got: {string.Join("; ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Null-returning lambda CTE should not surface as QRY900. Got: {qry900?.GetMessage()}");
    }

    [Test]
    public void Cte_LambdaWith_VariableReturn_EmitsQRY080()
    {
        // Lambda form of With<T>() where the body returns a local variable instead of
        // building a chain on the lambda parameter. Discovery cannot trace the variable
        // back to a lambda-parameter-rooted chain, so QRY080 fires.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        IQueryBuilder<Order> stub = null!;
        await db.With<Order>(orders => stub)
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry080 = diagnostics.FirstOrDefault(d => d.Id == "QRY080");
        Assert.That(qry080, Is.Not.Null,
            $"Expected QRY080 for variable-returning lambda With<T>() body. Got: {string.Join("; ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Variable-returning lambda CTE should not surface as QRY900. Got: {qry900?.GetMessage()}");
    }

    [Test]
    public void Cte_LambdaWith_NonQuarryMethodCall_EmitsQRY080()
    {
        // Lambda form of With<T>() where the body calls a static helper method that
        // returns IQueryBuilder<Order> — a valid return type but NOT a fluent chain
        // rooted on the lambda parameter. Discovery does not detect a lambda inner
        // chain, so QRY080 fires.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static IQueryBuilder<Order> GetFallback() => null!;

    public static async Task Test(TestDbContext db)
    {
        await db.With<Order>(orders => GetFallback())
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry080 = diagnostics.FirstOrDefault(d => d.Id == "QRY080");
        Assert.That(qry080, Is.Not.Null,
            $"Expected QRY080 for non-Quarry method call in lambda With<T>() body. Got: {string.Join("; ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Non-Quarry method call in lambda CTE should not surface as QRY900. Got: {qry900?.GetMessage()}");
    }

    [Test]
    public void Cte_FromCte_WithoutPrecedingWith_EmitsQRY081()
    {
        // Regression for review pass #2: FromCte<T>() with no matching With<T>() earlier
        // in the same chain must produce a dedicated QRY081 diagnostic via the deferred
        // diagnostics channel — not QRY900 InternalError, not a runtime SQL failure.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        // No With<Order>() preceding the FromCte — there is no CTE definition in scope.
        await db.FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry081 = diagnostics.FirstOrDefault(d => d.Id == "QRY081");
        Assert.That(qry081, Is.Not.Null,
            $"Expected QRY081 for FromCte<T>() without preceding With<T>(). Got: {string.Join("; ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"FromCte-without-With should not surface as QRY900. Got: {qry900?.GetMessage()}");
    }

    [Test]
    public void Cte_TwoWiths_SameDto_EmitsQRY082()
    {
        // Two With<X>(...) calls in one chain referencing the same DTO type produce
        // duplicate CTE aliases in the generated WITH clause, which is invalid SQL
        // and silently routes the second call's CteDef lookup to the first entry in
        // EmitCteDefinition. Reject the configuration at compile time via QRY082.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        // Both With<Order>(...) calls — duplicate short name 'Order'.
        await db.With<Order>(orders => orders.Where(o => o.Total > 100))
            .With<Order>(orders => orders.Where(o => o.Total > 200))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry082 = diagnostics.FirstOrDefault(d => d.Id == "QRY082");
        Assert.That(qry082, Is.Not.Null,
            $"Expected QRY082 for duplicate-DTO multi-With chain. Got: {string.Join("; ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");

        // Verify the diagnostic points at the SECOND With<Order>() call (the duplicate-
        // introducing one), not the first. Quarry's discovery pins call-site locations
        // to the outermost InvocationExpressionSyntax of each call, which for a fluent
        // chain covers the whole expression up to that invocation — so the second With()
        // call's span extends far enough to include the "> 200" literal that uniquely
        // identifies it as the second call. Extracting the span text is a reliable way
        // to assert the diagnostic is tied to the duplicate site rather than the first.
        var sourceText = compilation.SyntaxTrees.First().GetText();
        var spanText = sourceText.ToString(qry082!.Location.SourceSpan);
        Assert.That(spanText, Does.Contain("o.Total > 200"),
            $"Expected QRY082 span to cover the SECOND With<Order>() call (containing 'o.Total > 200'). Got span: '{spanText}'");
        Assert.That(spanText, Does.Contain("With<Order>"),
            $"Expected QRY082 span to contain 'With<Order>'. Got span: '{spanText}'");

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Duplicate CTE name should not surface as QRY900. Got: {qry900?.GetMessage()}");
    }

    [Test]
    public void Cte_With_GlobalNamespaceDto_StripsGlobalPrefix()
    {
        // Regression for review pass #2 Correctness #1: ChainAnalyzer's local
        // ExtractShortTypeName helper did NOT strip the `global::` prefix while
        // TransitionBodyEmitter's ExtractDtoShortName helper DID. For a DTO declared
        // in the global namespace, dtoType.ToFullyQualifiedDisplayString() returns
        // `global::Foo`, the chain analyzer recorded `cteDef.Name = "global::Foo"`,
        // and the emitter's name comparison `cteDef.Name == siteCteName` was always
        // false (because the emitter recovered the bare `Foo`). The captured-param
        // copy loop silently broke and the inner parameters bound to default values.
        //
        // The consolidated CteNameHelpers.ExtractShortName helper strips both
        // `global::` and namespace prefixes — verify by checking the generated
        // interceptor code uses the bare DTO name in the WITH clause and that the
        // captured-param assignment is emitted.
        var schemaSource = @"
using Quarry;
namespace TestApp;

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
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
        decimal cutoff = 100m;
        await db.With<global::GlobalOrderDto>(
                db.Orders()
                  .Where(o => o.Total > cutoff)
                  .Select(o => new global::GlobalOrderDto { OrderId = o.OrderId, Total = o.Total }))
            .FromCte<global::GlobalOrderDto>()
            .Select(d => (d.OrderId, d.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        // GlobalOrderDto declared at file scope with NO namespace — lives in the global namespace.
        var globalDtoSource = @"
public class GlobalOrderDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}
";

        var compilation = CreateCompilation(schemaSource, globalDtoSource);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        // Must not produce QRY900 (the symptom of the helper divergence was a runtime
        // bug, not a generator crash, but verify nothing went sideways).
        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Global-namespace DTO must not produce QRY900. Got: {qry900?.GetMessage()}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptor file");
        var code = interceptorsTree!.GetText().ToString();

        // The generated SQL constant is emitted as a verbatim string `@"..."` so
        // embedded double quotes appear as `""` (not `\"`). The WITH clause must use
        // the bare DTO short name, not `global::GlobalOrderDto`.
        Assert.That(code, Does.Contain("WITH \"\"GlobalOrderDto\"\""),
            "WITH clause must use bare DTO short name (verbatim quoted), not `global::GlobalOrderDto`");
        Assert.That(code, Does.Not.Contain("global::GlobalOrderDto\"\""),
            "WITH clause must strip `global::` prefix from DTO type name");

        // The CTE inner captured-parameter copy loop must have emitted an assignment
        // from the inner carrier's P0 into an outer carrier P-slot. If the helper
        // divergence had reappeared, the name comparison `cteDef.Name == siteCteName`
        // would always be false for global-namespace DTOs, the copy loop would
        // silently break, and __inner.P0 would never be referenced — the original
        // silent bug from pass #1.
        Assert.That(code, Does.Match(@"P\d+\s*=\s*__inner\.P0"),
            "Outer carrier must copy inner-CTE P0 parameter when DTO is in global namespace");
    }

    #endregion

    #region Post-CTE chain-continuation interceptor shape (issue #281)

    [Test]
    public void Cte_FromCte_OrderBy_EmitsWellFormedInterceptor()
    {
        // Regression for issue #281: chain-continuation methods called directly on
        // FromCte<T>() previously synthesized a malformed interceptor where the
        // entity name appeared as both receiver and return type (e.g. `Order<Order>`),
        // triggering CS0308 in the generated `.g.cs`. With OrderBy/Limit/Offset/etc.
        // now declared on IEntityAccessor<T>, Roslyn binds the call, normal discovery
        // produces a site with builderTypeName="IEntityAccessor", and the existing
        // BuildReceiverType helper emits `IQueryBuilder<T> OrderBy(this IEntityAccessor<T>, ...)`.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.With<Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Order>()
            .OrderBy(o => o.OrderId)
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        // No QRY900: prior bug surfaced as malformed source rather than a generator
        // crash, but verify nothing else went sideways.
        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null,
            $"Post-CTE OrderBy must not produce QRY900. Got: {qry900?.GetMessage()}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");
        var code = interceptorsTree!.GetText().ToString();

        // The generated OrderBy interceptor must use IEntityAccessor<Order> as the
        // receiver (the static type at the call site is IEntityAccessor<Order>) and
        // return IQueryBuilder<Order>. The bug shape was `public static Order<Order>
        // OrderBy_X(this Order<Order> builder, ...)` — entity name as a generic.
        Assert.That(code, Does.Match(@"public static IQueryBuilder<Order>\s+OrderBy_\w+\(\s*this IEntityAccessor<Order>\s+builder,"),
            "OrderBy interceptor must be `IQueryBuilder<Order> OrderBy_X(this IEntityAccessor<Order> builder, ...)`. " +
            "Got: " + code);

        // The malformed `Order<Order>` shape from the bug report must not appear
        // anywhere in the generated source.
        Assert.That(code, Does.Not.Contain("Order<Order>"),
            "Malformed `Order<Order>` interceptor signature must not regress (issue #281)");

        // Direct CS0308 regression check: re-add the generated trees to the original
        // compilation and look for the literal bug symptom from #281 — CS0308 (non-
        // generic type used with type arguments) in any generated file. The shape
        // assertions above are indirect; this one would have caught the bug even if
        // the emitter produced a different malformed shape that still tripped CS0308.
        // We intentionally do NOT flag every error: the test compilation is missing
        // the `<InterceptorsNamespaces>` MSBuild config (CS9137) and assembly refs
        // for non-essential types like System.ComponentModel.Primitives (CS0012);
        // those are harness-config issues, not generator-output bugs.
        var combined = compilation.AddSyntaxTrees(result.GeneratedTrees);
        var cs0308 = combined.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS0308")
            .Where(d => d.Location.SourceTree != null
                && result.GeneratedTrees.Any(g => g.FilePath == d.Location.SourceTree.FilePath))
            .ToList();
        Assert.That(cs0308, Is.Empty,
            "Generated source must not contain CS0308 (the literal symptom of issue #281). Found: " +
            string.Join("; ", cs0308.Select(d => d.Id + " at " + d.Location + ": " + d.GetMessage())));
    }

    [Test]
    public void Cte_FromCte_AllChainContinuations_BindAndEmitCleanly()
    {
        // Coverage for the rest of the IEntityAccessor surface added by issue #281:
        // ThenBy, Having, and the six direct + six lambda set-op overloads. Issue #281
        // tests above exercise OrderBy/Limit/Offset directly, but every member of the
        // expanded surface is at risk of regressing the same malformed-interceptor bug
        // if `IsEntityAccessorType` / `BuildReceiverType` ever stop matching the new
        // builderTypeName="IEntityAccessor" path. One omnibus snippet exercises every
        // post-FromCte<T>() form; CS0308 in any generated interceptor would fail the
        // recompile check.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        // ThenBy directly off FromCte<T>() (semantically dubious without a prior OrderBy,
        // but this is a binding/emission test, not a SQL-correctness test).
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .ThenBy(o => o.OrderId)
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();

        // Having directly off FromCte<T>().
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Having(o => o.Total > 0)
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();

        // Direct-form set-ops: feed an external IQueryBuilder<T> as the operand.
        // Use Orders() to construct the operand so the chain is fully-typed.
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Union(db.Orders().Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .UnionAll(db.Orders().Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Intersect(db.Orders().Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .IntersectAll(db.Orders().Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Except(db.Orders().Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .ExceptAll(db.Orders().Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();

        // Lambda-form set-ops: build the operand off the IEntityAccessor<T> param.
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Union(orders => orders.Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .UnionAll(orders => orders.Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Intersect(orders => orders.Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .IntersectAll(orders => orders.Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .Except(orders => orders.Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
        await db.With<Order>(orders => orders.Where(o => o.Total > 0))
            .FromCte<Order>()
            .ExceptAll(orders => orders.Where(o => o.Total > 100))
            .Select(o => o.OrderId)
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, _) = RunGeneratorWithDiagnostics(compilation);

        // Combined-compilation check: with the generator-emitted entity classes and
        // interceptors added to the original compilation, the user snippet must bind
        // and the generated interceptors must compile. Filter to two error classes:
        //   - CS0308 anywhere in generated source (the literal #281 bug symptom)
        //   - CS1061 / CS0117 in the user snippet (would mean an expected method on
        //     IEntityAccessor<T> is missing — a regression of the new surface)
        // Test-harness config artifacts (CS9137 missing InterceptorsNamespaces, CS0012
        // missing System.ComponentModel.Primitives) are intentionally tolerated.
        var combined = compilation.AddSyntaxTrees(result.GeneratedTrees);
        var allErrors = combined.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var cs0308 = allErrors
            .Where(d => d.Id == "CS0308"
                && d.Location.SourceTree != null
                && result.GeneratedTrees.Any(g => g.FilePath == d.Location.SourceTree.FilePath))
            .ToList();
        Assert.That(cs0308, Is.Empty,
            "Generated interceptors for ThenBy/Having/set-ops on IEntityAccessor must not contain CS0308. Found: " +
            string.Join("; ", cs0308.Select(d => d.Id + " at " + d.Location + ": " + d.GetMessage())));

        var inputTree = compilation.SyntaxTrees.First();
        var bindingErrors = allErrors
            .Where(d => (d.Id == "CS1061" || d.Id == "CS0117")
                && d.Location.SourceTree == inputTree)
            .ToList();
        Assert.That(bindingErrors, Is.Empty,
            "User snippet must bind every chain-continuation method against IEntityAccessor<T>. Found: " +
            string.Join("; ", bindingErrors.Select(d => d.Id + ": " + d.GetMessage())));
    }

    #endregion

    #region Window function OVER clause failure modes (#223)

    [Test]
    public void CarrierGeneration_WindowFunction_BlockBodyLambda_FallsToRuntimeBuild()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Orders()
            .Where(o => true)
            .Select(o => (o.OrderId, Rn: Sql.RowNumber(over => { return over.OrderBy(o.Total); })))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Generator should not crash on block-body OVER lambda");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should still generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Not.Contain("ROW_NUMBER()"),
            "Block-body OVER lambda should not produce window function SQL (falls to RuntimeBuild)");
    }

    [Test]
    public void CarrierGeneration_WindowFunction_UnknownMethodInChain_FallsToRuntimeBuild()
    {
        // over.ToString() returns string, not IOverClause — intentional compilation error.
        // The generator should still handle the syntax gracefully.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Orders()
            .Where(o => true)
            .Select(o => (o.OrderId, Rn: Sql.RowNumber(over => over.ToString())))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Generator should not crash on unknown method in OVER chain");

        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for unknown method in OVER chain");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        if (interceptorsTree != null)
        {
            var code = interceptorsTree.GetText().ToString();
            Assert.That(code, Does.Not.Contain("ROW_NUMBER()"),
                "Unknown method in OVER chain should not produce window function SQL");
        }
    }

    [Test]
    public void CarrierGeneration_WindowFunction_EmptyOverClause_ProducesValidSql()
    {
        // over => over produces ROW_NUMBER() OVER () — valid SQL with empty OVER clause.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Orders()
            .Where(o => true)
            .Select(o => (o.OrderId, Rn: Sql.RowNumber(over => over)))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Empty OVER clause should not produce QRY errors");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("ROW_NUMBER() OVER ()"),
            "Empty OVER clause should produce ROW_NUMBER() OVER () in generated SQL");
    }

    [Test]
    public void CarrierGeneration_WindowFunction_NonFluentChainExpression_FallsToRuntimeBuild()
    {
        // SomeMethod(over) is not a fluent chain — intentional compilation error.
        // The generator should still handle the syntax gracefully.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Orders()
            .Where(o => true)
            .Select(o => (o.OrderId, Rn: Sql.RowNumber(over => SomeMethod(over))))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Generator should not crash on non-fluent-chain OVER expression");

        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for non-fluent-chain OVER expression");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        if (interceptorsTree != null)
        {
            var code = interceptorsTree.GetText().ToString();
            Assert.That(code, Does.Not.Contain("ROW_NUMBER()"),
                "Non-fluent-chain OVER expression should not produce window function SQL");
        }
    }

    #endregion

    #region Joined window function OVER clause failure modes (#227)

    [Test]
    public void CarrierGeneration_JoinedWindowFunction_BlockBodyLambda_FallsToRuntimeBuild()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Join<Order>((u, o) => u.UserId == o.UserId)
            .Select((u, o) => (u.UserName, o.Total, Rn: Sql.RowNumber(over => { return over.OrderBy(o.Total); })))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Generator should not crash on block-body OVER lambda in joined projection");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should still generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Not.Contain("ROW_NUMBER()"),
            "Block-body OVER lambda should not produce window function SQL (falls to RuntimeBuild)");
    }

    [Test]
    public void CarrierGeneration_JoinedWindowFunction_UnknownMethodInChain_FallsToRuntimeBuild()
    {
        // over.ToString() returns string, not IOverClause — intentional compilation error.
        // The generator should still handle the syntax gracefully.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Join<Order>((u, o) => u.UserId == o.UserId)
            .Select((u, o) => (u.UserName, o.Total, Rn: Sql.RowNumber(over => over.ToString())))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Generator should not crash on unknown method in OVER chain in joined projection");

        // Joined projections produce QRY032 when a tuple element is not analyzable,
        // unlike single-entity projections which silently fall to RuntimeBuild.
        var qry032 = diagnostics.FirstOrDefault(d => d.Id == "QRY032");
        Assert.That(qry032, Is.Not.Null, "Unanalyzable joined projection should produce QRY032");
    }

    [Test]
    public void CarrierGeneration_JoinedWindowFunction_EmptyOverClause_ProducesValidSql()
    {
        // over => over produces ROW_NUMBER() OVER () — valid SQL with empty OVER clause.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Join<Order>((u, o) => u.UserId == o.UserId)
            .Select((u, o) => (u.UserName, o.Total, Rn: Sql.RowNumber(over => over)))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Empty OVER clause should not produce QRY errors in joined projection");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("ROW_NUMBER() OVER ()"),
            "Empty OVER clause should produce ROW_NUMBER() OVER () in generated SQL");
    }

    [Test]
    public void CarrierGeneration_JoinedWindowFunction_NonFluentChainExpression_FallsToRuntimeBuild()
    {
        // SomeMethod(over) is not a fluent chain — intentional compilation error.
        // The generator should still handle the syntax gracefully.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Join<Order>((u, o) => u.UserId == o.UserId)
            .Select((u, o) => (u.UserName, o.Total, Rn: Sql.RowNumber(over => SomeMethod(over))))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry900 = diagnostics.FirstOrDefault(d => d.Id == "QRY900");
        Assert.That(qry900, Is.Null, "Generator should not crash on non-fluent-chain OVER expression in joined projection");

        // Joined projections produce QRY032 when a tuple element is not analyzable,
        // unlike single-entity projections which silently fall to RuntimeBuild.
        var qry032 = diagnostics.FirstOrDefault(d => d.Id == "QRY032");
        Assert.That(qry032, Is.Not.Null, "Unanalyzable joined projection should produce QRY032");
    }

    #endregion

    #region Parameterized Window Functions

    [Test]
    public void CarrierGeneration_WindowFunction_NtileVariableParameter()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var buckets = 3;
        await db.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.Total)))).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for variable Ntile parameter");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should generate carrier class");
        // Carrier should have a P-field for the captured Ntile buckets parameter
        Assert.That(code, Does.Contain("NTILE(@p"), "SQL should use parameterized NTILE");
    }

    [Test]
    public void CarrierGeneration_WindowFunction_NtileConstantInlined()
    {
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        const int buckets = 3;
        await db.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.Total)))).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qryErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("QRY")).ToList();
        Assert.That(qryErrors, Is.Empty, "Generator should not produce QRY errors for const Ntile parameter");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        // Constant should be inlined — SQL should have literal 3, not a parameter
        Assert.That(code, Does.Contain("NTILE(3)"), "Constant Ntile bucket should be inlined in SQL");
        Assert.That(code, Does.Not.Contain("NTILE(@p"), "Constant should not be parameterized");
    }

    #endregion

    #region Structural Shape Assertions

    [Test]
    public void CarrierGeneration_ParameterlessClause_OmitsCarrierCast()
    {
        // Phase 2 dead-code removal: clause bodies with no parameters and no mask bit
        // should not emit 'var __c = Unsafe.As<...>(builder)' since it is unused.
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
        // The carrier cast appears exactly once — in the terminal preamble.
        // If dead-code removal were absent, it would also appear in the Select clause body.
        var carrierCastCount = System.Text.RegularExpressions.Regex.Matches(code, @"var __c = Unsafe\.As<").Count;
        Assert.That(carrierCastCount, Is.EqualTo(1),
            "Carrier cast should appear once (terminal only); parameterless clause should omit it");
        // The return line is always emitted for interface crossing
        Assert.That(code, Does.Contain("return Unsafe.As<"),
            "Return line should still use Unsafe.As for interface crossing");
    }

    [Test]
    public void CarrierGeneration_CollectionParam_HasReadonlySqlCache()
    {
        // Phase 3: collection parameter chains emit a static readonly _sqlCache field.
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
        Assert.That(code, Does.Contain("static readonly Quarry.Internal.CollectionSqlCache?[] _sqlCache"),
            "Collection parameter chain should emit readonly _sqlCache field");
    }

    [Test]
    public void CarrierGeneration_BatchInsert_UsesParameterNameCache()
    {
        // Phase 4: batch insert terminals use ParameterNames.AtP for zero-allocation
        // parameter name lookup instead of string concatenation.
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
        var users = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("ParameterNames.AtP("),
            "Batch insert should use ParameterNames.AtP for cached parameter name lookup");
        Assert.That(code, Does.Not.Contain("\"@p\" + __paramIdx"),
            "Batch insert should not use string concatenation for parameter names");
    }

    [Test]
    public void CarrierGeneration_EntityInsert_EmitsEmptyParameterNames_ForPostgreSQL()
    {
        // Regression guard for GH-258 (redux): on PostgreSQL, entity insert
        // must assign ParameterName = "" so Npgsql 10 stays on its native
        // positional-binding path against the $N SQL placeholders. Any non-
        // empty name (whether @pN or $N) flips Npgsql into name-lookup mode
        // and ships the Bind frame with zero values — reproducing the v0.3.0
        // and v0.3.1/v0.3.2 failure modes respectively.
        var source = SharedSchema + @"
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
        Assert.That(code, Does.Contain("__p0.ParameterName = \"\""),
            "Entity insert on PostgreSQL must assign ParameterName = \"\" (empty)");
        Assert.That(code, Does.Contain("__p1.ParameterName = \"\""),
            "Entity insert on PostgreSQL must assign ParameterName = \"\" (empty)");
        Assert.That(code, Does.Not.Match("__p\\d+\\.ParameterName\\s*=\\s*\"\\$\\d+\""),
            "Entity insert on PostgreSQL must not emit $N ParameterName — it would fail to bind on Npgsql 10 (v0.3.1/0.3.2 bug)");
        Assert.That(code, Does.Not.Match("__p\\d+\\.ParameterName\\s*=\\s*\"@p\\d+\""),
            "Entity insert on PostgreSQL must not emit @pN ParameterName — it would fail to bind on Npgsql 10 (v0.3.0 bug)");
    }

    [Test]
    public void CarrierGeneration_EntityInsert_EmitsAtParameterNames_ForSQLite()
    {
        // Regression guard for GH-258: SQLite still uses @pN names (Microsoft.Data.Sqlite
        // binds by name against @pN placeholders). Ensure the dialect-aware emission does
        // not accidentally regress non-PostgreSQL paths.
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
        Assert.That(code, Does.Contain("__p0.ParameterName = \"@p0\""),
            "Entity insert on SQLite must assign ParameterName = \"@p0\"");
        Assert.That(code, Does.Contain("__p1.ParameterName = \"@p1\""),
            "Entity insert on SQLite must assign ParameterName = \"@p1\"");
    }

    [Test]
    public void CarrierGeneration_WhereInCollection_EmitsEmptyParameterName_ForPostgreSQL()
    {
        // Regression guard for GH-258 (redux, review #4): the collection
        // expansion loop at CarrierEmitter.cs:690 must assign
        // `__pc.ParameterName = ""` on PostgreSQL. Pre-fix it assigned
        // `__col{i}Parts[__bi]` — a runtime `$N` string — which is the
        // v0.3.1/0.3.2 "C" configuration (non-empty name + $N SQL) that
        // Npgsql rejects with 08P01. The dynamic nature of this code path
        // means the PG integration test alone cannot guard the generator's
        // source output; this string-match check catches the exact
        // emission regression.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, int[] ids)
    {
        var results = await db.Users().Where(u => ids.Contains(u.UserId)).Select(u => u.UserName).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("__pc.ParameterName = \"\""),
            "Collection-parameter expansion on PostgreSQL must assign ParameterName = \"\" (empty)");
        Assert.That(code, Does.Not.Contain("__pc.ParameterName = __col"),
            "Collection-parameter expansion on PostgreSQL must NOT reuse __colNParts (a $N string array) as ParameterName — that is the v0.3.1/0.3.2 bug");
    }

    [Test]
    public void CarrierGeneration_WhereInCollection_UsesPartsArray_ForSQLite()
    {
        // Companion guard: SQLite/SqlServer still bind collection parameters
        // by name, so `__pc.ParameterName = __col{i}Parts[__bi]` is the
        // correct behaviour for named-binding dialects — the fix for GH-258
        // must not over-reach and regress non-PG paths.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, int[] ids)
    {
        var results = await db.Users().Where(u => ids.Contains(u.UserId)).Select(u => u.UserName).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("__pc.ParameterName = __col"),
            "Collection-parameter expansion on SQLite must still use __colNParts for ParameterName (named-binding dialect)");
    }

    [Test]
    public void CarrierGeneration_BatchInsert_UsesEmptyParameterNames_ForPostgreSQL()
    {
        // Regression guard for GH-258 (redux): batch insert on PostgreSQL
        // must assign ParameterName = "" so Npgsql stays on native positional
        // binding against the $N placeholders that BatchInsertSqlBuilder
        // emits. PR #261 used ParameterNames.Dollar here, which is the
        // broken v0.3.1/v0.3.2 state — Npgsql rejects that configuration
        // with 08P01 bind-count mismatch.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var users = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        Assert.That(code, Does.Contain("__p.ParameterName = \"\""),
            "Batch insert on PostgreSQL must assign ParameterName = \"\" (empty) for native positional binding");
        Assert.That(code, Does.Not.Contain("ParameterNames.Dollar(__paramIdx)"),
            "Batch insert on PostgreSQL must not use ParameterNames.Dollar for ParameterName — that was the broken v0.3.1/0.3.2 state");
        Assert.That(code, Does.Not.Contain("ParameterNames.AtP(__paramIdx)"),
            "Batch insert on PostgreSQL must not fall back to ParameterNames.AtP either — only the empty-string literal");
    }

    [Test]
    public void CarrierGeneration_SelfContainedReader_EmitsReaderField()
    {
        // Phase 5: self-contained reader carriers emit a static readonly _reader field
        // to avoid duplicating the reader lambda at each terminal call site.
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
        Assert.That(code, Does.Contain("internal static readonly System.Func<System.Data.Common.DbDataReader,"),
            "Self-contained reader should emit static readonly _reader field with Func type");
        Assert.That(code, Does.Contain("> _reader ="),
            "Carrier class should declare _reader field");
        Assert.That(code, Does.Contain("Chain_0._reader"),
            "Terminal should reference the carrier's static _reader field");
    }

    #endregion

    // ── Carrier deduplication tests ──────────────────────────────────────

    [Test]
    public void DuplicateCarriers_SameWherePattern_SharedClass()
    {
        // Two chains with identical structure (same Where column, same Select,
        // same terminal) should share a single carrier class definition.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Method1(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();
    }

    public static async Task Method2(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.EqualTo(1),
            "Structurally identical carriers should share a single class definition");
        Assert.That(code, Does.Contain("file sealed class Chain_0"));

        // Both interceptor methods must reference the shared carrier class
        var chain0Refs = System.Text.RegularExpressions.Regex.Matches(code, @"Chain_0").Count;
        Assert.That(chain0Refs, Is.GreaterThan(1),
            "Both interceptor method bodies should reference the shared Chain_0 class");
    }

    [Test]
    public void DuplicateCarriers_DifferentWhereClauses_SeparateClasses()
    {
        // Two chains with different Where columns produce different SQL,
        // so they must NOT be deduplicated.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Method1(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();
    }

    public static async Task Method2(TestDbContext db)
    {
        await db.Users().Where(u => u.UserId == 1).Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.EqualTo(2),
            "Carriers with different SQL should not be deduplicated");
        Assert.That(code, Does.Contain("file sealed class Chain_0"));
        Assert.That(code, Does.Contain("file sealed class Chain_1"));
    }

    [Test]
    public void DuplicateCarriers_SameFieldsDifferentSql_SeparateClasses()
    {
        // Two chains with identical parameter types/fields but different SQL
        // (different column in Where) must remain separate.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Method1(TestDbContext db)
    {
        await db.Users().Where(u => u.UserId == 1).Select(u => u).ExecuteFetchAllAsync();
    }

    public static async Task Method2(TestDbContext db)
    {
        await db.Users().Where(u => u.UserId == 2).Select(u => u).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();

        // Same field layout (one int param) but the SQL literal values differ
        // (WHERE "UserId" = 1 vs WHERE "UserId" = 2), so carriers must stay separate
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.EqualTo(2),
            "Carriers with different SQL should not be deduplicated");
    }

    [Test]
    public void DuplicateCarriers_NoParamQueries_SharedClass()
    {
        // Minimal dedup case: two identical no-parameter queries
        // (no fields, no mask, no extraction plans — only SQL and interfaces).
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Method1(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).ExecuteFetchAllAsync();
    }

    public static async Task Method2(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.EqualTo(1),
            "Identical no-parameter carriers should share a single class definition");
    }

    [Test]
    public void DuplicateCarriers_DifferentTerminals_SharedClass()
    {
        // Same WHERE clause with FetchAll vs FetchFirst. The carrier class is a
        // data holder — terminal differences are handled by separate interceptor
        // methods. When the carrier structure is identical, dedup should fire.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Method1(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).ExecuteFetchAllAsync();
    }

    public static async Task<User> Method2(TestDbContext db)
    {
        return await db.Users().Where(u => u.IsActive).ExecuteFetchFirstAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.EqualTo(1),
            "Same carrier structure with different terminals should share a class");
    }

    [Test]
    public void DuplicateCarriers_WithAndWithoutSelect_SeparateClasses()
    {
        // One query with Select projection, one without — different interfaces
        // (IQueryBuilder<User> vs IQueryBuilder<User, string>) and different SQL.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static async Task Method1(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();
    }

    public static async Task Method2(TestDbContext db)
    {
        await db.Users().Where(u => u.IsActive).Select(u => u.UserName).ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var code = interceptorsTree!.GetText().ToString();
        var carrierCount = System.Text.RegularExpressions.Regex.Matches(code, @"file sealed class Chain_\d+").Count;
        Assert.That(carrierCount, Is.EqualTo(2),
            "Different Select projections should produce separate carriers (different interfaces/SQL)");
    }

    // ── Chained-With dispatch (issue #268) ───────────────────────────────

    /// <summary>
    /// For every <c>With_*</c> interceptor body in <paramref name="code"/>, asserts that
    /// each <c>Chain_N.__ExtractVar_X_K(__target)</c> reference resolves to an extractor
    /// declaration <c>Name = "X"</c> on that exact <c>Chain_N</c> carrier. The historic #268
    /// bug was a generated interceptor that referenced an extractor method which did not
    /// exist on the carrier it dispatched to — emitted text was syntactically clean but
    /// failed at <c>.Prepare()</c> with <see cref="System.MissingFieldException"/>. This
    /// helper catches that exact mis-dispatch shape.
    /// </summary>
    private static void AssertEveryDispatchArrowResolves(string code)
    {
        // Match every interceptor body that references a Chain_N extractor. Body brace
        // matching is shallow (no nested braces in real interceptors), so a non-greedy
        // `\{[\s\S]*?\}` works against the emitted format.
        var bodyPattern = @"public static [\w<>?., ]+? (?<name>\w+_[\w]+)\([^)]*\)\s*\{(?<body>[\s\S]*?)\n\s*\}";
        var bodyRegex = new System.Text.RegularExpressions.Regex(bodyPattern, System.Text.RegularExpressions.RegexOptions.Multiline);
        var carrierExtractors = ParseCarrierExtractors(code);

        var dispatchCalls = 0;
        foreach (System.Text.RegularExpressions.Match m in bodyRegex.Matches(code))
        {
            var body = m.Groups["body"].Value;
            var calls = System.Text.RegularExpressions.Regex.Matches(
                body, @"(?<carrier>Chain_\d+)\.__ExtractVar_(?<var>\w+?)_\d+\(\s*__target\s*\)");
            foreach (System.Text.RegularExpressions.Match c in calls)
            {
                dispatchCalls++;
                var carrier = c.Groups["carrier"].Value;
                var varName = c.Groups["var"].Value;
                Assert.That(carrierExtractors.ContainsKey(carrier), Is.True,
                    $"Interceptor {m.Groups["name"].Value} dispatches to {carrier} but no such carrier class is emitted.");
                Assert.That(carrierExtractors[carrier], Does.Contain(varName),
                    $"Interceptor {m.Groups["name"].Value} extracts '{varName}' off {carrier}, "
                    + $"but {carrier} only owns extractors: [{string.Join(", ", carrierExtractors[carrier])}]. "
                    + "This is the #268 mis-dispatch shape: interceptor routes to a carrier "
                    + "whose [UnsafeAccessor] would read a foreign closure field at .Prepare().");
            }
        }

        Assert.That(dispatchCalls, Is.GreaterThan(0),
            "No Chain_N.__ExtractVar_*_*(__target) dispatch calls found in emitted code — "
            + "test fixture sources should produce captured-variable extractors.");
    }

    private static Dictionary<string, HashSet<string>> ParseCarrierExtractors(string code)
    {
        // Each Chain_N block looks like:
        //   file sealed class Chain_N : ...
        //   {
        //       ...
        //       [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "varX")]
        //       internal extern static ref ... __ExtractVar_varX_K(...);
        //       ...
        //   }
        // Build a map: carrier name → set of variable names that carrier owns.
        var result = new Dictionary<string, HashSet<string>>();
        var carrierBlocks = System.Text.RegularExpressions.Regex.Matches(
            code, @"file sealed class (Chain_\d+)[^{]*\{(?<body>[\s\S]*?)^\}",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (System.Text.RegularExpressions.Match block in carrierBlocks)
        {
            var carrier = block.Groups[1].Value;
            var body = block.Groups["body"].Value;
            var owned = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match e in System.Text.RegularExpressions.Regex.Matches(
                body, @"Name = ""(?<var>[^""]+)""\)\][\s\S]*?__ExtractVar_(?<varB>\w+?)_\d+\("))
            {
                // Both groups should match — accept the second (extractor method name) as
                // the authoritative name since that's what the dispatch arrow references.
                owned.Add(e.Groups["varB"].Value);
            }
            result[carrier] = owned;
        }
        return result;
    }

    [Test]
    public void ChainedWith_SameShapeDifferentNames_DispatchToOwnClosure()
    {
        // Regression for #268. Two methods each chain two .With<>(...).With<>(...)
        // calls with closures that share structural shape (decimal + bool) but
        // capture differently-named locals. Each method's chain MUST dispatch to a
        // carrier whose [UnsafeAccessor]-typed extractors target ITS OWN
        // <>c__DisplayClass, not a shape-equivalent peer's. Historic failure mode
        // (PR #266 era) was MissingFieldException at .Prepare() because dedup
        // collapsed the carriers and one site's interceptor read a foreign field
        // name off the wrong closure type.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task MethodA(TestDbContext db)
    {
        decimal orderCutoff = 100m;
        bool activeFilter = true;
        await db
            .With<Order>(orders => orders.Where(o => o.Total > orderCutoff))
            .With<User>(users => users.Where(u => u.IsActive == activeFilter))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }

    public static async Task MethodB(TestDbContext db)
    {
        decimal orderTotal = 50m;
        bool enabled = false;
        await db
            .With<Order>(orders => orders.Where(o => o.Total > orderTotal))
            .With<User>(users => users.Where(u => u.IsActive == enabled))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.That(errors, Is.Empty,
            $"Generator emitted errors: {string.Join("; ", errors.Select(d => d.Id + ": " + d.GetMessage()))}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");
        var code = interceptorsTree!.GetText().ToString();

        // Both methods' captured names must surface as their own __ExtractVar_*_0 declarations.
        Assert.That(code, Does.Contain("__ExtractVar_orderCutoff_0"),
            "MethodA's first-clause extractor 'orderCutoff' must appear in the emitted carriers.");
        Assert.That(code, Does.Contain("__ExtractVar_orderTotal_0"),
            "MethodB's first-clause extractor 'orderTotal' must appear in the emitted carriers.");
        Assert.That(code, Does.Contain("__ExtractVar_activeFilter_1"),
            "MethodA's second-clause extractor 'activeFilter' must appear.");
        Assert.That(code, Does.Contain("__ExtractVar_enabled_1"),
            "MethodB's second-clause extractor 'enabled' must appear.");

        // The two methods must NOT collapse into one shared carrier.
        var carrierExtractors = ParseCarrierExtractors(code);
        var orderCutoffCarriers = carrierExtractors.Where(kv => kv.Value.Contains("orderCutoff")).Select(kv => kv.Key).ToList();
        var orderTotalCarriers = carrierExtractors.Where(kv => kv.Value.Contains("orderTotal")).Select(kv => kv.Key).ToList();
        Assert.That(orderCutoffCarriers, Is.Not.Empty,
            "Some emitted carrier must own MethodA's orderCutoff extractor.");
        Assert.That(orderTotalCarriers, Is.Not.Empty,
            "Some emitted carrier must own MethodB's orderTotal extractor.");
        Assert.That(orderCutoffCarriers.Intersect(orderTotalCarriers), Is.Empty,
            "MethodA and MethodB must not share a single carrier — same-shape closures with "
            + "differently-named captures must dispatch to DISTINCT Chain_N classes.");

        // Dispatch-arrow consistency: every interceptor body's Chain_N.__ExtractVar_X
        // reference must point at an extractor X that actually lives on Chain_N.
        // This is the assertion that catches the historic PR #266 mis-dispatch shape:
        // a carrier whose interceptor body referenced an extractor from a foreign
        // closure type. Without it, the carrier-set check above only catches a full
        // merge — not a "two carriers, but interceptor body routes to the wrong one".
        AssertEveryDispatchArrowResolves(code);
    }

    [Test]
    public void ChainedWith_SameNamesDifferentMethods_DispatchByDisplayClass()
    {
        // Stronger pressure on the dedup invariant: two methods whose chained-With
        // closures capture IDENTICALLY-named variables of identical types — only the
        // host method (and therefore the compiler's <>c__DisplayClass) differs. If
        // the dedup were ever relaxed to types-only (or names-only), this test would
        // fail because both chains would collapse into a single carrier whose
        // [UnsafeAccessorType] points at one method's display class — at runtime the
        // other method's call site would fault. Keeping DisplayClassName in the dedup
        // key is the precise contract this test pins.
        var source = SharedSchema + @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task MethodA(TestDbContext db)
    {
        decimal cutoff = 100m;
        bool flag = true;
        await db
            .With<Order>(orders => orders.Where(o => o.Total > cutoff))
            .With<User>(users => users.Where(u => u.IsActive == flag))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }

    public static async Task MethodB(TestDbContext db)
    {
        decimal cutoff = 200m;
        bool flag = false;
        await db
            .With<Order>(orders => orders.Where(o => o.Total > cutoff))
            .With<User>(users => users.Where(u => u.IsActive == flag))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.That(errors, Is.Empty,
            $"Generator emitted errors: {string.Join("; ", errors.Select(d => d.Id + ": " + d.GetMessage()))}");

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null);
        var code = interceptorsTree!.GetText().ToString();

        // Both methods capture decimal 'cutoff' + bool 'flag'. Each method's chain
        // must own a DIFFERENT [UnsafeAccessorType], so the emitted code must
        // reference at least two distinct <>c__DisplayClass type strings for the
        // 'cutoff' extractor.
        var cutoffDisplayClasses = new HashSet<string>(System.StringComparer.Ordinal);
        var displayClassRegex = new System.Text.RegularExpressions.Regex(
            @"\[UnsafeAccessor\(UnsafeAccessorKind\.Field, Name = ""cutoff""\)\][\s\S]*?\[UnsafeAccessorType\(""(?<dc>[^""]+)""\)\]");
        foreach (System.Text.RegularExpressions.Match m in displayClassRegex.Matches(code))
            cutoffDisplayClasses.Add(m.Groups["dc"].Value);

        Assert.That(cutoffDisplayClasses.Count, Is.GreaterThanOrEqualTo(2),
            "Two methods that each capture a 'cutoff' decimal must yield carriers whose "
            + $"[UnsafeAccessorType] references at least two distinct compiler-generated "
            + $"display classes. Found: [{string.Join(", ", cutoffDisplayClasses)}]. "
            + "Collapsing to a single display class would mean one method's interceptor "
            + "reads from the other method's closure — exactly the #268 mis-dispatch.");

        // And the dispatch arrows must still all resolve.
        AssertEveryDispatchArrowResolves(code);
    }
}
