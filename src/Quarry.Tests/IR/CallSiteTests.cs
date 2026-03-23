using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

[TestFixture]
public class CallSiteTests
{
    #region RawCallSite Equality

    [Test]
    public void RawCallSite_EqualByValue()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void RawCallSite_DifferentUniqueId_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id2");

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentLine_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");
        var b = CreateRawCallSite("Where", "file.cs", 11, 5, "id1");

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentJoinedEntityTypeName_NotEqual()
    {
        var a = CreateRawCallSite("Join", "file.cs", 10, 5, "id1", joinedEntityTypeName: "Order");
        var b = CreateRawCallSite("Join", "file.cs", 10, 5, "id1", joinedEntityTypeName: "Product");

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentNonAnalyzableReason_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", nonAnalyzableReason: "stored in variable");
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", nonAnalyzableReason: "inside loop");

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentInitializedPropertyNames_NotEqual()
    {
        var a = CreateRawCallSite("Insert", "file.cs", 10, 5, "id1",
            initializedPropertyNames: ImmutableArray.Create("Name", "Age"));
        var b = CreateRawCallSite("Insert", "file.cs", 10, 5, "id1",
            initializedPropertyNames: ImmutableArray.Create("Name", "Email"));

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_SameInitializedPropertyNames_Equal()
    {
        var a = CreateRawCallSite("Insert", "file.cs", 10, 5, "id1",
            initializedPropertyNames: ImmutableArray.Create("Age", "Name"));
        var b = CreateRawCallSite("Insert", "file.cs", 10, 5, "id1",
            initializedPropertyNames: ImmutableArray.Create("Age", "Name"));

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void RawCallSite_DifferentIsInsideLoop_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", isInsideLoop: false);
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", isInsideLoop: true);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentIsPassedAsArgument_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", isPassedAsArgument: false);
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", isPassedAsArgument: true);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentIsAssignedFromNonQuarryMethod_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", isAssignedFromNonQuarryMethod: false);
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", isAssignedFromNonQuarryMethod: true);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentChainId_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", chainId: "chain_0");
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1", chainId: "chain_1");

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void RawCallSite_DifferentConditionalInfo_NotEqual()
    {
        var a = CreateRawCallSite("Where", "file.cs", 10, 5, "id1",
            conditionalInfo: new ConditionalInfo("x > 0", 1));
        var b = CreateRawCallSite("Where", "file.cs", 10, 5, "id1",
            conditionalInfo: new ConditionalInfo("x < 0", 1));

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void ConditionalInfo_Equality()
    {
        var a = new ConditionalInfo("x > 0", 1, BranchKind.Independent);
        var b = new ConditionalInfo("x > 0", 1, BranchKind.Independent);
        var c = new ConditionalInfo("x > 0", 1, BranchKind.MutuallyExclusive);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        Assert.That(a.Equals(c), Is.False);
    }

    #endregion

    #region EntityRef Tests

    [Test]
    public void EntityRef_FromEntityInfo_CopiesColumns()
    {
        var entity = CreateTestEntity();
        var entityRef = EntityRef.FromEntityInfo(entity);

        Assert.That(entityRef.EntityName, Is.EqualTo("User"));
        Assert.That(entityRef.TableName, Is.EqualTo("users"));
        Assert.That(entityRef.Columns.Count, Is.EqualTo(4));
    }

    [Test]
    public void EntityRef_Equality()
    {
        var entity = CreateTestEntity();
        var a = EntityRef.FromEntityInfo(entity);
        var b = EntityRef.FromEntityInfo(entity);

        Assert.That(a.Equals(b), Is.True);
    }

    #endregion

    #region BoundCallSite Tests

    [Test]
    public void BoundCallSite_Equality()
    {
        var raw = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");
        var entity = EntityRef.FromEntityInfo(CreateTestEntity());

        var a = new BoundCallSite(raw, "TestContext", "TestApp", GenSqlDialect.SQLite, "users", null, entity);
        var b = new BoundCallSite(raw, "TestContext", "TestApp", GenSqlDialect.SQLite, "users", null, entity);

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void BoundCallSite_DifferentDialect_NotEqual()
    {
        var raw = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");
        var entity = EntityRef.FromEntityInfo(CreateTestEntity());

        var a = new BoundCallSite(raw, "TestContext", "TestApp", GenSqlDialect.SQLite, "users", null, entity);
        var b = new BoundCallSite(raw, "TestContext", "TestApp", GenSqlDialect.PostgreSQL, "users", null, entity);

        Assert.That(a.Equals(b), Is.False);
    }

    #endregion

    #region TranslatedCallSite Tests

    [Test]
    public void TranslatedCallSite_WithoutClause_Equality()
    {
        var raw = CreateRawCallSite("Limit", "file.cs", 10, 5, "id1");
        var entity = EntityRef.FromEntityInfo(CreateTestEntity());
        var bound = new BoundCallSite(raw, "TestContext", "TestApp", GenSqlDialect.SQLite, "users", null, entity);

        var a = new TranslatedCallSite(bound);
        var b = new TranslatedCallSite(bound);

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void TranslatedCallSite_WithClause_Equality()
    {
        var raw = CreateRawCallSite("Where", "file.cs", 10, 5, "id1");
        var entity = EntityRef.FromEntityInfo(CreateTestEntity());
        var bound = new BoundCallSite(raw, "TestContext", "TestApp", GenSqlDialect.SQLite, "users", null, entity);

        var resolvedExpr = new BinaryOpExpr(
            new ResolvedColumnExpr("\"age\""),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("18", "int"));

        var clause = new TranslatedClause(
            ClauseKind.Where,
            resolvedExpr,
            System.Array.Empty<ParameterInfo>());

        var a = new TranslatedCallSite(bound, clause);
        var b = new TranslatedCallSite(bound, clause);

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void TranslatedClause_DifferentExpression_NotEqual()
    {
        var expr1 = new ResolvedColumnExpr("\"age\"");
        var expr2 = new ResolvedColumnExpr("\"name\"");

        var a = new TranslatedClause(ClauseKind.Where, expr1, System.Array.Empty<ParameterInfo>());
        var b = new TranslatedClause(ClauseKind.Where, expr2, System.Array.Empty<ParameterInfo>());

        Assert.That(a.Equals(b), Is.False);
    }

    #endregion

    #region Helpers

    private static RawCallSite CreateRawCallSite(
        string methodName, string filePath, int line, int column, string uniqueId,
        string? joinedEntityTypeName = null,
        string? nonAnalyzableReason = null,
        ImmutableArray<string>? initializedPropertyNames = null,
        bool isInsideLoop = false,
        bool isInsideTryCatch = false,
        bool isCapturedInLambda = false,
        bool isPassedAsArgument = false,
        bool isAssignedFromNonQuarryMethod = false,
        ConditionalInfo? conditionalInfo = null,
        string? chainId = null)
    {
        return new RawCallSite(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            uniqueId: uniqueId,
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: "TestApp.User",
            resultTypeName: null,
            isAnalyzable: nonAnalyzableReason == null,
            nonAnalyzableReason: nonAnalyzableReason,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: default,
            joinedEntityTypeName: joinedEntityTypeName,
            initializedPropertyNames: initializedPropertyNames,
            isInsideLoop: isInsideLoop,
            isInsideTryCatch: isInsideTryCatch,
            isCapturedInLambda: isCapturedInLambda,
            isPassedAsArgument: isPassedAsArgument,
            isAssignedFromNonQuarryMethod: isAssignedFromNonQuarryMethod,
            conditionalInfo: conditionalInfo,
            chainId: chainId);
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

    #endregion
}
