using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Quarry.Analyzers.CodeFixes;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class CodeFixTests
{
    // ── CountToAnyCodeFix ──

    [Test]
    public void CountToAnyCodeFix_FixesCorrectDiagnosticId()
    {
        var fix = new CountToAnyCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRA101"));
    }

    [Test]
    public void CountToAnyCodeFix_HasFixAllProvider()
    {
        var fix = new CountToAnyCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── SingleInToEqualsCodeFix ──

    [Test]
    public void SingleInToEqualsCodeFix_FixesCorrectDiagnosticId()
    {
        var fix = new SingleInToEqualsCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRA102"));
    }

    [Test]
    public void SingleInToEqualsCodeFix_HasFixAllProvider()
    {
        var fix = new SingleInToEqualsCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── RemoveUnusedJoinCodeFix ──

    [Test]
    public void RemoveUnusedJoinCodeFix_FixesCorrectDiagnosticId()
    {
        var fix = new RemoveUnusedJoinCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRA201"));
    }

    [Test]
    public void RemoveUnusedJoinCodeFix_HasFixAllProvider()
    {
        var fix = new RemoveUnusedJoinCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── Functional code fix tests ──

    [Test]
    public async Task CountToAnyCodeFix_RegistersCodeFix()
    {
        var fix = new CountToAnyCodeFix();
        var source = "class C { bool M() { return Count() > 0; } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        // Create a diagnostic at the Count() invocation location
        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.CountComparedToZero;
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();
        var diagnostic = Diagnostic.Create(descriptor, invocation.GetLocation(), ">");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);
        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Title, Does.Contain("Any"));
    }
}
