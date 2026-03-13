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
public sealed class SingleInToEqualsCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRA102");

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
                "Replace single-value Contains with ==",
                ct => ReplaceContainsWithEqualsAsync(context.Document, invocation, ct),
                equivalenceKey: "QRA102_SingleInToEquals"),
            diagnostic);
    }

    private static async Task<Document> ReplaceContainsWithEqualsAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var whereInvocation = invocation.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
            {
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                    return ma.Name.Identifier.Text == "Contains";
                return false;
            });

        if (whereInvocation?.Expression is not MemberAccessExpressionSyntax containsAccess)
            return document;

        var collection = containsAccess.Expression;

        if (whereInvocation.ArgumentList.Arguments.Count != 1)
            return document;

        var property = whereInvocation.ArgumentList.Arguments[0].Expression;

        ExpressionSyntax? singleValue = null;

        if (collection is ImplicitArrayCreationExpressionSyntax implicitArray)
        {
            if (implicitArray.Initializer.Expressions.Count == 1)
                singleValue = implicitArray.Initializer.Expressions[0];
        }
        else if (collection is ArrayCreationExpressionSyntax arrayCreation)
        {
            if (arrayCreation.Initializer?.Expressions.Count == 1)
                singleValue = arrayCreation.Initializer.Expressions[0];
        }
        else if (collection is ObjectCreationExpressionSyntax objectCreation)
        {
            if (objectCreation.Initializer?.Expressions.Count == 1)
                singleValue = objectCreation.Initializer.Expressions[0];
        }

        if (singleValue == null)
            return document;

        var equalsExpr = SyntaxFactory.BinaryExpression(
            SyntaxKind.EqualsExpression,
            property,
            singleValue);

        var newRoot = root.ReplaceNode(whereInvocation, equalsExpr.WithTriviaFrom(whereInvocation));
        return document.WithSyntaxRoot(newRoot);
    }
}
