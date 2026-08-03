using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Verifies that MySQL carrier bind blocks are emitted in SQL-text order when a renderer
/// emits placeholders out of chain order (#303). The DistinctOrderBy wrap hoists the
/// ORDER BY expression (param P1) textually before the WHERE (param P0); MySqlConnector
/// binds the Nth '?' to the Nth added parameter, so the P1 bind block must precede P0's.
/// Flat chains and named-placeholder dialects keep chain-order emission byte-identically.
/// </summary>
[TestFixture]
public class MySqlBindOrderGenerationTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string SourceTemplate = @"
using Quarry;
namespace TestApp;

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}

[QuarryContext(Dialect = SqlDialect.{DIALECT})]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        decimal threshold = 100.00m;
        decimal bias = 10000.00m;
        var totals = await db.Orders()
            .Where(o => o.Total > threshold)
            .OrderBy(o => o.Total + bias)
            {DISTINCT}
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();
    }
}
";

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static (string Code, IReadOnlyList<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out _, out var diagnostics);
        var result = driver.GetRunResult();

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");
        return (interceptorsTree!.GetText().ToString(), diagnostics);
    }

    private static string RunGeneratorAndGetInterceptors(string source) => RunGenerator(source).Code;

    private static string BuildSource(string dialect, bool distinct)
        => SourceTemplate
            .Replace("{DIALECT}", dialect)
            .Replace("{DISTINCT}", distinct ? ".Distinct()" : "");

    [Test]
    public void WrapChain_MySQL_EmitsBindBlocksInSqlTextOrder()
    {
        var code = RunGeneratorAndGetInterceptors(BuildSource("MySQL", distinct: true));

        // The wrap shape: hoisted ORDER BY '?' (slot 1) textually precedes the WHERE '?'
        // (slot 0), with markers fully rewritten to bare '?'.
        Assert.That(code, Does.Contain("(`Total` + ?) AS `_o0`"),
            "Wrap path should hoist the ORDER BY expression into the inner SELECT");
        Assert.That(code, Does.Contain("WHERE `Total` > ?"),
            "WHERE placeholder should be bare '?'");
        Assert.That(code, Does.Not.Contain("{__Q"),
            "Bind-order markers must never leak into generated source");

        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 bind block should exist");
        Assert.That(p1, Is.LessThan(p0),
            "MySQL wrap chain must bind P1 (hoisted ORDER BY param, 1st '?') before P0 " +
            "(WHERE param, 2nd '?') — SQL-text order, not chain order (#303)");
    }

    [Test]
    public void FlatChain_MySQL_KeepsChainOrderBinding()
    {
        var code = RunGeneratorAndGetInterceptors(BuildSource("MySQL", distinct: false));

        Assert.That(code, Does.Not.Contain("{__Q"),
            "Bind-order markers must never leak into generated source");

        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 bind block should exist");
        Assert.That(p0, Is.LessThan(p1),
            "Flat MySQL chain renders placeholders in chain order; binding must stay " +
            "in GlobalIndex order (identity — byte-identical to pre-#303 emission)");
    }

    private const string TwoConditionalFiltersSource = @"
using Quarry;
namespace TestApp;

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, bool byMin, bool byMax)
    {
        decimal threshold = 100.00m;
        int minId = 1;
        int maxId = 3;
        decimal bias = 10000.00m;
        IQueryBuilder<Order> q = db.Orders().Where(o => o.Total > threshold);
        if (byMin) { q = q.Where(o => o.OrderId >= minId); }
        if (byMax) { q = q.Where(o => o.OrderId <= maxId); }
        var totals = await q
            .OrderBy(o => o.Total + bias)
            .Distinct()
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();
    }
}
";

    [Test]
    public void TwoIndependentConditionalFilters_WrapChain_MySQL_NoQRY048_BindsHoistedOrderByFirst()
    {
        // Review pass-2 High finding: the mask enumerator feeds the merge singleton
        // variants ([threshold], [threshold,minId], [threshold,maxId]) before the
        // combined one; the old anchor-insertion merge guessed a placement, reported a
        // false "contradictory placeholder order" on the combined variant, and fell
        // back to identity — silently shipping the #303 misbind for this shape. The
        // topological merge must rank the hoisted ORDER BY slot first with no QRY048.
        var (code, diagnostics) = RunGenerator(TwoConditionalFiltersSource);

        Assert.That(diagnostics.Where(d => d.Id == "QRY048"), Is.Empty,
            "Independently conditional filters must merge cleanly — a QRY048 here means " +
            "the cross-variant merge reported a false contradiction");
        Assert.That(code, Does.Not.Contain("{__Q"),
            "Bind-order markers must never leak into generated source");

        // Chain order: threshold(0), minId(1), maxId(2), bias(3). SQL-text order in the
        // wrap: hoisted bias first, then the WHERE slots in clause order.
        var p3 = code.IndexOf("var __p3 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p2 = code.IndexOf("var __p2 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p3, Is.GreaterThanOrEqualTo(0), "P3 (bias) bind block should exist");
        Assert.That(p3, Is.LessThan(p0),
            "Hoisted ORDER BY param (1st '?' in every variant) must bind before the WHERE " +
            "threshold — identity order here means the merge fell back");
        Assert.That(p0, Is.LessThan(p1), "WHERE slots keep clause order after the hoisted slot");
        Assert.That(p1, Is.LessThan(p2), "WHERE slots keep clause order after the hoisted slot");
    }

    private const string ConditionalAfterParameterizedCteSource = @"
using Quarry;
namespace TestApp;

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db, bool byMax)
    {
        decimal threshold = 100.00m;
        int minId = 1;
        int maxId = 3;
        IQueryBuilder<Order> q = db.With<Order>(orders => orders.Where(o => o.Total > threshold))
            .FromCte<Order>()
            .Where(o => o.OrderId >= minId);
        if (byMax) { q = q.Where(o => o.OrderId <= maxId); }
        var rows = await q
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();
    }
}
";

    [Test]
    public void ConditionalOuterClause_AfterParameterizedCte_MySQL_NoQRY048()
    {
        // Issue #305 remediation (review F2): the alignment-observing pin for
        // AssembledPlan.BuildParamConditionalMap's CteDefinition offset advance.
        // Slots: threshold(0, CTE inner), minId(1, unconditional outer),
        // maxId(2, conditional bit 0). RewriteMySqlBindMarkers validates each SQL
        // variant's placeholder slot set against the conditional map's expected
        // active set — if the CTE's inner-param slot were skipped in that walk, the
        // conditional flag would land on slot 1 instead of slot 2, the mask-0
        // variant's active set would exclude a slot its text contains, and the
        // validation would fail loudly with QRY048. Unconditional chains cannot
        // observe this (every param is active in every variant regardless of keys),
        // so this previously-QRY037-blocked shape is the only pin for the
        // conditional-map half of the #305 fix.
        var (code, diagnostics) = RunGenerator(ConditionalAfterParameterizedCteSource);

        Assert.That(diagnostics.Where(d => d.Id == "QRY037"), Is.Empty,
            "Inner+outer captured CTE params must build (#305)");
        Assert.That(diagnostics.Where(d => d.Id == "QRY048"), Is.Empty,
            "A QRY048 here means the conditional map's keys are misaligned with the " +
            "chain's parameter slots — the CteDefinition offset advance regressed");
        Assert.That(code, Does.Not.Contain("{__Q"),
            "Bind-order markers must never leak into generated source");

        // WITH renders first and no wrap/hoist applies, so text order is identity;
        // the bind blocks must stay in slot order.
        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p2 = code.IndexOf("var __p2 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 (CTE inner) bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 (outer Where) bind block should exist");
        Assert.That(p2, Is.GreaterThanOrEqualTo(0), "P2 (conditional Where) bind block should exist");
        Assert.That(p0, Is.LessThan(p1), "CTE inner param binds before the outer param (WITH renders first)");
        Assert.That(p1, Is.LessThan(p2), "Unconditional outer param binds before the conditional one");
    }

    private const string MarkerLiteralSource = @"
using Quarry;
namespace TestApp;

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<string> Notes => Length(200);
    public Col<decimal> Total { get; }
}

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        int id = 1;
        await db.Orders()
            .Update()
            .Set(o => o.Notes = ""{__Q0__}"")
            .Where(o => o.OrderId == id)
            .ExecuteNonQueryAsync();
    }
}
";

    [Test]
    public void MarkerShapedStringLiteral_MySQL_SurfacesQRY048_AsWarning()
    {
        // A developer string constant shaped like a bind-order marker, in a position the
        // renderer inlines as a quoted SQL literal (UPDATE SET; top-level WHERE constants
        // are parameterized instead), is rewritten inside the quoted text — extraction
        // then sees slot 0 twice and validation fails. That fallback must surface as a
        // QRY048 warning — review pass 2 found the descriptor missing from
        // s_deferredDescriptors, which silently dropped every emission and made all five
        // fallback paths (and this corruption) invisible. This is the end-to-end guard on
        // the registration.
        var (code, diagnostics) = RunGenerator(MarkerLiteralSource);

        var qry048 = diagnostics.Where(d => d.Id == "QRY048").ToList();
        Assert.That(qry048, Is.Not.Empty,
            "Bind-order extraction failure must surface as QRY048 — silence means the " +
            "descriptor is not registered in s_deferredDescriptors and the fallback is invisible");
        Assert.That(qry048[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(qry048[0].GetMessage(), Does.Contain("duplicate"),
            "The message should carry the extraction-failure reason");

        Assert.That(code, Does.Not.Contain("{__Q"),
            "Markers must be stripped from generated source even when extraction fails");

        // Identity fallback: chain order P0 first (single real param, so bind order is
        // trivially identity — the point is the chain still generates).
        Assert.That(code, Does.Contain("var __p0 = __cmd.CreateParameter();"),
            "The chain must still generate with identity binding after the fallback");
    }

    [Test]
    public void Descriptor_QRY048_IsRegisteredWarningWithReasonSlot()
    {
        var d = Quarry.Generators.DiagnosticDescriptors.MySqlBindOrderFallback;
        Assert.That(d.Id, Is.EqualTo("QRY048"));
        Assert.That(d.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(d.MessageFormat.ToString(), Does.Contain("{0}"),
            "QRY048 must carry the extraction-failure reason");
    }

    [Test]
    public void WrapChain_PostgreSQL_KeepsChainOrderBinding()
    {
        var code = RunGeneratorAndGetInterceptors(BuildSource("PostgreSQL", distinct: true));

        // PG's $N placeholders carry identity, so the wrap needs no bind reordering:
        // the hoisted ORDER BY expression renders with its post-body slot ($2). The SQL
        // lives in a C# verbatim string, so each '"' is doubled in the generated source.
        Assert.That(code, Does.Contain("(\"\"Total\"\" + $2) AS \"\"_o0\"\""),
            "PG wrap should pre-allocate the hoisted ORDER BY param at its global slot");

        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 bind block should exist");
        Assert.That(p0, Is.LessThan(p1), "Named/positional-indexed dialects keep chain-order binding");
    }
}
