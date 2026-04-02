using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;
using Quarry.Generators.Sql;
using Quarry.Shared.Migration;

namespace Quarry.Tests.Parsing;

/// <summary>
/// Unit tests for <see cref="DisplayClassEnricher"/>, verifying batch enrichment
/// of display class names and captured variable types.
/// </summary>
[TestFixture]
public class DisplayClassEnricherTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static RawCallSite CreateSite(string uniqueId, LambdaExpressionSyntax? enrichmentLambda = null)
    {
        var site = new RawCallSite(
            methodName: "Where",
            filePath: "Test.cs",
            line: 1,
            column: 1,
            uniqueId: uniqueId,
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 1, 1, default));
        site.EnrichmentLambda = enrichmentLambda;
        return site;
    }

    [Test]
    public void EnrichAll_EmptyArray_ReturnsEmpty()
    {
        var compilation = CreateCompilation("class C {}");
        var result = DisplayClassEnricher.EnrichAll(
            ImmutableArray<RawCallSite>.Empty, compilation, null!, CancellationToken.None);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnrichAll_SiteWithoutLambda_LeavesDisplayClassNull()
    {
        var compilation = CreateCompilation("class C {}");
        var site = CreateSite("test-1");
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Null);
        Assert.That(result[0].CapturedVariableTypes, Is.Null);
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.None));
    }

    [Test]
    public void EnrichAll_LambdaCapturingLocal_SetsDisplayClassNameAndCapturedTypes()
    {
        var source = @"
using System;
class TestClass
{
    void TestMethod()
    {
        var name = ""test"";
        Func<string, bool> predicate = x => x.Contains(name);
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-1", lambda);
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].DisplayClassName, Does.Contain("<>c__DisplayClass"));
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.ClosureCapture));
        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes!, Does.ContainKey("name"));
    }

    [Test]
    public void EnrichAll_LambdaCapturingStaticField_SetsDisplayClassNameButNotCapturedTypes()
    {
        var source = @"
using System;
class TestClass
{
    private static string SearchTerm = ""test"";
    void TestMethod()
    {
        Func<string, bool> predicate = x => x.Contains(SearchTerm);
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-1", lambda);
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        // DisplayClassName is set (used by code generator for UnsafeAccessor detection)
        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].DisplayClassName, Does.Contain("<>c__DisplayClass"));
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.FieldCapture));
        // CapturedVariableTypes is null because no locals/params are captured
        Assert.That(result[0].CapturedVariableTypes, Is.Null);
    }

    [Test]
    public void EnrichAll_MultipleSitesInSameMethod_SharesAnalysis()
    {
        var source = @"
using System;
class TestClass
{
    void TestMethod()
    {
        var id = 42;
        var name = ""test"";
        Func<int, bool> pred1 = x => x == id;
        Func<string, bool> pred2 = x => x.Contains(name);
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambdas = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().ToArray();

        var site1 = CreateSite("test-1", lambdas[0]);
        var site2 = CreateSite("test-2", lambdas[1]);
        var sites = ImmutableArray.Create(site1, site2);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        // Both sites should have display class names from the same method
        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[1].DisplayClassName, Is.Not.Null);
        // Both should share the same method ordinal prefix
        var prefix0 = result[0].DisplayClassName!.Substring(0, result[0].DisplayClassName!.LastIndexOf('_'));
        var prefix1 = result[1].DisplayClassName!.Substring(0, result[1].DisplayClassName!.LastIndexOf('_'));
        Assert.That(prefix0, Is.EqualTo(prefix1));

        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[1].CapturedVariableTypes, Is.Not.Null);
    }

    [Test]
    public void EnrichAll_LambdaCapturingParameter_SetsCapturedTypes()
    {
        var source = @"
using System;
class TestClass
{
    void TestMethod(int threshold)
    {
        Func<int, bool> predicate = x => x > threshold;
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-1", lambda);
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.ClosureCapture));
        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes!, Does.ContainKey("threshold"));
    }

    [Test]
    public void EnrichAll_LambdaWithNoCapturedVariables_SetsDisplayClassNameOnly()
    {
        var source = @"
using System;
class TestClass
{
    void TestMethod()
    {
        Func<int, bool> predicate = x => x > 5;
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-1", lambda);
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        // DisplayClassName is always set for lambdas with enrichment targets
        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.FieldCapture));
        // No captured variables
        Assert.That(result[0].CapturedVariableTypes, Is.Null);
    }

    [Test]
    public void EnrichAll_SitesInDifferentMethods_ProduceDifferentPrefixes()
    {
        var source = @"
using System;
class TestClass
{
    void Method1()
    {
        var x = 1;
        Func<int, bool> pred = n => n == x;
    }

    void Method2()
    {
        var y = 2;
        Func<int, bool> pred = n => n == y;
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambdas = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().ToArray();

        var site1 = CreateSite("test-1", lambdas[0]);
        var site2 = CreateSite("test-2", lambdas[1]);
        var sites = ImmutableArray.Create(site1, site2);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[1].DisplayClassName, Is.Not.Null);
        // Different methods should produce different display class names
        Assert.That(result[0].DisplayClassName, Is.Not.EqualTo(result[1].DisplayClassName));
    }

    [Test]
    public void EnrichAll_Cancellation_ThrowsOperationCanceledException()
    {
        var source = @"
using System;
class TestClass
{
    void TestMethod()
    {
        var name = ""test"";
        Func<string, bool> predicate = x => x.Contains(name);
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-1", lambda);
        var sites = ImmutableArray.Create(site);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            DisplayClassEnricher.EnrichAll(sites, compilation, null!, cts.Token));
    }

    [Test]
    public void EnrichAll_MixedSitesWithAndWithoutLambda_OnlyEnrichesLambdaSites()
    {
        var source = @"
using System;
class TestClass
{
    void TestMethod()
    {
        var name = ""test"";
        Func<string, bool> predicate = x => x.Contains(name);
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var siteWithLambda = CreateSite("test-1", lambda);
        var siteWithoutLambda = CreateSite("test-2");
        var sites = ImmutableArray.Create(siteWithLambda, siteWithoutLambda);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.ClosureCapture));
        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[1].DisplayClassName, Is.Null);
        Assert.That(result[1].CaptureKind, Is.EqualTo(CaptureKind.None));
        Assert.That(result[1].CapturedVariableTypes, Is.Null);
    }

    [Test]
    public void EnrichAll_LambdaInsideLocalFunction_SetsDisplayClassNameAndCapturedTypes()
    {
        var source = @"
using System;
class TestClass
{
    void OuterMethod()
    {
        DoWork();

        void DoWork()
        {
            var threshold = 10;
            Func<int, bool> predicate = x => x > threshold;
        }
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-local-func", lambda);
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        // Lambda inside a local function should walk up to the containing method
        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].DisplayClassName, Does.Contain("<>c__DisplayClass"));
        Assert.That(result[0].CaptureKind, Is.EqualTo(CaptureKind.ClosureCapture));
        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes!, Does.ContainKey("threshold"));
    }

    [Test]
    public void EnrichAll_CapturedEntityVariable_UsesContextNamespaceNotSchemaNamespace()
    {
        // Simulates the MepSuite scenario: schema in App.Schemas, context in App.Data,
        // captured variable of generated entity type (error type at generator time).
        // The supplemental compilation adds the generated entity class in the context
        // namespace, so the semantic model resolves the type correctly.
        var source = @"
using System;
using App.Schemas;
using App.Data;

namespace App.Schemas
{
    public class FileSchema { }
}

namespace App.Data
{
    public partial class AppDb { }
}

namespace App.Services
{
    class FileService
    {
        void DeleteFile()
        {
            File deletedFile = default!;
            Func<bool> pred = () => deletedFile != null;
        }
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-entity-ns", lambda);
        var sites = ImmutableArray.Create(site);

        // Build an EntityRegistry with File entity in App.Data context
        var mods = new ColumnModifiers();
        var fileEntity = new EntityInfo("File", "FileSchema", "App.Schemas", "files",
            NamingStyleKind.SnakeCase,
            new[] { new ColumnInfo("Id", "id", "long", "long", false, ColumnKind.PrimaryKey, null, mods, isValueType: true) },
            Array.Empty<NavigationInfo>(), Array.Empty<IndexInfo>(), Location.None);
        var contextInfo = new ContextInfo("AppDb", "App.Data", Generators.Sql.SqlDialect.PostgreSQL, null,
            new[] { fileEntity }, new[] { new EntityMapping("Files", fileEntity) }, Location.None);
        var entityRegistry = EntityRegistry.Build(ImmutableArray.Create(contextInfo), CancellationToken.None);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, entityRegistry, CancellationToken.None);

        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes!, Does.ContainKey("deletedFile"));
        // Entity type should resolve to the context namespace (App.Data), not schema namespace (App.Schemas)
        Assert.That(result[0].CapturedVariableTypes!["deletedFile"], Is.EqualTo("global::App.Data.File"));
    }

    [Test]
    public void EnrichAll_CapturedEntityVariable_FallsBackToObjectWithoutEntityRegistry()
    {
        // Without entity registry info, the supplemental compilation cannot include
        // the generated entity class. The unresolved type falls back to "object".
        var source = @"
using System;
using App.Schemas;

namespace App.Schemas
{
    public class FileSchema { }
}

namespace App.Services
{
    class FileService
    {
        void DeleteFile()
        {
            File deletedFile = default!;
            Func<bool> pred = () => deletedFile != null;
        }
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().First();

        var site = CreateSite("test-fallback-ns", lambda);
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, null!, CancellationToken.None);

        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes!, Does.ContainKey("deletedFile"));
        // Without entity registry, type cannot be resolved — falls back to "object"
        Assert.That(result[0].CapturedVariableTypes!["deletedFile"], Is.EqualTo("object"));
    }

    [Test]
    public void ChainResult_ResolvableAccessor_SemanticModelResolvesFullTupleType()
    {
        // When the chain root accessor exists at analysis time, the semantic model
        // resolves the full tuple type through the IQueryBuilder interfaces.
        // This verifies the baseline: no EntityRegistry fallback needed.
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quarry
{
    public interface IQueryBuilder<T> where T : class
    {
        IQueryBuilder<T> Where(Func<T, bool> predicate) => throw new NotImplementedException();
        IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector) => throw new NotImplementedException();
    }

    public interface IQueryBuilder<TEntity, TResult> where TEntity : class
    {
        Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}

namespace App.Schemas
{
    public class PackageSchema
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int Status { get; set; }
    }
}

namespace App
{
    using App.Schemas;

    class Service
    {
        Quarry.IQueryBuilder<PackageSchema> Packages() => throw new NotImplementedException();

        async Task DoWork()
        {
            var fetched = await Packages()
                .Where(p => p.Id == 1)
                .Select(p => (p.Id, p.Status, p.Name))
                .ExecuteFetchFirstOrDefaultAsync();

            Func<bool> capturer = () => fetched != null;
        }
    }
}
";
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
        };
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>()
            .Last();

        var dataFlow = semanticModel.AnalyzeDataFlow(lambda)!;
        Assert.That(dataFlow.Succeeded, Is.True);

        var resolved = DisplayClassNameResolver.CollectCapturedVariableTypes(dataFlow, semanticModel);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!, Does.ContainKey("fetched"));
        // Semantic model resolves the full named tuple — no EntityRegistry needed
        Assert.That(resolved!["fetched"], Is.EqualTo("(long Id, int Status, string Name)"));
    }

    [Test]
    public void ChainResult_GeneratedAccessor_ResolvesViaSupplementalCompilation()
    {
        // Chain root is a GENERATED method (db.Packages() doesn't exist at analysis time).
        // The supplemental compilation adds entity classes and context accessor methods
        // so the semantic model resolves all types natively through IEntityAccessor<T>.
        var source = ChainInterfaceStub + @"
namespace App.Data
{
    public partial class AppDb { }
}

namespace App
{
    using App.Data;

    class Service
    {
        AppDb CreateDb() => throw new NotImplementedException();

        async Task DoWork()
        {
            var db = CreateDb();
            var fetched = await db.Packages()
                .Where(p => p.Id == 1)
                .Select(p => (p.Id, p.Status, p.Name))
                .ExecuteFetchFirstOrDefaultAsync();

            var derivedName = fetched.Name;

            Func<bool> capturer = () => fetched != null && derivedName != null;
        }
    }
}
";
        var registry = BuildTestRegistry();
        var resolved = ResolveChainCaptures(source, registry);

        Assert.That(resolved, Is.Not.Null);

        // fetched: tuple from Select projection resolved natively by semantic model.
        // No "?" suffix: for unconstrained generic TResult?, nullable annotation
        // doesn't affect value types in ToDisplayString(FullyQualifiedFormat).
        Assert.That(resolved!, Does.ContainKey("fetched"));
        Assert.That(resolved!["fetched"], Is.EqualTo("(long Id, int Status, string Name)"));

        // derivedName: resolved natively by the semantic model via member access on tuple
        Assert.That(resolved!, Does.ContainKey("derivedName"));
        Assert.That(resolved!["derivedName"], Is.EqualTo("string"));
    }

    /// <summary>
    /// Helper: builds an EntityRegistry with Package entity columns for chain resolution tests.
    /// </summary>
    private static EntityRegistry BuildTestRegistry(
        string entityName = "Package", string accessorName = "Packages",
        string contextNamespace = "App.Data", params (string Name, string ClrType, bool IsValueType)[] columns)
    {
        var mods = new ColumnModifiers();
        var cols = columns.Length > 0
            ? columns.Select(c => new ColumnInfo(c.Name, c.Name.ToLower(), c.ClrType, c.ClrType,
                false, ColumnKind.Standard, null, mods, isValueType: c.IsValueType)).ToArray()
            : new[]
            {
                new ColumnInfo("Id", "id", "long", "long", false, ColumnKind.PrimaryKey, null, mods, isValueType: true),
                new ColumnInfo("Status", "status", "int", "int", false, ColumnKind.Standard, null, mods, isValueType: true),
                new ColumnInfo("Name", "name", "string", "string", false, ColumnKind.Standard, null, mods),
            };
        var entity = new EntityInfo(entityName, entityName + "Schema", "App.Schemas",
            entityName.ToLower() + "s", NamingStyleKind.SnakeCase, cols,
            Array.Empty<NavigationInfo>(), Array.Empty<IndexInfo>(), Location.None);
        var context = new ContextInfo("AppDb", contextNamespace,
            Generators.Sql.SqlDialect.PostgreSQL, null,
            new[] { entity }, new[] { new EntityMapping(accessorName, entity) }, Location.None);
        return EntityRegistry.Build(ImmutableArray.Create(context), CancellationToken.None);
    }

    /// <summary>
    /// Helper: creates a compilation (with optional supplemental compilation from registry),
    /// finds the last lambda, runs CollectCapturedVariableTypes.
    /// </summary>
    private static Dictionary<string, string>? ResolveChainCaptures(string source, EntityRegistry? registry = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        // Build supplemental compilation from registry (mirrors DisplayClassEnricher behavior)
        if (registry != null)
        {
            var parseOptions = compilation.SyntaxTrees.First().Options as CSharpParseOptions;
            var generatedTrees = new List<SyntaxTree>();
            foreach (var ctx in registry.AllContexts)
            {
                foreach (var entity in ctx.Entities)
                    generatedTrees.Add(CSharpSyntaxTree.ParseText(
                        EntityCodeGenerator.GenerateEntityClass(entity, ctx.Namespace), parseOptions));
                generatedTrees.Add(CSharpSyntaxTree.ParseText(
                    ContextCodeGenerator.GenerateContextClass(ctx), parseOptions));
            }
            if (generatedTrees.Count > 0)
                compilation = (CSharpCompilation)compilation.AddSyntaxTrees(generatedTrees);
        }

        var tree = compilation.SyntaxTrees.First();
        var sm = compilation.GetSemanticModel(tree);
        var lambda = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().Last();
        var dataFlow = sm.AnalyzeDataFlow(lambda)!;
        return DisplayClassNameResolver.CollectCapturedVariableTypes(dataFlow, sm);
    }

    private const string ChainInterfaceStub = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Quarry
{
    public interface IEntityAccessor<T> where T : class
    {
        IQueryBuilder<T> Where(Func<T, bool> predicate) => throw new NotImplementedException();
        IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector) => throw new NotImplementedException();
    }
    public interface IQueryBuilder<T> where T : class
    {
        IQueryBuilder<T> Where(Func<T, bool> predicate) => throw new NotImplementedException();
        IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector) => throw new NotImplementedException();
    }
    public interface IQueryBuilder<TEntity, TResult> where TEntity : class
    {
        Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        Task<TResult> ExecuteFetchFirstAsync(CancellationToken ct = default) => throw new NotImplementedException();
        Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
";

    [Test]
    public void ChainResult_ExecuteFetchAllAsync_ResolvesList()
    {
        var registry = BuildTestRegistry();
        var source = ChainInterfaceStub + @"
namespace App.Data { public partial class AppDb { } }
namespace App
{
    using App.Data;
    class S
    {
        AppDb CreateDb() => throw new NotImplementedException();
        async Task DoWork()
        {
            var db = CreateDb();
            var all = await db.Packages()
                .Select(p => (p.Id, p.Name))
                .ExecuteFetchAllAsync();
            Func<bool> c = () => all != null;
        }
    }
}";
        var resolved = ResolveChainCaptures(source, registry);
        Assert.That(resolved!["all"], Is.EqualTo("global::System.Collections.Generic.List<(long Id, string Name)>"));
    }

    [Test]
    public void ChainResult_ExecuteFetchFirstAsync_ResolvesNonNullable()
    {
        var registry = BuildTestRegistry();
        var source = ChainInterfaceStub + @"
namespace App.Data { public partial class AppDb { } }
namespace App
{
    using App.Data;
    class S
    {
        AppDb CreateDb() => throw new NotImplementedException();
        async Task DoWork()
        {
            var db = CreateDb();
            var first = await db.Packages()
                .Select(p => (p.Id, p.Name))
                .ExecuteFetchFirstAsync();
            Func<bool> c = () => first.Id > 0;
        }
    }
}";
        var resolved = ResolveChainCaptures(source, registry);
        // Non-nullable — ExecuteFetchFirstAsync throws on empty, never returns null
        Assert.That(resolved!["first"], Is.EqualTo("(long Id, string Name)"));
    }

    [Test]
    public void ChainResult_SingleColumnSelect_ResolvesElementType()
    {
        var registry = BuildTestRegistry();
        var source = ChainInterfaceStub + @"
namespace App.Data { public partial class AppDb { } }
namespace App
{
    using App.Data;
    class S
    {
        AppDb CreateDb() => throw new NotImplementedException();
        async Task DoWork()
        {
            var db = CreateDb();
            var name = await db.Packages()
                .Select(p => p.Name)
                .ExecuteFetchFirstAsync();
            Func<bool> c = () => name != null;
        }
    }
}";
        var resolved = ResolveChainCaptures(source, registry);
        Assert.That(resolved!["name"], Is.EqualTo("string"));
    }

    [Test]
    public void ChainResult_NoSelect_FallsBackToObject()
    {
        // Without a Select clause, the chain ends with IQueryBuilder<T> which does
        // not have execution terminals — the semantic model can't resolve the type.
        // Falls back to "object". The carrier analyzer knows the entity type from
        // chain analysis independently.
        var registry = BuildTestRegistry();
        var source = ChainInterfaceStub + @"
namespace App.Data { public partial class AppDb { } }
namespace App
{
    using App.Data;
    class S
    {
        AppDb CreateDb() => throw new NotImplementedException();
        async Task DoWork()
        {
            var db = CreateDb();
            var pkg = await db.Packages()
                .Where(p => p.Id == 1)
                .ExecuteFetchFirstAsync();
            Func<bool> c = () => pkg != null;
        }
    }
}";
        var resolved = ResolveChainCaptures(source, registry);
        Assert.That(resolved!["pkg"], Is.EqualTo("object"));
    }

    [Test]
    public void ChainResult_IdentitySelect_ResolvesEntityType()
    {
        var registry = BuildTestRegistry();
        var source = ChainInterfaceStub + @"
namespace App.Data { public partial class AppDb { } }
namespace App
{
    using App.Data;
    class S
    {
        AppDb CreateDb() => throw new NotImplementedException();
        async Task DoWork()
        {
            var db = CreateDb();
            var pkg = await db.Packages()
                .Where(p => p.Id == 1)
                .Select(p => p)
                .ExecuteFetchFirstOrDefaultAsync();
            Func<bool> c = () => pkg != null;
        }
    }
}";
        var resolved = ResolveChainCaptures(source, registry);
        Assert.That(resolved!["pkg"], Is.EqualTo("global::App.Data.Package"));
    }

    [Test]
    public void ChainResult_MultiContext_DisambiguatesByImportedNamespace()
    {
        // Two contexts in different namespaces both define a User entity.
        // The source file imports Quarry.Tests.Samples (where TestDbContext lives),
        // not Quarry.Tests.Samples.My (where MyDb lives).
        // The supplemental compilation adds User entity to both namespaces;
        // Roslyn resolves the unqualified "User" to the imported namespace.
        var source = @"
using System;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Samples
{
    public partial class TestDbContext { }
}

namespace Quarry.Tests.Samples.My
{
    public partial class MyDb { }
}

namespace App
{
    class Service
    {
        void DoWork()
        {
            User u = default!;
            Func<bool> c = () => u != null;
        }
    }
}
";
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();

        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<LambdaExpressionSyntax>().Last();
        var site = CreateSite("test-multi-ctx", lambda);
        var sites = ImmutableArray.Create(site);

        // Build entity registry with two contexts that both have a User entity
        var mods = new ColumnModifiers();
        var userEntity = new EntityInfo("User", "UserSchema", "Quarry.Tests.Schemas", "users",
            NamingStyleKind.SnakeCase,
            new[] { new ColumnInfo("Id", "id", "long", "long", false, ColumnKind.PrimaryKey, null, mods, isValueType: true) },
            Array.Empty<NavigationInfo>(), Array.Empty<IndexInfo>(), Location.None);

        var ctx1 = new ContextInfo("TestDbContext", "Quarry.Tests.Samples", Generators.Sql.SqlDialect.SQLite, null,
            new[] { userEntity }, new[] { new EntityMapping("Users", userEntity) }, Location.None);
        var ctx2 = new ContextInfo("MyDb", "Quarry.Tests.Samples.My", Generators.Sql.SqlDialect.MySQL, null,
            new[] { userEntity }, new[] { new EntityMapping("Users", userEntity) }, Location.None);
        var entityRegistry = EntityRegistry.Build(
            ImmutableArray.Create(ctx1, ctx2), CancellationToken.None);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, entityRegistry, CancellationToken.None);

        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes!, Does.ContainKey("u"));
        // Resolves to the imported namespace's entity
        Assert.That(result[0].CapturedVariableTypes!["u"], Is.EqualTo("global::Quarry.Tests.Samples.User"));
    }

    [Test]
    public void SupplementalCompilation_GeneratedEntityWithNavigation_ResolvesPropertyTypes()
    {
        // Validates that the supplemental compilation resolves column properties
        // on generated entity classes — including non-trivial types.
        var registry = BuildTestRegistry(
            entityName: "Widget",
            accessorName: "Widgets",
            contextNamespace: "My.App",
            ("WidgetId", "int", true),
            ("Label", "string", false),
            ("Price", "decimal", true));
        var source = ChainInterfaceStub + @"
namespace My.App { public partial class AppDb { } }
namespace Test
{
    using My.App;
    class Svc
    {
        AppDb CreateDb() => throw new NotImplementedException();
        async Task DoWork()
        {
            var db = CreateDb();
            var item = await db.Widgets()
                .Select(w => (w.WidgetId, w.Label, w.Price))
                .ExecuteFetchFirstAsync();
            Func<bool> c = () => item.WidgetId > 0;
        }
    }
}";
        var resolved = ResolveChainCaptures(source, registry);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!, Does.ContainKey("item"));
        Assert.That(resolved!["item"], Is.EqualTo("(int WidgetId, string Label, decimal Price)"));
    }
}
