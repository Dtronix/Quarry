using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarry.Analyzers;

/// <summary>
/// Warns when a class annotated with <c>[QuarryContext]</c> lives in a namespace not
/// listed in the <c>InterceptorsNamespaces</c> MSBuild property. C# 12's interceptors
/// feature requires every emitting namespace to be explicitly opted in — the Quarry
/// generator emits interceptors into the context's namespace, so missing the opt-in
/// fails the build with CS9137. QRY044 surfaces the exact missing entry at authoring
/// time so authors can act on it before running a build.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class InterceptorsNamespacesAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(AnalyzerDiagnosticDescriptors.InterceptorsNamespaceMissing);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // Fast syntactic pre-check before asking the semantic model for attribute resolution.
        if (!HasQuarryContextAttributeSyntactic(classDecl))
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
        if (symbol is null)
            return;
        if (!HasQuarryContextAttribute(symbol))
            return;

        // Context classes in the global namespace emit their interceptors into the
        // Quarry.Generated fallback namespace (see FileEmitter.cs), which the shipped
        // Quarry.targets auto-registers. Nothing to flag.
        var containingNamespace = symbol.ContainingNamespace;
        if (containingNamespace is null || containingNamespace.IsGlobalNamespace)
            return;

        var namespaceName = containingNamespace.ToDisplayString();

        var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!options.TryGetValue("build_property.InterceptorsNamespaces", out var configured))
            configured = string.Empty;

        var opted = SplitNamespaces(configured);
        if (opted.Contains(namespaceName))
            return;

        var identifier = classDecl.Identifier;
        context.ReportDiagnostic(Diagnostic.Create(
            AnalyzerDiagnosticDescriptors.InterceptorsNamespaceMissing,
            identifier.GetLocation(),
            symbol.Name,
            namespaceName));
    }

    private static bool HasQuarryContextAttributeSyntactic(ClassDeclarationSyntax classDecl)
    {
        foreach (var list in classDecl.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = attr.Name switch
                {
                    QualifiedNameSyntax q => q.Right.Identifier.Text,
                    AliasQualifiedNameSyntax a => a.Name.Identifier.Text,
                    SimpleNameSyntax s => s.Identifier.Text,
                    _ => null
                };
                if (name == "QuarryContext" || name == "QuarryContextAttribute")
                    return true;
            }
        }
        return false;
    }

    private static bool HasQuarryContextAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var cls = attr.AttributeClass;
            if (cls is null) continue;
            if (cls.Name == "QuarryContextAttribute" || cls.Name == "QuarryContext")
                return true;
        }
        return false;
    }

    private static HashSet<string> SplitNamespaces(string value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
            return set;
        foreach (var part in value.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                set.Add(trimmed);
        }
        return set;
    }
}
