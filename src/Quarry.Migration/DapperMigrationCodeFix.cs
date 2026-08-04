using System;
using System.Collections.Generic;
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
internal sealed class DapperMigrationCodeFix : CodeFixProvider
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
        var site = detector.TryDetectSingle(semanticModel, invocation);
        if (site == null) return document;

        // Build schema map
        var resolver = new SchemaResolver();
        var schemaMap = resolver.Resolve(semanticModel.Compilation);

        // Translate — try SQLite first, fall back to SQL Server
        var parseResult = DapperMigrationAnalyzer.TryParseWithFallback(site.Sql);
        var emitter = new ChainEmitter(schemaMap);
        var result = emitter.Translate(parseResult, site);

        if (result.ChainCode == null) return document;

        // Safety: never substitute a manual-conversion suggestion (comment text)
        // for the invocation — that produces invalid C#.
        if (result.IsSuggestionOnly) return document;

        // Replace the invocation with the Quarry chain code
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var newExpression = SyntaxFactory.ParseExpression(result.ChainCode)
            .WithTriviaFrom(invocation);

        SyntaxNode updatedRoot;

        if (invocation.Parent is AwaitExpressionSyntax awaitExpr)
        {
            var awaitedChain = SyntaxFactory.ParseExpression($"await {result.ChainCode}")
                .WithTriviaFrom(awaitExpr);
            updatedRoot = root.ReplaceNode(awaitExpr, awaitedChain);
        }
        else
        {
            updatedRoot = root.ReplaceNode(invocation, newExpression);
        }

        // Add missing using directives
        if (updatedRoot is CompilationUnitSyntax compilationUnit)
        {
            updatedRoot = EnsureUsing(compilationUnit, "Quarry");
            updatedRoot = EnsureUsing((CompilationUnitSyntax)updatedRoot, "Quarry.Query");

            // Projected CTEs need DTO types the source does not declare (#331).
            updatedRoot = AddGeneratedTypes(
                (CompilationUnitSyntax)updatedRoot, result.GeneratedTypeDeclarations);
        }

        return document.WithSyntaxRoot(updatedRoot);
    }

    /// <summary>
    /// Appends generated type declarations to the compilation unit, skipping any whose name
    /// is already declared so the fix cannot introduce a duplicate type.
    /// </summary>
    internal static CompilationUnitSyntax AddGeneratedTypes(
        CompilationUnitSyntax root,
        IReadOnlyList<string> declarations)
    {
        if (declarations.Count == 0) return root;

        var existingNames = new HashSet<string>(
            root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text),
            StringComparer.Ordinal);

        foreach (var declarationText in declarations)
        {
            if (SyntaxFactory.ParseMemberDeclaration(declarationText) is not BaseTypeDeclarationSyntax typeDeclaration)
                continue;

            if (!existingNames.Add(typeDeclaration.Identifier.Text))
                continue;

            root = root.AddMembers(typeDeclaration
                .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
        }

        return root;
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
