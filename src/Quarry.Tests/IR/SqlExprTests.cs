using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

[TestFixture]
public class SqlExprTests
{
    #region Equality Tests

    [Test]
    public void ColumnRefExpr_EqualityByValue()
    {
        var a = new ColumnRefExpr("u", "Name");
        var b = new ColumnRefExpr("u", "Name");
        var c = new ColumnRefExpr("u", "Age");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        Assert.That(a.Equals(c), Is.False);
    }

    [Test]
    public void LiteralExpr_EqualityByValue()
    {
        var a = new LiteralExpr("42", "int");
        var b = new LiteralExpr("42", "int");
        var c = new LiteralExpr("43", "int");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    [Test]
    public void BinaryOpExpr_StructuralEquality()
    {
        var left = new ColumnRefExpr("u", "Age");
        var right = new LiteralExpr("18", "int");
        var a = new BinaryOpExpr(left, SqlBinaryOperator.GreaterThan, right);
        var b = new BinaryOpExpr(
            new ColumnRefExpr("u", "Age"),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("18", "int"));

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void IsNullCheckExpr_Equality()
    {
        var col = new ColumnRefExpr("u", "Email");
        var a = new IsNullCheckExpr(col, isNegated: false);
        var b = new IsNullCheckExpr(new ColumnRefExpr("u", "Email"), isNegated: false);
        var c = new IsNullCheckExpr(new ColumnRefExpr("u", "Email"), isNegated: true);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    [Test]
    public void UnaryOpExpr_Equality()
    {
        var operand = new ColumnRefExpr("u", "IsActive");
        var a = new UnaryOpExpr(SqlUnaryOperator.Not, operand);
        var b = new UnaryOpExpr(SqlUnaryOperator.Not, new ColumnRefExpr("u", "IsActive"));

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void NullEquality_ReturnsFalse()
    {
        var expr = new LiteralExpr("42", "int");
        Assert.That(expr.Equals(null), Is.False);
    }

    [Test]
    public void ReferenceEquality_ReturnsTrue()
    {
        var expr = new LiteralExpr("42", "int");
        Assert.That(expr.Equals(expr), Is.True);
    }

    #endregion

    #region Renderer Tests

    [Test]
    public void Renderer_ResolvedColumn_QuotedName()
    {
        var col = new ResolvedColumnExpr("\"user_name\"");
        var sql = SqlExprRenderer.Render(col, GenSqlDialect.SQLite);

        Assert.That(sql, Is.EqualTo("\"user_name\""));
    }

    [Test]
    public void Renderer_BinaryOp_WrappedInParens()
    {
        var left = new ResolvedColumnExpr("\"age\"");
        var right = new LiteralExpr("18", "int");
        var expr = new BinaryOpExpr(left, SqlBinaryOperator.GreaterThan, right);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("(\"age\" > 18)"));
    }

    [Test]
    public void Renderer_IsNull()
    {
        var col = new ResolvedColumnExpr("\"email\"");
        var expr = new IsNullCheckExpr(col, isNegated: false);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("\"email\" IS NULL"));
    }

    [Test]
    public void Renderer_IsNotNull()
    {
        var col = new ResolvedColumnExpr("\"email\"");
        var expr = new IsNullCheckExpr(col, isNegated: true);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("\"email\" IS NOT NULL"));
    }

    [Test]
    public void Renderer_UnaryNot()
    {
        var col = new ResolvedColumnExpr("\"is_active\"");
        var expr = new UnaryOpExpr(SqlUnaryOperator.Not, col);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("NOT (\"is_active\")"));
    }

    [Test]
    public void Renderer_FunctionCall()
    {
        var col = new ResolvedColumnExpr("\"name\"");
        var expr = new FunctionCallExpr("LOWER", new SqlExpr[] { col });

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("LOWER(\"name\")"));
    }

    [Test]
    public void Renderer_CountStar()
    {
        var expr = new FunctionCallExpr("COUNT", new SqlExpr[] { new SqlRawExpr("*") }, isAggregate: true);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("COUNT(*)"));
    }

    [Test]
    public void Renderer_InExpr()
    {
        var col = new ResolvedColumnExpr("\"status\"");
        var values = new SqlExpr[]
        {
            new LiteralExpr("1", "int"),
            new LiteralExpr("2", "int"),
            new LiteralExpr("3", "int")
        };
        var expr = new InExpr(col, values);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("\"status\" IN (1, 2, 3)"));
    }

    [Test]
    public void Renderer_ParamSlot_SQLite()
    {
        var param = new ParamSlotExpr(0, "string", "\"hello\"");
        var sql = SqlExprRenderer.Render(param, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("@p0"));
    }

    [Test]
    public void Renderer_ParamSlot_PostgreSQL()
    {
        var param = new ParamSlotExpr(0, "string", "\"hello\"");
        var sql = SqlExprRenderer.Render(param, GenSqlDialect.PostgreSQL);
        Assert.That(sql, Is.EqualTo("$1"));
    }

    [Test]
    public void Renderer_LiteralString()
    {
        var literal = new LiteralExpr("hello", "string");
        var sql = SqlExprRenderer.Render(literal, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("'hello'"));
    }

    [Test]
    public void Renderer_LiteralNull()
    {
        var literal = new LiteralExpr("NULL", "object", isNull: true);
        var sql = SqlExprRenderer.Render(literal, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("NULL"));
    }

    [Test]
    public void Renderer_LiteralBool_SQLite()
    {
        var literal = new LiteralExpr("TRUE", "bool");
        var sql = SqlExprRenderer.Render(literal, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("1"));
    }

    [Test]
    public void Renderer_LiteralBool_PostgreSQL()
    {
        var literal = new LiteralExpr("TRUE", "bool");
        var sql = SqlExprRenderer.Render(literal, GenSqlDialect.PostgreSQL);
        Assert.That(sql, Is.EqualTo("TRUE"));
    }

    [Test]
    public void Renderer_AndCombination()
    {
        var left = new BinaryOpExpr(
            new ResolvedColumnExpr("\"age\""),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("18", "int"));
        var right = new BinaryOpExpr(
            new ResolvedColumnExpr("\"name\""),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "string", "\"John\""));
        var expr = new BinaryOpExpr(left, SqlBinaryOperator.And, right);

        var sql = SqlExprRenderer.Render(expr, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("((\"age\" > 18) AND (\"name\" = @p0))"));
    }

    #endregion

    #region Binder Tests

    [Test]
    public void Binder_ResolvesColumnRef()
    {
        var entity = CreateTestEntity();
        var expr = new ColumnRefExpr("u", "Name");

        var bound = SqlExprBinder.Bind(expr, entity, GenSqlDialect.SQLite, "u");

        Assert.That(bound, Is.InstanceOf<ResolvedColumnExpr>());
        Assert.That(((ResolvedColumnExpr)bound).QuotedColumnName, Is.EqualTo("\"name\""));
    }

    [Test]
    public void Binder_ResolvesNestedBinaryExpr()
    {
        var entity = CreateTestEntity();
        var expr = new BinaryOpExpr(
            new ColumnRefExpr("u", "Age"),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("18", "int"));

        var bound = SqlExprBinder.Bind(expr, entity, GenSqlDialect.SQLite, "u");

        Assert.That(bound, Is.InstanceOf<BinaryOpExpr>());
        var bin = (BinaryOpExpr)bound;
        Assert.That(bin.Left, Is.InstanceOf<ResolvedColumnExpr>());
        Assert.That(((ResolvedColumnExpr)bin.Left).QuotedColumnName, Is.EqualTo("\"age\""));
    }

    [Test]
    public void Binder_UnresolvedColumn_ReturnsSqlRaw()
    {
        var entity = CreateTestEntity();
        var expr = new ColumnRefExpr("u", "NonExistent");

        var bound = SqlExprBinder.Bind(expr, entity, GenSqlDialect.SQLite, "u");

        Assert.That(bound, Is.InstanceOf<SqlRawExpr>());
    }

    [Test]
    public void Binder_BoolColumnInWhereContext_AddsTrueComparison()
    {
        var entity = CreateTestEntity();
        var expr = new ColumnRefExpr("u", "IsActive");

        var bound = SqlExprBinder.Bind(expr, entity, GenSqlDialect.SQLite, "u", inBooleanContext: true);

        Assert.That(bound, Is.InstanceOf<SqlRawExpr>());
        var sql = SqlExprRenderer.Render(bound, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("\"is_active\" = 1"));
    }

    [Test]
    public void Binder_LeavesLiteralsUntouched()
    {
        var entity = CreateTestEntity();
        var literal = new LiteralExpr("42", "int");

        var bound = SqlExprBinder.Bind(literal, entity, GenSqlDialect.SQLite, "u");

        Assert.That(ReferenceEquals(bound, literal), Is.True);
    }

    [Test]
    public void Binder_MySQL_QuotesWithBackticks()
    {
        var entity = CreateTestEntity();
        var expr = new ColumnRefExpr("u", "Name");

        var bound = SqlExprBinder.Bind(expr, entity, GenSqlDialect.MySQL, "u");

        Assert.That(bound, Is.InstanceOf<ResolvedColumnExpr>());
        Assert.That(((ResolvedColumnExpr)bound).QuotedColumnName, Is.EqualTo("`name`"));
    }

    [Test]
    public void Binder_SqlServer_QuotesWithBrackets()
    {
        var entity = CreateTestEntity();
        var expr = new ColumnRefExpr("u", "Name");

        var bound = SqlExprBinder.Bind(expr, entity, GenSqlDialect.SqlServer, "u");

        Assert.That(bound, Is.InstanceOf<ResolvedColumnExpr>());
        Assert.That(((ResolvedColumnExpr)bound).QuotedColumnName, Is.EqualTo("[name]"));
    }

    #endregion

    #region CollectParameters Tests

    [Test]
    public void CollectParameters_FindsAllParamSlots()
    {
        var expr = new BinaryOpExpr(
            new BinaryOpExpr(
                new ResolvedColumnExpr("\"age\""),
                SqlBinaryOperator.GreaterThan,
                new ParamSlotExpr(0, "int", "minAge")),
            SqlBinaryOperator.And,
            new BinaryOpExpr(
                new ResolvedColumnExpr("\"name\""),
                SqlBinaryOperator.Equal,
                new ParamSlotExpr(1, "string", "targetName")));

        var params_ = SqlExprRenderer.CollectParameters(expr);
        Assert.That(params_, Has.Count.EqualTo(2));
        Assert.That(params_[0].LocalIndex, Is.EqualTo(0));
        Assert.That(params_[1].LocalIndex, Is.EqualTo(1));
    }

    #endregion

    #region LIKE Renderer Tests

    [Test]
    public void Renderer_Like_Contains_SQLite()
    {
        var col = new ResolvedColumnExpr("\"name\"");
        var param = new ParamSlotExpr(0, "string", "\"john\"");
        var like = new LikeExpr(col, param, likePrefix: "%", likeSuffix: "%");

        var sql = SqlExprRenderer.Render(like, GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("\"name\" LIKE '%' || @p0 || '%'"));
    }

    [Test]
    public void Renderer_Like_Contains_MySQL()
    {
        var col = new ResolvedColumnExpr("`name`");
        var param = new ParamSlotExpr(0, "string", "\"john\"");
        var like = new LikeExpr(col, param, likePrefix: "%", likeSuffix: "%");

        var sql = SqlExprRenderer.Render(like, GenSqlDialect.MySQL);
        Assert.That(sql, Is.EqualTo("`name` LIKE CONCAT('%', @p0, '%')"));
    }

    [Test]
    public void Renderer_Like_WithEscape()
    {
        var col = new ResolvedColumnExpr("\"name\"");
        var param = new ParamSlotExpr(0, "string", "\"100%\"");
        var like = new LikeExpr(col, param, likePrefix: "%", likeSuffix: "%", needsEscape: true);

        var sql = SqlExprRenderer.Render(like, GenSqlDialect.SQLite);
        Assert.That(sql, Does.EndWith("ESCAPE '\\'"));
    }

    #endregion

    private static EntityInfo CreateTestEntity()
    {
        var mods = new ColumnModifiers();
        return new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("Name", "name", "string", "string", false, ColumnKind.Standard, null, mods),
                new ColumnInfo("Age", "age", "int", "int", false, ColumnKind.Standard, null, mods, isValueType: true),
                new ColumnInfo("Email", "email", "string?", "string?", true, ColumnKind.Standard, null, mods),
                new ColumnInfo("IsActive", "is_active", "bool", "bool", false, ColumnKind.Standard, null, mods, isValueType: true)
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);
    }
}
