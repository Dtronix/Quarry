using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectCteTests
{
    #region CTE FromCte (simple)

    [Test]
    public async Task Cte_FromCte_SimpleFilter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order>(Lite.Orders().Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(Pg.Orders().Where(o => o.Total > 100))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My.With<My.Order>(My.Orders().Where(o => o.Total > 100))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(Ss.Orders().Where(o => o.Total > 100))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > 100) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > 100) SELECT [OrderId], [Total] FROM [Order]");

        // Seed data: OrderId=1 Total=250.00, OrderId=2 Total=75.50, OrderId=3 Total=150.00
        // Orders with Total > 100: OrderId=1 (250.00), OrderId=3 (150.00)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    #endregion

    #region CTE FromCte (captured-variable inner parameter)

    /// <summary>
    /// Regression test for the runtime bug where CTE inner-query captured parameters
    /// were never copied from the inner carrier into the outer carrier, silently binding
    /// default values at execution time. The original simple-filter test masked this
    /// because it inlined the WHERE comparand as a literal.
    /// </summary>
    [Test]
    public async Task Cte_FromCte_CapturedParam()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal cutoff = 100m;

        var lt = Lite.With<Order>(Lite.Orders().Where(o => o.Total > cutoff))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(Pg.Orders().Where(o => o.Total > cutoff))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My.With<My.Order>(My.Orders().Where(o => o.Total > cutoff))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(Ss.Orders().Where(o => o.Total > cutoff))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > @p0) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > $1) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > ?) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > @p0) SELECT [OrderId], [Total] FROM [Order]");

        // Seed data: OrderId=1 Total=250.00, OrderId=2 Total=75.50, OrderId=3 Total=150.00
        // Orders with Total > 100: OrderId=1 (250.00), OrderId=3 (150.00)
        // If the captured cutoff is dropped to default(decimal) = 0, all three rows would match.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));

        // Re-create the prepared chain with a different captured value to confirm the
        // parameter is read from the closure at chain CONSTRUCTION time and not, e.g.,
        // hard-coded into the SQL or pinned to a value from a different chain instance.
        //
        // Note: PreparedQuery in this codebase is a SNAPSHOT at construction — the
        // generated Where_xxx interceptor extracts the captured variable into the
        // carrier P0 field at the time of the .Where() call, before Prepare() runs.
        // There is no Bind/SetParameter API to re-execute the same prepared instance
        // with a new value, so the correct way to verify "different captured value"
        // semantics is to build a fresh chain.
        cutoff = 200m;
        var lt2 = Lite.With<Order>(Lite.Orders().Where(o => o.Total > cutoff))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var results2 = await lt2.ExecuteFetchAllAsync();
        Assert.That(results2, Has.Count.EqualTo(1));
        Assert.That(results2[0], Is.EqualTo((1, 250.00m)));
    }

    #endregion

    #region CTE FromCte (identity / all columns)

    /// <summary>
    /// Coverage for the all-columns FromCte path: identity projection (<c>.Select(o => o)</c>)
    /// rather than a tuple projection. The remaining FromCte tests all use tuple
    /// projections, so this verifies that the column-set propagation from the inner
    /// CTE through FromCte to a full-entity outer projection works without column
    /// loss or reordering.
    /// </summary>
    [Test]
    public async Task Cte_FromCte_AllColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order>(Lite.Orders().Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => o)
            .Prepare();
        var pg = Pg.With<Pg.Order>(Pg.Orders().Where(o => o.Total > 100))
            .FromCte<Pg.Order>()
            .Select(o => o)
            .Prepare();
        var my = My.With<My.Order>(My.Orders().Where(o => o.Total > 100))
            .FromCte<My.Order>()
            .Select(o => o)
            .Prepare();
        var ss = Ss.With<Ss.Order>(Ss.Orders().Where(o => o.Total > 100))
            .FromCte<Ss.Order>()
            .Select(o => o)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > 100) SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > 100) SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [Order]");

        // Seed data: OrderId=1 Total=250.00, OrderId=2 Total=75.50, OrderId=3 Total=150.00
        // Orders with Total > 100: OrderId=1 (250.00), OrderId=3 (150.00)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].Total, Is.EqualTo(250.00m));
        Assert.That(results[1].OrderId, Is.EqualTo(3));
        Assert.That(results[1].Total, Is.EqualTo(150.00m));
    }

    #endregion

    #region CTE With() chained (multiple distinct CTEs)

    /// <summary>
    /// Regression test for issue #206. Two distinct CTEs (<c>Order</c>, <c>User</c>)
    /// chained via <c>db.With&lt;Order&gt;(...).With&lt;User&gt;(...)</c> where BOTH inner
    /// queries capture a local variable. Validates two interlocking fixes:
    /// (1) <see cref="IR.SqlAssembler"/> rebases inner CTE parameter placeholders so the
    /// outer WITH clause uses distinct names per CTE on named-placeholder dialects;
    /// (2) <see cref="CodeGen.TransitionBodyEmitter"/> reuses the carrier produced by the
    /// first <c>With&lt;&gt;</c> instead of allocating a fresh one (which would discard
    /// CTE A's captured-parameter state and corrupt <c>Ctx</c>).
    /// </summary>
    [Test]
    public async Task Cte_TwoChainedWiths_DistinctDtos_CapturedParams()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal orderCutoff = 100m;
        bool activeFilter = true;

        var lt = Lite
            .With<Order>(Lite.Orders().Where(o => o.Total > orderCutoff))
            .With<User>(Lite.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg
            .With<Pg.Order>(Pg.Orders().Where(o => o.Total > orderCutoff))
            .With<Pg.User>(Pg.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My
            .With<My.Order>(My.Orders().Where(o => o.Total > orderCutoff))
            .With<My.User>(My.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss
            .With<Ss.Order>(Ss.Orders().Where(o => o.Total > orderCutoff))
            .With<Ss.User>(Ss.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > @p0), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = @p1) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > $1), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = $2) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > ?), `User` AS (SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = ?) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > @p0), [User] AS (SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = @p1) SELECT [OrderId], [Total] FROM [Order]");

        // Seed data: orders (id, userid, total) = (1, 1, 250), (2, 1, 75.50), (3, 2, 150)
        // With cutoff = 100: orders 1 and 3 match. With the bug, the discarded carrier
        // would reset orderCutoff to default(decimal) = 0 and all 3 rows would match.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    /// <summary>
    /// Generalization check for the multi-CTE fix: three distinct CTEs chained, each with
    /// its own captured parameter at a distinct <c>ParameterOffset</c>. Validates that the
    /// first-vs-subsequent <c>With&lt;&gt;</c> detection in <c>EmitCteDefinition</c> and
    /// the parameter-placeholder rebasing in <c>SqlAssembler</c> generalize beyond the
    /// minimal 2-CTE regression case to N-CTE chains.
    /// </summary>
    [Test]
    public async Task Cte_ThreeChainedWiths_AllUsedDownstream()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal orderCutoff = 100m;
        bool activeFilter = true;
        int qtyFilter = 1;

        var lt = Lite
            .With<Order>(Lite.Orders().Where(o => o.Total > orderCutoff))
            .With<User>(Lite.Users().Where(u => u.IsActive == activeFilter))
            .With<OrderItem>(Lite.OrderItems().Where(oi => oi.Quantity > qtyFilter))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg
            .With<Pg.Order>(Pg.Orders().Where(o => o.Total > orderCutoff))
            .With<Pg.User>(Pg.Users().Where(u => u.IsActive == activeFilter))
            .With<Pg.OrderItem>(Pg.OrderItems().Where(oi => oi.Quantity > qtyFilter))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My
            .With<My.Order>(My.Orders().Where(o => o.Total > orderCutoff))
            .With<My.User>(My.Users().Where(u => u.IsActive == activeFilter))
            .With<My.OrderItem>(My.OrderItems().Where(oi => oi.Quantity > qtyFilter))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss
            .With<Ss.Order>(Ss.Orders().Where(o => o.Total > orderCutoff))
            .With<Ss.User>(Ss.Users().Where(u => u.IsActive == activeFilter))
            .With<Ss.OrderItem>(Ss.OrderItems().Where(oi => oi.Quantity > qtyFilter))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > @p0), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = @p1), \"OrderItem\" AS (SELECT \"OrderItemId\", \"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\" FROM \"order_items\" WHERE \"Quantity\" > @p2) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > $1), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = $2), \"OrderItem\" AS (SELECT \"OrderItemId\", \"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\" FROM \"order_items\" WHERE \"Quantity\" > $3) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > ?), `User` AS (SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = ?), `OrderItem` AS (SELECT `OrderItemId`, `OrderId`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal` FROM `order_items` WHERE `Quantity` > ?) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > @p0), [User] AS (SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = @p1), [OrderItem] AS (SELECT [OrderItemId], [OrderId], [ProductName], [Quantity], [UnitPrice], [LineTotal] FROM [order_items] WHERE [Quantity] > @p2) SELECT [OrderId], [Total] FROM [Order]");

        // Same expected result as the 2-CTE test (outer query reads only from Order CTE)
        // but with three CTEs declared and three captured params at offsets 0, 1, 2.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    /// <summary>
    /// Boundary test for the parameter-offset logic: the FIRST CTE has zero captured
    /// parameters (literal-only inner query) and the SECOND CTE has a captured parameter.
    /// With the fix, the second CTE's parameter lands at outer carrier slot P0 (because
    /// <c>cte.ParameterOffset</c> accumulates zero from the first CTE) and its inner
    /// SQL must render <c>@p0</c> (or <c>$1</c>) — NOT an offset placeholder. Validates
    /// that the rebase correctly handles the zero-offset case for a non-first CTE and
    /// that the carrier <c>Unsafe.As</c> recovery path works even when no prior params
    /// have been copied into the shared carrier.
    /// </summary>
    [Test]
    public async Task Cte_TwoChainedWiths_FirstEmptySecondCaptured_CapturedParam()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool activeFilter = true;

        // First With uses a literal-only Where (no captured params). Second With
        // captures activeFilter. The second CTE's parameter must appear as @p0/$1
        // because the first CTE contributes zero parameters to the outer slot array.
        var lt = Lite
            .With<Order>(Lite.Orders().Where(o => o.Total > 100))
            .With<User>(Lite.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg
            .With<Pg.Order>(Pg.Orders().Where(o => o.Total > 100))
            .With<Pg.User>(Pg.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My
            .With<My.Order>(My.Orders().Where(o => o.Total > 100))
            .With<My.User>(My.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss
            .With<Ss.Order>(Ss.Orders().Where(o => o.Total > 100))
            .With<Ss.User>(Ss.Users().Where(u => u.IsActive == activeFilter))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = @p0) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = $1) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > 100), `User` AS (SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = ?) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > 100), [User] AS (SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = @p0) SELECT [OrderId], [Total] FROM [Order]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    #endregion

    #region CTE FromCte (dedicated DTO via projection)

    /// <summary>
    /// Coverage for the dedicated-DTO path: the CTE name is derived from a DTO class
    /// that is not a schema entity. The 2-arg <c>With&lt;TEntity, TDto&gt;</c> overload
    /// composes a Select projection into a DTO type, and FromCte&lt;TDto&gt; uses that
    /// DTO as the primary FROM source. Verifies that <c>CteDtoResolver.ResolveColumns</c>
    /// produces the right column metadata for a non-entity class.
    /// </summary>
    [Test]
    public async Task Cte_FromCte_DedicatedDto()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order, OrderSummaryDto>(
                Lite.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order, OrderSummaryDto>(
                Pg.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var my = My.With<My.Order, OrderSummaryDto>(
                My.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order, OrderSummaryDto>(
                Ss.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"OrderSummaryDto\" AS (SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"OrderSummaryDto\"",
            pg:     "WITH \"OrderSummaryDto\" AS (SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"OrderSummaryDto\"",
            mysql:  "WITH `OrderSummaryDto` AS (SELECT `OrderId`, `Total`, `Status` FROM `orders` WHERE `Total` > 100) SELECT `OrderId`, `Total` FROM `OrderSummaryDto`",
            ss:     "WITH [OrderSummaryDto] AS (SELECT [OrderId], [Total], [Status] FROM [orders] WHERE [Total] > 100) SELECT [OrderId], [Total] FROM [OrderSummaryDto]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    #endregion
}
