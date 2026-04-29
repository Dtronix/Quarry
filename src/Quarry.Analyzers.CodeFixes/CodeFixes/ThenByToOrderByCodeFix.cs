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
public sealed class ThenByToOrderByCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRA403");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var memberAccess = node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
        if (memberAccess == null) return;

        if (memberAccess.Name.Identifier.Text != "ThenBy") return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Replace ThenBy with OrderBy",
                ct => ReplaceThenByAsync(context.Document, memberAccess, ct),
                equivalenceKey: "QRA403_ThenByToOrderBy"),
            diagnostic);
    }

    private static async Task<Document> ReplaceThenByAsync(
        Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Use WithIdentifier on the existing Name node so that GenericNameSyntax
        // (e.g. .ThenBy<TKey>(...)) preserves its TypeArgumentList and trivia.
        var newName = memberAccess.Name.WithIdentifier(
            SyntaxFactory.Identifier("OrderBy").WithTriviaFrom(memberAccess.Name.Identifier));

        var newMemberAccess = memberAccess.WithName(newName);
        var newRoot = root.ReplaceNode(memberAccess, newMemberAccess);
        return document.WithSyntaxRoot(newRoot);
    }
}
