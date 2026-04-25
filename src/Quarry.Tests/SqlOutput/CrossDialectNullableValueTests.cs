using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Tests for Nullable&lt;T&gt;.Value and .HasValue access on nullable columns in WHERE clauses.
/// Regression tests for silent WHERE clause dropping when .Value is used (GitHub issue: complex
/// conditional WHERE with nullable .Value in Contains caused entire WHERE to be silently omitted).
/// </summary>
[TestFixture]
internal class CrossDialectNullableValueTests
{
    #region .Value access

    [Test]
    public async Task Where_NullableColumn_Value_InComparison()
    {
        // Regression: .Value on a nullable column previously produced SqlRawExpr which
        // triggered ContainsUnsupportedRawExpr, silently dropping the entire WHERE clause.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var cutoff = new DateTime(2024, 6, 1);
        var lt = Lite.Users().Where(u => u.LastLogin != null && u.LastLogin.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.LastLogin != null && u.LastLogin.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.LastLogin != null && u.LastLogin.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.LastLogin != null && u.LastLogin.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();

        // .Value should unwrap to just the column name — SQL doesn't need .Value
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NOT NULL AND \"LastLogin\" >= @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NOT NULL AND \"LastLogin\" >= $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` IS NOT NULL AND `LastLogin` >= ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] IS NOT NULL AND [LastLogin] >= @p0");

        // Execution: Alice (2024-06-01) >= cutoff, Charlie (2024-05-15) < cutoff, Bob has NULL LastLogin.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_NullableColumn_Value_Standalone()
    {
        // Simpler test: .Value in a comparison without other AND conditions
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var cutoff = new DateTime(2024, 6, 1);
        var lt = Lite.Users().Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" >= @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" >= $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` >= ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] >= @p0");
    }

    #endregion

    #region .HasValue access

    [Test]
    public async Task Where_NullableColumn_HasValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NOT NULL",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NOT NULL",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` IS NOT NULL",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] IS NOT NULL");

        // Execution: Alice and Charlie have LastLogin, Bob does not
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Where_NullableColumn_NotHasValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => !u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => !u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => !u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => !u.LastLogin.HasValue).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NULL",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NULL",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` IS NULL",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] IS NULL");

        // Execution: Only Bob has NULL LastLogin
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region .Value with .HasValue guard (combined pattern)

    [Test]
    public async Task Where_HasValue_Then_Value_ChainedWhere()
    {
        // Pattern from real-world usage: guard with HasValue in one Where, use .Value in next
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var cutoff = new DateTime(2024, 5, 20);
        var lt = Lite.Users().Where(u => u.LastLogin.HasValue).Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.LastLogin.HasValue).Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.LastLogin.HasValue).Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.LastLogin.HasValue).Where(u => u.LastLogin!.Value >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"LastLogin\" IS NOT NULL) AND (\"LastLogin\" >= @p0)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"LastLogin\" IS NOT NULL) AND (\"LastLogin\" >= $1)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (`LastLogin` IS NOT NULL) AND (`LastLogin` >= ?)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE ([LastLogin] IS NOT NULL) AND ([LastLogin] >= @p0)");

        // Execution: Alice (2024-06-01) and Charlie (2024-05-15 < cutoff). Bob has NULL.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Complex nullable conditional patterns (regression for silent WHERE drop)

    [Test]
    public async Task Where_NullableConditional_OrPattern()
    {
        // Pattern: (capturedNullable == null || column >= capturedNullable)
        // This is the "nullable conditional comparison" pattern from the bug report.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        DateTime? cutoff = new DateTime(2024, 6, 1);
        var lt = Lite.Users().Where(u => cutoff == null || u.CreatedAt >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => cutoff == null || u.CreatedAt >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => cutoff == null || u.CreatedAt >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => cutoff == null || u.CreatedAt >= cutoff).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE @p0 IS NULL OR \"CreatedAt\" >= @p1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE $1 IS NULL OR \"CreatedAt\" >= $2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE ? IS NULL OR `CreatedAt` >= ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE @p0 IS NULL OR [CreatedAt] >= @p1");
    }

    [Test]
    public async Task Where_BooleanNot_WithNullCheck()
    {
        // Pattern: (!boolCapture || column == null)
        // This is the "boolean NOT with IS NULL check" pattern from the bug report.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        bool onlyWithoutEmail = false;
        var lt = Lite.Users().Where(u => !onlyWithoutEmail || u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => !onlyWithoutEmail || u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => !onlyWithoutEmail || u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => !onlyWithoutEmail || u.Email == null).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE NOT (@p0) OR \"Email\" IS NULL",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE NOT ($1) OR \"Email\" IS NULL",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE NOT (?) OR `Email` IS NULL",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE NOT (@p0) OR [Email] IS NULL");
    }

    [Test]
    public async Task Where_ArrayContains_WithMultipleConditions()
    {
        // Pattern: array.Contains(column) && otherCondition
        // Tests array Contains in combination with other conditions.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        int[] userIds = [1, 2];
        var lt = Lite.Users().Where(u => userIds.Contains(u.UserId) && u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => userIds.Contains(u.UserId) && u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => userIds.Contains(u.UserId) && u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => userIds.Contains(u.UserId) && u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        // ToDiagnostics() shows the runtime-expanded SQL: the collection {__COL_P0__}
        // is expanded to individual parameter slots based on the actual 2-element array.
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" IN (@p0, @p1) AND \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" IN ($1, $2) AND \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` IN (?, ?) AND `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] IN (@p0, @p1) AND [IsActive] = 1");

        // Execution: Users 1 (Alice, active) and 2 (Bob, active)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_ArrayContains_NullableColumn_Value()
    {
        // THE root cause pattern: array.Contains(column.Value) where column is nullable.
        // Before the fix, .Value produced SqlRawExpr inside the InExpr, triggering
        // ContainsUnsupportedRawExpr and silently dropping the entire WHERE clause.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        DateTime[] dates = [new DateTime(2024, 6, 1), new DateTime(2024, 5, 15)];
        var lt = Lite.Users().Where(u => u.LastLogin != null && dates.Contains(u.LastLogin.Value)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.LastLogin != null && dates.Contains(u.LastLogin.Value)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.LastLogin != null && dates.Contains(u.LastLogin.Value)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.LastLogin != null && dates.Contains(u.LastLogin.Value)).Select(u => (u.UserId, u.UserName)).Prepare();

        // .Value unwraps to just the column — the IN operand is "LastLogin", not SqlRawExpr
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NOT NULL AND \"LastLogin\" IN (@p0, @p1)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IS NOT NULL AND \"LastLogin\" IN ($1, $2)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` IS NOT NULL AND `LastLogin` IN (?, ?)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] IS NOT NULL AND [LastLogin] IN (@p0, @p1)");

        // Execution: Alice (2024-06-01) and Charlie (2024-05-15) have matching LastLogin dates
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Where_ContainsOnly_NullableColumn_Value()
    {
        // Simpler variant: Contains(column.Value) without a null guard, in its own WHERE.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        DateTime[] dates = [new DateTime(2024, 6, 1), new DateTime(2024, 5, 15)];
        var lt = Lite.Users().Where(u => dates.Contains(u.LastLogin!.Value)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => dates.Contains(u.LastLogin!.Value)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => dates.Contains(u.LastLogin!.Value)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => dates.Contains(u.LastLogin!.Value)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IN (@p0, @p1)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IN ($1, $2)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` IN (?, ?)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] IN (@p0, @p1)");
    }

    #endregion

    #region Nullable element type collection Contains (regression: IReadOnlyList<T> vs IReadOnlyList<T?>)

    [Test]
    public async Task Where_NullableArrayContains_NullableColumn()
    {
        // Regression: long?[] (or DateTime?[]) used in .Contains() on a nullable column
        // should generate IReadOnlyList<DateTime?>, not IReadOnlyList<DateTime>.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        DateTime?[] dates = [new DateTime(2024, 6, 1), new DateTime(2024, 5, 15)];
        var lt = Lite.Users().Where(u => dates.Contains(u.LastLogin)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => dates.Contains(u.LastLogin)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => dates.Contains(u.LastLogin)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => dates.Contains(u.LastLogin)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IN (@p0, @p1)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"LastLogin\" IN ($1, $2)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `LastLogin` IN (?, ?)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [LastLogin] IN (@p0, @p1)");

        // Execution: Alice (2024-06-01) and Charlie (2024-05-15) have matching LastLogin dates
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((3, "Charlie")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Where_NullableListContains_NonNullableColumn()
    {
        // Nullable collection element type (int?) against a non-nullable column (Key<int>).
        // Verifies the generator emits IReadOnlyList<int?> even when the column itself isn't nullable.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ids = new List<int?> { 1, 3 };
        var lt = Lite.Users().Where(u => ids.Contains(u.UserId)).Select(u => u.UserName).Prepare();
        var pg = Pg.Users().Where(u => ids.Contains(u.UserId)).Select(u => u.UserName).Prepare();
        var my = My.Users().Where(u => ids.Contains(u.UserId)).Select(u => u.UserName).Prepare();
        var ss = Ss.Users().Where(u => ids.Contains(u.UserId)).Select(u => u.UserName).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" IN (@p0, @p1)",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" IN ($1, $2)",
            mysql:  "SELECT `UserName` FROM `users` WHERE `UserId` IN (?, ?)",
            ss:     "SELECT [UserName] FROM [users] WHERE [UserId] IN (@p0, @p1)");
    }

    [Test]
    public async Task Where_NullableListContains_NonNullableColumn_WithExecution()
    {
        // Execution-level verification for nullable collection against non-nullable column.
        // The column (UserId) is the first column — tests that the carrier emits
        // IReadOnlyList<int?> and the cast/binding work at runtime.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var ids = new List<int?> { 1, 3 };
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .Prepare()
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo("Alice"));
        Assert.That(results[1], Is.EqualTo("Charlie"));

        var pgResults = await Pg.Users()
            .Where(u => ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .Prepare()
            .ExecuteFetchAllAsync();

        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo("Alice"));
        Assert.That(pgResults[1], Is.EqualTo("Charlie"));
    }

    #endregion
}
