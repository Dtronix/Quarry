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

        var currentName = memberAccess.Name.Identifier.Text;
        var newName = currentName switch
        {
            "ThenBy" => "OrderBy",
            "ThenByDescending" => "OrderByDescending",
            _ => null
        };
        if (newName == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Replace {currentName} with {newName}",
                ct => ReplaceThenByAsync(context.Document, memberAccess, newName, ct),
                equivalenceKey: "QRA403_ThenByToOrderBy"),
            diagnostic);
    }

    private static async Task<Document> ReplaceThenByAsync(
        Document document, MemberAccessExpressionSyntax memberAccess, string newName, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName(newName).WithTriviaFrom(memberAccess.Name));

        var newRoot = root.ReplaceNode(memberAccess, newMemberAccess);
        return document.WithSyntaxRoot(newRoot);
    }
}
