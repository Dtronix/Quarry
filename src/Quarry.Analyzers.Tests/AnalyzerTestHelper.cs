using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Analyzers;

namespace Quarry.Analyzers.Tests;

/// <summary>
/// Helper for running the QuarryQueryAnalyzer against test source code.
/// Uses the full Roslyn compilation pipeline to test analyzer behavior end-to-end.
/// </summary>
internal static class AnalyzerTestHelper
{
    /// <summary>
    /// Compiles the given source with Quarry references and runs the analyzer,
    /// returning all diagnostics with QRA prefix.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Collect all referenced assemblies needed for Quarry types
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };

        // Add System.Runtime for netcore
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRef = System.IO.Path.Combine(runtimeDir, "System.Runtime.dll");
        if (System.IO.File.Exists(runtimeRef))
            references.Add(MetadataReference.CreateFromFile(runtimeRef));

        // Add Quarry assembly
        var quarryAssembly = typeof(Quarry.QuarryContext).Assembly.Location;
        if (!string.IsNullOrEmpty(quarryAssembly))
            references.Add(MetadataReference.CreateFromFile(quarryAssembly));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new QuarryQueryAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics
            .Where(d => d.Id.StartsWith("QRA"))
            .ToImmutableArray();
    }
}
