// CORPUS — embedded resource for QuarryGenerator benchmarks; not compiled into Quarry.Benchmarks.
using Quarry;

namespace BenchHarness;

public static class BenchUsageSites
{
    // ---- Where (Q1..Q5) ----
    public static object Q1(BenchDb db) => db.Users().Where(u => u.IsActive).Prepare();
    public static object Q2(BenchDb db) => db.Orders().Where(o => o.Total > 100m).Prepare();
    public static object Q3(BenchDb db) => db.Products().Where(p => p.Price < 50m).Prepare();
    public static object Q4(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 1).Prepare();
    public static object Q5(BenchDb db) => db.Addresses().Where(a => a.City == "Seattle").Prepare();

    // ---- Select anonymous-type (Q6..Q10) ----
    public static object Q6(BenchDb db) => db.Users().Select(u => new { u.UserId, u.UserName }).Prepare();
    public static object Q7(BenchDb db) => db.Orders().Select(o => new { o.OrderId, o.Total }).Prepare();
    public static object Q8(BenchDb db) => db.Products().Select(p => new { p.ProductId, p.ProductName, p.Price }).Prepare();
    public static object Q9(BenchDb db) => db.OrderItems().Select(i => new { i.OrderItemId, i.ProductName, i.Quantity }).Prepare();
    public static object Q10(BenchDb db) => db.Addresses().Select(a => new { a.AddressId, a.City, a.Street }).Prepare();

    // ---- Select value-tuple (Q11..Q15) ----
    public static object Q11(BenchDb db) => db.Users().Select(u => (u.UserName, u.Email)).Prepare();
    public static object Q12(BenchDb db) => db.Orders().Select(o => (o.OrderId, o.Total, o.Status)).Prepare();
    public static object Q13(BenchDb db) => db.Products().Select(p => (p.ProductName, p.Price)).Prepare();
    public static object Q14(BenchDb db) => db.OrderItems().Select(i => (i.ProductName, i.LineTotal)).Prepare();
    public static object Q15(BenchDb db) => db.Addresses().Select(a => (a.City, a.ZipCode)).Prepare();

    // ---- OrderBy + Limit (Q16..Q20) ----
    public static object Q16(BenchDb db) => db.Users().Select(u => u).OrderBy(u => u.UserName).Limit(10).Prepare();
    public static object Q17(BenchDb db) => db.Orders().Select(o => o).OrderBy(o => o.Total, Direction.Descending).Limit(20).Prepare();
    public static object Q18(BenchDb db) => db.Products().Select(p => p).OrderBy(p => p.Price).ThenBy(p => p.ProductName).Limit(15).Prepare();
    public static object Q19(BenchDb db) => db.OrderItems().Select(i => i).OrderBy(i => i.LineTotal, Direction.Descending).Limit(25).Prepare();
    public static object Q20(BenchDb db) => db.Addresses().Select(a => a).OrderBy(a => a.City).Limit(50).Prepare();

    // ---- GroupBy + Count (Q21..Q25) ----
    public static object Q21(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).Prepare();
    public static object Q22(BenchDb db) => db.Users().Where(u => true).GroupBy(u => u.IsActive).Select(u => (u.IsActive, Sql.Count())).Prepare();
    public static object Q23(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Count())).Prepare();
    public static object Q24(BenchDb db) => db.Addresses().Where(a => true).GroupBy(a => a.City).Select(a => (a.City, Sql.Count())).Prepare();
    public static object Q25(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Count())).Prepare();

    // ---- GroupBy + Sum/Avg/Min/Max (Q26..Q30) ----
    public static object Q26(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Sum(o.Total))).Prepare();
    public static object Q27(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Avg(o.Total))).Prepare();
    public static object Q28(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Sum(i.LineTotal))).Prepare();
    public static object Q29(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Min(p.Price))).Prepare();
    public static object Q30(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Max(i.UnitPrice))).Prepare();

    // ---- Join (Q31..Q35) ----
    public static object Q31(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
    public static object Q32(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Select((o, i) => (o.OrderId, i.ProductName)).Prepare();
    public static object Q33(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Status)).Prepare();
    public static object Q34(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Where((o, i) => o.Total > 100m).Select((o, i) => (o.Status, i.LineTotal)).Prepare();
    public static object Q35(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => new { u.UserName, o.OrderId, o.Total }).Prepare();

    // ---- Distinct (Q36..Q40) ----
    public static object Q36(BenchDb db) => db.Orders().Select(o => o.Status).Distinct().Prepare();
    public static object Q37(BenchDb db) => db.Addresses().Select(a => a.City).Distinct().Prepare();
    public static object Q38(BenchDb db) => db.Products().Select(p => p.ProductName).Distinct().Prepare();
    public static object Q39(BenchDb db) => db.OrderItems().Select(i => i.ProductName).Distinct().Prepare();
    public static object Q40(BenchDb db) => db.Users().Select(u => u.IsActive).Distinct().Prepare();

    // ---- Single-column aggregate (Q41..Q45) ----
    public static object Q41(BenchDb db) => db.Orders().Select(o => Sql.Sum(o.Total)).Prepare();
    public static object Q42(BenchDb db) => db.Orders().Select(o => Sql.Avg(o.Total)).Prepare();
    public static object Q43(BenchDb db) => db.OrderItems().Select(i => Sql.Sum(i.LineTotal)).Prepare();
    public static object Q44(BenchDb db) => db.Products().Select(p => Sql.Min(p.Price)).Prepare();
    public static object Q45(BenchDb db) => db.Products().Select(p => Sql.Max(p.Price)).Prepare();

    // ---- Multi-predicate Where (Q46..Q50) ----
    public static object Q46(BenchDb db) => db.Users().Where(u => u.IsActive && u.UserId > 5).Prepare();
    public static object Q47(BenchDb db) => db.Orders().Where(o => o.Total > 100m && o.Status == "Open").Prepare();
    public static object Q48(BenchDb db) => db.Products().Where(p => p.Price > 10m && p.Price < 100m).Prepare();
    public static object Q49(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 1 && i.UnitPrice > 5m).Prepare();
    public static object Q50(BenchDb db) => db.Addresses().Where(a => a.City == "Portland" || a.City == "Seattle").Prepare();
}
