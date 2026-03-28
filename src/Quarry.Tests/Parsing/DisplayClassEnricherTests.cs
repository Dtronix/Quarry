using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;

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
            ImmutableArray<RawCallSite>.Empty, compilation, CancellationToken.None);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EnrichAll_SiteWithoutLambda_LeavesDisplayClassNull()
    {
        var compilation = CreateCompilation("class C {}");
        var site = CreateSite("test-1");
        var sites = ImmutableArray.Create(site);

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Null);
        Assert.That(result[0].CapturedVariableTypes, Is.Null);
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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].DisplayClassName, Does.Contain("<>c__DisplayClass"));
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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

        // DisplayClassName is set (used by code generator for UnsafeAccessor detection)
        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].DisplayClassName, Does.Contain("<>c__DisplayClass"));
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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

        // DisplayClassName is always set for lambdas with enrichment targets
        Assert.That(result[0].DisplayClassName, Is.Not.Null);
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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

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
            DisplayClassEnricher.EnrichAll(sites, compilation, cts.Token));
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

        var result = DisplayClassEnricher.EnrichAll(sites, compilation, CancellationToken.None);

        Assert.That(result[0].DisplayClassName, Is.Not.Null);
        Assert.That(result[0].CapturedVariableTypes, Is.Not.Null);
        Assert.That(result[1].DisplayClassName, Is.Null);
        Assert.That(result[1].CapturedVariableTypes, Is.Null);
    }
}
