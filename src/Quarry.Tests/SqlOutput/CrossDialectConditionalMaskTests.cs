using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable CS0162 // Unreachable code — boundary tests use if(true)/if(false) literals intentionally

/// <summary>
/// Cross-dialect SQL-output coverage for conditional clause mask boundary configurations.
/// Companion to <c>ConditionalCarrierTests</c> (which verifies carrier emission at the
/// generator level). These tests exercise the resulting SQL string per dialect at:
///   - the 8-bit mask limit (<c>ConditionalMaskBuilder.MaxConditionalBits</c>)
///   - the 2-deep nesting limit (<c>ConditionalMaskBuilder.MaxIfNestingDepth</c>)
///   - mutually-exclusive if/else groups (1 bit, two SQL variants)
///   - conditional clauses combined with unconditional ones (OrderBy/Limit/GroupBy/Having)
/// </summary>
[TestFixture]
internal class CrossDialectConditionalMaskTests
{
    #region Unenumerated Mask Guard (#307)

    [Test]
    public async Task Mask_ElseIfChain_UnenumeratedMask_ThrowsActionableGuard()
    {
        // PIN (#307 step 2): a one-level else-if cascade currently produces an
        // unenumerated mask when the first arm executes — branch groups are keyed by
        // condition text, so the first arm's bit is enumerated as independent while
        // the later arms form the exclusive group (issue #307 defect 2). Until step 5
        // makes these shapes fully supported, the terminal must fail with the
        // actionable guard exception, not a provider null-CommandText error.
        // Step 5 replaces this pin with correct-execution assertions.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        bool a = true, b = false;
        var q = Lite.Users().Select(u => u);
        if (a)
            q = q.Where(u => u.UserId >= 1);
        else if (b)
            q = q.Where(u => u.UserId >= 2);
        else
            q = q.Where(u => u.UserId >= 3);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await q.ExecuteFetchAllAsync());
        Assert.That(ex!.Message, Does.Contain("mask"));
        Assert.That(ex.Message, Does.Contain("Quarry"));
    }

    #endregion

    #region Conditional WithTimeout (#307 — no bit consumed)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalWithTimeout_SameSqlBothWays_Executes(bool slow)
    {
        // WithTimeout inside an if consumes no conditional bit: SQL is identical
        // whether or not the branch is taken, and the timeout falls back to
        // DefaultTimeout when the interceptor never runs.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // SQL-identity and variant-count for conditional WithTimeout are asserted at the
        // generator level (ConditionalCarrierTests.ConditionalWithTimeout_ConsumesNoBit);
        // here each dialect executes the chain with the branch taken and not taken.
        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (slow)
        {
            lt = lt.WithTimeout(TimeSpan.FromSeconds(90));
            pg = pg.WithTimeout(TimeSpan.FromSeconds(90));
            my = my.WithTimeout(TimeSpan.FromSeconds(90));
            ss = ss.WithTimeout(TimeSpan.FromSeconds(90));
        }

        Assert.That((await lt.ExecuteFetchAllAsync()).Count, Is.EqualTo(3));
        Assert.That((await pg.ExecuteFetchAllAsync()).Count, Is.EqualTo(3));
        Assert.That((await my.ExecuteFetchAllAsync()).Count, Is.EqualTo(3));
        Assert.That((await ss.ExecuteFetchAllAsync()).Count, Is.EqualTo(3));
    }

    #endregion

    #region 8-Bit Mask Boundaries

    [Test]
    public async Task Mask_AllEightBitsActive_RendersAllPredicates()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (true) { lt = lt.Where(u => u.UserId > 0); }
        if (true) { lt = lt.Where(u => u.UserId > 1); }
        if (true) { lt = lt.Where(u => u.UserId > 2); }
        if (true) { lt = lt.Where(u => u.UserId > 3); }
        if (true) { lt = lt.Where(u => u.UserId > 4); }
        if (true) { lt = lt.Where(u => u.UserId > 5); }
        if (true) { lt = lt.Where(u => u.UserId > 6); }
        if (true) { lt = lt.Where(u => u.UserId > 7); }

        if (true) { pg = pg.Where(u => u.UserId > 0); }
        if (true) { pg = pg.Where(u => u.UserId > 1); }
        if (true) { pg = pg.Where(u => u.UserId > 2); }
        if (true) { pg = pg.Where(u => u.UserId > 3); }
        if (true) { pg = pg.Where(u => u.UserId > 4); }
        if (true) { pg = pg.Where(u => u.UserId > 5); }
        if (true) { pg = pg.Where(u => u.UserId > 6); }
        if (true) { pg = pg.Where(u => u.UserId > 7); }

        if (true) { my = my.Where(u => u.UserId > 0); }
        if (true) { my = my.Where(u => u.UserId > 1); }
        if (true) { my = my.Where(u => u.UserId > 2); }
        if (true) { my = my.Where(u => u.UserId > 3); }
        if (true) { my = my.Where(u => u.UserId > 4); }
        if (true) { my = my.Where(u => u.UserId > 5); }
        if (true) { my = my.Where(u => u.UserId > 6); }
        if (true) { my = my.Where(u => u.UserId > 7); }

        if (true) { ss = ss.Where(u => u.UserId > 0); }
        if (true) { ss = ss.Where(u => u.UserId > 1); }
        if (true) { ss = ss.Where(u => u.UserId > 2); }
        if (true) { ss = ss.Where(u => u.UserId > 3); }
        if (true) { ss = ss.Where(u => u.UserId > 4); }
        if (true) { ss = ss.Where(u => u.UserId > 5); }
        if (true) { ss = ss.Where(u => u.UserId > 6); }
        if (true) { ss = ss.Where(u => u.UserId > 7); }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserId\" > 0) AND (\"UserId\" > 1) AND (\"UserId\" > 2) AND (\"UserId\" > 3) AND (\"UserId\" > 4) AND (\"UserId\" > 5) AND (\"UserId\" > 6) AND (\"UserId\" > 7)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserId\" > 0) AND (\"UserId\" > 1) AND (\"UserId\" > 2) AND (\"UserId\" > 3) AND (\"UserId\" > 4) AND (\"UserId\" > 5) AND (\"UserId\" > 6) AND (\"UserId\" > 7)",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (`UserId` > 0) AND (`UserId` > 1) AND (`UserId` > 2) AND (`UserId` > 3) AND (`UserId` > 4) AND (`UserId` > 5) AND (`UserId` > 6) AND (`UserId` > 7)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE ([UserId] > 0) AND ([UserId] > 1) AND ([UserId] > 2) AND ([UserId] > 3) AND ([UserId] > 4) AND ([UserId] > 5) AND ([UserId] > 6) AND ([UserId] > 7)");
    }

    [Test]
    public async Task Mask_NoBitsActive_RendersBareSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (false) { lt = lt.Where(u => u.UserId > 0); }
        if (false) { lt = lt.Where(u => u.UserId > 1); }
        if (false) { lt = lt.Where(u => u.UserId > 2); }
        if (false) { lt = lt.Where(u => u.UserId > 3); }
        if (false) { lt = lt.Where(u => u.UserId > 4); }
        if (false) { lt = lt.Where(u => u.UserId > 5); }
        if (false) { lt = lt.Where(u => u.UserId > 6); }
        if (false) { lt = lt.Where(u => u.UserId > 7); }

        if (false) { pg = pg.Where(u => u.UserId > 0); }
        if (false) { pg = pg.Where(u => u.UserId > 1); }
        if (false) { pg = pg.Where(u => u.UserId > 2); }
        if (false) { pg = pg.Where(u => u.UserId > 3); }
        if (false) { pg = pg.Where(u => u.UserId > 4); }
        if (false) { pg = pg.Where(u => u.UserId > 5); }
        if (false) { pg = pg.Where(u => u.UserId > 6); }
        if (false) { pg = pg.Where(u => u.UserId > 7); }

        if (false) { my = my.Where(u => u.UserId > 0); }
        if (false) { my = my.Where(u => u.UserId > 1); }
        if (false) { my = my.Where(u => u.UserId > 2); }
        if (false) { my = my.Where(u => u.UserId > 3); }
        if (false) { my = my.Where(u => u.UserId > 4); }
        if (false) { my = my.Where(u => u.UserId > 5); }
        if (false) { my = my.Where(u => u.UserId > 6); }
        if (false) { my = my.Where(u => u.UserId > 7); }

        if (false) { ss = ss.Where(u => u.UserId > 0); }
        if (false) { ss = ss.Where(u => u.UserId > 1); }
        if (false) { ss = ss.Where(u => u.UserId > 2); }
        if (false) { ss = ss.Where(u => u.UserId > 3); }
        if (false) { ss = ss.Where(u => u.UserId > 4); }
        if (false) { ss = ss.Where(u => u.UserId > 5); }
        if (false) { ss = ss.Where(u => u.UserId > 6); }
        if (false) { ss = ss.Where(u => u.UserId > 7); }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");
    }

    [Test]
    public async Task Mask_AlternatingBits_RendersOnlyActiveTerms()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        // Pattern: T,F,T,F,T,F,T,F — bits 0/2/4/6 active, predicates with 0/2/4/6 rendered.
        if (true) { lt = lt.Where(u => u.UserId > 0); }
        if (false) { lt = lt.Where(u => u.UserId > 1); }
        if (true) { lt = lt.Where(u => u.UserId > 2); }
        if (false) { lt = lt.Where(u => u.UserId > 3); }
        if (true) { lt = lt.Where(u => u.UserId > 4); }
        if (false) { lt = lt.Where(u => u.UserId > 5); }
        if (true) { lt = lt.Where(u => u.UserId > 6); }
        if (false) { lt = lt.Where(u => u.UserId > 7); }

        if (true) { pg = pg.Where(u => u.UserId > 0); }
        if (false) { pg = pg.Where(u => u.UserId > 1); }
        if (true) { pg = pg.Where(u => u.UserId > 2); }
        if (false) { pg = pg.Where(u => u.UserId > 3); }
        if (true) { pg = pg.Where(u => u.UserId > 4); }
        if (false) { pg = pg.Where(u => u.UserId > 5); }
        if (true) { pg = pg.Where(u => u.UserId > 6); }
        if (false) { pg = pg.Where(u => u.UserId > 7); }

        if (true) { my = my.Where(u => u.UserId > 0); }
        if (false) { my = my.Where(u => u.UserId > 1); }
        if (true) { my = my.Where(u => u.UserId > 2); }
        if (false) { my = my.Where(u => u.UserId > 3); }
        if (true) { my = my.Where(u => u.UserId > 4); }
        if (false) { my = my.Where(u => u.UserId > 5); }
        if (true) { my = my.Where(u => u.UserId > 6); }
        if (false) { my = my.Where(u => u.UserId > 7); }

        if (true) { ss = ss.Where(u => u.UserId > 0); }
        if (false) { ss = ss.Where(u => u.UserId > 1); }
        if (true) { ss = ss.Where(u => u.UserId > 2); }
        if (false) { ss = ss.Where(u => u.UserId > 3); }
        if (true) { ss = ss.Where(u => u.UserId > 4); }
        if (false) { ss = ss.Where(u => u.UserId > 5); }
        if (true) { ss = ss.Where(u => u.UserId > 6); }
        if (false) { ss = ss.Where(u => u.UserId > 7); }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserId\" > 0) AND (\"UserId\" > 2) AND (\"UserId\" > 4) AND (\"UserId\" > 6)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserId\" > 0) AND (\"UserId\" > 2) AND (\"UserId\" > 4) AND (\"UserId\" > 6)",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (`UserId` > 0) AND (`UserId` > 2) AND (`UserId` > 4) AND (`UserId` > 6)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE ([UserId] > 0) AND ([UserId] > 2) AND ([UserId] > 4) AND ([UserId] > 6)");
    }

    #endregion

    #region Depth-2 Nesting

    [Test]
    public async Task Mask_DepthTwoNesting_OuterTrueInnerTrue_RendersInnerPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (true)
        {
            if (true) { lt = lt.Where(u => u.UserId > 0); }
        }
        if (true)
        {
            if (true) { pg = pg.Where(u => u.UserId > 0); }
        }
        if (true)
        {
            if (true) { my = my.Where(u => u.UserId > 0); }
        }
        if (true)
        {
            if (true) { ss = ss.Where(u => u.UserId > 0); }
        }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" > 0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" > 0",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` > 0",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] > 0");
    }

    [Test]
    public async Task Mask_DepthTwoNesting_OuterTrueInnerFalse_OmitsInnerPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (true)
        {
            if (false) { lt = lt.Where(u => u.UserId > 0); }
        }
        if (true)
        {
            if (false) { pg = pg.Where(u => u.UserId > 0); }
        }
        if (true)
        {
            if (false) { my = my.Where(u => u.UserId > 0); }
        }
        if (true)
        {
            if (false) { ss = ss.Where(u => u.UserId > 0); }
        }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users`",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users]");
    }

    #endregion

    #region Mutually-Exclusive Branches

    [Test]
    public async Task Mask_MutuallyExclusiveOrderBy_NameBranch()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var sortByName = true;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (sortByName) { lt = lt.OrderBy(u => u.UserName); }
        else { lt = lt.OrderBy(u => u.UserId); }

        if (sortByName) { pg = pg.OrderBy(u => u.UserName); }
        else { pg = pg.OrderBy(u => u.UserId); }

        if (sortByName) { my = my.OrderBy(u => u.UserName); }
        else { my = my.OrderBy(u => u.UserId); }

        if (sortByName) { ss = ss.OrderBy(u => u.UserName); }
        else { ss = ss.OrderBy(u => u.UserId); }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" ORDER BY \"UserName\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" ORDER BY \"UserName\" ASC",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` ORDER BY `UserName` ASC",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] ORDER BY [UserName] ASC");
    }

    [Test]
    public async Task Mask_MutuallyExclusiveOrderBy_IdBranch()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var sortByName = false;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (sortByName) { lt = lt.OrderBy(u => u.UserName); }
        else { lt = lt.OrderBy(u => u.UserId); }

        if (sortByName) { pg = pg.OrderBy(u => u.UserName); }
        else { pg = pg.OrderBy(u => u.UserId); }

        if (sortByName) { my = my.OrderBy(u => u.UserName); }
        else { my = my.OrderBy(u => u.UserId); }

        if (sortByName) { ss = ss.OrderBy(u => u.UserName); }
        else { ss = ss.OrderBy(u => u.UserId); }

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` ORDER BY `UserId` ASC",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] ORDER BY [UserId] ASC");
    }

    #endregion

    #region Conditional + Unconditional Mix

    [Test]
    public async Task Mask_ConditionalWhere_PlusUnconditionalOrderByLimit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // .Where(u => true) bridges IEntityAccessor<T> -> IQueryBuilder<T> with no SQL effect.
        IQueryBuilder<User> lt = Lite.Users().Where(u => true);
        IQueryBuilder<Pg.User> pg = Pg.Users().Where(u => true);
        IQueryBuilder<My.User> my = My.Users().Where(u => true);
        IQueryBuilder<Ss.User> ss = Ss.Users().Where(u => true);

        if (true) { lt = lt.Where(u => u.IsActive); }
        if (true) { pg = pg.Where(u => u.IsActive); }
        if (true) { my = my.Where(u => u.IsActive); }
        if (true) { ss = ss.Where(u => u.IsActive); }

        var ltP = lt.OrderBy(u => u.UserName).Limit(10);
        var pgP = pg.OrderBy(u => u.UserName).Limit(10);
        var myP = my.OrderBy(u => u.UserName).Limit(10);
        var ssP = ss.OrderBy(u => u.UserName).Limit(10);

        QueryTestHarness.AssertDialects(
            ltP.ToDiagnostics(), pgP.ToDiagnostics(),
            myP.ToDiagnostics(), ssP.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC LIMIT 10",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserName\" ASC LIMIT 10",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserName` ASC LIMIT 10",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserName] ASC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");
    }

    // NOTE: A conditional-Having test (e.g. `var ltG = lt.GroupBy(...); if (true) ltG = ltG.Having(...);`)
    // currently triggers a generator misattribution: the chain binds to `CteDb` instead of
    // `TestDbContext` because both expose `IEntityAccessor<Order>` Orders() and the chain root's
    // context type is lost across the GroupBy/Having variable split. Single-line GroupBy chains
    // are fine (see CrossDialectAggregateTests). Filed as follow-up; not blocking this batch.

    #endregion
}
