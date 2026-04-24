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
/// <remarks>
/// Roslyn's editorconfig key-value parser treats <c>;</c> (and <c>#</c>) inside a value
/// as an inline-comment marker, silently truncating the value at the first such character.
/// That breaks a direct read of <c>build_property.InterceptorsNamespaces</c> whenever the
/// evaluated MSBuild value contains more than one namespace — which is the normal case
/// once <c>Quarry.targets</c> appends <c>Quarry.Generated</c>. To work around this the
/// analyzer prefers the Quarry-exposed <c>QuarryInterceptorsNamespaces</c> property
/// whose value is a <c>|</c>-delimited form of the same list (set by <c>Quarry.targets</c>
/// and <c>Quarry.Generator.props</c>). It falls back to the raw <c>InterceptorsNamespaces</c>
/// property only when the alt form is absent (e.g. consumer pinned to an older Quarry
/// version that didn't expose it).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class InterceptorsNamespacesAnalyzer : DiagnosticAnalyzer
{
    private const string PipeDelimitedPropertyKey = "build_property.QuarryInterceptorsNamespaces";
    private const string SemicolonDelimitedPropertyKey = "build_property.InterceptorsNamespaces";

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
        var opted = ReadOptedNamespaces(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        if (opted.Contains(namespaceName))
            return;

        var identifier = classDecl.Identifier;
        context.ReportDiagnostic(Diagnostic.Create(
            AnalyzerDiagnosticDescriptors.InterceptorsNamespaceMissing,
            identifier.GetLocation(),
            symbol.Name,
            namespaceName));
    }

    private static HashSet<string> ReadOptedNamespaces(AnalyzerConfigOptions options)
    {
        // Prefer the pipe-delimited alt property. Roslyn's editorconfig key-value regex
        // truncates values at the first `;`, so the raw `InterceptorsNamespaces` value
        // can be read wrong when it contains multiple entries. `|` is never a legal C#
        // namespace character, so the `;` → `|` substitution is lossless.
        if (options.TryGetValue(PipeDelimitedPropertyKey, out var pipeValue) &&
            !string.IsNullOrWhiteSpace(pipeValue))
        {
            return SplitNamespaces(pipeValue, '|');
        }

        // Legacy path: consumer without a Quarry version new enough to expose the alt
        // property. Will under-read when the value has multiple entries, but that's
        // what we're hardening against, not a guarantee we provide.
        if (options.TryGetValue(SemicolonDelimitedPropertyKey, out var semiValue))
        {
            return SplitNamespaces(semiValue, ';');
        }

        return new HashSet<string>(StringComparer.Ordinal);
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

    private static HashSet<string> SplitNamespaces(string? value, char separator)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
            return set;
        foreach (var part in value!.Split(separator))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                set.Add(trimmed);
        }
        return set;
    }
}
