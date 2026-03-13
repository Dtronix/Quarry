using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

[TestFixture]
public class SyntacticClauseTranslatorTests
{
    #region String Contains Tests

    [Test]
    public void Contains_LiteralString_ParameterizesLikePattern()
    {
        var clause = TranslateStringMethodCall("Contains", "john", "string");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Is.EqualTo("\"Name\" LIKE '%' || @p0 || '%'"));
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
        Assert.That(clause.Parameters[0].IsCaptured, Is.False);
    }

    [Test]
    public void Contains_LiteralWithSpecialChars_EscapesWithBackslash()
    {
        var clause = TranslateStringMethodCall("Contains", "100%", "string");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Is.EqualTo("\"Name\" LIKE '%' || @p0 || '%' ESCAPE '\\'"));
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
    }

    [Test]
    public void Contains_CapturedVariable_UsesParameterWithConcat()
    {
        var clause = TranslateCapturedStringMethodCall("Contains");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("LIKE"));
        Assert.That(clause.SqlFragment, Does.Contain("@p0"));
        Assert.That(clause.SqlFragment, Does.Contain("'%'"));
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
        Assert.That(clause.Parameters[0].IsCaptured, Is.True);
    }

    #endregion

    #region String StartsWith Tests

    [Test]
    public void StartsWith_LiteralString_ParameterizesLikePattern()
    {
        var clause = TranslateStringMethodCall("StartsWith", "User0", "string");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Is.EqualTo("\"Name\" LIKE @p0 || '%'"));
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
        Assert.That(clause.Parameters[0].IsCaptured, Is.False);
    }

    [Test]
    public void StartsWith_CapturedVariable_UsesParameterWithConcat()
    {
        var clause = TranslateCapturedStringMethodCall("StartsWith");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("LIKE"));
        Assert.That(clause.SqlFragment, Does.Contain("@p0"));
        Assert.That(clause.SqlFragment, Does.Not.Contain("'%' ||"), "StartsWith should not have leading wildcard");
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
        Assert.That(clause.Parameters[0].IsCaptured, Is.True);
    }

    #endregion

    #region String EndsWith Tests

    [Test]
    public void EndsWith_LiteralString_ParameterizesLikePattern()
    {
        var clause = TranslateStringMethodCall("EndsWith", "son", "string");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Is.EqualTo("\"Name\" LIKE '%' || @p0"));
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
        Assert.That(clause.Parameters[0].IsCaptured, Is.False);
    }

    [Test]
    public void EndsWith_CapturedVariable_UsesParameterWithConcat()
    {
        var clause = TranslateCapturedStringMethodCall("EndsWith");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("LIKE"));
        Assert.That(clause.SqlFragment, Does.Contain("@p0"));
        Assert.That(clause.SqlFragment, Does.Not.Contain("|| '%'"), "EndsWith should not have trailing wildcard");
        Assert.That(clause.Parameters, Has.Count.EqualTo(1));
        Assert.That(clause.Parameters[0].IsCaptured, Is.True);
    }

    #endregion

    #region Dialect-Specific Contains Tests

    [Test]
    public void Contains_CapturedVariable_MySQL_UsesCONCAT()
    {
        var clause = TranslateCapturedStringMethodCall("Contains", GenSqlDialect.MySQL);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("CONCAT("));
    }

    [Test]
    public void Contains_CapturedVariable_SqlServer_UsesPlus()
    {
        var clause = TranslateCapturedStringMethodCall("Contains", GenSqlDialect.SqlServer);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("'%' + @p0 + '%'"));
    }

    [Test]
    public void Contains_CapturedVariable_SQLite_UsesPipe()
    {
        var clause = TranslateCapturedStringMethodCall("Contains", GenSqlDialect.SQLite);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("'%' || @p0 || '%'"));
    }

    #endregion

    #region Helper Methods

    private static EntityInfo CreateTestEntity()
    {
        var columns = new List<ColumnInfo>
        {
            new("Id", "Id", "int", "System.Int32", false, ColumnKind.PrimaryKey, null, new ColumnModifiers()),
            new("Name", "Name", "string", "System.String", false, ColumnKind.Standard, null, new ColumnModifiers(maxLength: 100)),
        };

        return new EntityInfo(
            "TestEntity",
            "TestEntitySchema",
            "TestApp",
            "test_entities",
            NamingStyleKind.Exact,
            columns,
            new List<NavigationInfo>(),
            Array.Empty<IndexInfo>(),
            Location.None);
    }

    private static ClauseInfo TranslateStringMethodCall(
        string methodName,
        string literalValue,
        string literalType,
        GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var entity = CreateTestEntity();
        var target = new SyntacticPropertyAccess("u", "Name");
        var arg = new SyntacticLiteral(literalValue, literalType);
        var methodCall = new SyntacticMethodCall(target, methodName, new[] { arg });
        var pending = new PendingClauseInfo(ClauseKind.Where, "u", methodCall);

        var translator = new SyntacticClauseTranslator(entity, dialect);
        return translator.Translate(pending);
    }

    private static ClauseInfo TranslateCapturedStringMethodCall(
        string methodName,
        GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var entity = CreateTestEntity();
        var target = new SyntacticPropertyAccess("u", "Name");
        var arg = new SyntacticCapturedVariable("searchTerm", "searchTerm", "Body.Arguments[0]");
        var methodCall = new SyntacticMethodCall(target, methodName, new[] { arg });
        var pending = new PendingClauseInfo(ClauseKind.Where, "u", methodCall);

        var translator = new SyntacticClauseTranslator(entity, dialect);
        return translator.Translate(pending);
    }

    #endregion
}
