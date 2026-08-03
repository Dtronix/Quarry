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
        new("BatchInsert_NonQuery", "ExecuteNonQueryAsync",
            @"var rows = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ExecuteNonQueryAsync();"),
        new("BatchInsert_ScalarAsync", "ExecuteScalarAsync",
            @"var rows = new[] { new User { UserName = ""a"", IsActive = true } };
        await db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ExecuteScalarAsync<int>();"),
        new("Projected_ScalarAsync", "ExecuteScalarAsync",
            "await db.Users().Where(u => u.UserId > 0).Select(u => u.UserId).ExecuteScalarAsync<int>();"),
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

        Assert.That(interceptorSource, Does.Contain("this IQueryBuilder<User, User> builder"),
            $"'{shape.Name}' no longer emits the two-arity receiver for a chain that never " +
            "projects. If #329 is fixed, remove this pin and the .Select(...) workarounds " +
            "in the Postgres/MySql/SqlServer integration suites.");
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

        // Interceptors are emitted into the context's own namespace, so every
        // context namespace in the fixture must be enabled for the compiler to
        // validate (rather than reject) the generated [InterceptsLocation]s.
        var parseOptions = FixtureParseOptions;

        var sources = new List<string> { SharedSource };
        if (crossContext)
            sources.Add(SubContextSource);
        sources.Add(serviceSource);

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

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(Schema).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in new[]
        {
            "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll",
            "System.Linq.Expressions.dll", "netstandard.dll", "System.Threading.Tasks.dll",
            // Without this reference the generator's entity-type resolution
            // degrades to an identity-projection fallback (see #329 notes), which
            // would make these shapes fail for a reason unrelated to binding.
            "System.ComponentModel.Primitives.dll",
        })
        {
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, dll)));
        }

        return references;
    }
}
