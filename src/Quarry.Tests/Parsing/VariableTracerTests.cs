using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Parsing;

namespace Quarry.Tests.Parsing;

/// <summary>
/// Unit tests for <see cref="VariableTracer"/>, focusing on
/// TraceToChainRoot's Initializers collection.
/// </summary>
[TestFixture]
public class VariableTracerTests
{
    /// <summary>
    /// Creates a minimal compilation with stub builder types that match
    /// VariableTracer.IsBuilderType, without depending on the source generator.
    /// </summary>
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

    /// <summary>
    /// Stub source that defines types whose display names match IsBuilderType checks.
    /// </summary>
    private const string StubTypes = @"
namespace Stubs
{
    public class QueryBuilder<T>
    {
        public QueryBuilder<T> Where(string predicate) => this;
        public QueryBuilder<T> Select(string columns) => this;
        public string ToDiagnostics() => """";
    }

    public class Db
    {
        public QueryBuilder<int> Users() => new QueryBuilder<int>();
    }
}
";

    [Test]
    public void TraceToChainRoot_SingleHop_ReturnsOneInitializer()
    {
        var source = StubTypes + @"
namespace TestApp
{
    public class Service
    {
        public void Test()
        {
            var db = new Stubs.Db();
            var query = db.Users().Where(""active"");
            query.Select(""name"").ToDiagnostics();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the 'query' identifier in 'query.Select("name")'
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var selectInvocation = invocations.First(inv =>
            inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Select");

        var receiver = ((MemberAccessExpressionSyntax)selectInvocation.Expression).Expression;
        var root = VariableTracer.WalkFluentChainRoot(receiver);

        var traceResult = VariableTracer.TraceToChainRoot(root, semanticModel, CancellationToken.None, maxHops: 2);

        Assert.That(traceResult.Traced, Is.True);
        Assert.That(traceResult.Hops, Is.EqualTo(1));
        Assert.That(traceResult.Initializers, Is.Not.Null);
        Assert.That(traceResult.Initializers!, Has.Count.EqualTo(1));
        // The initializer should be the Where invocation expression
        Assert.That(traceResult.Initializers![0], Is.InstanceOf<InvocationExpressionSyntax>());
        Assert.That(traceResult.Initializers[0].ToString(), Does.Contain("Where"));
    }

    [Test]
    public void TraceToChainRoot_TwoHops_ReturnsTwoInitializers()
    {
        var source = StubTypes + @"
namespace TestApp
{
    public class Service
    {
        public void Test()
        {
            var db = new Stubs.Db();
            var query = db.Users().Where(""active"");
            var filtered = query.Select(""name"");
            filtered.ToDiagnostics();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find 'filtered' identifier in 'filtered.ToDiagnostics()'
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var terminalInvocation = invocations.First(inv =>
            inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "ToDiagnostics");

        var receiver = ((MemberAccessExpressionSyntax)terminalInvocation.Expression).Expression;
        var root = VariableTracer.WalkFluentChainRoot(receiver);

        var traceResult = VariableTracer.TraceToChainRoot(root, semanticModel, CancellationToken.None, maxHops: 2);

        Assert.That(traceResult.Traced, Is.True);
        Assert.That(traceResult.Hops, Is.EqualTo(2));
        Assert.That(traceResult.Initializers, Is.Not.Null);
        Assert.That(traceResult.Initializers!, Has.Count.EqualTo(2));
        // First initializer (hop 1): query.Select("name")
        Assert.That(traceResult.Initializers[0].ToString(), Does.Contain("Select"));
        // Second initializer (hop 2): db.Users().Where("active")
        Assert.That(traceResult.Initializers[1].ToString(), Does.Contain("Where"));
    }

    [Test]
    public void TraceToChainRoot_NoHops_InitializersIsNull()
    {
        var source = StubTypes + @"
namespace TestApp
{
    public class Service
    {
        public void Test()
        {
            var db = new Stubs.Db();
            // Direct fluent chain — no variable hop
            db.Users().Select(""name"").ToDiagnostics();
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the ToDiagnostics invocation
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var terminalInvocation = invocations.First(inv =>
            inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "ToDiagnostics");

        var receiver = ((MemberAccessExpressionSyntax)terminalInvocation.Expression).Expression;
        var root = VariableTracer.WalkFluentChainRoot(receiver);

        var traceResult = VariableTracer.TraceToChainRoot(root, semanticModel, CancellationToken.None, maxHops: 2);

        Assert.That(traceResult.Traced, Is.False);
        Assert.That(traceResult.Hops, Is.EqualTo(0));
        Assert.That(traceResult.Initializers, Is.Null);
    }
}
