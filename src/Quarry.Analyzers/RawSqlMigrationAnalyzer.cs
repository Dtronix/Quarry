using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;

namespace Quarry.Analyzers;

/// <summary>
/// Detects RawSqlAsync&lt;T&gt; calls with string literal SQL that can be expressed as chain queries.
/// Emits QRY040 with the generated chain code stored in diagnostic properties for the code fix.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class RawSqlMigrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(AnalyzerDiagnosticDescriptors.RawSqlConvertibleToChain);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var contextCache = new ConcurrentDictionary<string, ContextInfo>(StringComparer.Ordinal);

            // Pre-discover all contexts and their entities (same pattern as QuarryQueryAnalyzer)
            foreach (var syntaxTree in compilationContext.Compilation.SyntaxTrees)
            {
#pragma warning disable RS1030
                var semanticModel = compilationContext.Compilation.GetSemanticModel(syntaxTree);
#pragma warning restore RS1030
                var root = syntaxTree.GetRoot(compilationContext.CancellationToken);

                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!ContextParser.HasQuarryContextAttribute(classDecl))
                        continue;

                    var contextInfo = ContextParser.ParseContext(classDecl, semanticModel, compilationContext.CancellationToken);
                    if (contextInfo == null)
                        continue;

                    contextCache.TryAdd(contextInfo.ClassName, contextInfo);
                }
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, contextCache),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext nodeContext,
        ConcurrentDictionary<string, ContextInfo> contextCache)
    {
        var invocation = (InvocationExpressionSyntax)nodeContext.Node;

        // Must be a member access: something.RawSqlAsync<T>(...)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        // Method name must be RawSqlAsync
        if (memberAccess.Name is not GenericNameSyntax genericName
            || genericName.Identifier.Text != "RawSqlAsync")
            return;

        // Must have at least one argument (the SQL string)
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var firstArg = invocation.ArgumentList.Arguments[0].Expression;

        // SQL must be a string literal
        if (firstArg is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        // Verify the receiver is a QuarryContext via semantic model
        var symbolInfo = nodeContext.SemanticModel.GetSymbolInfo(invocation, nodeContext.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var containingType = methodSymbol.ContainingType;
        if (!InheritsFromQuarryContext(containingType))
            return;

        // Resolve the context info
        ContextInfo? contextInfo = null;
        var receiverType = nodeContext.SemanticModel.GetTypeInfo(memberAccess.Expression, nodeContext.CancellationToken).Type;
        if (receiverType != null)
            contextCache.TryGetValue(receiverType.Name, out contextInfo);

        // For Phase 1: emit diagnostic for any string-literal RawSqlAsync on a QuarryContext.
        // Phase 2 will add convertibility checking before emitting.
        var properties = ImmutableDictionary<string, string?>.Empty;

        // Store the SQL text for later phases
        properties = properties.Add("Sql", literal.Token.ValueText);

        // Store the context class name if resolved
        if (contextInfo != null)
            properties = properties.Add("ContextClass", contextInfo.ClassName);

        var diagnostic = Diagnostic.Create(
            AnalyzerDiagnosticDescriptors.RawSqlConvertibleToChain,
            invocation.GetLocation(),
            properties);

        nodeContext.ReportDiagnostic(diagnostic);
    }

    private static bool InheritsFromQuarryContext(INamedTypeSymbol? type)
    {
        while (type != null)
        {
            if (type.Name == "QuarryContext" &&
                type.ContainingNamespace?.ToDisplayString() == "Quarry")
                return true;

            type = type.BaseType;
        }

        return false;
    }
}
