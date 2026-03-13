using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class RemoveUnusedJoinCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRA201");

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
                "Remove unused join",
                ct => RemoveJoinAsync(context.Document, invocation, ct),
                equivalenceKey: "QRA201_RemoveUnusedJoin"),
            diagnostic);
    }

    private static async Task<Document> RemoveJoinAsync(
        Document document, InvocationExpressionSyntax joinInvocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        if (joinInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        var receiver = memberAccess.Expression;

        var newRoot = root.ReplaceNode(joinInvocation, receiver.WithTriviaFrom(joinInvocation));
        return document.WithSyntaxRoot(newRoot);
    }
}
