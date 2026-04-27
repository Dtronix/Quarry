using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Cross-dialect end-to-end tests for [EntityReader] custom materialization.
/// Verifies that schemas annotated with [EntityReader] are materialized via the
/// per-context reader on every dialect (Lite / Pg / My / Ss). The per-context
/// readers (<see cref="Pg.ProductReader"/>, <see cref="My.ProductReader"/>,
/// <see cref="Ss.ProductReader"/>) and the global <see cref="ProductReader"/>
/// each set <c>DisplayLabel</c> as proof that the custom reader ran.
/// </summary>
[TestFixture]
internal class CrossDialectEntityReaderTests
{
    #region Identity projection — custom reader runs (4-dialect execution)

    [Test]
    public async Task Select_IdentityProjection_UsesCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchAllAsync();

        Assert.That(lt, Has.Count.EqualTo(1));
        Assert.That(pg, Has.Count.EqualTo(1));
        Assert.That(my, Has.Count.EqualTo(1));
        Assert.That(ss, Has.Count.EqualTo(1));

        Assert.That(lt[0].DisplayLabel, Is.EqualTo("[Widget] $29.99"), "Lite: custom reader populated DisplayLabel");
        Assert.That(pg[0].DisplayLabel, Is.EqualTo("[Widget] $29.99"), "Pg: per-context reader populated DisplayLabel");
        Assert.That(my[0].DisplayLabel, Is.EqualTo("[Widget] $29.99"), "My: per-context reader populated DisplayLabel");
        Assert.That(ss[0].DisplayLabel, Is.EqualTo("[Widget] $29.99"), "Ss: per-context reader populated DisplayLabel");

        Assert.That(lt[0].ProductId, Is.EqualTo(1));
        Assert.That(pg[0].ProductId, Is.EqualTo(1));
        Assert.That(my[0].ProductId, Is.EqualTo(1));
        Assert.That(ss[0].ProductId, Is.EqualTo(1));

        Assert.That(lt[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(pg[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(my[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(ss[0].ProductName, Is.EqualTo("Widget"));

        Assert.That(lt[0].Price, Is.EqualTo(29.99m));
        Assert.That(pg[0].Price, Is.EqualTo(29.99m));
        Assert.That(my[0].Price, Is.EqualTo(29.99m));
        Assert.That(ss[0].Price, Is.EqualTo(29.99m));

        Assert.That(lt[0].Description, Is.EqualTo("A fine widget"));
        Assert.That(pg[0].Description, Is.EqualTo("A fine widget"));
        Assert.That(my[0].Description, Is.EqualTo("A fine widget"));
        Assert.That(ss[0].Description, Is.EqualTo("A fine widget"));
    }

    [Test]
    public async Task Select_IdentityProjection_HandlesNullColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId == 2).Select(p => p).ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductId == 2).Select(p => p).ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductId == 2).Select(p => p).ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductId == 2).Select(p => p).ExecuteFetchAllAsync();

        Assert.That(lt[0].ProductName, Is.EqualTo("Gadget"));
        Assert.That(pg[0].ProductName, Is.EqualTo("Gadget"));
        Assert.That(my[0].ProductName, Is.EqualTo("Gadget"));
        Assert.That(ss[0].ProductName, Is.EqualTo("Gadget"));

        Assert.That(lt[0].Description, Is.Null, "Lite: custom reader handles NULL Description");
        Assert.That(pg[0].Description, Is.Null, "Pg: per-context reader handles NULL Description");
        Assert.That(my[0].Description, Is.Null, "My: per-context reader handles NULL Description");
        Assert.That(ss[0].Description, Is.Null, "Ss: per-context reader handles NULL Description");

        Assert.That(lt[0].DisplayLabel, Is.EqualTo("[Gadget] $49.50"));
        Assert.That(pg[0].DisplayLabel, Is.EqualTo("[Gadget] $49.50"));
        Assert.That(my[0].DisplayLabel, Is.EqualTo("[Gadget] $49.50"));
        Assert.That(ss[0].DisplayLabel, Is.EqualTo("[Gadget] $49.50"));
    }

    [Test]
    public async Task Select_IdentityProjection_MultipleRows_AllHaveDisplayLabel()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId <= 3).Select(p => p).ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductId <= 3).Select(p => p).ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductId <= 3).Select(p => p).ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductId <= 3).Select(p => p).ExecuteFetchAllAsync();

        Assert.That(lt, Has.Count.EqualTo(3));
        Assert.That(pg, Has.Count.EqualTo(3));
        Assert.That(my, Has.Count.EqualTo(3));
        Assert.That(ss, Has.Count.EqualTo(3));

        foreach (var product in lt)
            Assert.That(product.DisplayLabel, Does.StartWith("["), $"Lite: Product {product.ProductId} DisplayLabel format");
        foreach (var product in pg)
            Assert.That(product.DisplayLabel, Does.StartWith("["), $"Pg: Product {product.ProductId} DisplayLabel format");
        foreach (var product in my)
            Assert.That(product.DisplayLabel, Does.StartWith("["), $"My: Product {product.ProductId} DisplayLabel format");
        foreach (var product in ss)
            Assert.That(product.DisplayLabel, Does.StartWith("["), $"Ss: Product {product.ProductId} DisplayLabel format");
    }

    #endregion

    #region Tuple / single-column projections — custom reader does NOT apply

    [Test]
    public async Task Select_TupleProjection_DoesNotUseCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId <= 3).Select(p => (p.ProductId, p.ProductName, p.Price)).ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductId <= 3).Select(p => (p.ProductId, p.ProductName, p.Price)).ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductId <= 3).Select(p => (p.ProductId, p.ProductName, p.Price)).ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductId <= 3).Select(p => (p.ProductId, p.ProductName, p.Price)).ExecuteFetchAllAsync();

        Assert.That(lt, Has.Count.EqualTo(3));
        Assert.That(pg, Has.Count.EqualTo(3));
        Assert.That(my, Has.Count.EqualTo(3));
        Assert.That(ss, Has.Count.EqualTo(3));

        Assert.That(lt[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(pg[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(my[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(ss[0].ProductName, Is.EqualTo("Widget"));

        Assert.That(lt[0].Price, Is.EqualTo(29.99m));
        Assert.That(pg[0].Price, Is.EqualTo(29.99m));
        Assert.That(my[0].Price, Is.EqualTo(29.99m));
        Assert.That(ss[0].Price, Is.EqualTo(29.99m));
    }

    [Test]
    public async Task Select_SingleColumn_DoesNotUseCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId <= 3).Select(p => p.ProductName).ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductId <= 3).Select(p => p.ProductName).ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductId <= 3).Select(p => p.ProductName).ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductId <= 3).Select(p => p.ProductName).ExecuteFetchAllAsync();

        Assert.That(lt, Does.Contain("Widget").And.Contain("Gadget").And.Contain("Doohickey"));
        Assert.That(pg, Does.Contain("Widget").And.Contain("Gadget").And.Contain("Doohickey"));
        Assert.That(my, Does.Contain("Widget").And.Contain("Gadget").And.Contain("Doohickey"));
        Assert.That(ss, Does.Contain("Widget").And.Contain("Gadget").And.Contain("Doohickey"));
    }

    #endregion

    #region ExecuteFetchFirst / ExecuteFetchFirstOrDefault — custom reader runs

    [Test]
    public async Task ExecuteFetchFirst_UsesCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchFirstAsync();
        var pg = await Pg.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchFirstAsync();
        var my = await My.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchFirstAsync();
        var ss = await Ss.Products().Where(p => p.ProductId == 1).Select(p => p).ExecuteFetchFirstAsync();

        Assert.That(lt.DisplayLabel, Is.EqualTo("[Widget] $29.99"));
        Assert.That(pg.DisplayLabel, Is.EqualTo("[Widget] $29.99"));
        Assert.That(my.DisplayLabel, Is.EqualTo("[Widget] $29.99"));
        Assert.That(ss.DisplayLabel, Is.EqualTo("[Widget] $29.99"));
    }

    [Test]
    public async Task ExecuteFetchFirstOrDefault_UsesCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = await Lite.Products().Where(p => p.ProductId == 3).Select(p => p).ExecuteFetchFirstOrDefaultAsync();
        var pg = await Pg.Products().Where(p => p.ProductId == 3).Select(p => p).ExecuteFetchFirstOrDefaultAsync();
        var my = await My.Products().Where(p => p.ProductId == 3).Select(p => p).ExecuteFetchFirstOrDefaultAsync();
        var ss = await Ss.Products().Where(p => p.ProductId == 3).Select(p => p).ExecuteFetchFirstOrDefaultAsync();

        Assert.That(lt, Is.Not.Null);
        Assert.That(pg, Is.Not.Null);
        Assert.That(my, Is.Not.Null);
        Assert.That(ss, Is.Not.Null);

        Assert.That(lt!.DisplayLabel, Is.EqualTo("[Doohickey] $9.95"));
        Assert.That(pg!.DisplayLabel, Is.EqualTo("[Doohickey] $9.95"));
        Assert.That(my!.DisplayLabel, Is.EqualTo("[Doohickey] $9.95"));
        Assert.That(ss!.DisplayLabel, Is.EqualTo("[Doohickey] $9.95"));
    }

    #endregion

    #region Set operation — custom reader runs on identity projection over a UNION

    [Test]
    public async Task Union_IdentityProjection_UsesCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // UNION of two identity-projection chains over the [EntityReader]-active
        // Products schema. The terminal materialization must still route through
        // the per-context custom reader on every dialect — DisplayLabel populated
        // by ProductReader.Read proves it ran. Uses .Prepare() to match the
        // established cross-dialect set-op pattern (see CrossDialectSetOperationTests).
        var lt = await Lite.Products().Where(p => p.ProductId == 1).Union(Lite.Products().Where(p => p.ProductId == 3)).Prepare().ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductId == 1).Union(Pg.Products().Where(p => p.ProductId == 3)).Prepare().ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductId == 1).Union(My.Products().Where(p => p.ProductId == 3)).Prepare().ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductId == 1).Union(Ss.Products().Where(p => p.ProductId == 3)).Prepare().ExecuteFetchAllAsync();

        Assert.That(lt, Has.Count.EqualTo(2));
        Assert.That(pg, Has.Count.EqualTo(2));
        Assert.That(my, Has.Count.EqualTo(2));
        Assert.That(ss, Has.Count.EqualTo(2));

        foreach (var p in lt) Assert.That(p.DisplayLabel, Does.StartWith("["), $"Lite: row {p.ProductId} DisplayLabel populated by reader after UNION");
        foreach (var p in pg) Assert.That(p.DisplayLabel, Does.StartWith("["), $"Pg: row {p.ProductId} DisplayLabel populated by per-context reader after UNION");
        foreach (var p in my) Assert.That(p.DisplayLabel, Does.StartWith("["), $"My: row {p.ProductId} DisplayLabel populated by per-context reader after UNION");
        foreach (var p in ss) Assert.That(p.DisplayLabel, Does.StartWith("["), $"Ss: row {p.ProductId} DisplayLabel populated by per-context reader after UNION");
    }

    #endregion

    #region CTE — custom reader runs on identity projection from a CTE source

    [Test]
    public async Task Cte_FromCte_IdentityProjection_UsesCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // CTE with the [EntityReader]-active Products inner chain. The outer
        // chain's identity projection from the CTE must still route through the
        // per-context custom reader. This exercises the
        // ChainAnalyzer lambda-inner path that propagates
        // ProjectionInfo.CustomEntityReaderClass through the reduced inner plan.
        var lt = await Lite.With<Product>(products => products.Where(p => p.ProductId <= 3))
            .FromCte<Product>()
            .Select(p => p)
            .ExecuteFetchAllAsync();
        var pg = await Pg.With<Pg.Product>(products => products.Where(p => p.ProductId <= 3))
            .FromCte<Pg.Product>()
            .Select(p => p)
            .ExecuteFetchAllAsync();
        var my = await My.With<My.Product>(products => products.Where(p => p.ProductId <= 3))
            .FromCte<My.Product>()
            .Select(p => p)
            .ExecuteFetchAllAsync();
        var ss = await Ss.With<Ss.Product>(products => products.Where(p => p.ProductId <= 3))
            .FromCte<Ss.Product>()
            .Select(p => p)
            .ExecuteFetchAllAsync();

        Assert.That(lt, Has.Count.EqualTo(3));
        Assert.That(pg, Has.Count.EqualTo(3));
        Assert.That(my, Has.Count.EqualTo(3));
        Assert.That(ss, Has.Count.EqualTo(3));

        foreach (var p in lt) Assert.That(p.DisplayLabel, Does.StartWith("["), $"Lite: row {p.ProductId} DisplayLabel populated by reader after CTE");
        foreach (var p in pg) Assert.That(p.DisplayLabel, Does.StartWith("["), $"Pg: row {p.ProductId} DisplayLabel populated by per-context reader after CTE");
        foreach (var p in my) Assert.That(p.DisplayLabel, Does.StartWith("["), $"My: row {p.ProductId} DisplayLabel populated by per-context reader after CTE");
        foreach (var p in ss) Assert.That(p.DisplayLabel, Does.StartWith("["), $"Ss: row {p.ProductId} DisplayLabel populated by per-context reader after CTE");
    }

    #endregion

    #region Insert + Select round-trip — custom reader runs on materialization

    [Test]
    public async Task RoundTrip_InsertThenSelectEntity_UsesCustomReader()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Each context inserts into its per-container products table (transactions
        // rolled back on dispose for Pg/My/Ss; Lite is in-memory, scoped to harness).
        await Lite.Products().Insert(new Product { ProductName = "Thingamajig", Price = 199.99m, Description = "Premium item" }).ExecuteNonQueryAsync();
        await Pg.Products().Insert(new Pg.Product { ProductName = "Thingamajig", Price = 199.99m, Description = "Premium item" }).ExecuteNonQueryAsync();
        await My.Products().Insert(new My.Product { ProductName = "Thingamajig", Price = 199.99m, Description = "Premium item" }).ExecuteNonQueryAsync();
        await Ss.Products().Insert(new Ss.Product { ProductName = "Thingamajig", Price = 199.99m, Description = "Premium item" }).ExecuteNonQueryAsync();

        var lt = await Lite.Products().Where(p => p.ProductName == "Thingamajig").Select(p => p).ExecuteFetchAllAsync();
        var pg = await Pg.Products().Where(p => p.ProductName == "Thingamajig").Select(p => p).ExecuteFetchAllAsync();
        var my = await My.Products().Where(p => p.ProductName == "Thingamajig").Select(p => p).ExecuteFetchAllAsync();
        var ss = await Ss.Products().Where(p => p.ProductName == "Thingamajig").Select(p => p).ExecuteFetchAllAsync();

        Assert.That(lt, Has.Count.EqualTo(1));
        Assert.That(pg, Has.Count.EqualTo(1));
        Assert.That(my, Has.Count.EqualTo(1));
        Assert.That(ss, Has.Count.EqualTo(1));

        Assert.That(lt[0].Price, Is.EqualTo(199.99m));
        Assert.That(pg[0].Price, Is.EqualTo(199.99m));
        Assert.That(my[0].Price, Is.EqualTo(199.99m));
        Assert.That(ss[0].Price, Is.EqualTo(199.99m));

        Assert.That(lt[0].Description, Is.EqualTo("Premium item"));
        Assert.That(pg[0].Description, Is.EqualTo("Premium item"));
        Assert.That(my[0].Description, Is.EqualTo("Premium item"));
        Assert.That(ss[0].Description, Is.EqualTo("Premium item"));

        Assert.That(lt[0].DisplayLabel, Is.EqualTo("[Thingamajig] $199.99"));
        Assert.That(pg[0].DisplayLabel, Is.EqualTo("[Thingamajig] $199.99"));
        Assert.That(my[0].DisplayLabel, Is.EqualTo("[Thingamajig] $199.99"));
        Assert.That(ss[0].DisplayLabel, Is.EqualTo("[Thingamajig] $199.99"));
    }

    #endregion
}
