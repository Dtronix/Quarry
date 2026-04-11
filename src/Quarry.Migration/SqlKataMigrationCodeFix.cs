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
internal sealed class SqlKataMigrationCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRM031", "QRM032");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Find the ObjectCreationExpression or its containing chain expression
        var creation = node.AncestorsAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        if (creation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to Quarry chain API",
                ct => ConvertToQuarryAsync(context.Document, creation, ct),
                equivalenceKey: "QRM_SqlKataConvertToQuarry"),
            diagnostic);
    }

    private static async Task<Document> ConvertToQuarryAsync(
        Document document,
        ObjectCreationExpressionSyntax creation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var detector = new SqlKataDetector();
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot == null) return document;

        var sites = detector.Detect(semanticModel, syntaxRoot);
        // Find the site that corresponds to this creation
        var site = sites.FirstOrDefault(s =>
            s.ChainExpression.Span.Contains(creation.Span));
        if (site == null) return document;

        var resolver = new SchemaResolver();
        var schemaMap = resolver.Resolve(semanticModel.Compilation);

        var result = SqlKataConverter.Translate(site, schemaMap);
        if (result.ChainCode == null) return document;

        var root = syntaxRoot;
        var newExpression = SyntaxFactory.ParseExpression(result.ChainCode)
            .WithTriviaFrom(site.ChainExpression);

        SyntaxNode updatedRoot;

        if (site.ChainExpression.Parent is AwaitExpressionSyntax awaitExpr)
        {
            var awaitedChain = SyntaxFactory.ParseExpression($"await {result.ChainCode}")
                .WithTriviaFrom(awaitExpr);
            updatedRoot = root.ReplaceNode(awaitExpr, awaitedChain);
        }
        else
        {
            updatedRoot = root.ReplaceNode(site.ChainExpression, newExpression);
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
