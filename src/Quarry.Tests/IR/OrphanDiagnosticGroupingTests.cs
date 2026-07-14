using System.Collections.Immutable;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Tests.IR;

/// <summary>
/// Pins the orphan-diagnostic guarantee (#311 review F4): deferred diagnostics reach
/// the user through (context, file) groups, so a diagnostic attached to a file that
/// produces no <see cref="FileInterceptorGroup"/> — e.g. every site context-less —
/// must be collected into the synthetic "OrphanDiagnostics" group instead of
/// silently vanishing.
/// </summary>
[TestFixture]
public class OrphanDiagnosticGroupingTests
{
    [Test]
    public void DiagnosticForFileWithNoGroup_LandsInSyntheticOrphanGroup()
    {
        // A non-analyzable, context-less site: CollectTranslatedDiagnostics emits
        // QRY001 for it, but the empty ContextClassName excludes it from file
        // grouping, so no group claims "Orphan.cs".
        var raw = new RawCallSite(
            methodName: "Where",
            filePath: "Orphan.cs",
            line: 5, column: 9,
            uniqueId: "orphan_001",
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: "TestApp.User",
            resultTypeName: null,
            isAnalyzable: false,
            nonAnalyzableReason: "variable receiver",
            interceptableLocationData: "fake",
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Orphan.cs", 5, 9, default),
            chainId: null);

        var bound = new BoundCallSite(
            raw, contextClassName: "", contextNamespace: "TestApp",
            new SqlDialectConfig(Quarry.Generators.Sql.SqlDialect.SQLite), "users", null,
            EntityRef.Empty("TestApp.User"));

        var sites = ImmutableArray.Create(new TranslatedCallSite(bound));
        var registry = EntityRegistry.Build(
            ImmutableArray<ContextInfo>.Empty, System.Threading.CancellationToken.None);

        var groups = PipelineOrchestrator.AnalyzeAndGroupTranslated(
            sites, registry, System.Threading.CancellationToken.None);

        var orphan = groups.FirstOrDefault(g => g.FileTag == "OrphanDiagnostics");
        Assert.That(orphan, Is.Not.Null,
            "a diagnostic whose file has no interceptor group must be collected into the synthetic group");
        Assert.That(orphan!.Sites, Is.Empty);
        Assert.That(orphan.Diagnostics.Select(d => d.DiagnosticId), Does.Contain("QRY001"));
    }

    [Test]
    public void DiagnosticForGroupedFile_NotDuplicatedIntoOrphanGroup()
    {
        // Same shape but WITH a context: the file gets a real group, the diagnostic is
        // claimed by it, and no synthetic group should appear.
        var raw = new RawCallSite(
            methodName: "Where",
            filePath: "Claimed.cs",
            line: 5, column: 9,
            uniqueId: "claimed_001",
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: "TestApp.User",
            resultTypeName: null,
            isAnalyzable: false,
            nonAnalyzableReason: "variable receiver",
            interceptableLocationData: "fake",
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Claimed.cs", 5, 9, default),
            chainId: null);

        var bound = new BoundCallSite(
            raw, contextClassName: "TestDbContext", contextNamespace: "TestApp",
            new SqlDialectConfig(Quarry.Generators.Sql.SqlDialect.SQLite), "users", null,
            EntityRef.Empty("TestApp.User"));

        var sites = ImmutableArray.Create(new TranslatedCallSite(bound));
        var registry = EntityRegistry.Build(
            ImmutableArray<ContextInfo>.Empty, System.Threading.CancellationToken.None);

        var groups = PipelineOrchestrator.AnalyzeAndGroupTranslated(
            sites, registry, System.Threading.CancellationToken.None);

        Assert.That(groups.Any(g => g.FileTag == "OrphanDiagnostics"), Is.False,
            "diagnostics claimed by a real group must not spawn the synthetic group");
        var claimed = groups.FirstOrDefault(g => g.SourceFilePath == "Claimed.cs");
        Assert.That(claimed, Is.Not.Null);
        Assert.That(claimed!.Diagnostics.Select(d => d.DiagnosticId), Does.Contain("QRY001"));
    }
}
