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
public sealed class CountToAnyCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRA101");

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
                "Replace Count() comparison with Any()",
                ct => ReplaceCountWithAnyAsync(context.Document, invocation, ct),
                equivalenceKey: "QRA101_CountToAny"),
            diagnostic);
    }

    private static async Task<Document> ReplaceCountWithAnyAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var parent = invocation.Parent;
        while (parent is ParenthesizedExpressionSyntax)
            parent = parent.Parent;

        if (parent is not BinaryExpressionSyntax binary)
            return document;

        var isNegated = binary.Kind() == SyntaxKind.EqualsExpression;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        var methodName = memberAccess.Name.Identifier.Text;
        var newMethodName = methodName.Contains("Async") ? "AnyAsync" : "Any";

        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName(newMethodName));
        var anyCall = invocation.WithExpression(newMemberAccess);

        ExpressionSyntax replacement = anyCall;
        if (isNegated)
        {
            replacement = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression, replacement);
        }

        var newRoot = root.ReplaceNode(binary, replacement.WithTriviaFrom(binary));
        return document.WithSyntaxRoot(newRoot);
    }
}
