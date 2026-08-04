using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    // ── RawSqlToChainCodeFix ──

    [Test]
    public void RawSqlToChainCodeFix_FixesCorrectDiagnosticId()
    {
        var fix = new RawSqlToChainCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRY042"));
    }

    [Test]
    public void RawSqlToChainCodeFix_HasFixAllProvider()
    {
        var fix = new RawSqlToChainCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    [Test]
    public async Task RawSqlToChainCodeFix_RegistersCodeFix()
    {
        var fix = new RawSqlToChainCodeFix();
        var source = @"class C { void M() { db.RawSqlAsync<User>(""SELECT * FROM users""); } }";
        var tree = CSharpSyntaxTree.ParseText(source);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.RawSqlConvertibleToChain;
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("ChainCode", "db.Users()\n    .Select(u => u)\n    .ToAsyncEnumerable()");

        var diagnostic = Diagnostic.Create(descriptor, invocation.GetLocation(), properties);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);
        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Title, Is.EqualTo("Replace with chain query"));
    }

    [Test]
    public async Task RawSqlToChainCodeFix_InsertsSynthesizedCteDto()
    {
        // A projected CTE needs a DTO type that does not exist in the source, so the fix
        // must add the declaration as well as replacing the expression (#331).
        var fix = new RawSqlToChainCodeFix();
        var source = @"class C { void M() { db.RawSqlAsync<User>(""WITH a AS (SELECT x FROM t) SELECT x FROM a""); } }";
        var tree = CSharpSyntaxTree.ParseText(source);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.RawSqlConvertibleToChain;
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("ChainCode", "db.With<T, A>(t => t.Select(x => new A { X = x.X }))\n    .FromCte<A>()\n    .Select(a => a.X)\n    .ToAsyncEnumerable()")
            .Add("DtoDeclarations", "public class A\n{\n    public int X { get; set; }\n}");

        var diagnostic = Diagnostic.Create(descriptor, invocation.GetLocation(), properties);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);
        Assert.That(actions, Has.Count.EqualTo(1));

        var operations = await actions[0].GetOperationsAsync(default);
        var applied = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applied.ChangedSolution.GetDocument(document.Id)!;
        var text = (await changedDocument.GetTextAsync()).ToString();

        Assert.That(text, Does.Contain(".FromCte<A>()"));
        Assert.That(text, Does.Contain("public class A"));
        Assert.That(text, Does.Contain("public int X { get; set; }"));
    }

    [Test]
    public async Task RawSqlToChainCodeFix_SkipsDtoWhenTypeAlreadyDeclared()
    {
        // The fix must never introduce a duplicate type declaration.
        var fix = new RawSqlToChainCodeFix();
        var source = @"class A { public int X { get; set; } }
class C { void M() { db.RawSqlAsync<User>(""WITH a AS (SELECT x FROM t) SELECT x FROM a""); } }";
        var tree = CSharpSyntaxTree.ParseText(source);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.RawSqlConvertibleToChain;
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("ChainCode", "db.FromCte<A>()\n    .Select(a => a.X)\n    .ToAsyncEnumerable()")
            .Add("DtoDeclarations", "public class A\n{\n    public int X { get; set; }\n}");

        var diagnostic = Diagnostic.Create(descriptor, invocation.GetLocation(), properties);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);

        var operations = await actions[0].GetOperationsAsync(default);
        var applied = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applied.ChangedSolution.GetDocument(document.Id)!;
        var changedRoot = (await changedDocument.GetSyntaxRootAsync())!;

        var classCount = changedRoot.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Count(c => c.Identifier.Text == "A");
        Assert.That(classCount, Is.EqualTo(1));
    }

    // ── ThenByToOrderByCodeFix ──

    [Test]
    public void ThenByToOrderByCodeFix_FixesCorrectDiagnosticId()
    {
        var fix = new ThenByToOrderByCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRA403"));
    }

    [Test]
    public void ThenByToOrderByCodeFix_HasFixAllProvider()
    {
        var fix = new ThenByToOrderByCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    [Test]
    public async Task ThenByToOrderByCodeFix_RegistersCodeFix()
    {
        var fix = new ThenByToOrderByCodeFix();
        var source = "class C { void M(dynamic db) { db.Users().ThenBy(x => x.Id); } }";
        var tree = CSharpSyntaxTree.ParseText(source);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy;
        var root = await tree.GetRootAsync();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.Text == "ThenBy");
        var diagnostic = Diagnostic.Create(descriptor, memberAccess.Name.GetLocation());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);
        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Title, Is.EqualTo("Replace ThenBy with OrderBy"));
    }

    [Test]
    public async Task ThenByToOrderByCodeFix_AppliedFix_RewritesMethodName()
    {
        var fix = new ThenByToOrderByCodeFix();
        var source = "class C { void M(dynamic db) { db.Users().ThenBy(x => x.Id); } }";
        var tree = CSharpSyntaxTree.ParseText(source);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy;
        var root = await tree.GetRootAsync();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.Text == "ThenBy");
        var diagnostic = Diagnostic.Create(descriptor, memberAccess.Name.GetLocation());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);
        var operations = await actions[0].GetOperationsAsync(default);
        var applyChanges = operations.OfType<ApplyChangesOperation>().First();
        var newDoc = applyChanges.ChangedSolution.GetDocument(document.Id)!;
        var newText = (await newDoc.GetTextAsync()).ToString();

        Assert.That(newText, Does.Contain(".OrderBy(x => x.Id)"));
        Assert.That(newText, Does.Not.Contain("ThenBy"));
    }

    [Test]
    public async Task ThenByToOrderByCodeFix_GenericName_PreservesTypeArguments()
    {
        // Guards against silently dropping the explicit TypeArgumentList on the GenericNameSyntax
        // form (.ThenBy<int>(...)). The fix must rewrite only the identifier, not the whole name.
        var fix = new ThenByToOrderByCodeFix();
        var source = "class C { void M(dynamic db) { db.Users().ThenBy<int>(x => x.Id); } }";
        var tree = CSharpSyntaxTree.ParseText(source);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var descriptor = Quarry.Analyzers.AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy;
        var root = await tree.GetRootAsync();
        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(m => m.Name.Identifier.Text == "ThenBy");
        var diagnostic = Diagnostic.Create(descriptor, memberAccess.Name.GetLocation());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), default);

        await fix.RegisterCodeFixesAsync(context);
        var operations = await actions[0].GetOperationsAsync(default);
        var applyChanges = operations.OfType<ApplyChangesOperation>().First();
        var newDoc = applyChanges.ChangedSolution.GetDocument(document.Id)!;
        var newText = (await newDoc.GetTextAsync()).ToString();

        Assert.That(newText, Does.Contain(".OrderBy<int>(x => x.Id)"));
        Assert.That(newText, Does.Not.Contain("ThenBy"));
    }
}
