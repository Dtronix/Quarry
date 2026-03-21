using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

/// <summary>
/// Stress tests for the SqlExpr IR pipeline: Build → Bind → Render → verify against raw SQL.
/// Covers multi-table joins, nested expressions, type mappings, and cross-dialect rendering.
/// </summary>
[TestFixture]
public class SqlExprStressTests
{
    // ── Shared test entities ──────────────────────────────────────────

    private static EntityInfo UserEntity => CreateEntity("User", "users", new[]
    {
        Col("UserId", "user_id", "int", ColumnKind.PrimaryKey, isValueType: true),
        Col("Name", "name", "string"),
        Col("Email", "email", "string?", isNullable: true),
        Col("Age", "age", "int", isValueType: true),
        Col("IsActive", "is_active", "bool", isValueType: true),
        Col("DepartmentId", "department_id", "int", ColumnKind.ForeignKey, isValueType: true, referencedEntity: "Department"),
        Col("Score", "score", "decimal", isValueType: true),
        Col("Status", "status", "int", isValueType: true, isEnum: true),
        Col("Rating", "rating", "decimal", isValueType: true, customTypeMapping: "RatingMapping"),
    });

    private static EntityInfo OrderEntity => CreateEntity("Order", "orders", new[]
    {
        Col("OrderId", "order_id", "int", ColumnKind.PrimaryKey, isValueType: true),
        Col("UserId", "user_id", "int", ColumnKind.ForeignKey, isValueType: true, referencedEntity: "User"),
        Col("Total", "total", "decimal", isValueType: true),
        Col("CreatedAt", "created_at", "DateTime", isValueType: true),
        Col("IsShipped", "is_shipped", "bool", isValueType: true),
        Col("Note", "note", "string?", isNullable: true),
    });

    private static EntityInfo ProductEntity => CreateEntity("Product", "products", new[]
    {
        Col("ProductId", "product_id", "int", ColumnKind.PrimaryKey, isValueType: true),
        Col("ProductName", "product_name", "string"),
        Col("Price", "price", "decimal", isValueType: true),
        Col("CategoryId", "category_id", "int", ColumnKind.ForeignKey, isValueType: true, referencedEntity: "Category"),
        Col("IsAvailable", "is_available", "bool", isValueType: true),
    });

    private static EntityInfo OrderItemEntity => CreateEntity("OrderItem", "order_items", new[]
    {
        Col("Id", "id", "int", ColumnKind.PrimaryKey, isValueType: true),
        Col("OrderId", "order_id", "int", ColumnKind.ForeignKey, isValueType: true, referencedEntity: "Order"),
        Col("ProductId", "product_id", "int", ColumnKind.ForeignKey, isValueType: true, referencedEntity: "Product"),
        Col("Quantity", "quantity", "int", isValueType: true),
        Col("UnitPrice", "unit_price", "decimal", isValueType: true),
    });

    // ═══════════════════════════════════════════════════════════════════
    // 1. MULTI-TABLE JOIN TESTS
    // ═══════════════════════════════════════════════════════════════════

    #region Multi-Table Joins

    [Test]
    public void Join_TwoTables_OnForeignKey()
    {
        // u.UserId == o.UserId  (join condition across two tables)
        var expr = new BinaryOpExpr(
            new ColumnRefExpr("u", "UserId"),
            SqlBinaryOperator.Equal,
            new ColumnRefExpr("o", "UserId"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo("(\"t0\".\"user_id\" = \"t1\".\"user_id\")"));
    }

    [Test]
    public void Join_TwoTables_OnForeignKey_MySQL()
    {
        var expr = new BinaryOpExpr(
            new ColumnRefExpr("u", "UserId"),
            SqlBinaryOperator.Equal,
            new ColumnRefExpr("o", "UserId"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.MySQL, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo("(`t0`.`user_id` = `t1`.`user_id`)"));
    }

    [Test]
    public void Join_ThreeTables_ComplexOnCondition()
    {
        // u.UserId == o.UserId AND o.OrderId == oi.OrderId
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new ColumnRefExpr("u", "UserId"),
                SqlBinaryOperator.Equal,
                new ColumnRefExpr("o", "UserId")),
            SqlBinaryOperator.And,
            new BinaryOpExpr(
                new ColumnRefExpr("o", "OrderId"),
                SqlBinaryOperator.Equal,
                new ColumnRefExpr("oi", "OrderId")));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo>
            {
                ["o"] = OrderEntity,
                ["oi"] = OrderItemEntity
            },
            tableAliases: new Dictionary<string, string>
            {
                ["u"] = "t0", ["o"] = "t1", ["oi"] = "t2"
            });

        Assert.That(sql, Is.EqualTo(
            "((\"t0\".\"user_id\" = \"t1\".\"user_id\") AND (\"t1\".\"order_id\" = \"t2\".\"order_id\"))"));
    }

    [Test]
    public void Join_WhereOnJoinedColumn()
    {
        // WHERE o.Total > 100 AND o.IsShipped = 1
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new ColumnRefExpr("o", "Total"),
                SqlBinaryOperator.GreaterThan,
                new LiteralExpr("100", "decimal")),
            SqlBinaryOperator.And,
            new BinaryOpExpr(
                new ColumnRefExpr("o", "IsShipped"),
                SqlBinaryOperator.Equal,
                new LiteralExpr("TRUE", "bool")));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo(
            "((\"t1\".\"total\" > 100) AND (\"t1\".\"is_shipped\" = 1))"));
    }

    [Test]
    public void Join_RefId_ForeignKeyResolution()
    {
        // u.DepartmentId (FK column — accessed as u.DepartmentId.Id in C#)
        var expr = new ColumnRefExpr("u", "DepartmentId", nestedProperty: "Id");

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"department_id\""));
    }

    [Test]
    public void Join_RefId_WithTableAlias()
    {
        var expr = new ColumnRefExpr("u", "DepartmentId", nestedProperty: "Id");

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["d"] = UserEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["d"] = "t1" });

        Assert.That(sql, Is.EqualTo("\"t0\".\"department_id\""));
    }

    [Test]
    public void Join_MixedPrimaryAndJoinedColumns_PostgreSQL()
    {
        // u.Name = o.Note (cross-table string comparison)
        var expr = new BinaryOpExpr(
            new ColumnRefExpr("u", "Name"),
            SqlBinaryOperator.Equal,
            new ColumnRefExpr("o", "Note"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.PostgreSQL, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo("(\"t0\".\"name\" = \"t1\".\"note\")"));
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // 2. COMPLEX EXPRESSION TESTS
    // ═══════════════════════════════════════════════════════════════════

    #region Complex Expressions

    [Test]
    public void Complex_ThreeWayAnd()
    {
        // u.Age > 18 AND u.IsActive AND u.Email IS NOT NULL
        // Note: boolean context only applies at the top level (standalone .Where(u => u.IsActive)).
        // Inside a compound expression, boolean columns render as bare column names.
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new BinaryOpExpr(
                    new ColumnRefExpr("u", "Age"),
                    SqlBinaryOperator.GreaterThan,
                    new LiteralExpr("18", "int")),
                SqlBinaryOperator.And,
                new ColumnRefExpr("u", "IsActive")),
            SqlBinaryOperator.And,
            new IsNullCheckExpr(
                new ColumnRefExpr("u", "Email"),
                isNegated: true));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u", inBooleanContext: true);

        Assert.That(sql, Is.EqualTo(
            "(((\"age\" > 18) AND \"is_active\") AND \"email\" IS NOT NULL)"));
    }

    [Test]
    public void Complex_OrWithNestedAnd()
    {
        // (u.Age < 18 OR u.Age > 65) AND u.IsActive
        // Boolean column inside compound expr renders as bare column name
        var orExpr = new BinaryOpExpr(
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.LessThan,
                new LiteralExpr("18", "int")),
            SqlBinaryOperator.Or,
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.GreaterThan,
                new LiteralExpr("65", "int")));

        var expr = new BinaryOpExpr(
            orExpr,
            SqlBinaryOperator.And,
            new ColumnRefExpr("u", "IsActive"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u", inBooleanContext: true);

        Assert.That(sql, Is.EqualTo(
            "(((\"age\" < 18) OR (\"age\" > 65)) AND \"is_active\")"));
    }

    [Test]
    public void Complex_NotWithNestedOr()
    {
        // NOT (u.Age < 18 OR u.Email IS NULL)
        var inner = new BinaryOpExpr(
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.LessThan,
                new LiteralExpr("18", "int")),
            SqlBinaryOperator.Or,
            new IsNullCheckExpr(new ColumnRefExpr("u", "Email")));

        var expr = new UnaryOpExpr(SqlUnaryOperator.Not, inner);

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo(
            "NOT (((\"age\" < 18) OR \"email\" IS NULL))"));
    }

    [Test]
    public void Complex_ArithmeticInWhere()
    {
        // u.Score * 2 + 10 > 100
        var arithmetic = new BinaryOpExpr(
            new BinaryOpExpr(
                new BinaryOpExpr(
                    new ColumnRefExpr("u", "Score"),
                    SqlBinaryOperator.Multiply,
                    new LiteralExpr("2", "int")),
                SqlBinaryOperator.Add,
                new LiteralExpr("10", "int")),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("100", "int"));

        var sql = BindAndRender(arithmetic, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo(
            "(((\"score\" * 2) + 10) > 100)"));
    }

    [Test]
    public void Complex_StringFunction_Lower()
    {
        // LOWER(u.Name) = @p0
        var expr = new BinaryOpExpr(
            new FunctionCallExpr("LOWER", new SqlExpr[] { new ColumnRefExpr("u", "Name") }),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "string", "searchTerm", isCaptured: true));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("(LOWER(\"name\") = @p0)"));
    }

    [Test]
    public void Complex_StringFunction_Lower_PostgreSQL()
    {
        var expr = new BinaryOpExpr(
            new FunctionCallExpr("LOWER", new SqlExpr[] { new ColumnRefExpr("u", "Name") }),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "string", "searchTerm", isCaptured: true));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.PostgreSQL, "u");

        Assert.That(sql, Is.EqualTo("(LOWER(\"name\") = $1)"));
    }

    [Test]
    public void Complex_NestedFunctions_TrimLower()
    {
        // LOWER(TRIM(u.Name)) = @p0
        var expr = new BinaryOpExpr(
            new FunctionCallExpr("LOWER", new SqlExpr[]
            {
                new FunctionCallExpr("TRIM", new SqlExpr[] { new ColumnRefExpr("u", "Name") })
            }),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "string", "search", isCaptured: true));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("(LOWER(TRIM(\"name\")) = @p0)"));
    }

    [Test]
    public void Complex_LikeContains_WithParam()
    {
        // u.Name LIKE '%' || @p0 || '%'
        var like = new LikeExpr(
            new ColumnRefExpr("u", "Name"),
            new ParamSlotExpr(0, "string", "search", isCaptured: true),
            likePrefix: "%", likeSuffix: "%");

        var sql = BindAndRender(like, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"name\" LIKE '%' || @p0 || '%'"));
    }

    [Test]
    public void Complex_LikeContains_MySQL()
    {
        var like = new LikeExpr(
            new ColumnRefExpr("u", "Name"),
            new ParamSlotExpr(0, "string", "search", isCaptured: true),
            likePrefix: "%", likeSuffix: "%");

        var sql = BindAndRender(like, UserEntity, GenSqlDialect.MySQL, "u");

        Assert.That(sql, Is.EqualTo("`name` LIKE CONCAT('%', @p0, '%')"));
    }

    [Test]
    public void Complex_LikeStartsWith_SqlServer()
    {
        var like = new LikeExpr(
            new ColumnRefExpr("u", "Name"),
            new ParamSlotExpr(0, "string", "prefix", isCaptured: true),
            likeSuffix: "%");

        var sql = BindAndRender(like, UserEntity, GenSqlDialect.SqlServer, "u");

        Assert.That(sql, Is.EqualTo("[name] LIKE @p0 + '%'"));
    }

    [Test]
    public void Complex_LikeWithEscape()
    {
        var like = new LikeExpr(
            new ColumnRefExpr("u", "Name"),
            new ParamSlotExpr(0, "string", "search", isCaptured: true),
            likePrefix: "%", likeSuffix: "%", needsEscape: true);

        var sql = BindAndRender(like, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"name\" LIKE '%' || @p0 || '%' ESCAPE '\\'"));
    }

    [Test]
    public void Complex_InExpr_LiteralValues()
    {
        // u.Status IN (1, 2, 3)
        var inExpr = new InExpr(
            new ColumnRefExpr("u", "Status"),
            new SqlExpr[]
            {
                new LiteralExpr("1", "int"),
                new LiteralExpr("2", "int"),
                new LiteralExpr("3", "int")
            });

        var sql = BindAndRender(inExpr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"status\" IN (1, 2, 3)"));
    }

    [Test]
    public void Complex_InExpr_WithParameter()
    {
        // u.Status IN (@p0)
        var inExpr = new InExpr(
            new ColumnRefExpr("u", "Status"),
            new SqlExpr[] { new ParamSlotExpr(0, "int[]", "statuses", isCollection: true) });

        var sql = BindAndRender(inExpr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"status\" IN (@p0)"));
    }

    [Test]
    public void Complex_MultipleParamsWithBaseIndex()
    {
        // u.Age > @p3 AND u.Score < @p4
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.GreaterThan,
                new ParamSlotExpr(0, "int", "minAge")),
            SqlBinaryOperator.And,
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Score"),
                SqlBinaryOperator.LessThan,
                new ParamSlotExpr(1, "decimal", "maxScore")));

        var bound = SqlExprBinder.Bind(expr, UserEntity, GenSqlDialect.SQLite, "u");
        var sql = SqlExprRenderer.Render(bound, GenSqlDialect.SQLite, parameterBaseIndex: 3);

        Assert.That(sql, Is.EqualTo("((\"age\" > @p3) AND (\"score\" < @p4))"));
    }

    [Test]
    public void Complex_NegateExpression()
    {
        // -(u.Score)
        var expr = new UnaryOpExpr(
            SqlUnaryOperator.Negate,
            new ColumnRefExpr("u", "Score"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("-\"score\""));
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // 3. AGGREGATE / SELECT EXPRESSION TESTS
    // ═══════════════════════════════════════════════════════════════════

    #region Aggregates and Select Expressions

    [Test]
    public void Aggregate_CountStar()
    {
        var expr = new FunctionCallExpr("COUNT",
            new SqlExpr[] { new SqlRawExpr("*") }, isAggregate: true);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);

        Assert.That(sql, Is.EqualTo("COUNT(*)"));
    }

    [Test]
    public void Aggregate_SumColumn()
    {
        var expr = new FunctionCallExpr("SUM",
            new SqlExpr[] { new ColumnRefExpr("o", "Total") }, isAggregate: true);

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo("SUM(\"t1\".\"total\")"));
    }

    [Test]
    public void Aggregate_AvgWithArithmetic()
    {
        // AVG(oi.UnitPrice * oi.Quantity)
        var expr = new FunctionCallExpr("AVG",
            new SqlExpr[]
            {
                new BinaryOpExpr(
                    new ColumnRefExpr("oi", "UnitPrice"),
                    SqlBinaryOperator.Multiply,
                    new ColumnRefExpr("oi", "Quantity"))
            }, isAggregate: true);

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["oi"] = OrderItemEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["oi"] = "t1" });

        Assert.That(sql, Is.EqualTo("AVG((\"t1\".\"unit_price\" * \"t1\".\"quantity\"))"));
    }

    [Test]
    public void Aggregate_CountWithCondition_Having()
    {
        // COUNT(o.OrderId) > 5
        var expr = new BinaryOpExpr(
            new FunctionCallExpr("COUNT",
                new SqlExpr[] { new ColumnRefExpr("o", "OrderId") }, isAggregate: true),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("5", "int"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo("(COUNT(\"t1\".\"order_id\") > 5)"));
    }

    [Test]
    public void Aggregate_MinMax_PostgreSQL()
    {
        // MAX(o.Total) - MIN(o.Total)
        var expr = new BinaryOpExpr(
            new FunctionCallExpr("MAX",
                new SqlExpr[] { new ColumnRefExpr("o", "Total") }, isAggregate: true),
            SqlBinaryOperator.Subtract,
            new FunctionCallExpr("MIN",
                new SqlExpr[] { new ColumnRefExpr("o", "Total") }, isAggregate: true));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.PostgreSQL, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" });

        Assert.That(sql, Is.EqualTo("(MAX(\"t1\".\"total\") - MIN(\"t1\".\"total\"))"));
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // 4. TYPE MAPPING & NULLABLE TESTS
    // ═══════════════════════════════════════════════════════════════════

    #region Type Mappings and Nullability

    [Test]
    public void TypeMapping_ColumnWithCustomMapping_ParamGetsMapping()
    {
        // u.Rating = @p0  (Rating has CustomTypeMappingClass = "RatingMapping")
        var expr = new BinaryOpExpr(
            new ColumnRefExpr("u", "Rating"),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "decimal", "value", customTypeMappingClass: "RatingMapping"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("(\"rating\" = @p0)"));

        // Verify the parameter retains its mapping class through bind
        var bound = SqlExprBinder.Bind(expr, UserEntity, GenSqlDialect.SQLite, "u");
        var params_ = SqlExprRenderer.CollectParameters(bound);
        Assert.That(params_, Has.Count.EqualTo(1));
        Assert.That(params_[0].CustomTypeMappingClass, Is.EqualTo("RatingMapping"));
    }

    [Test]
    public void TypeMapping_EnumColumn_IntComparison()
    {
        // u.Status = @p0  (Status is enum → int comparison)
        var expr = new BinaryOpExpr(
            new ColumnRefExpr("u", "Status"),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "MyEnum", "statusValue", isEnum: true, enumUnderlyingType: "int"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("(\"status\" = @p0)"));

        var bound = SqlExprBinder.Bind(expr, UserEntity, GenSqlDialect.SQLite, "u");
        var params_ = SqlExprRenderer.CollectParameters(bound);
        Assert.That(params_[0].IsEnum, Is.True);
        Assert.That(params_[0].EnumUnderlyingType, Is.EqualTo("int"));
    }

    [Test]
    public void Nullable_IsNull()
    {
        var expr = new IsNullCheckExpr(new ColumnRefExpr("u", "Email"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"email\" IS NULL"));
    }

    [Test]
    public void Nullable_IsNotNull()
    {
        var expr = new IsNullCheckExpr(new ColumnRefExpr("u", "Email"), isNegated: true);

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo("\"email\" IS NOT NULL"));
    }

    [Test]
    public void Nullable_CoalesceWithIsNull_Pattern()
    {
        // u.Email IS NOT NULL AND LOWER(u.Email) LIKE '%' || @p0 || '%'
        var expr = new BinaryOpExpr(
            new IsNullCheckExpr(new ColumnRefExpr("u", "Email"), isNegated: true),
            SqlBinaryOperator.And,
            new LikeExpr(
                new FunctionCallExpr("LOWER", new SqlExpr[] { new ColumnRefExpr("u", "Email") }),
                new ParamSlotExpr(0, "string", "search", isCaptured: true),
                likePrefix: "%", likeSuffix: "%"));

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");

        Assert.That(sql, Is.EqualTo(
            "(\"email\" IS NOT NULL AND LOWER(\"email\") LIKE '%' || @p0 || '%')"));
    }

    [Test]
    public void Boolean_InWhereContext_SQLite()
    {
        var expr = new ColumnRefExpr("u", "IsActive");

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u", inBooleanContext: true);

        Assert.That(sql, Is.EqualTo("\"is_active\" = 1"));
    }

    [Test]
    public void Boolean_InWhereContext_PostgreSQL()
    {
        var expr = new ColumnRefExpr("u", "IsActive");

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.PostgreSQL, "u", inBooleanContext: true);

        Assert.That(sql, Is.EqualTo("\"is_active\" = TRUE"));
    }

    [Test]
    public void Boolean_InJoinedTable_WhereContext()
    {
        var expr = new ColumnRefExpr("o", "IsShipped");

        var sql = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u",
            joinedEntities: new Dictionary<string, EntityInfo> { ["o"] = OrderEntity },
            tableAliases: new Dictionary<string, string> { ["u"] = "t0", ["o"] = "t1" },
            inBooleanContext: true);

        Assert.That(sql, Is.EqualTo("\"t1\".\"is_shipped\" = 1"));
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // 5. CROSS-DIALECT RENDERING
    // ═══════════════════════════════════════════════════════════════════

    #region Cross-Dialect

    [Test]
    public void CrossDialect_SameExpr_FourDialects()
    {
        // u.Age > @p0 AND u.Name = @p1
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.GreaterThan,
                new ParamSlotExpr(0, "int", "minAge")),
            SqlBinaryOperator.And,
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Name"),
                SqlBinaryOperator.Equal,
                new ParamSlotExpr(1, "string", "name")));

        var sqlite = BindAndRender(expr, UserEntity, GenSqlDialect.SQLite, "u");
        var pg = BindAndRender(expr, UserEntity, GenSqlDialect.PostgreSQL, "u");
        var mysql = BindAndRender(expr, UserEntity, GenSqlDialect.MySQL, "u");
        var sqlsrv = BindAndRender(expr, UserEntity, GenSqlDialect.SqlServer, "u");

        Assert.That(sqlite,  Is.EqualTo("((\"age\" > @p0) AND (\"name\" = @p1))"));
        Assert.That(pg,      Is.EqualTo("((\"age\" > $1) AND (\"name\" = $2))"));
        Assert.That(mysql,   Is.EqualTo("((`age` > @p0) AND (`name` = @p1))"));
        Assert.That(sqlsrv,  Is.EqualTo("(([age] > @p0) AND ([name] = @p1))"));
    }

    [Test]
    public void CrossDialect_BooleanLiteral()
    {
        var expr = new LiteralExpr("TRUE", "bool");

        Assert.That(SqlExprRenderer.Render(expr, GenSqlDialect.SQLite), Is.EqualTo("1"));
        Assert.That(SqlExprRenderer.Render(expr, GenSqlDialect.PostgreSQL), Is.EqualTo("TRUE"));
        Assert.That(SqlExprRenderer.Render(expr, GenSqlDialect.MySQL), Is.EqualTo("1"));
        Assert.That(SqlExprRenderer.Render(expr, GenSqlDialect.SqlServer), Is.EqualTo("1"));
    }

    [Test]
    public void CrossDialect_LikeConcat()
    {
        var like = new LikeExpr(
            new ResolvedColumnExpr("col"),
            new ParamSlotExpr(0, "string", "s"),
            likePrefix: "%", likeSuffix: "%");

        Assert.That(SqlExprRenderer.Render(like, GenSqlDialect.SQLite),
            Is.EqualTo("col LIKE '%' || @p0 || '%'"));
        Assert.That(SqlExprRenderer.Render(like, GenSqlDialect.PostgreSQL),
            Is.EqualTo("col LIKE '%' || $1 || '%'"));
        Assert.That(SqlExprRenderer.Render(like, GenSqlDialect.MySQL),
            Is.EqualTo("col LIKE CONCAT('%', @p0, '%')"));
        Assert.That(SqlExprRenderer.Render(like, GenSqlDialect.SqlServer),
            Is.EqualTo("col LIKE '%' + @p0 + '%'"));
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // 6. PARAMETER COLLECTION STRESS
    // ═══════════════════════════════════════════════════════════════════

    #region Parameter Collection

    [Test]
    public void Params_DeepNesting_CollectsAll()
    {
        // (((@p0 AND @p1) OR @p2) AND NOT @p3)
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new BinaryOpExpr(
                    new ParamSlotExpr(0, "bool", "a"),
                    SqlBinaryOperator.And,
                    new ParamSlotExpr(1, "bool", "b")),
                SqlBinaryOperator.Or,
                new ParamSlotExpr(2, "bool", "c")),
            SqlBinaryOperator.And,
            new UnaryOpExpr(SqlUnaryOperator.Not, new ParamSlotExpr(3, "bool", "d")));

        var params_ = SqlExprRenderer.CollectParameters(expr);

        Assert.That(params_, Has.Count.EqualTo(4));
        Assert.That(params_[0].LocalIndex, Is.EqualTo(0));
        Assert.That(params_[1].LocalIndex, Is.EqualTo(1));
        Assert.That(params_[2].LocalIndex, Is.EqualTo(2));
        Assert.That(params_[3].LocalIndex, Is.EqualTo(3));
    }

    [Test]
    public void Params_InLikeAndFunction_CollectsAll()
    {
        // u.Name LIKE @p0 AND u.Status IN (@p1) AND LOWER(u.Email) = @p2
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new LikeExpr(
                    new ResolvedColumnExpr("\"name\""),
                    new ParamSlotExpr(0, "string", "pattern"),
                    likePrefix: "%"),
                SqlBinaryOperator.And,
                new InExpr(
                    new ResolvedColumnExpr("\"status\""),
                    new SqlExpr[] { new ParamSlotExpr(1, "int[]", "statuses", isCollection: true) })),
            SqlBinaryOperator.And,
            new BinaryOpExpr(
                new FunctionCallExpr("LOWER", new SqlExpr[] { new ResolvedColumnExpr("\"email\"") }),
                SqlBinaryOperator.Equal,
                new ParamSlotExpr(2, "string", "email")));

        var params_ = SqlExprRenderer.CollectParameters(expr);

        Assert.That(params_, Has.Count.EqualTo(3));
        Assert.That(params_[0].ValueExpression, Is.EqualTo("pattern"));
        Assert.That(params_[1].IsCollection, Is.True);
        Assert.That(params_[2].ValueExpression, Is.EqualTo("email"));
    }

    #endregion

    // ── Helpers ────────────────────────────────────────────────────────

    private static string BindAndRender(
        SqlExpr expr,
        EntityInfo primaryEntity,
        GenSqlDialect dialect,
        string lambdaParam,
        IReadOnlyDictionary<string, EntityInfo>? joinedEntities = null,
        IReadOnlyDictionary<string, string>? tableAliases = null,
        bool inBooleanContext = false)
    {
        var bound = SqlExprBinder.Bind(expr, primaryEntity, dialect, lambdaParam,
            joinedEntities, tableAliases, inBooleanContext);
        return SqlExprRenderer.Render(bound, dialect);
    }

    private static EntityInfo CreateEntity(string name, string tableName, ColumnInfo[] columns)
    {
        return new EntityInfo(
            entityName: name,
            schemaClassName: $"{name}Schema",
            schemaNamespace: "TestApp.Schema",
            tableName: tableName,
            namingStyle: NamingStyleKind.SnakeCase,
            columns: columns,
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);
    }

    private static ColumnInfo Col(
        string propertyName,
        string columnName,
        string clrType,
        ColumnKind kind = ColumnKind.Standard,
        bool isNullable = false,
        bool isValueType = false,
        bool isEnum = false,
        string? referencedEntity = null,
        string? customTypeMapping = null)
    {
        return new ColumnInfo(
            propertyName, columnName, clrType, clrType,
            isNullable, kind, referencedEntity,
            new ColumnModifiers(customTypeMapping: customTypeMapping),
            isValueType: isValueType,
            isEnum: isEnum,
            customTypeMappingClass: customTypeMapping);
    }
}
