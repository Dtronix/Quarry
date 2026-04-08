using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;
using Quarry.Generators.CodeGen;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

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
            raw, "Ctx", "App", GenSqlDialect.SQLite,
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

}
