using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using Quarry.Generators.IR;

namespace Quarry.Tests.IR;

/// <summary>
/// Unit tests for SqlExprAnnotator.InlineConstantCollections, focusing on
/// the safety guards that prevent inlining when the source variable is mutable.
/// </summary>
[TestFixture]
public class SqlExprAnnotatorInliningTests
{
    [Test]
    public void InlineConstantCollections_LocalArray_Inlines()
    {
        var (expr, body, model) = BuildInExprFromSource(@"
class C {
    void M() {
        var statuses = new[] { ""a"", ""b"" };
        System.Func<string, bool> f = x => statuses.Contains(x);
    }
}");
        var result = SqlExprAnnotator.InlineConstantCollections(expr, body, model);

        // Should inline: local is never reassigned
        Assert.That(result, Is.InstanceOf<InExpr>());
        var inExpr = (InExpr)result;
        Assert.That(inExpr.Values, Has.Count.EqualTo(2));
        Assert.That(inExpr.Values[0], Is.InstanceOf<LiteralExpr>());
    }

    [Test]
    public void InlineConstantCollections_ReassignedLocal_DoesNotInline()
    {
        var (expr, body, model) = BuildInExprFromSource(@"
class C {
    void M() {
        var statuses = new[] { ""a"", ""b"" };
        statuses = new[] { ""c"" };
        System.Func<string, bool> f = x => statuses.Contains(x);
    }
}");
        var result = SqlExprAnnotator.InlineConstantCollections(expr, body, model);

        // Should NOT inline: local is reassigned
        Assert.That(result, Is.InstanceOf<InExpr>());
        var inExpr = (InExpr)result;
        Assert.That(inExpr.Values, Has.Count.EqualTo(1));
        Assert.That(inExpr.Values[0], Is.InstanceOf<CapturedValueExpr>(),
            "Reassigned local array should remain as a captured value (parameterized)");
    }

    [Test]
    public void InlineConstantCollections_StaticReadonlyField_Inlines()
    {
        var (expr, body, model) = BuildInExprFromSource(@"
class C {
    private static readonly string[] _statuses = new[] { ""a"", ""b"" };
    void M() {
        System.Func<string, bool> f = x => _statuses.Contains(x);
    }
}");
        var result = SqlExprAnnotator.InlineConstantCollections(expr, body, model);

        Assert.That(result, Is.InstanceOf<InExpr>());
        var inExpr = (InExpr)result;
        Assert.That(inExpr.Values, Has.Count.EqualTo(2));
        Assert.That(inExpr.Values[0], Is.InstanceOf<LiteralExpr>());
    }

    [Test]
    public void InlineConstantCollections_MutableStaticField_DoesNotInline()
    {
        var (expr, body, model) = BuildInExprFromSource(@"
class C {
    private static string[] _statuses = new[] { ""a"", ""b"" };
    void M() {
        System.Func<string, bool> f = x => _statuses.Contains(x);
    }
}");
        var result = SqlExprAnnotator.InlineConstantCollections(expr, body, model);

        Assert.That(result, Is.InstanceOf<InExpr>());
        var inExpr = (InExpr)result;
        Assert.That(inExpr.Values, Has.Count.EqualTo(1));
        Assert.That(inExpr.Values[0], Is.InstanceOf<CapturedValueExpr>(),
            "Mutable static array should remain as a captured value (parameterized)");
    }

    /// <summary>
    /// Parses C# source containing a .Contains() call inside a lambda,
    /// builds an InExpr with a CapturedValueExpr, and returns it along with
    /// the lambda body and semantic model for testing InlineConstantCollections.
    /// </summary>
    private static (SqlExpr expr, ExpressionSyntax lambdaBody, SemanticModel model) BuildInExprFromSource(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        // Find the lambda expression (x => ...)
        var lambda = tree.GetRoot().DescendantNodes()
            .OfType<SimpleLambdaExpressionSyntax>()
            .First();

        var lambdaBody = (ExpressionSyntax)lambda.Body;

        // Find the variable name used in .Contains() — the receiver of .Contains()
        var containsInvocation = lambdaBody.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .First(inv => inv.Expression is MemberAccessExpressionSyntax ma
                          && ma.Name.Identifier.ValueText == "Contains");
        var receiver = ((MemberAccessExpressionSyntax)containsInvocation.Expression).Expression;
        var variableName = receiver switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => "unknown"
        };

        // Build the InExpr matching what the SQL parser would produce
        var capturedValue = new CapturedValueExpr(variableName, "string[]", receiver.ToString());
        var operand = new ColumnRefExpr(null!, "x"); // simplified column ref
        var inExpr = new InExpr(operand, new SqlExpr[] { capturedValue }, isNegated: false);

        return (inExpr, lambdaBody, model);
    }
}
