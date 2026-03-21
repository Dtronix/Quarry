using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

/// <summary>
/// Tests that the SyntacticExpression → SqlExpr → Bind → Render pipeline
/// produces equivalent SQL to the existing SyntacticClauseTranslator.
/// </summary>
[TestFixture]
public class AdapterRoundTripTests
{
    #region SyntacticExpressionAdapter Tests

    [Test]
    public void Adapter_PropertyAccess_ProducesColumnRef()
    {
        var syntactic = new SyntacticPropertyAccess("u", "Name");
        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<ColumnRefExpr>());
        var col = (ColumnRefExpr)sqlExpr;
        Assert.That(col.ParameterName, Is.EqualTo("u"));
        Assert.That(col.PropertyName, Is.EqualTo("Name"));
    }

    [Test]
    public void Adapter_RefIdAccess_ProducesColumnRefWithNestedProperty()
    {
        // SyntacticPropertyAccess stores "UserId.Id" for Ref<T>.Id
        var syntactic = new SyntacticPropertyAccess("u", "UserId.Id");
        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<ColumnRefExpr>());
        var col = (ColumnRefExpr)sqlExpr;
        Assert.That(col.PropertyName, Is.EqualTo("UserId"));
        Assert.That(col.NestedProperty, Is.EqualTo("Id"));
    }

    [Test]
    public void Adapter_Literal_Null()
    {
        var syntactic = new SyntacticLiteral("null", "object", isNull: true);
        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<LiteralExpr>());
        Assert.That(((LiteralExpr)sqlExpr).IsNull, Is.True);
    }

    [Test]
    public void Adapter_Literal_Int()
    {
        var syntactic = new SyntacticLiteral("42", "int");
        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<LiteralExpr>());
        Assert.That(((LiteralExpr)sqlExpr).SqlText, Is.EqualTo("42"));
        Assert.That(((LiteralExpr)sqlExpr).ClrType, Is.EqualTo("int"));
    }

    [Test]
    public void Adapter_Literal_Bool()
    {
        var syntactic = new SyntacticLiteral("true", "bool");
        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<LiteralExpr>());
        Assert.That(((LiteralExpr)sqlExpr).SqlText, Is.EqualTo("TRUE"));
    }

    [Test]
    public void Adapter_BinaryEquals_ProducesBinaryOp()
    {
        var left = new SyntacticPropertyAccess("u", "Age");
        var right = new SyntacticLiteral("18", "int");
        var syntactic = new SyntacticBinary(left, "==", right);

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<BinaryOpExpr>());
        var bin = (BinaryOpExpr)sqlExpr;
        Assert.That(bin.Operator, Is.EqualTo(SqlBinaryOperator.Equal));
        Assert.That(bin.Left, Is.InstanceOf<ColumnRefExpr>());
        Assert.That(bin.Right, Is.InstanceOf<LiteralExpr>());
    }

    [Test]
    public void Adapter_NullComparison_ProducesIsNullCheck()
    {
        var left = new SyntacticPropertyAccess("u", "Email");
        var right = new SyntacticLiteral("null", "object", isNull: true);
        var syntactic = new SyntacticBinary(left, "==", right);

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<IsNullCheckExpr>());
        Assert.That(((IsNullCheckExpr)sqlExpr).IsNegated, Is.False);
    }

    [Test]
    public void Adapter_NotNullComparison_ProducesNegatedIsNullCheck()
    {
        var left = new SyntacticPropertyAccess("u", "Email");
        var right = new SyntacticLiteral("null", "object", isNull: true);
        var syntactic = new SyntacticBinary(left, "!=", right);

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<IsNullCheckExpr>());
        Assert.That(((IsNullCheckExpr)sqlExpr).IsNegated, Is.True);
    }

    [Test]
    public void Adapter_UnaryNot_ProducesUnaryOp()
    {
        var operand = new SyntacticPropertyAccess("u", "IsActive");
        var syntactic = new SyntacticUnary("!", operand);

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<UnaryOpExpr>());
        Assert.That(((UnaryOpExpr)sqlExpr).Operator, Is.EqualTo(SqlUnaryOperator.Not));
    }

    [Test]
    public void Adapter_CapturedVariable_ProducesCapturedValueExpr()
    {
        var syntactic = new SyntacticCapturedVariable("minAge", "minAge", "Body.Right");

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<CapturedValueExpr>());
        var captured = (CapturedValueExpr)sqlExpr;
        Assert.That(captured.VariableName, Is.EqualTo("minAge"));
        Assert.That(captured.ExpressionPath, Is.EqualTo("Body.Right"));
    }

    [Test]
    public void Adapter_MethodCall_Contains_ProducesLikeExpr()
    {
        var target = new SyntacticPropertyAccess("u", "Name");
        var arg = new SyntacticLiteral("john", "string");
        var syntactic = new SyntacticMethodCall(target, "Contains", new SyntacticExpression[] { arg });

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<LikeExpr>());
        var like = (LikeExpr)sqlExpr;
        Assert.That(like.LikePrefix, Is.EqualTo("%"));
        Assert.That(like.LikeSuffix, Is.EqualTo("%"));
    }

    [Test]
    public void Adapter_MethodCall_ToLower_ProducesFunctionCall()
    {
        var target = new SyntacticPropertyAccess("u", "Name");
        var syntactic = new SyntacticMethodCall(target, "ToLower", System.Array.Empty<SyntacticExpression>());

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<FunctionCallExpr>());
        Assert.That(((FunctionCallExpr)sqlExpr).FunctionName, Is.EqualTo("LOWER"));
    }

    [Test]
    public void Adapter_Unknown_ProducesSqlRaw()
    {
        var syntactic = new SyntacticUnknown("x ? y : z", "Conditional not supported");

        var sqlExpr = SyntacticExpressionAdapter.Convert(syntactic);

        Assert.That(sqlExpr, Is.InstanceOf<SqlRawExpr>());
        Assert.That(((SqlRawExpr)sqlExpr).SqlText, Is.EqualTo("x ? y : z"));
    }

    #endregion

    #region SqlExprClauseTranslator Round-Trip Tests

    [Test]
    public void RoundTrip_SimpleWhereEquals_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        // Old path
        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticBinary(
                new SyntacticPropertyAccess("u", "Age"),
                "==",
                new SyntacticLiteral("18", "int")));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        // New path
        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_WhereIsNull_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticBinary(
                new SyntacticPropertyAccess("u", "Email"),
                "==",
                new SyntacticLiteral("null", "object", isNull: true)));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_WhereNotNull_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticBinary(
                new SyntacticPropertyAccess("u", "Email"),
                "!=",
                new SyntacticLiteral("null", "object", isNull: true)));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_WhereBoolean_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticPropertyAccess("u", "IsActive"));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_OrderBy_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.OrderBy, "u",
            new SyntacticPropertyAccess("u", "Name"),
            isDescending: true);

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
        Assert.That(newResult, Is.InstanceOf<OrderByClauseInfo>());
        Assert.That(((OrderByClauseInfo)newResult).IsDescending, Is.EqualTo(((OrderByClauseInfo)oldResult).IsDescending));
    }

    [Test]
    public void RoundTrip_WhereCapturedVariable_MatchesParameterCount()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticBinary(
                new SyntacticPropertyAccess("u", "Age"),
                ">",
                new SyntacticCapturedVariable("minAge", "minAge", "Body.Right")));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.Parameters.Count, Is.EqualTo(oldResult.Parameters.Count));
        Assert.That(newResult.Parameters[0].IsCaptured, Is.True);
        Assert.That(newResult.Parameters[0].ExpressionPath, Is.EqualTo("Body.Right"));
    }

    [Test]
    public void RoundTrip_WhereAndCombination_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        var left = new SyntacticBinary(
            new SyntacticPropertyAccess("u", "Age"),
            ">",
            new SyntacticLiteral("18", "int"));
        var right = new SyntacticBinary(
            new SyntacticPropertyAccess("u", "Age"),
            "<",
            new SyntacticLiteral("65", "int"));
        var combined = new SyntacticBinary(left, "&&", right);

        var pending = new PendingClauseInfo(ClauseKind.Where, "u", combined);

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_WhereUnaryNot_MatchesOldTranslator()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticUnary("!", new SyntacticPropertyAccess("u", "IsActive")));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.SQLite);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.SQLite);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_PostgreSQL_BooleanLiteral()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticPropertyAccess("u", "IsActive"));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.PostgreSQL);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.PostgreSQL);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
    }

    [Test]
    public void RoundTrip_MySQL_Quoting()
    {
        var entity = CreateTestEntity();

        var pending = new PendingClauseInfo(
            ClauseKind.Where, "u",
            new SyntacticBinary(
                new SyntacticPropertyAccess("u", "Name"),
                "==",
                new SyntacticCapturedVariable("n", "n")));

        var oldTranslator = new SyntacticClauseTranslator(entity, GenSqlDialect.MySQL);
        var oldResult = oldTranslator.Translate(pending);

        var newTranslator = new SqlExprClauseTranslator(entity, GenSqlDialect.MySQL);
        var newResult = newTranslator.Translate(pending);

        Assert.That(oldResult.IsSuccess, Is.True);
        Assert.That(newResult.IsSuccess, Is.True);
        Assert.That(newResult.SqlFragment, Is.EqualTo(oldResult.SqlFragment));
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
