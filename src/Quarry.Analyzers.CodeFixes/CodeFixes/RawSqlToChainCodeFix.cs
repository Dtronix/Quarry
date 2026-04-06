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

namespace Quarry.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class RawSqlToChainCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRY042");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();

        // The chain code is stored in diagnostic properties by the analyzer
        if (!diagnostic.Properties.TryGetValue("ChainCode", out var chainCode) || string.IsNullOrEmpty(chainCode))
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Replace with chain query",
                ct => ReplaceWithChainAsync(context.Document, invocation, chainCode!, ct),
                equivalenceKey: "QRY042_RawSqlToChain"),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithChainAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string chainCode,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Determine the replacement scope.
        // If .ToListAsync() is chained after, replace the entire expression including that call.
        SyntaxNode nodeToReplace = invocation;

        if (invocation.Parent is MemberAccessExpressionSyntax parentMemberAccess
            && parentMemberAccess.Name.Identifier.Text == "ToListAsync"
            && parentMemberAccess.Parent is InvocationExpressionSyntax toListInvocation)
        {
            nodeToReplace = toListInvocation;
        }

        // Parse the chain code into an expression
        var replacement = SyntaxFactory.ParseExpression(chainCode);

        // Preserve trivia from the original node
        replacement = replacement.WithTriviaFrom(nodeToReplace);

        var newRoot = root.ReplaceNode(nodeToReplace, replacement);
        return document.WithSyntaxRoot(newRoot);
    }
}
