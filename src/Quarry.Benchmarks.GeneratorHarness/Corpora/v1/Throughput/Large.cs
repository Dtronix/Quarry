// CORPUS — embedded resource for QuarryGenerator benchmarks; not compiled into Quarry.Benchmarks.
using Quarry;

namespace BenchHarness;

public static class BenchUsageSites
{
    // ---- Where (20) ----
    public static object Q1(BenchDb db) => db.Users().Where(u => u.IsActive).Prepare();
    public static object Q2(BenchDb db) => db.Users().Where(u => u.UserId > 1).Prepare();
    public static object Q3(BenchDb db) => db.Users().Where(u => u.UserId > 7).Prepare();
    public static object Q4(BenchDb db) => db.Users().Where(u => u.UserId == 13).Prepare();
    public static object Q5(BenchDb db) => db.Users().Where(u => u.UserName == "Alice").Prepare();
    public static object Q6(BenchDb db) => db.Orders().Where(o => o.Total > 100m).Prepare();
    public static object Q7(BenchDb db) => db.Orders().Where(o => o.Total > 250m).Prepare();
    public static object Q8(BenchDb db) => db.Orders().Where(o => o.Status == "Open").Prepare();
    public static object Q9(BenchDb db) => db.Orders().Where(o => o.Status == "Closed").Prepare();
    public static object Q10(BenchDb db) => db.Orders().Where(o => o.Priority == OrderPriority.High).Prepare();
    public static object Q11(BenchDb db) => db.Products().Where(p => p.Price < 50m).Prepare();
    public static object Q12(BenchDb db) => db.Products().Where(p => p.Price > 100m).Prepare();
    public static object Q13(BenchDb db) => db.Products().Where(p => p.ProductName == "Widget").Prepare();
    public static object Q14(BenchDb db) => db.Products().Where(p => p.ProductId == 42).Prepare();
    public static object Q15(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 1).Prepare();
    public static object Q16(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 5).Prepare();
    public static object Q17(BenchDb db) => db.OrderItems().Where(i => i.UnitPrice > 10m).Prepare();
    public static object Q18(BenchDb db) => db.Addresses().Where(a => a.City == "Seattle").Prepare();
    public static object Q19(BenchDb db) => db.Addresses().Where(a => a.City == "Portland").Prepare();
    public static object Q20(BenchDb db) => db.Addresses().Where(a => a.AddressId > 100).Prepare();

    // ---- Select anonymous-type (20) ----
    public static object Q21(BenchDb db) => db.Users().Select(u => new { u.UserId, u.UserName }).Prepare();
    public static object Q22(BenchDb db) => db.Users().Select(u => new { u.UserId, u.Email }).Prepare();
    public static object Q23(BenchDb db) => db.Users().Select(u => new { u.UserName, u.IsActive }).Prepare();
    public static object Q24(BenchDb db) => db.Users().Select(u => new { u.UserName, u.CreatedAt }).Prepare();
    public static object Q25(BenchDb db) => db.Orders().Select(o => new { o.OrderId, o.Total }).Prepare();
    public static object Q26(BenchDb db) => db.Orders().Select(o => new { o.OrderId, o.Status }).Prepare();
    public static object Q27(BenchDb db) => db.Orders().Select(o => new { o.OrderId, o.Priority }).Prepare();
    public static object Q28(BenchDb db) => db.Orders().Select(o => new { o.OrderId, o.OrderDate }).Prepare();
    public static object Q29(BenchDb db) => db.Products().Select(p => new { p.ProductId, p.ProductName }).Prepare();
    public static object Q30(BenchDb db) => db.Products().Select(p => new { p.ProductId, p.Price }).Prepare();
    public static object Q31(BenchDb db) => db.Products().Select(p => new { p.ProductName, p.Description }).Prepare();
    public static object Q32(BenchDb db) => db.Products().Select(p => new { p.ProductName, p.Price }).Prepare();
    public static object Q33(BenchDb db) => db.OrderItems().Select(i => new { i.OrderItemId, i.ProductName }).Prepare();
    public static object Q34(BenchDb db) => db.OrderItems().Select(i => new { i.OrderItemId, i.Quantity }).Prepare();
    public static object Q35(BenchDb db) => db.OrderItems().Select(i => new { i.ProductName, i.LineTotal }).Prepare();
    public static object Q36(BenchDb db) => db.OrderItems().Select(i => new { i.ProductName, i.UnitPrice }).Prepare();
    public static object Q37(BenchDb db) => db.Addresses().Select(a => new { a.AddressId, a.City }).Prepare();
    public static object Q38(BenchDb db) => db.Addresses().Select(a => new { a.City, a.Street }).Prepare();
    public static object Q39(BenchDb db) => db.Addresses().Select(a => new { a.City, a.ZipCode }).Prepare();
    public static object Q40(BenchDb db) => db.Addresses().Select(a => new { a.AddressId, a.Street, a.ZipCode }).Prepare();

    // ---- Select value-tuple (20) ----
    public static object Q41(BenchDb db) => db.Users().Select(u => (u.UserName, u.Email)).Prepare();
    public static object Q42(BenchDb db) => db.Users().Select(u => (u.UserId, u.UserName)).Prepare();
    public static object Q43(BenchDb db) => db.Users().Select(u => (u.UserName, u.IsActive)).Prepare();
    public static object Q44(BenchDb db) => db.Users().Select(u => (u.UserName, u.CreatedAt, u.LastLogin)).Prepare();
    public static object Q45(BenchDb db) => db.Orders().Select(o => (o.OrderId, o.Total, o.Status)).Prepare();
    public static object Q46(BenchDb db) => db.Orders().Select(o => (o.OrderId, o.Priority)).Prepare();
    public static object Q47(BenchDb db) => db.Orders().Select(o => (o.Status, o.Total)).Prepare();
    public static object Q48(BenchDb db) => db.Orders().Select(o => (o.OrderDate, o.Total)).Prepare();
    public static object Q49(BenchDb db) => db.Products().Select(p => (p.ProductName, p.Price)).Prepare();
    public static object Q50(BenchDb db) => db.Products().Select(p => (p.ProductId, p.Price)).Prepare();
    public static object Q51(BenchDb db) => db.Products().Select(p => (p.ProductName, p.Description)).Prepare();
    public static object Q52(BenchDb db) => db.Products().Select(p => (p.ProductId, p.ProductName, p.Price)).Prepare();
    public static object Q53(BenchDb db) => db.OrderItems().Select(i => (i.ProductName, i.LineTotal)).Prepare();
    public static object Q54(BenchDb db) => db.OrderItems().Select(i => (i.OrderItemId, i.Quantity)).Prepare();
    public static object Q55(BenchDb db) => db.OrderItems().Select(i => (i.UnitPrice, i.Quantity)).Prepare();
    public static object Q56(BenchDb db) => db.OrderItems().Select(i => (i.ProductName, i.UnitPrice, i.Quantity)).Prepare();
    public static object Q57(BenchDb db) => db.Addresses().Select(a => (a.City, a.Street)).Prepare();
    public static object Q58(BenchDb db) => db.Addresses().Select(a => (a.City, a.ZipCode)).Prepare();
    public static object Q59(BenchDb db) => db.Addresses().Select(a => (a.AddressId, a.City)).Prepare();
    public static object Q60(BenchDb db) => db.Addresses().Select(a => (a.Street, a.ZipCode)).Prepare();

    // ---- OrderBy + Limit (20) ----
    public static object Q61(BenchDb db) => db.Users().Select(u => u).OrderBy(u => u.UserName).Limit(10).Prepare();
    public static object Q62(BenchDb db) => db.Users().Select(u => u).OrderBy(u => u.CreatedAt, Direction.Descending).Limit(20).Prepare();
    public static object Q63(BenchDb db) => db.Users().Select(u => u).OrderBy(u => u.UserId).Limit(5).Prepare();
    public static object Q64(BenchDb db) => db.Users().Select(u => u).OrderBy(u => u.UserName).ThenBy(u => u.UserId).Limit(50).Prepare();
    public static object Q65(BenchDb db) => db.Orders().Select(o => o).OrderBy(o => o.Total, Direction.Descending).Limit(20).Prepare();
    public static object Q66(BenchDb db) => db.Orders().Select(o => o).OrderBy(o => o.OrderDate).Limit(15).Prepare();
    public static object Q67(BenchDb db) => db.Orders().Select(o => o).OrderBy(o => o.Status).ThenBy(o => o.Total).Limit(25).Prepare();
    public static object Q68(BenchDb db) => db.Orders().Select(o => o).OrderBy(o => o.Priority, Direction.Descending).Limit(30).Prepare();
    public static object Q69(BenchDb db) => db.Products().Select(p => p).OrderBy(p => p.Price).Limit(15).Prepare();
    public static object Q70(BenchDb db) => db.Products().Select(p => p).OrderBy(p => p.Price, Direction.Descending).Limit(10).Prepare();
    public static object Q71(BenchDb db) => db.Products().Select(p => p).OrderBy(p => p.ProductName).Limit(40).Prepare();
    public static object Q72(BenchDb db) => db.Products().Select(p => p).OrderBy(p => p.Price).ThenBy(p => p.ProductName).Limit(20).Prepare();
    public static object Q73(BenchDb db) => db.OrderItems().Select(i => i).OrderBy(i => i.LineTotal, Direction.Descending).Limit(25).Prepare();
    public static object Q74(BenchDb db) => db.OrderItems().Select(i => i).OrderBy(i => i.Quantity).Limit(10).Prepare();
    public static object Q75(BenchDb db) => db.OrderItems().Select(i => i).OrderBy(i => i.UnitPrice, Direction.Descending).Limit(50).Prepare();
    public static object Q76(BenchDb db) => db.OrderItems().Select(i => i).OrderBy(i => i.ProductName).ThenBy(i => i.LineTotal).Limit(35).Prepare();
    public static object Q77(BenchDb db) => db.Addresses().Select(a => a).OrderBy(a => a.City).Limit(50).Prepare();
    public static object Q78(BenchDb db) => db.Addresses().Select(a => a).OrderBy(a => a.City).ThenBy(a => a.Street).Limit(100).Prepare();
    public static object Q79(BenchDb db) => db.Addresses().Select(a => a).OrderBy(a => a.AddressId, Direction.Descending).Limit(20).Prepare();
    public static object Q80(BenchDb db) => db.Addresses().Select(a => a).OrderBy(a => a.ZipCode).Limit(75).Prepare();

    // ---- GroupBy + Count (20) ----
    public static object Q81(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).Prepare();
    public static object Q82(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Count())).Prepare();
    public static object Q83(BenchDb db) => db.Orders().Where(o => o.Total > 0m).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).Prepare();
    public static object Q84(BenchDb db) => db.Orders().Where(o => o.Total > 50m).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).Prepare();
    public static object Q85(BenchDb db) => db.Users().Where(u => true).GroupBy(u => u.IsActive).Select(u => (u.IsActive, Sql.Count())).Prepare();
    public static object Q86(BenchDb db) => db.Users().Where(u => u.UserId > 0).GroupBy(u => u.IsActive).Select(u => (u.IsActive, Sql.Count())).Prepare();
    public static object Q87(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Count())).Prepare();
    public static object Q88(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 0).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Count())).Prepare();
    public static object Q89(BenchDb db) => db.OrderItems().Where(i => i.UnitPrice > 5m).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Count())).Prepare();
    public static object Q90(BenchDb db) => db.Addresses().Where(a => true).GroupBy(a => a.City).Select(a => (a.City, Sql.Count())).Prepare();
    public static object Q91(BenchDb db) => db.Addresses().Where(a => true).GroupBy(a => a.ZipCode).Select(a => (a.ZipCode, Sql.Count())).Prepare();
    public static object Q92(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Count())).Prepare();
    public static object Q93(BenchDb db) => db.Products().Where(p => p.Price > 0m).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Count())).Prepare();
    public static object Q94(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count())).Prepare();
    public static object Q95(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).Prepare();
    public static object Q96(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Having(o => Sql.Count() > 2).Select(o => (o.Priority, Sql.Count())).Prepare();
    public static object Q97(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Having(i => Sql.Count() > 3).Select(i => (i.ProductName, Sql.Count())).Prepare();
    public static object Q98(BenchDb db) => db.Addresses().Where(a => true).GroupBy(a => a.City).Having(a => Sql.Count() > 1).Select(a => (a.City, Sql.Count())).Prepare();
    public static object Q99(BenchDb db) => db.Users().Where(u => true).GroupBy(u => u.IsActive).Having(u => Sql.Count() > 0).Select(u => (u.IsActive, Sql.Count())).Prepare();
    public static object Q100(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Having(p => Sql.Count() > 1).Select(p => (p.ProductName, Sql.Count())).Prepare();

    // ---- GroupBy + Sum/Avg/Min/Max (20) ----
    public static object Q101(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Sum(o.Total))).Prepare();
    public static object Q102(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();
    public static object Q103(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).Prepare();
    public static object Q104(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).Prepare();
    public static object Q105(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Sum(o.Total))).Prepare();
    public static object Q106(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Avg(o.Total))).Prepare();
    public static object Q107(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Min(o.Total))).Prepare();
    public static object Q108(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Priority).Select(o => (o.Priority, Sql.Max(o.Total))).Prepare();
    public static object Q109(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Sum(i.LineTotal))).Prepare();
    public static object Q110(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Sum(i.Quantity))).Prepare();
    public static object Q111(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Avg(i.UnitPrice))).Prepare();
    public static object Q112(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Min(i.UnitPrice))).Prepare();
    public static object Q113(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Select(i => (i.ProductName, Sql.Max(i.UnitPrice))).Prepare();
    public static object Q114(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Avg(p.Price))).Prepare();
    public static object Q115(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Min(p.Price))).Prepare();
    public static object Q116(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Max(p.Price))).Prepare();
    public static object Q117(BenchDb db) => db.Products().Where(p => true).GroupBy(p => p.ProductName).Select(p => (p.ProductName, Sql.Sum(p.Price))).Prepare();
    public static object Q118(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Sum(o.Total) > 100m).Select(o => (o.Status, Sql.Sum(o.Total))).Prepare();
    public static object Q119(BenchDb db) => db.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Avg(o.Total) > 50m).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();
    public static object Q120(BenchDb db) => db.OrderItems().Where(i => true).GroupBy(i => i.ProductName).Having(i => Sql.Sum(i.LineTotal) > 200m).Select(i => (i.ProductName, Sql.Sum(i.LineTotal))).Prepare();

    // ---- Join (20) ----
    public static object Q121(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
    public static object Q122(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Status)).Prepare();
    public static object Q123(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserId, o.OrderId)).Prepare();
    public static object Q124(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, o.Status)).Prepare();
    public static object Q125(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => new { u.UserName, o.OrderId, o.Total }).Prepare();
    public static object Q126(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
    public static object Q127(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100m).Select((u, o) => (u.UserName, o.Total)).Prepare();
    public static object Q128(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Status == "Open").Select((u, o) => (u.UserName, o.Status)).Prepare();
    public static object Q129(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.UserId > 1).Select((u, o) => (u.UserName, o.OrderId)).Prepare();
    public static object Q130(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Priority == OrderPriority.High).Select((u, o) => (u.UserName, o.Priority)).Prepare();
    public static object Q131(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Select((o, i) => (o.OrderId, i.ProductName)).Prepare();
    public static object Q132(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Select((o, i) => (o.Status, i.LineTotal)).Prepare();
    public static object Q133(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Select((o, i) => (o.OrderId, i.Quantity, i.UnitPrice)).Prepare();
    public static object Q134(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Select((o, i) => new { o.OrderId, i.ProductName, i.LineTotal }).Prepare();
    public static object Q135(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Where((o, i) => o.Total > 100m).Select((o, i) => (o.Status, i.LineTotal)).Prepare();
    public static object Q136(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Where((o, i) => i.Quantity > 1).Select((o, i) => (o.OrderId, i.ProductName)).Prepare();
    public static object Q137(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Where((o, i) => i.UnitPrice > 10m).Select((o, i) => (o.OrderId, i.UnitPrice)).Prepare();
    public static object Q138(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Where((o, i) => o.Status == "Closed").Select((o, i) => (o.OrderId, i.LineTotal)).Prepare();
    public static object Q139(BenchDb db) => db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).OrderBy((u, o) => u.UserName).Limit(20).Prepare();
    public static object Q140(BenchDb db) => db.Orders().Join<OrderItem>((o, i) => o.OrderId == i.OrderId.Id).Select((o, i) => (o.OrderId, i.LineTotal)).OrderBy((o, i) => i.LineTotal, Direction.Descending).Limit(50).Prepare();

    // ---- Distinct (20) ----
    public static object Q141(BenchDb db) => db.Orders().Select(o => o.Status).Distinct().Prepare();
    public static object Q142(BenchDb db) => db.Orders().Select(o => o.Priority).Distinct().Prepare();
    public static object Q143(BenchDb db) => db.Orders().Select(o => o.Total).Distinct().Prepare();
    public static object Q144(BenchDb db) => db.Orders().Select(o => (o.Status, o.Priority)).Distinct().Prepare();
    public static object Q145(BenchDb db) => db.Orders().Select(o => new { o.Status, o.Priority }).Distinct().Prepare();
    public static object Q146(BenchDb db) => db.Addresses().Select(a => a.City).Distinct().Prepare();
    public static object Q147(BenchDb db) => db.Addresses().Select(a => a.ZipCode).Distinct().Prepare();
    public static object Q148(BenchDb db) => db.Addresses().Select(a => (a.City, a.ZipCode)).Distinct().Prepare();
    public static object Q149(BenchDb db) => db.Addresses().Select(a => new { a.City, a.Street }).Distinct().Prepare();
    public static object Q150(BenchDb db) => db.Products().Select(p => p.ProductName).Distinct().Prepare();
    public static object Q151(BenchDb db) => db.Products().Select(p => p.Price).Distinct().Prepare();
    public static object Q152(BenchDb db) => db.Products().Select(p => (p.ProductName, p.Price)).Distinct().Prepare();
    public static object Q153(BenchDb db) => db.OrderItems().Select(i => i.ProductName).Distinct().Prepare();
    public static object Q154(BenchDb db) => db.OrderItems().Select(i => i.UnitPrice).Distinct().Prepare();
    public static object Q155(BenchDb db) => db.OrderItems().Select(i => (i.ProductName, i.UnitPrice)).Distinct().Prepare();
    public static object Q156(BenchDb db) => db.Users().Select(u => u.IsActive).Distinct().Prepare();
    public static object Q157(BenchDb db) => db.Users().Select(u => u.UserName).Distinct().Prepare();
    public static object Q158(BenchDb db) => db.Users().Where(u => u.IsActive).Select(u => u.UserName).Distinct().Prepare();
    public static object Q159(BenchDb db) => db.Orders().Where(o => o.Total > 0m).Select(o => o.Status).Distinct().Prepare();
    public static object Q160(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 0).Select(i => i.ProductName).Distinct().Prepare();

    // ---- Single-column aggregate (20) ----
    public static object Q161(BenchDb db) => db.Orders().Select(o => Sql.Sum(o.Total)).Prepare();
    public static object Q162(BenchDb db) => db.Orders().Select(o => Sql.Avg(o.Total)).Prepare();
    public static object Q163(BenchDb db) => db.Orders().Select(o => Sql.Min(o.Total)).Prepare();
    public static object Q164(BenchDb db) => db.Orders().Select(o => Sql.Max(o.Total)).Prepare();
    public static object Q165(BenchDb db) => db.Orders().Select(o => Sql.Count()).Prepare();
    public static object Q166(BenchDb db) => db.OrderItems().Select(i => Sql.Sum(i.LineTotal)).Prepare();
    public static object Q167(BenchDb db) => db.OrderItems().Select(i => Sql.Sum(i.Quantity)).Prepare();
    public static object Q168(BenchDb db) => db.OrderItems().Select(i => Sql.Avg(i.UnitPrice)).Prepare();
    public static object Q169(BenchDb db) => db.OrderItems().Select(i => Sql.Min(i.UnitPrice)).Prepare();
    public static object Q170(BenchDb db) => db.OrderItems().Select(i => Sql.Max(i.UnitPrice)).Prepare();
    public static object Q171(BenchDb db) => db.Products().Select(p => Sql.Min(p.Price)).Prepare();
    public static object Q172(BenchDb db) => db.Products().Select(p => Sql.Max(p.Price)).Prepare();
    public static object Q173(BenchDb db) => db.Products().Select(p => Sql.Avg(p.Price)).Prepare();
    public static object Q174(BenchDb db) => db.Products().Select(p => Sql.Sum(p.Price)).Prepare();
    public static object Q175(BenchDb db) => db.Users().Select(u => Sql.Count()).Prepare();
    public static object Q176(BenchDb db) => db.Addresses().Select(a => Sql.Count()).Prepare();
    public static object Q177(BenchDb db) => db.Orders().Where(o => o.Total > 100m).Select(o => Sql.Sum(o.Total)).Prepare();
    public static object Q178(BenchDb db) => db.Orders().Where(o => o.Status == "Open").Select(o => Sql.Count()).Prepare();
    public static object Q179(BenchDb db) => db.Products().Where(p => p.Price > 50m).Select(p => Sql.Avg(p.Price)).Prepare();
    public static object Q180(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 1).Select(i => Sql.Sum(i.LineTotal)).Prepare();

    // ---- Multi-predicate Where (20) ----
    public static object Q181(BenchDb db) => db.Users().Where(u => u.IsActive && u.UserId > 5).Prepare();
    public static object Q182(BenchDb db) => db.Users().Where(u => u.IsActive && u.UserId > 10).Prepare();
    public static object Q183(BenchDb db) => db.Users().Where(u => u.IsActive || u.UserId == 1).Prepare();
    public static object Q184(BenchDb db) => db.Users().Where(u => u.UserId > 1 && u.UserId < 100).Prepare();
    public static object Q185(BenchDb db) => db.Users().Where(u => u.UserName == "Alice" || u.UserName == "Bob").Prepare();
    public static object Q186(BenchDb db) => db.Orders().Where(o => o.Total > 100m && o.Status == "Open").Prepare();
    public static object Q187(BenchDb db) => db.Orders().Where(o => o.Total > 250m && o.Status == "Closed").Prepare();
    public static object Q188(BenchDb db) => db.Orders().Where(o => o.Total > 50m || o.Priority == OrderPriority.Urgent).Prepare();
    public static object Q189(BenchDb db) => db.Orders().Where(o => o.Status == "Open" && o.Priority == OrderPriority.High).Prepare();
    public static object Q190(BenchDb db) => db.Orders().Where(o => o.Total > 0m && o.Total < 1000m).Prepare();
    public static object Q191(BenchDb db) => db.Products().Where(p => p.Price > 10m && p.Price < 100m).Prepare();
    public static object Q192(BenchDb db) => db.Products().Where(p => p.Price > 50m && p.ProductName == "Widget").Prepare();
    public static object Q193(BenchDb db) => db.Products().Where(p => p.ProductId > 0 && p.Price > 0m).Prepare();
    public static object Q194(BenchDb db) => db.Products().Where(p => p.ProductName == "A" || p.ProductName == "B").Prepare();
    public static object Q195(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 1 && i.UnitPrice > 5m).Prepare();
    public static object Q196(BenchDb db) => db.OrderItems().Where(i => i.Quantity > 5 || i.LineTotal > 100m).Prepare();
    public static object Q197(BenchDb db) => db.OrderItems().Where(i => i.UnitPrice > 10m && i.Quantity < 10).Prepare();
    public static object Q198(BenchDb db) => db.Addresses().Where(a => a.City == "Portland" || a.City == "Seattle").Prepare();
    public static object Q199(BenchDb db) => db.Addresses().Where(a => a.City == "NYC" && a.ZipCode == "10001").Prepare();
    public static object Q200(BenchDb db) => db.Addresses().Where(a => a.AddressId > 100 && a.City == "Boston").Prepare();
}
