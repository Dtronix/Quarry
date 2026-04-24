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
        string source,
        string? interceptorsNamespaces,
        string? quarryInterceptorsNamespaces = null)
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
        var options = new InterceptorsAnalyzerOptions(interceptorsNamespaces, quarryInterceptorsNamespaces);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, options));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(d => d.Id == "QRY044").ToImmutableArray();
    }

    private const string SingleContextSource = @"
using Quarry;

namespace MyApp.Data
{
    [QuarryContext(Dialect = SqlDialect.SQLite)]
    public partial class AppDb : QuarryContext
    {
    }
}";

    // ── Pipe-delimited (preferred) property ──

    [Test]
    public async Task NoDiagnostic_WhenPipeValueContainsTargetNamespace()
    {
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "MyApp.Data|Quarry.Generated");
        Assert.That(d, Is.Empty);
    }

    [Test]
    public async Task NoDiagnostic_WhenPipeValueHasLeadingPipeFromEmptyUpstream()
    {
        // Reproduces the issue #264 failure shape: the raw `InterceptorsNamespaces`
        // ends up `;MyApp.Data;Quarry.Generated` when the consumer writes
        // `$(InterceptorsNamespaces);MyApp.Data` with an empty upstream, and the
        // alt property is the `|`-substituted form `|MyApp.Data|Quarry.Generated`.
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "|MyApp.Data|Quarry.Generated");
        Assert.That(d, Is.Empty);
    }

    [Test]
    public async Task NoDiagnostic_WhenPipeValueHasTargetNotLast()
    {
        // Issue #264 row 2: upstream packages contribute earlier entries, so the
        // target namespace sits in the middle of a 3+ entry list.
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Logsmith.Generated|MyApp.Data|Quarry.Generated");
        Assert.That(d, Is.Empty);
    }

    [Test]
    public async Task NoDiagnostic_WhenPipeValueHasDuplicateEntries()
    {
        // Issue #264 row 3: consumer manually wrote `Quarry.Generated` and then
        // `Quarry.targets` auto-appended it again.
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Logsmith.Generated|Quarry.Generated|MyApp.Data|Quarry.Generated");
        Assert.That(d, Is.Empty);
    }

    [Test]
    public async Task EmitsQRY044_WhenPipeValueLacksTarget()
    {
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Quarry.Generated");
        Assert.That(d, Has.Length.EqualTo(1));
        Assert.That(d[0].GetMessage(), Does.Contain("MyApp.Data"));
    }

    // ── Legacy fallback (semicolon-delimited, only property available) ──

    [Test]
    public async Task LegacyFallback_NoDiagnostic_WhenTargetPresent()
    {
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: "MyApp.Data;Quarry.Generated",
            quarryInterceptorsNamespaces: null);
        Assert.That(d, Is.Empty);
    }

    [Test]
    public async Task LegacyFallback_EmitsQRY044_WhenTargetAbsent()
    {
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: "Quarry.Generated",
            quarryInterceptorsNamespaces: null);
        Assert.That(d, Has.Length.EqualTo(1));
        Assert.That(d[0].GetMessage(), Does.Contain("MyApp.Data"));
    }

    [Test]
    public async Task PipeValueWinsOverLegacyWhenBothAreSet()
    {
        // Simulates the case where both properties are exposed (standard config).
        // The pipe property has the target; the semicolon property is truncated
        // (what Roslyn's parser would give us for `;MyApp.Data;Quarry.Generated`).
        // The analyzer must trust the pipe property and not fire.
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: "",
            quarryInterceptorsNamespaces: "|MyApp.Data|Quarry.Generated");
        Assert.That(d, Is.Empty);
    }

    // ── Unchanged behavior (existing coverage) ──

    [Test]
    public async Task EmitsQRY044_WhenNeitherPropertySet()
    {
        var d = await GetDiagnosticsAsync(SingleContextSource,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: null);
        Assert.That(d, Has.Length.EqualTo(1));
        Assert.That(d[0].GetMessage(), Does.Contain("MyApp.Data"));
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

        var diagnostics = await GetDiagnosticsAsync(source,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Quarry.Generated");
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
        var diagnostics = await GetDiagnosticsAsync(source,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Quarry.Generated|MyApp.DataA");
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("AppDbB"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("MyApp.DataB"));
    }

    [Test]
    public async Task EmitsQRY044_WhenAttributeUsesAliasQualifiedName()
    {
        // `[global::QuarryContextAttribute]` decomposes into AliasQualifiedNameSyntax at the
        // attribute's top-level name, which the syntactic pre-filter must handle explicitly —
        // otherwise the analyzer silently skips the class and the diagnostic never fires.
        // This also covers extern-alias-qualified attribute forms.
        var source = @"
using Quarry;
using QuarryContextAttribute = Quarry.QuarryContextAttribute;

namespace MyApp.Data
{
    [global::QuarryContextAttribute(Dialect = SqlDialect.SQLite)]
    public partial class AppDb : QuarryContext
    {
    }
}";

        var diagnostics = await GetDiagnosticsAsync(source,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Quarry.Generated");
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("AppDb"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("MyApp.Data"));
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

        var diagnostics = await GetDiagnosticsAsync(source,
            interceptorsNamespaces: null,
            quarryInterceptorsNamespaces: "Quarry.Generated");
        Assert.That(diagnostics, Is.Empty);
    }

    // ── Test harness for injecting synthetic MSBuild property values ──

    private sealed class InterceptorsAnalyzerOptions : AnalyzerConfigOptionsProvider
    {
        public InterceptorsAnalyzerOptions(string? interceptorsNamespaces, string? quarryInterceptorsNamespaces)
        {
            GlobalOptions = new Options(interceptorsNamespaces, quarryInterceptorsNamespaces);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options : AnalyzerConfigOptions
        {
            private readonly string? _interceptorsNamespaces;
            private readonly string? _quarryInterceptorsNamespaces;

            public Options(string? interceptorsNamespaces, string? quarryInterceptorsNamespaces)
            {
                _interceptorsNamespaces = interceptorsNamespaces;
                _quarryInterceptorsNamespaces = quarryInterceptorsNamespaces;
            }

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                switch (key)
                {
                    case "build_property.InterceptorsNamespaces" when _interceptorsNamespaces != null:
                        value = _interceptorsNamespaces;
                        return true;
                    case "build_property.QuarryInterceptorsNamespaces" when _quarryInterceptorsNamespaces != null:
                        value = _quarryInterceptorsNamespaces;
                        return true;
                    default:
                        value = null;
                        return false;
                }
            }
        }
    }
}
