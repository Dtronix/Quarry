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
using Quarry.Migration;

namespace Quarry.Migration.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class DapperMigrationCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("QRM001", "QRM002");

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
                equivalenceKey: "QRM_ConvertToQuarry"),
            diagnostic);
    }

    private static async Task<Document> ConvertToQuarryAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        // Detect the Dapper call
        var detector = new DapperDetector();
        var sites = detector.Detect(semanticModel, invocation);
        if (sites.Count == 0) return document;

        var site = sites[0];

        // Build schema map
        var resolver = new SchemaResolver();
        var schemaMap = resolver.Resolve(semanticModel.Compilation);

        // Translate
        var parseResult = Quarry.Shared.Sql.Parser.SqlParser.Parse(site.Sql, SqlDialect.SQLite);
        var emitter = new ChainEmitter(schemaMap);
        var result = emitter.Translate(parseResult, site);

        if (result.ChainCode == null) return document;

        // Replace the invocation with the Quarry chain code
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Find the statement containing the invocation to replace the full expression
        // The Dapper call is typically: await connection.QueryAsync<T>("SQL", new { params })
        // Replace just the invocation expression
        var newExpression = SyntaxFactory.ParseExpression(result.ChainCode)
            .WithTriviaFrom(invocation);

        // If the invocation is part of an await expression, we need to handle that
        var parent = invocation.Parent;
        SyntaxNode nodeToReplace = invocation;

        if (parent is AwaitExpressionSyntax awaitExpr)
        {
            // The await is part of the expression — replace the await expression
            // since Quarry chains end with async methods that need await
            var awaitedChain = SyntaxFactory.ParseExpression($"await {result.ChainCode}")
                .WithTriviaFrom(awaitExpr);
            var newRoot = root.ReplaceNode(awaitExpr, awaitedChain);
            return document.WithSyntaxRoot(newRoot);
        }

        var updatedRoot = root.ReplaceNode(invocation, newExpression);
        return document.WithSyntaxRoot(updatedRoot);
    }
}
