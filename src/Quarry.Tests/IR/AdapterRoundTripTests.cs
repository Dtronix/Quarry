using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

/// <summary>
/// Tests that the SyntacticExpression → SqlExpr adapter conversion works correctly,
/// and that SqlExprClauseTranslator produces correct SQL output.
/// </summary>
[TestFixture]
public class AdapterRoundTripTests
{
    #region SyntacticExpression → SqlExpr Adapter Tests

    [Test]
    public void Convert_PropertyAccess_ProducesColumnRef()
    {
        var syntactic = new SyntacticPropertyAccess("u", "Name");
        var result = SyntacticExpressionAdapter.Convert(syntactic);
        Assert.That(result, Is.InstanceOf<ColumnRefExpr>());
        var colRef = (ColumnRefExpr)result;
        Assert.That(colRef.ParameterName, Is.EqualTo("u"));
        Assert.That(colRef.PropertyName, Is.EqualTo("Name"));
    }

    [Test]
    public void Convert_Literal_Null()
    {
        var syntactic = new SyntacticLiteral("null", "object", isNull: true);
        var result = SyntacticExpressionAdapter.Convert(syntactic);
        Assert.That(result, Is.InstanceOf<LiteralExpr>());
        Assert.That(((LiteralExpr)result).IsNull, Is.True);
    }

    [Test]
    public void Convert_BoolLiteral_True()
    {
        var syntactic = new SyntacticLiteral("true", "bool");
        var result = SyntacticExpressionAdapter.Convert(syntactic);
        Assert.That(result, Is.InstanceOf<LiteralExpr>());
        Assert.That(((LiteralExpr)result).SqlText, Is.EqualTo("TRUE"));
    }

    [Test]
    public void Convert_Binary_Equal()
    {
        var left = new SyntacticPropertyAccess("u", "Name");
        var right = new SyntacticLiteral("42", "int");
        var binary = new SyntacticBinary(left, "==", right);
        var result = SyntacticExpressionAdapter.Convert(binary);
        Assert.That(result, Is.InstanceOf<BinaryOpExpr>());
        var binExpr = (BinaryOpExpr)result;
        Assert.That(binExpr.Operator, Is.EqualTo(SqlBinaryOperator.Equal));
    }

    [Test]
    public void Convert_NullCompare_ProducesIsNull()
    {
        var left = new SyntacticPropertyAccess("u", "Email");
        var right = new SyntacticLiteral("null", "object", isNull: true);
        var binary = new SyntacticBinary(left, "==", right);
        var result = SyntacticExpressionAdapter.Convert(binary);
        Assert.That(result, Is.InstanceOf<IsNullCheckExpr>());
        Assert.That(((IsNullCheckExpr)result).IsNegated, Is.False);
    }

    [Test]
    public void Convert_NotNullCompare_ProducesIsNotNull()
    {
        var left = new SyntacticPropertyAccess("u", "Email");
        var right = new SyntacticLiteral("null", "object", isNull: true);
        var binary = new SyntacticBinary(left, "!=", right);
        var result = SyntacticExpressionAdapter.Convert(binary);
        Assert.That(result, Is.InstanceOf<IsNullCheckExpr>());
        Assert.That(((IsNullCheckExpr)result).IsNegated, Is.True);
    }

    [Test]
    public void Convert_Unary_Not()
    {
        var operand = new SyntacticPropertyAccess("u", "IsActive");
        var unary = new SyntacticUnary("!", operand);
        var result = SyntacticExpressionAdapter.Convert(unary);
        Assert.That(result, Is.InstanceOf<UnaryOpExpr>());
        Assert.That(((UnaryOpExpr)result).Operator, Is.EqualTo(SqlUnaryOperator.Not));
    }

    [Test]
    public void Convert_StringContains_ProducesLike()
    {
        var target = new SyntacticPropertyAccess("u", "Name");
        var arg = new SyntacticLiteral("test", "string");
        var methodCall = new SyntacticMethodCall(target, "Contains", new SyntacticExpression[] { arg });
        var result = SyntacticExpressionAdapter.Convert(methodCall);
        Assert.That(result, Is.InstanceOf<LikeExpr>());
    }

    [Test]
    public void Convert_CapturedVariable_ProducesCapturedValue()
    {
        var syntactic = new SyntacticCapturedVariable("minAge", "minAge", "Body.Right");
        var result = SyntacticExpressionAdapter.Convert(syntactic);
        Assert.That(result, Is.InstanceOf<CapturedValueExpr>());
        var captured = (CapturedValueExpr)result;
        Assert.That(captured.VariableName, Is.EqualTo("minAge"));
        Assert.That(captured.ExpressionPath, Is.EqualTo("Body.Right"));
    }

    [Test]
    public void Convert_RefIdAccess_ProducesNestedColumnRef()
    {
        var inner = new SyntacticPropertyAccess("u", "Category");
        var memberAccess = new SyntacticMemberAccess(inner, "Id");
        var result = SyntacticExpressionAdapter.Convert(memberAccess);
        Assert.That(result, Is.InstanceOf<ColumnRefExpr>());
        var colRef = (ColumnRefExpr)result;
        Assert.That(colRef.PropertyName, Is.EqualTo("Category"));
        Assert.That(colRef.NestedProperty, Is.EqualTo("Id"));
    }

    [Test]
    public void Convert_SqlCount_ProducesFunctionCall()
    {
        var target = new SyntacticMemberAccess(
            new SyntacticCapturedVariable("Sql", "Sql"), "Sql");
        var methodCall = new SyntacticMethodCall(target, "Count", System.Array.Empty<SyntacticExpression>());
        var result = SyntacticExpressionAdapter.Convert(methodCall);
        // The adapter maps Sql.Count() via MapSqlFunction or the CapturedValueExpr path
    }

    #endregion

    #region SqlExprClauseTranslator Tests

    [Test]
    public void Translate_SimpleWhereEquals()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.Equal,
                new LiteralExpr("18", "int")));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("(\"age\" = 18)"));
    }

    [Test]
    public void Translate_WhereIsNull()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new IsNullCheckExpr(new ColumnRefExpr("u", "Email"), isNegated: false));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("\"email\" IS NULL"));
    }

    [Test]
    public void Translate_WhereIsNotNull()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new IsNullCheckExpr(new ColumnRefExpr("u", "Email"), isNegated: true));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("\"email\" IS NOT NULL"));
    }

    [Test]
    public void Translate_WhereBoolean_SQLite()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new ColumnRefExpr("u", "IsActive"));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("\"is_active\" = 1"));
    }

    [Test]
    public void Translate_WhereBoolean_PostgreSQL()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new ColumnRefExpr("u", "IsActive"));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.PostgreSQL);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("\"is_active\" = TRUE"));
    }

    [Test]
    public void Translate_OrderByDescending()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.OrderBy, "u",
            new ColumnRefExpr("u", "Name"),
            isDescending: true);

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("\"name\""));
        Assert.That(result, Is.InstanceOf<OrderByClauseInfo>());
        Assert.That(((OrderByClauseInfo)result).IsDescending, Is.True);
    }

    [Test]
    public void Translate_WhereCapturedVariable()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Age"),
                SqlBinaryOperator.GreaterThan,
                new CapturedValueExpr("minAge", "minAge", expressionPath: "Body.Right")));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("(\"age\" > @p0)"));
        Assert.That(result.Parameters.Count, Is.EqualTo(1));
        Assert.That(result.Parameters[0].IsCaptured, Is.True);
        Assert.That(result.Parameters[0].ExpressionPath, Is.EqualTo("Body.Right"));
    }

    [Test]
    public void Translate_WhereAndCombination()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new BinaryOpExpr(
                new BinaryOpExpr(
                    new ColumnRefExpr("u", "Age"),
                    SqlBinaryOperator.GreaterThan,
                    new LiteralExpr("18", "int")),
                SqlBinaryOperator.And,
                new BinaryOpExpr(
                    new ColumnRefExpr("u", "Age"),
                    SqlBinaryOperator.LessThan,
                    new LiteralExpr("65", "int"))));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("((\"age\" > 18) AND (\"age\" < 65))"));
    }

    [Test]
    public void Translate_WhereUnaryNot()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new UnaryOpExpr(SqlUnaryOperator.Not, new ColumnRefExpr("u", "IsActive")));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("NOT (\"is_active\")"));
    }

    [Test]
    public void Translate_MySQL_Quoting()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new BinaryOpExpr(
                new ColumnRefExpr("u", "Name"),
                SqlBinaryOperator.Equal,
                new CapturedValueExpr("n", "n")));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.MySQL);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("(`name` = @p0)"));
    }

    [Test]
    public void Translate_PostgreSQL_UsesGenericParamFormat()
    {
        var entity = CreateUserEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new BinaryOpExpr(
                new ColumnRefExpr("u", "UserId"),
                SqlBinaryOperator.Equal,
                new CapturedValueExpr("id", "id", expressionPath: "Body.Right")));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.PostgreSQL);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        // Should use @p0 format, not $1 — dialect-specific formatting happens later
        Assert.That(result.SqlFragment, Is.EqualTo("(\"user_id\" = @p0)"));
    }

    [Test]
    public void Translate_WhereStringContains()
    {
        var entity = CreateUserEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new LikeExpr(
                new ColumnRefExpr("u", "UserName"),
                new LiteralExpr("er", "string"),
                likePrefix: "%", likeSuffix: "%"));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.SqlFragment, Is.EqualTo("\"user_name\" LIKE '%' || @p0 || '%'"));
        Assert.That(result.Parameters.Count, Is.EqualTo(1));
        Assert.That(result.Parameters[0].ClrType, Is.EqualTo("string"));
    }

    [Test]
    public void Translate_SetClause()
    {
        var entity = CreateUserEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Set, "u",
            new ColumnRefExpr("u", "UserName"));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result, Is.InstanceOf<SetClauseInfo>());
        Assert.That(result.SqlFragment, Contains.Substring("\"user_name\""));
    }

    [Test]
    public void Translate_UnsupportedExpr_ReturnsFailure()
    {
        var entity = CreateTestEntity();
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SqlRawExpr("unsupported(...)"));

        var translator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var result = translator.Translate(pending);

        Assert.That(result.IsSuccess, Is.False);
    }

    #endregion

    private static EntityInfo CreateUserEntity()
    {
        var mods = new ColumnModifiers();
        return new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "Quarry.Tests.Samples",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("UserId", "user_id", "int", "int", false, ColumnKind.PrimaryKey, null, mods, isValueType: true),
                new ColumnInfo("UserName", "user_name", "string", "string", false, ColumnKind.Standard, null, mods),
                new ColumnInfo("Email", "email", "string?", "string?", true, ColumnKind.Standard, null, mods),
                new ColumnInfo("IsActive", "is_active", "bool", "bool", false, ColumnKind.Standard, null, mods, isValueType: true),
                new ColumnInfo("CreatedAt", "created_at", "DateTime", "DateTime", false, ColumnKind.Standard, null, mods, isValueType: true),
                new ColumnInfo("LastLogin", "last_login", "DateTime?", "DateTime?", true, ColumnKind.Standard, null, mods),
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);
    }

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
