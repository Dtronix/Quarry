using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectEnumTests
{
    #region Enum in WHERE

    [Test]
    public async Task Where_EnumCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var priority = OrderPriority.Urgent;

        var lt = Lite.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var pg = Pg.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var my = My.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var ss = Ss.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = @p0",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = $1",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders` WHERE `Priority` = ?",
            ss:     "SELECT [OrderId], [Total] FROM [orders] WHERE [Priority] = @p0");

        // Priority: Order1=2(High), Order2=1(Normal), Order3=3(Urgent) — only Order3 matches Urgent(3)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0], Is.EqualTo((3, 150.00m)));
    }

    [Test]
    public async Task Where_EnumCapturedVariable_ExecutesCorrectly()
    {
        // Regression test: enum parameter must be cast to underlying int for SQLite binding.
        // Without the cast, the enum object is boxed and SQLite rejects or mismatches it.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var priority = OrderPriority.High;

        var lt = Lite.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var pg = Pg.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var my = My.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var ss = Ss.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = @p0",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = $1",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders` WHERE `Priority` = ?",
            ss:     "SELECT [OrderId], [Total] FROM [orders] WHERE [Priority] = @p0");

        // Seed: Order1(High), Order2(Normal), Order3(Urgent) — only Order1 matches High
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].OrderId, Is.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].OrderId, Is.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].OrderId, Is.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].OrderId, Is.EqualTo(1));
    }

    [Test]
    public async Task Where_EnumCompoundCondition_ExecutesCorrectly()
    {
        // Regression test: enum parameter in a compound WHERE with other conditions.
        // The enum parameter goes through EnrichParametersFromColumns which must set both
        // IsEnum and EnumUnderlyingType so the terminal emits (int) cast, not bare object boxing.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var priority = OrderPriority.High;
        var minTotal = 100m;

        var lt = Lite.Orders().Where(o => o.Priority == priority && o.Total > minTotal).Select(o => (o.OrderId, o.Total)).Prepare();
        var pg = Pg.Orders().Where(o => o.Priority == priority && o.Total > minTotal).Select(o => (o.OrderId, o.Total)).Prepare();
        var my = My.Orders().Where(o => o.Priority == priority && o.Total > minTotal).Select(o => (o.OrderId, o.Total)).Prepare();
        var ss = Ss.Orders().Where(o => o.Priority == priority && o.Total > minTotal).Select(o => (o.OrderId, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = @p0 AND \"Total\" > @p1",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = $1 AND \"Total\" > $2",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders` WHERE `Priority` = ? AND `Total` > ?",
            ss:     "SELECT [OrderId], [Total] FROM [orders] WHERE [Priority] = @p0 AND [Total] > @p1");

        // Seed: Order1(High,250), Order2(Normal,75.50), Order3(Urgent,150) — only Order1 matches
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].OrderId, Is.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].OrderId, Is.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].OrderId, Is.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].OrderId, Is.EqualTo(1));
    }

    [Test]
    public async Task Where_EnumDiagnostics_ParameterValueIsInteger()
    {
        // Verify the diagnostics report the enum parameter value as an integer, not the enum name.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var priority = OrderPriority.Urgent;

        var lt = Lite.Orders().Where(o => o.Priority == priority).Select(o => o.OrderId).Prepare();
        var pg = Pg.Orders().Where(o => o.Priority == priority).Select(o => o.OrderId).Prepare();
        var my = My.Orders().Where(o => o.Priority == priority).Select(o => o.OrderId).Prepare();
        var ss = Ss.Orders().Where(o => o.Priority == priority).Select(o => o.OrderId).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\" FROM \"orders\" WHERE \"Priority\" = @p0",
            pg:     "SELECT \"OrderId\" FROM \"orders\" WHERE \"Priority\" = $1",
            mysql:  "SELECT `OrderId` FROM `orders` WHERE `Priority` = ?",
            ss:     "SELECT [OrderId] FROM [orders] WHERE [Priority] = @p0");

        var diag = lt.ToDiagnostics();
        Assert.That(diag.Parameters, Has.Count.EqualTo(1));
        // The parameter value should be the underlying integer (3), not the enum name
        Assert.That(diag.Parameters[0].Value, Is.EqualTo(3));

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
    }

    #endregion

    #region Boolean in INSERT

    [Test]
    public async Task Insert_WithBooleanColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Boolean values are parameterized in INSERT, so no literal TRUE/1 difference
        var lt = Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = DateTime.UtcNow }).Prepare();
        var pg = Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = DateTime.UtcNow }).Prepare();
        var my = My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = DateTime.UtcNow }).Prepare();
        var ss = Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = DateTime.UtcNow }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) OUTPUT INSERTED.[UserId] VALUES (@p0, @p1, @p2)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion

    #region Enum in INSERT

    [Test]
    public async Task Insert_WithEnumColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = new DateTime(2024, 1, 1) }).Prepare();
        var pg = Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = new DateTime(2024, 1, 1) }).Prepare();
        var my = My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = new DateTime(2024, 1, 1) }).Prepare();
        var ss = Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = new DateTime(2024, 1, 1) }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\") VALUES ($1, $2, $3, $4, $5) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `Priority`, `OrderDate`) VALUES (?, ?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [Priority], [OrderDate]) OUTPUT INSERTED.[OrderId] VALUES (@p0, @p1, @p2, @p3, @p4)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion

    #region Enum in UPDATE SET

    [Test]
    public async Task Update_Set_EnumColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();
        var pg = Pg.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();
        var my = My.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();
        var ss = Ss.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"orders\" SET \"Priority\" = 2 WHERE \"OrderId\" = 1",
            pg:     "UPDATE \"orders\" SET \"Priority\" = 2 WHERE \"OrderId\" = 1",
            mysql:  "UPDATE `orders` SET `Priority` = 2 WHERE `OrderId` = 1",
            ss:     "UPDATE [orders] SET [Priority] = 2 WHERE [OrderId] = 1");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));
    }

    #endregion
}
