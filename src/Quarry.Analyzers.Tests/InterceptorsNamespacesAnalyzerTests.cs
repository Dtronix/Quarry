using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Analyzers;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class InterceptorsNamespacesAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source, string? interceptorsNamespaces)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Quarry.QuarryContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var asm in new[] { "System.Runtime.dll", "netstandard.dll", "System.Data.Common.dll" })
        {
            var path = System.IO.Path.Combine(runtimeDir, asm);
            if (System.IO.File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new InterceptorsNamespacesAnalyzer();
        var options = new InterceptorsAnalyzerOptions(interceptorsNamespaces);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, options));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(d => d.Id == "QRY044").ToImmutableArray();
    }

    [Test]
    public async Task NoDiagnostic_WhenContextNamespaceIsOptedIn()
    {
        var source = @"
using Quarry;

namespace MyApp.Data
{
    [QuarryContext(Dialect = SqlDialect.SQLite)]
    public partial class AppDb : QuarryContext
    {
    }
}";

        var diagnostics = await GetDiagnosticsAsync(source, "Quarry.Generated;MyApp.Data");
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task EmitsQRY044_WhenContextNamespaceMissing()
    {
        var source = @"
using Quarry;

namespace MyApp.Data
{
    [QuarryContext(Dialect = SqlDialect.SQLite)]
    public partial class AppDb : QuarryContext
    {
    }
}";

        var diagnostics = await GetDiagnosticsAsync(source, "Quarry.Generated");
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var d = diagnostics[0];
        Assert.That(d.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        var message = d.GetMessage();
        Assert.That(message, Does.Contain("AppDb"));
        Assert.That(message, Does.Contain("MyApp.Data"));
        Assert.That(message, Does.Contain("<InterceptorsNamespaces>"));
    }

    [Test]
    public async Task NoDiagnostic_ForContextInGlobalNamespace()
    {
        // Classes in the global namespace use the Quarry.Generated fallback, which
        // is auto-registered by the shipped Quarry.targets.
        var source = @"
using Quarry;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
}";

        var diagnostics = await GetDiagnosticsAsync(source, "Quarry.Generated");
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task FlagsOnlyMissingNamespaces_WhenMultipleContexts()
    {
        var source = @"
using Quarry;

namespace MyApp.DataA
{
    [QuarryContext(Dialect = SqlDialect.SQLite)]
    public partial class AppDbA : QuarryContext
    {
    }
}

namespace MyApp.DataB
{
    [QuarryContext(Dialect = SqlDialect.SQLite)]
    public partial class AppDbB : QuarryContext
    {
    }
}";

        // Only DataA is opted in.
        var diagnostics = await GetDiagnosticsAsync(source, "Quarry.Generated;MyApp.DataA");
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("AppDbB"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("MyApp.DataB"));
    }

    [Test]
    public async Task NoDiagnostic_ForNonContextClass()
    {
        // Plain class without [QuarryContext] — should be ignored even if namespace is missing.
        var source = @"
namespace MyApp.Data
{
    public class NotAContext
    {
    }
}";

        var diagnostics = await GetDiagnosticsAsync(source, "Quarry.Generated");
        Assert.That(diagnostics, Is.Empty);
    }

    // ── Test harness for injecting a synthetic InterceptorsNamespaces MSBuild value ──

    private sealed class InterceptorsAnalyzerOptions : AnalyzerConfigOptionsProvider
    {
        public InterceptorsAnalyzerOptions(string? interceptorsNamespaces)
        {
            GlobalOptions = new Options(interceptorsNamespaces);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options : AnalyzerConfigOptions
        {
            private readonly string? _interceptorsNamespaces;

            public Options(string? interceptorsNamespaces)
            {
                _interceptorsNamespaces = interceptorsNamespaces;
            }

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (key == "build_property.InterceptorsNamespaces" && _interceptorsNamespaces != null)
                {
                    value = _interceptorsNamespaces;
                    return true;
                }
                value = null;
                return false;
            }
        }
    }
}
