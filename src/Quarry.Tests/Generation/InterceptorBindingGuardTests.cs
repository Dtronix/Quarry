using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Guard matrix over the chain shapes whose interceptors are most at risk of
/// binding failure: entity-terminals (chains ending on <c>IQueryBuilder&lt;T&gt;</c>
/// with no explicit <c>.Select</c>) and generic terminals invoked on a generic
/// receiver (<c>ExecuteScalarAsync&lt;TKey&gt;</c>), where the emitted interceptor's
/// signature or generic arity must match the intercepted call exactly.
/// </summary>
/// <remarks>
/// <para>
/// Two compiler diagnostics mark a broken binding, and neither is a generator
/// diagnostic — both surface only when the compiler validates the emitted
/// <c>[InterceptsLocation]</c> methods against the real call sites:
/// <c>CS9144</c> (signature mismatch — e.g. an interceptor typed
/// <c>IQueryBuilder&lt;T, T&gt;</c> emitted for an <c>IQueryBuilder&lt;T&gt;</c>
/// receiver, the defect tracked as #329) and <c>CS9177</c> (generic-arity
/// mismatch — the combined arity of a generic method on a generic receiver).
/// </para>
/// <para>
/// Every shape below currently compiles without either diagnostic, so the
/// matrix is a regression guard: a future emitter change that mistypes an
/// interceptor fails here with the offending shape named, instead of silently
/// falling back to the unintercepted default interface member (which throws
/// only at runtime). The fixture also carries one bug pin — see
/// <see cref="KnownBug_Issue329_EntityTerminal_EmitsTwoArityReceiver"/>, which
/// asserts a mismatch the compiler does <em>not</em> report here.
/// </para>
/// <para>
/// <b>Which assertion is load-bearing for which shape.</b> The CS9144/CS9177
/// check is not uniformly strong. For <em>clause</em> shapes it is: mutating
/// <c>CarrierEmitter.ResolveCarrierReceiverType</c> to emit a two-arity receiver
/// makes the real project fail to build with CS9144 on <c>Distinct()</c>,
/// <c>Limit(int)</c> and <c>Union(...)</c> — which is why the matrix includes
/// those shapes. For the <em>entity-terminal</em> shapes it is not: an isolated
/// compilation does not report CS9144 for a wrong-arity terminal receiver at all
/// (a hand-written <c>[InterceptsLocation]</c> with a deliberately wrong receiver
/// arity is also accepted silently), which is the whole reason the #329 pin below
/// asserts on emitted text instead. For those nine shapes the load-bearing checks
/// are the "an interceptor was emitted for this terminal" probe and the
/// error-free-compilation assertion — not the diagnostic filter.
/// </para>
/// <para>
/// The assertions are deliberately independent of <c>Quarry.Tests.csproj</c>:
/// each case compiles its own <see cref="CSharpCompilation"/> with interceptors
/// enabled for the fixture namespaces, so no project-level <c>NoWarn</c> can
/// mask a mismatch here.
/// </para>
/// </remarks>
[TestFixture]
public class InterceptorBindingGuardTests
{
    /// <summary>Schema plus the primary context, shared by every case.</summary>
    private const string SharedSource = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<bool> IsActive { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    /// <summary>
    /// A second context over the same entity, declared in a nested namespace.
    /// Cross-context resolution is the condition under which the #329
    /// entity-terminal mismatch was originally observed, so the matrix runs
    /// against this shape as well as the single-context one.
    /// </summary>
    private const string SubContextSource = @"
using Quarry;
using TestApp;

namespace TestApp.Sub;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class SubDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    private const string ServiceTemplate = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Quarry;
using TestApp;

namespace TestApp.Services;

public class Service
{
    public async Task Run(__CONTEXT__ db)
    {
        __BODY__
    }
}
";

    /// <summary>
    /// A chain shape: the C# statement(s) to place in the service body, and the
    /// chain method whose interceptor must be emitted for the binding to hold.
    /// </summary>
    public sealed record Shape(string Name, string Terminal, string Body)
    {
        public override string ToString() => Name;
    }

    // ── Entity terminals: receiver is IQueryBuilder<T>, no explicit .Select ───
    // IEntityAccessor<T> exposes no terminals of its own, so each chain passes
    // through exactly one builder-returning method before terminating.

    private static readonly Shape[] EntityTerminalShapes =
    {
        new("Where_FetchAll", "ExecuteFetchAllAsync",
            "await db.Users().Where(u => u.UserId > 0).ExecuteFetchAllAsync();"),
        new("Where_FetchFirst", "ExecuteFetchFirstAsync",
            "await db.Users().Where(u => u.UserId > 0).ExecuteFetchFirstAsync();"),
        // The exact probe from #329: entity terminal after a Where.
        new("Where_FetchFirstOrDefault", "ExecuteFetchFirstOrDefaultAsync",
            "await db.Users().Where(u => u.UserId > 0).ExecuteFetchFirstOrDefaultAsync();"),
        new("Where_FetchSingle", "ExecuteFetchSingleAsync",
            "await db.Users().Where(u => u.UserId == 1).ExecuteFetchSingleAsync();"),
        new("Where_FetchSingleOrDefault", "ExecuteFetchSingleOrDefaultAsync",
            "await db.Users().Where(u => u.UserId == 1).ExecuteFetchSingleOrDefaultAsync();"),
        new("OrderBy_FetchAll", "ExecuteFetchAllAsync",
            "await db.Users().OrderBy(u => u.UserId).ExecuteFetchAllAsync();"),
        new("Limit_FetchFirst", "ExecuteFetchFirstAsync",
            "await db.Users().Limit(1).ExecuteFetchFirstAsync();"),
        new("Distinct_FetchAll", "ExecuteFetchAllAsync",
            "await db.Users().Distinct().ExecuteFetchAllAsync();"),
        new("Where_ToAsyncEnumerable", "ToAsyncEnumerable",
            "await foreach (var u in db.Users().Where(u => u.UserId > 0).ToAsyncEnumerable()) { _ = u; }"),
    };

    // ── Generic terminals on a generic receiver: the CS9177 arity family ─────

    private static readonly Shape[] GenericTerminalShapes =
    {
        new("Insert_ScalarAsync", "ExecuteScalarAsync",
            @"await db.Users().Insert(new User { UserName = ""a"", IsActive = true }).ExecuteScalarAsync<int>();"),
        new("Insert_NonQuery", "ExecuteNonQueryAsync",
            @"await db.Users().Insert(new User { UserName = ""a"", IsActive = true }).ExecuteNonQueryAsync();"),
        new("Projected_ScalarAsync", "ExecuteScalarAsync",
            "await db.Users().Where(u => u.UserId > 0).Select(u => u.UserId).ExecuteScalarAsync<int>();"),
        // ToDiagnostics constructs QueryDiagnostics in the consumer's assembly. This shape covers
        // the general path (TerminalEmitHelpers.EmitDiagnosticsConstruction) that every non-batch
        // chain uses; BatchInsert_ToDiagnostics below covers the separate batch path. Both were
        // uncovered, and both were broken by an internal constructor until #334.
        new("Projected_ToDiagnostics", "ToDiagnostics",
            "var diag = db.Users().Where(u => u.UserId > 0).Select(u => u.UserName).ToDiagnostics();\n        _ = diag.Sql;"),
        // Batch insert emits a call to Quarry.Internal.BatchInsertSqlBuilder. These two shapes were
        // held out of the matrix while that type was internal (#334) — an ordinary consumer could
        // not compile them at all. They are back in the clean-binding set now that it is public.
        new("BatchInsert_NonQuery", "ExecuteNonQueryAsync",
            @"var rows = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ExecuteNonQueryAsync();"),
        new("BatchInsert_ScalarAsync", "ExecuteScalarAsync",
            @"var rows = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ExecuteScalarAsync<int>();"),
        // The batch-insert *diagnostics* terminal is emitted by a separate method
        // (TerminalBodyEmitter.EmitBatchInsertDiagnosticsTerminal) that carries its own
        // hard-coded BatchInsertSqlBuilder call. The #334 pin only ever reached the carrier
        // terminal, so this second site shipped the same defect untested.
        new("BatchInsert_ToDiagnostics", "ToDiagnostics",
            @"var rows = new[] { new User { UserName = ""a"", IsActive = true } };
        var diag = db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ToDiagnostics();
        _ = diag.Sql;"),
    };

    // ── Modification terminals ───────────────────────────────────────────────

    private static readonly Shape[] ModificationShapes =
    {
        new("Delete_Where_NonQuery", "ExecuteNonQueryAsync",
            "await db.Users().Delete().Where(u => u.UserId > 0).ExecuteNonQueryAsync();"),
        new("Delete_All_NonQuery", "ExecuteNonQueryAsync",
            "await db.Users().Delete().All().ExecuteNonQueryAsync();"),
        new("Update_Set_Where_NonQuery", "ExecuteNonQueryAsync",
            "await db.Users().Update().Set(u => u.IsActive = false).Where(u => u.UserId > 0).ExecuteNonQueryAsync();"),
        new("Update_Set_All_NonQuery", "ExecuteNonQueryAsync",
            "await db.Users().Update().Set(u => u.IsActive = false).All().ExecuteNonQueryAsync();"),
    };

    public static IEnumerable<Shape> AllShapes =>
        EntityTerminalShapes.Concat(GenericTerminalShapes).Concat(ModificationShapes);

    public static IEnumerable<Shape> EntityTerminalOnlyShapes => EntityTerminalShapes;

    [TestCaseSource(nameof(AllShapes))]
    public void Shape_BindsWithoutInterceptorMismatch(Shape shape)
        => AssertBindsCleanly(shape, "TestDbContext", crossContext: false);

    /// <summary>
    /// Same entity-terminal shapes, but resolved against a context in a nested
    /// namespace while a second context over the same entity is in scope — the
    /// configuration under which a mistyped entity-terminal interceptor was
    /// originally reported (#329).
    /// </summary>
    [TestCaseSource(nameof(EntityTerminalOnlyShapes))]
    public void Shape_CrossNamespaceContext_BindsWithoutInterceptorMismatch(Shape shape)
        => AssertBindsCleanly(shape, "TestApp.Sub.SubDbContext", crossContext: true);

    /// <summary>
    /// Compiler diagnostics that mean "the generated code named something a consumer cannot reach".
    /// <c>CS0122</c> is the call-site case (#334); the rest are the inconsistent-accessibility
    /// family, which fires when an emitted member's own signature exposes a less-accessible type.
    /// </summary>
    private static readonly string[] AccessibilityDiagnosticIds =
        { "CS0122", "CS0050", "CS0051", "CS0053", "CS0060" };

    private static void AssertBindsCleanly(Shape shape, string contextType, bool crossContext)
    {
        var (generatedSources, diagnostics) = Run(shape, contextType, crossContext);

        // A generator crash silently removes every interceptor, which would make
        // the mismatch assertions below pass vacuously.
        var crashes = diagnostics.Where(d => d.Id == "CS8785").ToList();
        Assert.That(crashes, Is.Empty, () => $"Generator crashed on '{shape.Name}': {Describe(crashes)}");

        var mismatches = diagnostics
            .Where(d => d.Id is "CS9144" or "CS9177")
            .ToList();
        Assert.That(mismatches, Is.Empty, () =>
            $"Interceptor binding mismatch on '{shape.Name}' " +
            $"(CS9144 = signature, CS9177 = generic arity): {Describe(mismatches)}");

        // Generated interceptors are emitted into the *consumer's* assembly, so every Quarry
        // type they name has to be reachable from outside Quarry's InternalsVisibleTo list.
        // CS0122 here means the emitter referenced a type only a friend assembly can see —
        // the #334 defect, where InsertBatch simply did not compile for any real consumer.
        // Checked before the catch-all below so the failure names the actual cause instead of
        // reporting a generic "fixture does not compile".
        var inaccessible = diagnostics
            .Where(d => AccessibilityDiagnosticIds.Contains(d.Id))
            .ToList();
        Assert.That(inaccessible, Is.Empty, () =>
            $"Generated interceptor for '{shape.Name}' names a type that is inaccessible outside " +
            "Quarry's InternalsVisibleTo list, so this chain does not compile for any ordinary " +
            "consumer. Every project in this repo is a friend assembly, so no other build can " +
            $"catch this — make the referenced type public: {Describe(inaccessible)}");

        // The compiler only validates [InterceptsLocation] bindings on a compilation it
        // can otherwise bind. An unrelated error in the fixture (a stale type name after
        // an edit, a missing reference) stops that validation and every shape would then
        // pass green — so require the fixture itself to be clean before believing the
        // absence of CS9144/CS9177 above.
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.That(errors, Is.Empty, () =>
            $"Fixture for '{shape.Name}' does not compile cleanly, so interceptor binding was " +
            $"never validated and the mismatch assertions above are vacuous: {Describe(errors)}" +
            // CS1729 naming a Quarry type is the accessibility defect in disguise: when a type's
            // only constructor is internal it is not a candidate at all outside a friend assembly,
            // so the compiler reports "no constructor takes N arguments" rather than CS0122. That
            // is how the internal QueryDiagnostics ctor hid (#334). CS1729 is deliberately not in
            // AccessibilityDiagnosticIds — it usually is a genuine emitter arity bug, and
            // mislabelling those would blunt this matrix.
            "\nNote: a CS1729 naming a Quarry type may mean that type's constructor is internal, " +
            "not that the emitter passed the wrong number of arguments.");

        // Absence of a mismatch is only meaningful if an interceptor was emitted
        // for the terminal at all — an unintercepted call produces no diagnostic
        // and would otherwise pass this guard.
        var interceptorSource = string.Concat(generatedSources);
        Assert.That(interceptorSource, Does.Contain($"Intercepts {shape.Terminal}() call at"),
            $"No interceptor was emitted for the '{shape.Terminal}' terminal of '{shape.Name}' — " +
            "the call falls through to the throwing default interface member.");
    }

    /// <summary>
    /// Bug pin for #329. An entity-terminal chain never projects, so its
    /// terminal receiver is <c>IQueryBuilder&lt;User&gt;</c> — but the emitter
    /// types the interceptor <c>IQueryBuilder&lt;User, User&gt;</c>. In an
    /// isolated compilation the compiler accepts the mismatch silently; in the
    /// full test project the same shape fails <c>CS9144</c>, which is why every
    /// integration chain carries an explicit <c>.Select(...)</c> workaround.
    /// </summary>
    /// <remarks>
    /// This pins the current, defective emission rather than the correct one:
    /// when #329 is fixed the receiver becomes one-arity, this test fails, and
    /// that failure is the signal to drop both the pin and the <c>.Select</c>
    /// workarounds in the integration suites.
    /// <para>
    /// Verified against the emitted source rather than against a compiler
    /// diagnostic on purpose — the mismatch produces no diagnostic here, so the
    /// matrix above cannot see it.
    /// </para>
    /// </remarks>
    [TestCaseSource(nameof(EntityTerminalOnlyShapes))]
    public void KnownBug_Issue329_EntityTerminal_EmitsTwoArityReceiver(Shape shape)
    {
        var (generatedSources, _) = Run(shape, "TestDbContext", crossContext: false);
        var interceptorSource = string.Concat(generatedSources);

        // Scoped to *this terminal's own* declaration. Searching the whole generated text
        // for the two-arity receiver would keep passing on any unrelated emission that
        // happens to carry one — and a bug pin that cannot go green when the bug is fixed
        // is worse than no pin, because the .Select(...) workarounds would outlive it.
        var declaration = new Regex(
            $@"\b{Regex.Escape(shape.Terminal)}_\w*\s*\(\s*this\s+IQueryBuilder<\s*User\s*,\s*User\s*>\s+builder",
            RegexOptions.Singleline);

        Assert.That(declaration.IsMatch(interceptorSource), Is.True,
            $"'{shape.Name}' no longer declares {shape.Terminal} with the two-arity receiver " +
            "for a chain that never projects. If #329 is fixed, remove this pin and the " +
            ".Select(...) workarounds in the Postgres/MySql/SqlServer integration suites.");
    }

    /// <summary>
    /// Proves the accessibility guard in <see cref="AssertBindsCleanly"/> can actually fire.
    /// </summary>
    /// <remarks>
    /// The guard is only meaningful if this fixture's compilation is genuinely <em>not</em> a friend
    /// of <c>Quarry</c>. If that ever stopped being true — a stray <c>InternalsVisibleTo</c>, a
    /// renamed assembly, a reference swapped for the source project — every shape would keep passing
    /// green while guarding nothing, exactly the blind spot that let #334 ship.
    /// <para>
    /// <c>Quarry.Internal.ScalarConverter</c> is the negative control: it is internal
    /// <em>by design</em> (called only from <c>QueryExecutor</c> inside the runtime assembly, never
    /// named by emitted code), so it stays internal and this probe stays valid. Note this is why the
    /// guard cannot be a namespace convention — <c>Quarry.Internal</c> holds both the public emitted
    /// surface and internal runtime-private helpers.
    /// </para>
    /// </remarks>
    [Test]
    public void AccessibilityGuard_DetectsAnInaccessibleType()
    {
        const string probe = @"
namespace TestApp.Probe;

public class Probe
{
    public int Run(object v) => Quarry.Internal.ScalarConverter.Convert<int>(v);
}
";
        var (_, diagnostics) = CompileNonFriend(new[] { probe });

        var inaccessible = diagnostics
            .Where(d => AccessibilityDiagnosticIds.Contains(d.Id))
            .ToList();

        Assert.That(inaccessible, Is.Not.Empty,
            "Referencing an internal Quarry type from this fixture's compilation produced no " +
            "accessibility diagnostic, which means the compilation has friend access to Quarry. " +
            "The accessibility assertion in AssertBindsCleanly is therefore vacuous for every " +
            $"shape in the matrix. Diagnostics were: {Describe(diagnostics)}");

        Assert.That(Describe(inaccessible), Does.Contain("ScalarConverter"),
            "Expected the accessibility diagnostic to name the probed internal type.");
    }

    private static CSharpParseOptions FixtureParseOptions =>
        new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures(new[]
            {
                new KeyValuePair<string, string>("InterceptorsNamespaces", "TestApp;TestApp.Sub"),
            });

    private static (IReadOnlyList<string> GeneratedSources, IReadOnlyList<Diagnostic> Diagnostics) Run(
        Shape shape, string contextType, bool crossContext)
    {
        var serviceSource = ServiceTemplate
            .Replace("__CONTEXT__", contextType)
            .Replace("__BODY__", shape.Body);

        var sources = new List<string> { SharedSource };
        if (crossContext)
            sources.Add(SubContextSource);
        sources.Add(serviceSource);

        return CompileNonFriend(sources);
    }

    /// <summary>
    /// Runs the generator over <paramref name="sources"/> in a compilation named
    /// <c>InterceptorBindingGuardAssembly</c> — deliberately absent from Quarry's
    /// <c>InternalsVisibleTo</c> list, so it sees exactly what an ordinary consumer sees.
    /// </summary>
    private static (IReadOnlyList<string> GeneratedSources, IReadOnlyList<Diagnostic> Diagnostics)
        CompileNonFriend(IEnumerable<string> sources)
    {
        // Interceptors are emitted into the context's own namespace, so every
        // context namespace in the fixture must be enabled for the compiler to
        // validate (rather than reject) the generated [InterceptsLocation]s.
        var parseOptions = FixtureParseOptions;

        var trees = sources
            .Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, path: $"Source{i}.cs"))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "InterceptorBindingGuardAssembly",
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new QuarryGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var genDiagnostics);

        var generated = driver.GetRunResult().GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToList();

        var all = genDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToList();
        return (generated, all);
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics)
        => string.Join("; ", diagnostics.Select(d => $"{d.Id} {d.GetMessage()}"));

    private static IReadOnlyList<MetadataReference> References =>
        Testing.GeneratorTestReferences.All;
}
