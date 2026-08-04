using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Tests that the incremental pipeline caches correctly on valid, interceptable
/// chains. Fixtures use named-tuple / single-column projections so every chain
/// reaches PrebuiltDispatch — a fixture regression to RuntimeBuild (hollow
/// interceptors) fails the QRY014/QRY032 guard in every test. Stage-level
/// assertions go through <see cref="TrackingNames"/>; re-runs parse identical
/// source into fresh trees so the driver must invoke model equality rather than
/// short-circuiting on reference-equal inputs.
/// </summary>
[TestFixture]
public class IncrementalCachingTests
{
    private const string SharedSource = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    private const string FileASource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public class ServiceA
{
    public async void DoWork(TestDbContext db)
    {
        await db.Users().Select(u => (Id: u.UserId, Name: u.UserName)).ExecuteFetchAllAsync();
    }
}
";

    private const string FileBSource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public class ServiceB
{
    public async void DoWork(TestDbContext db)
    {
        await db.Users().Where(u => u.UserId == 1).Select(u => u.UserName).ExecuteFetchFirstAsync();
    }
}
";

    // Metadata references are built once and shared so re-created compilations
    // differ only by their syntax trees, mirroring a real incremental update.
    private static IReadOnlyList<MetadataReference> References =>
        Testing.GeneratorTestReferences.All;

    private static CSharpCompilation CreateCompilation(params (string Source, string Path)[] files)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = files.Select(f =>
            CSharpSyntaxTree.ParseText(f.Source, parseOptions, path: f.Path)).ToList();

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static GeneratorDriver CreateDriver()
    {
        return CSharpGeneratorDriver.Create(
            generators: new[] { new QuarryGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    /// <summary>
    /// Guard against the hollow-fixture failure mode: a run must produce no
    /// generator error diagnostics (QRY014 anonymous projection, QRY032
    /// RuntimeBuild disqualification, ...) and no generator crash (CS8785).
    /// </summary>
    private static void AssertHealthyRun(GeneratorDriverRunResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785")
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
        Assert.That(errors, Is.Empty, "Generator must not report errors or crash on valid fixtures");
    }

    /// <summary>
    /// Returns interceptor file path → source text, asserting each file has a
    /// real interceptor body rather than an empty shell.
    /// </summary>
    private static Dictionary<string, string> GetRealInterceptorTexts(GeneratorDriverRunResult result)
    {
        var texts = result.GeneratedTrees
            .Where(t => t.FilePath.Contains(".Interceptors."))
            .ToDictionary(t => Path.GetFileName(t.FilePath), t => t.GetText().ToString());

        Assert.That(texts, Is.Not.Empty, "Expected interceptor outputs");
        foreach (var (file, text) in texts)
        {
            Assert.That(text, Does.Contain("InterceptsLocation"),
                $"{file} should contain interceptor attributes — hollow shell detected");
            Assert.That(text, Does.Contain("SELECT"),
                $"{file} should contain pre-built SQL — hollow shell detected");
        }

        return texts;
    }

    private static IEnumerable<(object Value, IncrementalStepRunReason Reason)> StageOutputs(
        GeneratorDriverRunResult result, string stage)
    {
        Assert.That(result.Results[0].TrackedSteps.ContainsKey(stage), Is.True,
            $"Stage '{stage}' missing from tracked steps — was WithTrackingName removed? " +
            $"Present: [{string.Join(", ", result.Results[0].TrackedSteps.Keys)}]");
        return result.Results[0].TrackedSteps[stage].SelectMany(s => s.Outputs);
    }

    /// <summary>
    /// Asserts no named pipeline stage recorded a Modified/New output. Nodes
    /// that were wholesale-skipped (fully cached) record no steps at all, so
    /// an absent stage is itself a cached signal; what must never appear on an
    /// unchanged re-run is a recomputed-and-different output.
    /// </summary>
    private static void AssertNoStageRecomputedDifferently(GeneratorDriverRunResult result)
    {
        // Only the named model-pipeline stages are held to strict equality.
        // Output steps re-run whenever the Compilation instance changes (they
        // Combine CompilationProvider) and their diagnostic bundles hold new
        // tree references, so they can report Modified with byte-identical
        // sources — the text-identity assertions cover that side.
        var namedStages = new[]
        {
            TrackingNames.ContextDeclarations,
            TrackingNames.EntityRegistry,
            TrackingNames.RawCallSites,
            TrackingNames.EnrichedCallSites,
            TrackingNames.BindResults,
            TrackingNames.TranslatedCallSites,
            TrackingNames.PerFileGroups,
        };

        var tracked = result.Results[0].TrackedSteps
            .Where(kv => namedStages.Contains(kv.Key))
            .ToList();

        // Without this, the loop below is vacuous in the one way that matters: if tracking
        // were switched off, a name were renamed, or the driver were run without
        // trackIncrementalGeneratorSteps, TrackedSteps would hold none of these stages and
        // every assertion would pass by iterating nothing. "An absent stage means cached"
        // is true per-stage, but it cannot also mean "no stage was ever observed".
        Assert.That(tracked, Is.Not.Empty,
            "No named pipeline stage was tracked at all — the run reasons below were never " +
            $"examined. Expected at least one of: {string.Join(", ", namedStages)}.");

        foreach (var (stage, steps) in tracked)
        {
            var reasons = steps.SelectMany(s => s.Outputs).Select(o => o.Reason).ToList();
            Assert.That(reasons, Is.All.Matches<IncrementalStepRunReason>(r =>
                    r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
                $"Stage '{stage}' recomputed a different value on identical re-run: [{string.Join(", ", reasons)}]");
        }
    }

    [Test]
    public void ValidFixtures_ProduceRealInterceptors()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();

        AssertHealthyRun(result);
        var texts = GetRealInterceptorTexts(result);

        // Per-file fan-out: one interceptor file per source file containing chains.
        Assert.That(texts.Keys, Has.Some.Contains("ServiceA"),
            "ServiceA chains should emit a per-file interceptor output");
        Assert.That(texts.Keys, Has.Some.Contains("ServiceB"),
            "ServiceB chains should emit a per-file interceptor output");

        // The tuple projection's SQL selects both columns; the single-column chain filters.
        var serviceAText = texts.First(kv => kv.Key.Contains("ServiceA")).Value;
        var serviceBText = texts.First(kv => kv.Key.Contains("ServiceB")).Value;
        Assert.That(serviceAText, Does.Contain("UserId").And.Contain("UserName"));
        Assert.That(serviceBText, Does.Contain("WHERE"));
    }

    [Test]
    public void UnchangedSource_FreshlyParsedTrees_AllStagesCached()
    {
        var files = new[]
        {
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"),
        };

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(CreateCompilation(files), out _, out _);
        AssertHealthyRun(driver.GetRunResult());
        var initialTexts = GetRealInterceptorTexts(driver.GetRunResult());

        // Re-parse identical text into a brand-new compilation: every input is
        // reference-distinct, so Cached/Unchanged results can only come from
        // the pipeline models' .Equals implementations actually returning true.
        driver = driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(files), out _, out _);
        var result = driver.GetRunResult();

        AssertHealthyRun(result);
        AssertNoStageRecomputedDifferently(result);

        Assert.That(GetRealInterceptorTexts(result), Is.EqualTo(initialTexts),
            "Interceptor text must be byte-identical on an unchanged re-run");
    }

    [Test]
    public void ModifyOneFile_OtherFilesGroupPinnedModified_TextIdentical()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        AssertHealthyRun(driver.GetRunResult());
        var initialTexts = GetRealInterceptorTexts(driver.GetRunResult());

        // Leading comment shifts the chain's line numbers, so ServiceA's call
        // sites (and its [InterceptsLocation] data) genuinely change.
        var modifiedFileASource = "// Modified comment to trigger change\r\n" + FileASource;
        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "ServiceA.cs");
        var newTree = CSharpSyntaxTree.ParseText(modifiedFileASource,
            new CSharpParseOptions(LanguageVersion.Latest), path: "ServiceA.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var result = driver.GetRunResult();
        AssertHealthyRun(result);

        // Per-file groups: ServiceB's group must be reused, ServiceA's must not.
        var groupReasons = StageOutputs(result, TrackingNames.PerFileGroups)
            .Select(o => (Group: (Quarry.Generators.Models.FileInterceptorGroup)o.Value, o.Reason))
            .ToList();
        var serviceAGroup = groupReasons.Single(g => g.Group.FileTag.Contains("ServiceA"));
        var serviceBGroup = groupReasons.Single(g => g.Group.FileTag.Contains("ServiceB"));

        Assert.That(serviceAGroup.Reason,
            Is.EqualTo(IncrementalStepRunReason.Modified).Or.EqualTo(IncrementalStepRunReason.New),
            "Modified file's group must be regenerated");

        // PINS KNOWN BUG https://github.com/Dtronix/Quarry/issues/310 (model
        // hygiene): the emission output action mutates cached AssembledPlan
        // instances (ProjectionInfo/ReaderDelegateCode, QuarryGenerator.cs
        // Stage-5e emission), and ReaderDelegateCode participates in
        // AssembledPlan.Equals — so the recomputed pristine group never equals
        // the cached-then-mutated one and the unchanged file reports Modified
        // instead of Cached/Unchanged. When this assertion FAILS, #310's
        // mutation defect is likely fixed: flip it to expect Cached/Unchanged.
        Assert.That(serviceBGroup.Reason, Is.EqualTo(IncrementalStepRunReason.Modified),
            "Pinned #310: unchanged file's group is expected to (wrongly) report Modified. " +
            "If this fails, the output-action mutation is likely fixed — flip this assertion " +
            "to expect Cached/Unchanged.");

        var newTexts = GetRealInterceptorTexts(result);
        var serviceBFile = initialTexts.Keys.Single(k => k.Contains("ServiceB"));
        var serviceAFile = initialTexts.Keys.Single(k => k.Contains("ServiceA"));
        Assert.That(newTexts[serviceBFile], Is.EqualTo(initialTexts[serviceBFile]),
            "Unchanged file's interceptor text must be identical");
        Assert.That(newTexts[serviceAFile], Is.Not.EqualTo(initialTexts[serviceAFile]),
            "Modified file's interceptor text must reflect the new call-site locations");
    }

    [Test]
    public void ModifyQuery_RegeneratesAffectedFileOnly()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        AssertHealthyRun(driver.GetRunResult());
        var initialTexts = GetRealInterceptorTexts(driver.GetRunResult());

        // Change ServiceA's projection: named tuple -> single column.
        var modifiedFileASource = FileASource.Replace(
            ".Select(u => (Id: u.UserId, Name: u.UserName))",
            ".Select(u => u.UserName)");
        Assert.That(modifiedFileASource, Is.Not.EqualTo(FileASource),
            "Fixture replace must hit — did the projection text drift?");

        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "ServiceA.cs");
        var newTree = CSharpSyntaxTree.ParseText(modifiedFileASource,
            new CSharpParseOptions(LanguageVersion.Latest), path: "ServiceA.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var result = driver.GetRunResult();
        AssertHealthyRun(result);

        var newTexts = GetRealInterceptorTexts(result);
        var serviceAFile = initialTexts.Keys.Single(k => k.Contains("ServiceA"));
        var serviceBFile = initialTexts.Keys.Single(k => k.Contains("ServiceB"));

        Assert.That(newTexts[serviceAFile], Is.Not.EqualTo(initialTexts[serviceAFile]),
            "Changed query must regenerate its file's interceptor");
        Assert.That(newTexts[serviceAFile], Does.Not.Contain("UserId\", \"UserName"),
            "Regenerated SQL should no longer select both columns");
        Assert.That(newTexts[serviceBFile], Is.EqualTo(initialTexts[serviceBFile]),
            "Untouched file's interceptor must be byte-identical");
    }

    [Test]
    public void SchemaOnlyEdit_InvalidatesRegistry_RegeneratesEntities()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        AssertHealthyRun(driver.GetRunResult());

        // Add a column to the schema class — an entity-shape change.
        var modifiedShared = SharedSource.Replace(
            "public Col<string> UserName => Length(100);",
            "public Col<string> UserName => Length(100);\r\n    public Col<string> Email => Length(200);");
        Assert.That(modifiedShared, Is.Not.EqualTo(SharedSource));

        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "Shared.cs");
        var newTree = CSharpSyntaxTree.ParseText(modifiedShared,
            new CSharpParseOptions(LanguageVersion.Latest), path: "Shared.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var result = driver.GetRunResult();
        AssertHealthyRun(result);

        // The registry must rebuild (it is the barrier feeding all interceptor stages).
        var registryReasons = StageOutputs(result, TrackingNames.EntityRegistry)
            .Select(o => o.Reason).ToList();
        Assert.That(registryReasons, Has.Some.EqualTo(IncrementalStepRunReason.Modified),
            "Schema edit must rebuild the EntityRegistry");

        // Entity output must include the new column.
        var entityTexts = result.GeneratedTrees
            .Where(t => t.FilePath.Contains("User.g.cs"))
            .Select(t => t.GetText().ToString())
            .ToList();
        Assert.That(entityTexts, Has.Some.Contains("Email"),
            "Regenerated entity must carry the new schema column");

        // Interceptors must remain real (analysis re-ran against the new registry).
        GetRealInterceptorTexts(result);
    }

    /// <summary>
    /// The other half of the schema-edit test above. Invalidation is the easy direction to
    /// get right; a pipeline that simply rebuilt everything on every run would pass it. This
    /// asserts the opposite: an edit that changes no semantics — reformatting a file that
    /// contains no Quarry chain at all — must leave every stage cached and every interceptor
    /// byte-identical. A false invalidation hides here, not there.
    /// </summary>
    [Test]
    public void WhitespaceEditToUnrelatedFile_LeavesEveryStageCached()
    {
        const string unrelatedSource = @"
namespace TestApp.Services;

public class Unrelated
{
    public int Compute(int x) => x + 1;
}
";

        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"),
            (unrelatedSource, "Unrelated.cs"));

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        AssertHealthyRun(driver.GetRunResult());
        var initialTexts = GetRealInterceptorTexts(driver.GetRunResult());

        // Reformat only the unrelated file: extra blank lines and indentation, no token change.
        var reformatted = unrelatedSource
            .Replace("public int Compute(int x) => x + 1;",
                     "\r\n        public int Compute(int x)  =>  x + 1;\r\n");
        Assert.That(reformatted, Is.Not.EqualTo(unrelatedSource));

        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "Unrelated.cs");
        var newTree = CSharpSyntaxTree.ParseText(reformatted,
            new CSharpParseOptions(LanguageVersion.Latest), path: "Unrelated.cs");

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation.ReplaceSyntaxTree(oldTree, newTree), out _, out _);

        var result = driver.GetRunResult();
        AssertHealthyRun(result);
        AssertNoStageRecomputedDifferently(result);

        Assert.That(GetRealInterceptorTexts(result), Is.EqualTo(initialTexts),
            "Reformatting a file with no Quarry chain must not change any interceptor");
    }

    [Test]
    public void PerFileOutput_TwoFiles_GeneratesSeparateInterceptorOutputs()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();
        AssertHealthyRun(result);

        var interceptorFiles = result.GeneratedTrees
            .Where(t => t.FilePath.Contains(".Interceptors."))
            .Select(t => Path.GetFileName(t.FilePath))
            .ToList();

        Assert.That(interceptorFiles.Count, Is.GreaterThanOrEqualTo(2),
            "Queries in different files should produce separate interceptor outputs");
        Assert.That(interceptorFiles.Distinct().Count(), Is.EqualTo(interceptorFiles.Count),
            "Each interceptor file should have a unique per-file name");
    }

    // ── Known-bug pin: issue #310 defect 1 ────────────────────────────────────

    private const string PartialHostFillerSource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public partial class Host
{
    public void Filler(TestDbContext db) { }
}
";

    private const string PartialHostChainSource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public partial class Host
{
    public async void DoWork(TestDbContext db)
    {
        var name = ""x"";
        await db.Users().Where(u => u.UserName == name).Select(u => u.UserName).ExecuteFetchAllAsync();
    }
}
";

    /// <summary>
    /// PINS KNOWN BUG https://github.com/Dtronix/Quarry/issues/310 (defect 1):
    /// adding a member to a *different partial declaration* of the containing
    /// type shifts the display-class method ordinal without touching the
    /// call-site file. Enrichment mutates cached RawCallSite instances in
    /// place, so downstream stages stay Cached and the emitted interceptor
    /// keeps the STALE display-class name, while a fresh (clean-build) run of
    /// the same final compilation emits the new one.
    /// When this test FAILS, the bug has been fixed: delete this pin and
    /// enable the correct-behavior assertion in its place.
    /// </summary>
    [Test]
    public void KnownBug_Issue310_CrossPartialOrdinalShift_EmitsStaleDisplayClassName()
    {
        var files = new[]
        {
            (SharedSource, "Shared.cs"),
            (PartialHostFillerSource, "HostFiller.cs"),
            (PartialHostChainSource, "HostChain.cs"),
        };
        var compilation = CreateCompilation(files);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        AssertHealthyRun(driver.GetRunResult());
        var initialText = driver.GetRunResult().GeneratedTrees
            .Single(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.Contains("HostChain"))
            .GetText().ToString();
        Assert.That(initialText, Does.Contain("c__DisplayClass"),
            "Captured-variable chain should embed a display-class accessor name");

        // Add a member to the OTHER partial declaration — DoWork's ordinal shifts.
        var modifiedFiller = PartialHostFillerSource.Replace(
            "public void Filler(TestDbContext db) { }",
            "public void Filler(TestDbContext db) { }\r\n    public void Filler2(TestDbContext db) { }");
        Assert.That(modifiedFiller, Is.Not.EqualTo(PartialHostFillerSource));

        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "HostFiller.cs");
        var newTree = CSharpSyntaxTree.ParseText(modifiedFiller,
            new CSharpParseOptions(LanguageVersion.Latest), path: "HostFiller.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var incrementalText = driver.GetRunResult().GeneratedTrees
            .Single(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.Contains("HostChain"))
            .GetText().ToString();

        // A clean driver over the SAME final source is the ground truth.
        var freshFiles = new[]
        {
            (SharedSource, "Shared.cs"),
            (modifiedFiller, "HostFiller.cs"),
            (PartialHostChainSource, "HostChain.cs"),
        };
        var freshDriver = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(CreateCompilation(freshFiles), out _, out _);
        var freshText = freshDriver.GetRunResult().GeneratedTrees
            .Single(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.Contains("HostChain"))
            .GetText().ToString();

        // BUGGY behavior, pinned: the incremental run keeps the pre-shift name
        // (identical to the initial emission) and disagrees with a clean build.
        Assert.That(incrementalText, Is.EqualTo(initialText),
            "Pinned #310: incremental emission is expected to keep the stale display-class name. " +
            "If this assertion fails, #310 defect 1 is likely FIXED — replace this pin with the " +
            "correct-behavior test (incremental == fresh).");
        Assert.That(incrementalText, Is.Not.EqualTo(freshText),
            "Pinned #310: incremental and clean-build emission are expected to disagree after a " +
            "cross-partial ordinal shift. If this fails, the bug is likely fixed — remove the pin.");
    }
}
