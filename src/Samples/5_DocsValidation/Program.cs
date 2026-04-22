using DocsValidation;
using Microsoft.Data.Sqlite;
using Quarry;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

await CreateSchemaAndSeedAsync(connection);

await using var db = new AppDb(connection);

Console.WriteLine("Quarry v0.3.0 docs validation — exercising every rewritten query.");
Console.WriteLine(new string('=', 72));

var results = new List<(string Name, bool Passed, string Detail)>();

results.Add(await Run("landing page / getting-started — Where + Select tuple + OrderBy + Limit",
    () => LandingPage(db)));

results.Add(await Run("scaffolding.md — Where + Select tuple (3 fields) + OrderBy + Limit",
    () => Scaffolding(db)));

results.Add(await Run("querying.md — HasManyThrough tuple with TagCount scalar subquery",
    () => HasManyThroughCount(db)));

results.Add(await Run("querying.md / llm.md — Many<T>.Sum in Where clause",
    () => ManyAggregateSum(db)));

results.Add(await Run("querying.md / llm.md — Many<T>.Max in Where clause",
    () => ManyAggregateMax(db)));

results.Add(await Run("querying.md / llm.md — Many<T>.Average in Where clause",
    () => ManyAggregateAverage(db)));

results.Add(await Run("querying.md / llm.md / release notes — window functions tuple (Rank/Sum-OVER/Lag)",
    () => WindowFunctions(db)));

results.Add(await Run("querying.md — Sql.Raw<bool> in Where clause",
    () => SqlRawInWhere(db)));

results.Add(await Run("CodeFixes README — Join with condition + Select tuple",
    () => JoinWithCondition(db)));

Console.WriteLine();
Console.WriteLine(new string('=', 72));
int passed = results.Count(r => r.Passed);
int failed = results.Count - passed;
Console.WriteLine($"{passed} passed, {failed} failed of {results.Count} queries.");
return failed > 0 ? 1 : 0;


static async Task<(string Name, bool Passed, string Detail)> Run(string name, Func<Task<string>> action)
{
    Console.WriteLine();
    Console.WriteLine($"▶ {name}");
    try
    {
        var detail = await action();
        Console.WriteLine($"  ✓ {detail}");
        return (name, true, detail);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ FAILED: {ex.GetType().Name}: {ex.Message}");
        return (name, false, ex.Message);
    }
}


// docs/index.md + docs/articles/getting-started.md
static async Task<string> LandingPage(AppDb db)
{
    var activeUsers = await db.Users()
        .Where(u => u.IsActive)
        .Select(u => (u.UserName, u.Email))
        .OrderBy(u => u.UserName)
        .Limit(10)
        .ExecuteFetchAllAsync();
    return $"{activeUsers.Count} rows; first = ({activeUsers[0].UserName}, {activeUsers[0].Email})";
}


// docs/articles/scaffolding.md
static async Task<string> Scaffolding(AppDb db)
{
    var activeStudents = await db.Students()
        .Where(s => s.IsActive)
        .Select(s => (s.FirstName, s.LastName, s.Email))
        .OrderBy(s => s.LastName)
        .Limit(20)
        .ExecuteFetchAllAsync();
    return $"{activeStudents.Count} rows; first = ({activeStudents[0].FirstName} {activeStudents[0].LastName}, {activeStudents[0].Email})";
}


// docs/articles/querying.md — HasManyThrough Count() scalar subquery in Where
static async Task<string> HasManyThroughCount(AppDb db)
{
    var tagged = await db.Orders()
        .Where(o => o.Tags.Count() > 0)
        .Select(o => o.OrderId)
        .ExecuteFetchAllAsync();
    return $"{tagged.Count} orders have ≥1 tag";
}


// docs/articles/querying.md + llm.md — Many<T>.Sum in Where
static async Task<string> ManyAggregateSum(AppDb db)
{
    var bigSpenders = await db.Users()
        .Where(u => u.Orders.Sum(o => o.Total) > 100m)
        .Select(u => u.UserName)
        .ExecuteFetchAllAsync();
    return $"{bigSpenders.Count} users with total > 100 (first = {bigSpenders[0]})";
}


// docs/articles/querying.md + llm.md — Many<T>.Max in Where
static async Task<string> ManyAggregateMax(AppDb db)
{
    var premiumCustomers = await db.Users()
        .Where(u => u.Orders.Max(o => o.Total) >= 300m)
        .Select(u => u.UserName)
        .ExecuteFetchAllAsync();
    return $"{premiumCustomers.Count} users with at least one order ≥ 300 (first = {premiumCustomers[0]})";
}


// docs/articles/querying.md + llm.md — Many<T>.Average in Where
static async Task<string> ManyAggregateAverage(AppDb db)
{
    var above100 = await db.Users()
        .Where(u => u.Orders.Average(o => o.Total) > 100m)
        .Select(u => u.UserName)
        .ExecuteFetchAllAsync();
    return $"{above100.Count} users with average order > 100 (first = {above100[0]})";
}


// Window functions: Rank + Sum-OVER + Lag (3-arg with default)
static async Task<string> WindowFunctions(AppDb db)
{
    var ranked = await db.Sales()
        .Select(s => (
            s.Region,
            s.Amount,
            Rank: Sql.Rank(over => over.PartitionBy(s.Region).OrderByDescending(s.Amount)),
            RunningTotal: Sql.Sum(s.Amount, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
            Previous: Sql.Lag(s.Amount, 1, 0m, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate))
        ))
        .ExecuteFetchAllAsync();
    return $"{ranked.Count} rows; first = (region={ranked[0].Region}, amt={ranked[0].Amount}, rank={ranked[0].Rank}, running={ranked[0].RunningTotal}, prev={ranked[0].Previous})";
}


// docs/articles/querying.md — Sql.Raw<bool> inside a Where clause (supported form)
static async Task<string> SqlRawInWhere(AppDb db)
{
    var big = await db.Orders()
        .Where(o => Sql.Raw<bool>("\"Total\" BETWEEN {0} AND {1}", 100m, 250m))
        .Select(o => o.OrderId)
        .ExecuteFetchAllAsync();
    return $"{big.Count} orders with Total between 100 and 250 (first = {big[0]})";
}


// src/Quarry.Analyzers.CodeFixes/README.md — post-fix QRA205 example
static async Task<string> JoinWithCondition(AppDb db)
{
    var results = await db.Users()
        .Join<Order>((u, o) => u.UserId == o.UserId.Id)
        .Select((u, o) => (u.UserName, o.Total))
        .ExecuteFetchAllAsync();
    return $"{results.Count} rows";
}


// DDL + seed
static async Task CreateSchemaAndSeedAsync(SqliteConnection connection)
{
    var ddl = new[]
    {
        """
        CREATE TABLE "users" (
            "UserId" INTEGER PRIMARY KEY AUTOINCREMENT,
            "UserName" TEXT NOT NULL,
            "Email" TEXT NULL,
            "IsActive" INTEGER NOT NULL DEFAULT 1,
            "CreatedAt" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE "orders" (
            "OrderId" INTEGER PRIMARY KEY AUTOINCREMENT,
            "UserId" INTEGER NOT NULL REFERENCES "users"("UserId"),
            "Total" NUMERIC NOT NULL,
            "Status" TEXT NOT NULL,
            "OrderDate" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE "tags" (
            "TagId" INTEGER PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE "order_tags" (
            "OrderTagId" INTEGER PRIMARY KEY AUTOINCREMENT,
            "OrderId" INTEGER NOT NULL REFERENCES "orders"("OrderId"),
            "TagId" INTEGER NOT NULL REFERENCES "tags"("TagId")
        );
        """,
        """
        CREATE TABLE "sales" (
            "SaleId" INTEGER PRIMARY KEY AUTOINCREMENT,
            "Region" TEXT NOT NULL,
            "Amount" NUMERIC NOT NULL,
            "SaleDate" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE "students" (
            "StudentId" INTEGER PRIMARY KEY AUTOINCREMENT,
            "FirstName" TEXT NOT NULL,
            "LastName" TEXT NOT NULL,
            "Email" TEXT NULL,
            "IsActive" INTEGER NOT NULL DEFAULT 1
        );
        """,
    };

    foreach (var sql in ddl)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    var now = new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);

    await Exec(connection, "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES ('alice', 'alice@example.com', 1, @t)", ("@t", now));
    await Exec(connection, "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES ('bob',   'bob@example.com',   1, @t)", ("@t", now));
    await Exec(connection, "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES ('carol', NULL,                0, @t)", ("@t", now));

    await Exec(connection, "INSERT INTO orders (UserId, Total, Status, OrderDate) VALUES (1, 100.00, 'paid',    @t)", ("@t", now));
    await Exec(connection, "INSERT INTO orders (UserId, Total, Status, OrderDate) VALUES (1,  50.00, 'paid',    @t)", ("@t", now.AddDays(1)));
    await Exec(connection, "INSERT INTO orders (UserId, Total, Status, OrderDate) VALUES (2, 200.00, 'pending', @t)", ("@t", now));
    await Exec(connection, "INSERT INTO orders (UserId, Total, Status, OrderDate) VALUES (2, 300.00, 'paid',    @t)", ("@t", now.AddDays(2)));

    await Exec(connection, "INSERT INTO tags (Name) VALUES ('urgent')");
    await Exec(connection, "INSERT INTO tags (Name) VALUES ('gift')");
    await Exec(connection, "INSERT INTO order_tags (OrderId, TagId) VALUES (1, 1)");
    await Exec(connection, "INSERT INTO order_tags (OrderId, TagId) VALUES (1, 2)");
    await Exec(connection, "INSERT INTO order_tags (OrderId, TagId) VALUES (3, 1)");

    await Exec(connection, "INSERT INTO sales (Region, Amount, SaleDate) VALUES ('West', 100.00, @t)", ("@t", now.AddDays(0)));
    await Exec(connection, "INSERT INTO sales (Region, Amount, SaleDate) VALUES ('West', 200.00, @t)", ("@t", now.AddDays(1)));
    await Exec(connection, "INSERT INTO sales (Region, Amount, SaleDate) VALUES ('West', 150.00, @t)", ("@t", now.AddDays(2)));
    await Exec(connection, "INSERT INTO sales (Region, Amount, SaleDate) VALUES ('East',  75.00, @t)", ("@t", now.AddDays(0)));
    await Exec(connection, "INSERT INTO sales (Region, Amount, SaleDate) VALUES ('East',  80.00, @t)", ("@t", now.AddDays(1)));

    await Exec(connection, "INSERT INTO students (FirstName, LastName, Email, IsActive) VALUES ('Ada',   'Lovelace',  'ada@example.com',   1)");
    await Exec(connection, "INSERT INTO students (FirstName, LastName, Email, IsActive) VALUES ('Grace', 'Hopper',    'grace@example.com', 1)");
    await Exec(connection, "INSERT INTO students (FirstName, LastName, Email, IsActive) VALUES ('Alan',  'Turing',    NULL,                0)");
}

static async Task Exec(SqliteConnection connection, string sql, params (string, object)[] parameters)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    foreach (var (name, value) in parameters)
        cmd.Parameters.AddWithValue(name, value);
    await cmd.ExecuteNonQueryAsync();
}
