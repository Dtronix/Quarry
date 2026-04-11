using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
internal sealed class AdoNetMigrationCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRM021", "QRM022");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to Quarry chain API",
                ct => ConvertToQuarryAsync(context.Document, invocation, ct),
                equivalenceKey: "QRM_AdoNetConvertToQuarry"),
            diagnostic);
    }

    private static async Task<Document> ConvertToQuarryAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var detector = new AdoNetDetector();
        var site = detector.TryDetectSingle(semanticModel, invocation);
        if (site == null) return document;

        var resolver = new SchemaResolver();
        var schemaMap = resolver.Resolve(semanticModel.Compilation);

        var result = AdoNetConverter.TranslateWithFallback(site, schemaMap);
        if (result.ChainCode == null) return document;
        if (result.IsSuggestionOnly) return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // For ADO.NET, we replace only the Execute* call with the chain code
        // and add a TODO comment about removing the dead DbCommand setup.
        var todoComment = SyntaxFactory.Comment(
            "// TODO: Remove DbCommand setup above — now using Quarry chain API\r\n");

        var newExpression = SyntaxFactory.ParseExpression(result.ChainCode)
            .WithTriviaFrom(invocation);

        SyntaxNode updatedRoot;

        if (invocation.Parent is AwaitExpressionSyntax awaitExpr)
        {
            var awaitedChain = SyntaxFactory.ParseExpression($"await {result.ChainCode}")
                .WithLeadingTrivia(todoComment)
                .WithTrailingTrivia(awaitExpr.GetTrailingTrivia());
            updatedRoot = root.ReplaceNode(awaitExpr, awaitedChain);
        }
        else
        {
            newExpression = newExpression
                .WithLeadingTrivia(todoComment)
                .WithTrailingTrivia(invocation.GetTrailingTrivia());
            updatedRoot = root.ReplaceNode(invocation, newExpression);
        }

        if (updatedRoot is CompilationUnitSyntax compilationUnit)
        {
            updatedRoot = EnsureUsing(compilationUnit, "Quarry");
            updatedRoot = EnsureUsing((CompilationUnitSyntax)updatedRoot, "Quarry.Query");
        }

        return document.WithSyntaxRoot(updatedRoot);
    }

    private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root, string namespaceName)
    {
        var hasUsing = root.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (hasUsing) return root;

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        return root.AddUsings(usingDirective);
    }
}
