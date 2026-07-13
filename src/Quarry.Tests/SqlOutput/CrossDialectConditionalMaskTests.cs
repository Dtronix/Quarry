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
    #region Cascades — else-if chains and multi-clause arms (#307 defect 2)

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Mask_ElseIfChain_ThreeArms_ExecutesEachArm(int arm)
    {
        // Replaces the step-2 guard pin: with structural cascade grouping the else-if
        // shape enumerates one mask per arm ({1,2,4}) and every runtime path dispatches
        // a real variant. Before #307 step 5, arm 0 hit an unenumerated mask (null SQL).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool a = arm == 0, b = arm == 1;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId);

        if (a)
        {
            lt = lt.Where(u => u.UserId >= 1);
            pg = pg.Where(u => u.UserId >= 1);
            my = my.Where(u => u.UserId >= 1);
            ss = ss.Where(u => u.UserId >= 1);
        }
        else if (b)
        {
            lt = lt.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.UserId >= 2);
            my = my.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.UserId >= 2);
        }
        else
        {
            lt = lt.Where(u => u.UserId >= 3);
            pg = pg.Where(u => u.UserId >= 3);
            my = my.Where(u => u.UserId >= 3);
            ss = ss.Where(u => u.UserId >= 3);
        }

        var expectedCount = 3 - arm;
        var expectedFirstId = arm + 1;

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(expectedFirstId));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Mask_ElseIfChain_ThreeArms_Sql(int arm)
    {
        // ToDiagnostics consistency: the reported SQL carries exactly the taken arm's
        // predicate in every dialect.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool a = arm == 0, b = arm == 1;

        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (a)
        {
            lt = lt.Where(u => u.UserId >= 1);
            pg = pg.Where(u => u.UserId >= 1);
            my = my.Where(u => u.UserId >= 1);
            ss = ss.Where(u => u.UserId >= 1);
        }
        else if (b)
        {
            lt = lt.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.UserId >= 2);
            my = my.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.UserId >= 2);
        }
        else
        {
            lt = lt.Where(u => u.UserId >= 3);
            pg = pg.Where(u => u.UserId >= 3);
            my = my.Where(u => u.UserId >= 3);
            ss = ss.Where(u => u.UserId >= 3);
        }

        var bound = arm + 1;
        var cols = "\"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\"";
        var colsMy = "`UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin`";
        var colsSs = "[UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin]";
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: $"SELECT {cols} FROM \"users\" WHERE \"UserId\" >= {bound}",
            pg:     $"SELECT {cols} FROM \"users\" WHERE \"UserId\" >= {bound}",
            mysql:  $"SELECT {colsMy} FROM `users` WHERE `UserId` >= {bound}",
            ss:     $"SELECT {colsSs} FROM [users] WHERE [UserId] >= {bound}");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_TwoClausesInOneBranch_ExecutesBothWays(bool strict)
    {
        // Repro shape 2 from #307: both clauses of the taken arm must apply together.
        // Before step 5 the both-bits mask was never enumerated (null SQL at runtime),
        // and the enumerated single-bit variants each carried only half the predicates.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId);

        if (strict)
        {
            lt = lt.Where(u => u.UserId >= 2);
            lt = lt.Where(u => u.IsActive);
            pg = pg.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.IsActive);
            my = my.Where(u => u.UserId >= 2);
            my = my.Where(u => u.IsActive);
            ss = ss.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.IsActive);
        }
        else
        {
            lt = lt.Where(u => u.UserId >= 1);
            pg = pg.Where(u => u.UserId >= 1);
            my = my.Where(u => u.UserId >= 1);
            ss = ss.Where(u => u.UserId >= 1);
        }

        // strict: UserId >= 2 AND IsActive → only Bob (2). else: all 3 users.
        var expectedCount = strict ? 1 : 3;
        var expectedFirstId = strict ? 2 : 1;

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(expectedFirstId));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Mask_ElseIfNoFinalElse_ExecutesIncludingNoArmPath(int arm)
    {
        // Without a final else the cascade can take no arm — mask 0 must dispatch a
        // real (unfiltered) variant. arm 2 drives that path.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool a = arm == 0, b = arm == 1;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId);

        if (a)
        {
            lt = lt.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.UserId >= 2);
            my = my.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.UserId >= 2);
        }
        else if (b)
        {
            lt = lt.Where(u => u.UserId >= 3);
            pg = pg.Where(u => u.UserId >= 3);
            my = my.Where(u => u.UserId >= 3);
            ss = ss.Where(u => u.UserId >= 3);
        }

        var expectedCount = arm switch { 0 => 2, 1 => 1, _ => 3 };
        var expectedFirstId = arm switch { 0 => 2, 1 => 3, _ => 1 };

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(expectedFirstId));
    }

    #endregion

    #region Nested cascades (#307 review F3/F7)

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task Mask_NestedIfElse_InConditionalArm_Executes(int path)
    {
        // Review F3: a fully-represented if/else inside an outer conditional arm.
        // path 0 = outer skipped (mask 0 — previously an unenumerated throw),
        // path 1 = outer + then-arm, path 2 = outer + else-arm.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool outer = path != 0, inner = path == 1;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId);

        if (outer)
        {
            if (inner)
            {
                lt = lt.Where(u => u.UserId >= 2);
                pg = pg.Where(u => u.UserId >= 2);
                my = my.Where(u => u.UserId >= 2);
                ss = ss.Where(u => u.UserId >= 2);
            }
            else
            {
                lt = lt.Where(u => u.UserId >= 3);
                pg = pg.Where(u => u.UserId >= 3);
                my = my.Where(u => u.UserId >= 3);
                ss = ss.Where(u => u.UserId >= 3);
            }
        }

        var expectedCount = path switch { 0 => 3, 1 => 2, _ => 1 };
        var expectedFirstId = path switch { 0 => 1, 1 => 2, _ => 3 };

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(expectedFirstId));
    }

    #endregion

    #region Offset-without-LIMIT idiom (#307 review F5)

    [Test]
    public async Task Pagination_OffsetWithoutLimit_Executes()
    {
        // Bare `OFFSET n` is rejected by SQLite/MySQL — FormatLimitOffset now emits
        // the dialect's no-limit idiom (LIMIT -1 / LIMIT 18446744073709551615).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltRows = await Lite.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1).ExecuteFetchAllAsync();
        var pgRows = await Pg.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1).ExecuteFetchAllAsync();
        var myRows = await My.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1).ExecuteFetchAllAsync();
        var ssRows = await Ss.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1).ExecuteFetchAllAsync();

        Assert.That(ltRows.Count, Is.EqualTo(2));
        Assert.That(ltRows[0].UserId, Is.EqualTo(2));
        Assert.That(pgRows.Count, Is.EqualTo(2));
        Assert.That(pgRows[0].UserId, Is.EqualTo(2));
        Assert.That(myRows.Count, Is.EqualTo(2));
        Assert.That(myRows[0].UserId, Is.EqualTo(2));
        Assert.That(ssRows.Count, Is.EqualTo(2));
        Assert.That(ssRows[0].UserId, Is.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalLimit_UnconditionalOffset_Executes(bool capped)
    {
        // The limit-inactive variant is an offset-only variant manufactured by mask
        // gating — it must use the no-limit idiom, not bare OFFSET.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId).Offset(1);

        if (capped)
        {
            lt = lt.Limit(1);
            pg = pg.Limit(1);
            my = my.Limit(1);
            ss = ss.Limit(1);
        }

        var expectedCount = capped ? 1 : 2;

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(2));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(2));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(2));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(2));
    }

    #endregion

    #region MySQL bind order with conditional runtime pagination (#307 review F8)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalRuntimeLimit_WithParameterizedWhere_Executes(bool limitOn)
    {
        // A captured-variable Where (real chain parameter) plus a conditional
        // runtime-valued Limit (virtual pagination slot). On MySQL this drives the
        // per-variant positional `?` bind-order extraction with a trailing slot that
        // exists in only one variant.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        int minId = 2, n = 1;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId).Where(u => u.UserId >= minId);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId).Where(u => u.UserId >= minId);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId).Where(u => u.UserId >= minId);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId).Where(u => u.UserId >= minId);

        if (limitOn)
        {
            lt = lt.Limit(n);
            pg = pg.Limit(n);
            my = my.Limit(n);
            ss = ss.Limit(n);
        }

        var expectedCount = limitOn ? 1 : 2;

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(2));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(2));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(2));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(2));
    }

    #endregion

    #region Conditional Limit/Offset/Distinct (#307 — mask-gated)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalLimitLiteral_Sql(bool limitOn)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (limitOn)
        {
            lt = lt.Limit(2);
            pg = pg.Limit(2);
            my = my.Limit(2);
            ss = ss.Limit(2);
        }

        var cols = "\"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\"";
        var colsMy = "`UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin`";
        var colsSs = "[UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin]";
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: $"SELECT {cols} FROM \"users\"" + (limitOn ? " LIMIT 2" : ""),
            pg:     $"SELECT {cols} FROM \"users\"" + (limitOn ? " LIMIT 2" : ""),
            mysql:  $"SELECT {colsMy} FROM `users`" + (limitOn ? " LIMIT 2" : ""),
            ss:     $"SELECT {colsSs} FROM [users]" + (limitOn ? " ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY" : ""));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalLimitLiteral_Executes(bool limitOn)
    {
        // The issue's silent-truncation repro: with the branch NOT taken, the full
        // row set must come back (previously LIMIT 25 was baked into every variant).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (limitOn)
        {
            lt = lt.Limit(2);
            pg = pg.Limit(2);
            my = my.Limit(2);
            ss = ss.Limit(2);
        }

        var expected = limitOn ? 2 : 3;
        Assert.That((await lt.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await pg.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await my.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await ss.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalLimitRuntime_Executes(bool limitOn)
    {
        // Runtime-valued limit: with the branch NOT taken the carrier field stays at
        // its 0 default — the variant must omit LIMIT entirely, not emit LIMIT 0
        // (which would silently return zero rows).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        int n = 2;
        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (limitOn)
        {
            lt = lt.Limit(n);
            pg = pg.Limit(n);
            my = my.Limit(n);
            ss = ss.Limit(n);
        }

        var expected = limitOn ? 2 : 3;
        Assert.That((await lt.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await pg.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await my.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await ss.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalOffset_Executes(bool skipFirst)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Unconditional Limit + conditional Offset. (Offset WITHOUT Limit is covered by
        // Pagination_OffsetWithoutLimit_Executes — fixed via the no-limit idiom, review F5.)
        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);

        if (skipFirst)
        {
            lt = lt.Offset(1);
            pg = pg.Offset(1);
            my = my.Offset(1);
            ss = ss.Offset(1);
        }

        var expected = skipFirst ? 2 : 3;
        var firstId = skipFirst ? 2 : 1;
        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expected));
        Assert.That(ltRows[0].UserId, Is.EqualTo(firstId));
        Assert.That(pgRows.Count, Is.EqualTo(expected));
        Assert.That(pgRows[0].UserId, Is.EqualTo(firstId));
        Assert.That(myRows.Count, Is.EqualTo(expected));
        Assert.That(myRows[0].UserId, Is.EqualTo(firstId));
        Assert.That(ssRows.Count, Is.EqualTo(expected));
        Assert.That(ssRows[0].UserId, Is.EqualTo(firstId));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalDistinct_Executes(bool dedupe)
    {
        // users has 3 rows with 2 distinct IsActive values — DISTINCT collapses to 2.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u.IsActive);
        var pg = Pg.Users().Select(u => u.IsActive);
        var my = My.Users().Select(u => u.IsActive);
        var ss = Ss.Users().Select(u => u.IsActive);

        if (dedupe)
        {
            lt = lt.Distinct();
            pg = pg.Distinct();
            my = my.Distinct();
            ss = ss.Distinct();
        }

        var expected = dedupe ? 2 : 3;
        Assert.That((await lt.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await pg.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await my.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
        Assert.That((await ss.ExecuteFetchAllAsync()).Count, Is.EqualTo(expected));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Mask_ConditionalWhereAndLimit_Executes(bool filter, bool limitOn)
    {
        // Two bits, four variants — every combination executed with row-content asserts.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId);

        if (filter)
        {
            lt = lt.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.UserId >= 2);
            my = my.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.UserId >= 2);
        }
        if (limitOn)
        {
            lt = lt.Limit(1);
            pg = pg.Limit(1);
            my = my.Limit(1);
            ss = ss.Limit(1);
        }

        var expectedCount = (filter, limitOn) switch
        {
            (false, false) => 3,
            (true, false) => 2,
            _ => 1
        };
        var expectedFirstId = filter ? 2 : 1;

        var ltRows = await lt.ExecuteFetchAllAsync();
        var pgRows = await pg.ExecuteFetchAllAsync();
        var myRows = await my.ExecuteFetchAllAsync();
        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ltRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ltRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(pgRows.Count, Is.EqualTo(expectedCount));
        Assert.That(pgRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(myRows.Count, Is.EqualTo(expectedCount));
        Assert.That(myRows[0].UserId, Is.EqualTo(expectedFirstId));
        Assert.That(ssRows.Count, Is.EqualTo(expectedCount));
        Assert.That(ssRows[0].UserId, Is.EqualTo(expectedFirstId));
    }

    #endregion

    #region ToDiagnostics consistency for gated modifiers and multi-clause arms (#307 review F10)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalOffset_Sql(bool skipFirst)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);
        var pg = Pg.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);
        var my = My.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);
        var ss = Ss.Users().Select(u => u).OrderBy(u => u.UserId).Limit(10);

        if (skipFirst)
        {
            lt = lt.Offset(1);
            pg = pg.Offset(1);
            my = my.Offset(1);
            ss = ss.Offset(1);
        }

        var cols = "\"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\"";
        var colsMy = "`UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin`";
        var colsSs = "[UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin]";
        var tail = skipFirst ? " OFFSET 1" : "";
        var ssOffset = skipFirst ? 1 : 0;
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: $"SELECT {cols} FROM \"users\" ORDER BY \"UserId\" ASC LIMIT 10{tail}",
            pg:     $"SELECT {cols} FROM \"users\" ORDER BY \"UserId\" ASC LIMIT 10{tail}",
            mysql:  $"SELECT {colsMy} FROM `users` ORDER BY `UserId` ASC LIMIT 10{tail}",
            ss:     $"SELECT {colsSs} FROM [users] ORDER BY [UserId] ASC OFFSET {ssOffset} ROWS FETCH NEXT 10 ROWS ONLY");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_ConditionalDistinct_Sql(bool dedupe)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u.IsActive);
        var pg = Pg.Users().Select(u => u.IsActive);
        var my = My.Users().Select(u => u.IsActive);
        var ss = Ss.Users().Select(u => u.IsActive);

        if (dedupe)
        {
            lt = lt.Distinct();
            pg = pg.Distinct();
            my = my.Distinct();
            ss = ss.Distinct();
        }

        var kw = dedupe ? "DISTINCT " : "";
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: $"SELECT {kw}\"IsActive\" FROM \"users\"",
            pg:     $"SELECT {kw}\"IsActive\" FROM \"users\"",
            mysql:  $"SELECT {kw}`IsActive` FROM `users`",
            ss:     $"SELECT {kw}[IsActive] FROM [users]");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mask_TwoClausesInOneBranch_Sql(bool strict)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (strict)
        {
            lt = lt.Where(u => u.UserId >= 2);
            lt = lt.Where(u => u.IsActive);
            pg = pg.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.IsActive);
            my = my.Where(u => u.UserId >= 2);
            my = my.Where(u => u.IsActive);
            ss = ss.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.IsActive);
        }
        else
        {
            lt = lt.Where(u => u.UserId >= 1);
            pg = pg.Where(u => u.UserId >= 1);
            my = my.Where(u => u.UserId >= 1);
            ss = ss.Where(u => u.UserId >= 1);
        }

        var cols = "\"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\"";
        var colsMy = "`UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin`";
        var colsSs = "[UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin]";
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: $"SELECT {cols} FROM \"users\" WHERE " + (strict ? "(\"UserId\" >= 2) AND (\"IsActive\" = 1)" : "\"UserId\" >= 1"),
            pg:     $"SELECT {cols} FROM \"users\" WHERE " + (strict ? "(\"UserId\" >= 2) AND (\"IsActive\" = TRUE)" : "\"UserId\" >= 1"),
            mysql:  $"SELECT {colsMy} FROM `users` WHERE " + (strict ? "(`UserId` >= 2) AND (`IsActive` = 1)" : "`UserId` >= 1"),
            ss:     $"SELECT {colsSs} FROM [users] WHERE " + (strict ? "([UserId] >= 2) AND ([IsActive] = 1)" : "[UserId] >= 1"));
    }

    [Test]
    public async Task Mask_ElseIfNoFinalElse_NoArm_Sql()
    {
        // No arm taken → mask 0 → diagnostics must report the unfiltered SQL.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool a = false, b = false;

        var lt = Lite.Users().Select(u => u);
        var pg = Pg.Users().Select(u => u);
        var my = My.Users().Select(u => u);
        var ss = Ss.Users().Select(u => u);

        if (a)
        {
            lt = lt.Where(u => u.UserId >= 2);
            pg = pg.Where(u => u.UserId >= 2);
            my = my.Where(u => u.UserId >= 2);
            ss = ss.Where(u => u.UserId >= 2);
        }
        else if (b)
        {
            lt = lt.Where(u => u.UserId >= 3);
            pg = pg.Where(u => u.UserId >= 3);
            my = my.Where(u => u.UserId >= 3);
            ss = ss.Where(u => u.UserId >= 3);
        }

        var cols = "\"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\"";
        var colsMy = "`UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin`";
        var colsSs = "[UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin]";
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: $"SELECT {cols} FROM \"users\"",
            pg:     $"SELECT {cols} FROM \"users\"",
            mysql:  $"SELECT {colsMy} FROM `users`",
            ss:     $"SELECT {colsSs} FROM [users]");
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
