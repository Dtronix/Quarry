// CORPUS — embedded resource for QuarryGenerator benchmarks; not compiled into Quarry.Benchmarks.
using Quarry;

namespace BenchHarness;

public static class BenchUsageSites
{
    // 1: simple Where
    public static object Q1(BenchDb db) => db.Users().Where(u => u.IsActive).Prepare();

    // 2: Select anonymous-type
    public static object Q2(BenchDb db) => db.Users().Select(u => new { u.UserId, u.UserName }).Prepare();

    // 3: Select value-tuple
    public static object Q3(BenchDb db) => db.Orders().Select(o => (o.OrderId, o.Total)).Prepare();

    // 4: OrderBy + Limit
    public static object Q4(BenchDb db) => db.Products().Select(p => p).OrderBy(p => p.Price).Limit(10).Prepare();

    // 5: GroupBy + Count
    public static object Q5(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).Prepare();

    // 6: GroupBy + Sum
    public static object Q6(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Sum(o.Total))).Prepare();

    // 7: Join
    public static object Q7(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

    // 8: Distinct
    public static object Q8(BenchDb db) => db.Orders().Select(o => o.Status).Distinct().Prepare();

    // 9: single-column aggregate
    public static object Q9(BenchDb db) => db.Orders().Select(o => Sql.Sum(o.Total)).Prepare();

    // 10: multi-predicate Where
    public static object Q10(BenchDb db) => db.Users().Where(u => u.IsActive && u.UserId > 5).Prepare();
}
