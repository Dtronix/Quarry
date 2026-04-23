using Quarry.Generators.Models;
using Quarry.Generators.Parsing;
using Quarry.Shared.Migration;
using Quarry.Shared.Sql;

namespace Quarry.Tests;

/// <summary>
/// Unit tests for SQL dialect implementations.
/// Uses parameterized tests to verify behavior across all supported databases.
/// </summary>
public class DialectTests
{
    #region Identifier Quoting Tests

    [TestCase(SqlDialect.SQLite, "users", "\"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "users", "\"users\"")]
    [TestCase(SqlDialect.MySQL, "users", "`users`")]
    [TestCase(SqlDialect.SqlServer, "users", "[users]")]
    [TestCase(SqlDialect.SQLite, "user_name", "\"user_name\"")]
    [TestCase(SqlDialect.PostgreSQL, "user_name", "\"user_name\"")]
    [TestCase(SqlDialect.MySQL, "user_name", "`user_name`")]
    [TestCase(SqlDialect.SqlServer, "user_name", "[user_name]")]
    public void QuoteIdentifier_QuotesCorrectly(SqlDialect dialectType, string identifier, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.QuoteIdentifier(dialect, identifier);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "user\"name", "\"user\"\"name\"")]
    [TestCase(SqlDialect.PostgreSQL, "user\"name", "\"user\"\"name\"")]
    [TestCase(SqlDialect.MySQL, "user`name", "`user``name`")]
    [TestCase(SqlDialect.SqlServer, "user]name", "[user]]name]")]
    public void QuoteIdentifier_EscapesQuoteCharacters(SqlDialect dialectType, string identifier, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.QuoteIdentifier(dialect, identifier);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void QuoteIdentifier_ThrowsOnNull(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        Assert.Throws<ArgumentNullException>(() => SqlFormatting.QuoteIdentifier(dialect, null!));
    }

    #endregion

    #region Table Name Formatting Tests

    [TestCase(SqlDialect.SQLite, "users", null, "\"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "users", null, "\"users\"")]
    [TestCase(SqlDialect.MySQL, "users", null, "`users`")]
    [TestCase(SqlDialect.SqlServer, "users", null, "[users]")]
    public void FormatTableName_WithoutSchema_ReturnsQuotedTable(SqlDialect dialectType, string table, string? schema, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatTableName(dialect, table, schema);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "users", "main", "\"main\".\"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "users", "public", "\"public\".\"users\"")]
    [TestCase(SqlDialect.MySQL, "users", "mydb", "`mydb`.`users`")]
    [TestCase(SqlDialect.SqlServer, "users", "dbo", "[dbo].[users]")]
    public void FormatTableName_WithSchema_ReturnsQualifiedName(SqlDialect dialectType, string table, string schema, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatTableName(dialect, table, schema);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "users", "", "\"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "users", "", "\"users\"")]
    [TestCase(SqlDialect.MySQL, "users", "", "`users`")]
    [TestCase(SqlDialect.SqlServer, "users", "", "[users]")]
    public void FormatTableName_WithEmptySchema_ReturnsUnqualifiedName(SqlDialect dialectType, string table, string schema, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatTableName(dialect, table, schema);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Parameter Formatting Tests

    [TestCase(SqlDialect.SQLite, 0, "@p0")]
    [TestCase(SqlDialect.SQLite, 1, "@p1")]
    [TestCase(SqlDialect.SQLite, 10, "@p10")]
    [TestCase(SqlDialect.PostgreSQL, 0, "$1")]
    [TestCase(SqlDialect.PostgreSQL, 1, "$2")]
    [TestCase(SqlDialect.PostgreSQL, 10, "$11")]
    [TestCase(SqlDialect.MySQL, 0, "?")]
    [TestCase(SqlDialect.MySQL, 1, "?")]
    [TestCase(SqlDialect.MySQL, 10, "?")]
    [TestCase(SqlDialect.SqlServer, 0, "@p0")]
    [TestCase(SqlDialect.SqlServer, 1, "@p1")]
    [TestCase(SqlDialect.SqlServer, 10, "@p10")]
    public void FormatParameter_ReturnsCorrectSyntax(SqlDialect dialectType, int index, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatParameter(dialect, index);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, 0, "@p0")]
    [TestCase(SqlDialect.SQLite, 5, "@p5")]
    [TestCase(SqlDialect.PostgreSQL, 0, "$1")]
    [TestCase(SqlDialect.PostgreSQL, 5, "$6")]
    [TestCase(SqlDialect.MySQL, 0, "@p0")]
    [TestCase(SqlDialect.MySQL, 5, "@p5")]
    [TestCase(SqlDialect.SqlServer, 0, "@p0")]
    [TestCase(SqlDialect.SqlServer, 5, "@p5")]
    public void GetParameterName_ReturnsNameForDbParameter(SqlDialect dialectType, int index, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.GetParameterName(dialect, index);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void GetParameterName_MatchesFormatParameter_ForNamedDialects(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        for (int i = 0; i < 10; i++)
        {
            Assert.That(
                SqlFormatting.GetParameterName(dialect, i),
                Is.EqualTo(SqlFormatting.FormatParameter(dialect, i)),
                $"ParameterName must equal the SQL placeholder for named-binding dialects (index {i})");
        }
    }

    [Test]
    public void GetParameterName_IsUniquePerIndex_ForMySql()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var names = Enumerable.Range(0, 10)
                              .Select(i => SqlFormatting.GetParameterName(dialect, i))
                              .ToArray();
        Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length));
    }

    #endregion

    #region Boolean Literal Tests

    [TestCase(SqlDialect.SQLite, true, "1")]
    [TestCase(SqlDialect.SQLite, false, "0")]
    [TestCase(SqlDialect.PostgreSQL, true, "TRUE")]
    [TestCase(SqlDialect.PostgreSQL, false, "FALSE")]
    [TestCase(SqlDialect.MySQL, true, "1")]
    [TestCase(SqlDialect.MySQL, false, "0")]
    [TestCase(SqlDialect.SqlServer, true, "1")]
    [TestCase(SqlDialect.SqlServer, false, "0")]
    public void FormatBoolean_ReturnsCorrectLiteral(SqlDialect dialectType, bool value, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatBoolean(dialect, value);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Pagination Tests

    [TestCase(SqlDialect.SQLite, 10, null, "LIMIT 10")]
    [TestCase(SqlDialect.PostgreSQL, 10, null, "LIMIT 10")]
    [TestCase(SqlDialect.MySQL, 10, null, "LIMIT 10")]
    [TestCase(SqlDialect.SqlServer, 10, null, "OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY")]
    public void FormatPagination_LimitOnly_ReturnsCorrectSyntax(SqlDialect dialectType, int limit, int? offset, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatPagination(dialect, limit, offset);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, null, 20, "OFFSET 20")]
    [TestCase(SqlDialect.PostgreSQL, null, 20, "OFFSET 20")]
    [TestCase(SqlDialect.MySQL, null, 20, "OFFSET 20")]
    [TestCase(SqlDialect.SqlServer, null, 20, "OFFSET 20 ROWS")]
    public void FormatPagination_OffsetOnly_ReturnsCorrectSyntax(SqlDialect dialectType, int? limit, int offset, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatPagination(dialect, limit, offset);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, 10, 20, "LIMIT 10 OFFSET 20")]
    [TestCase(SqlDialect.PostgreSQL, 10, 20, "LIMIT 10 OFFSET 20")]
    [TestCase(SqlDialect.MySQL, 10, 20, "LIMIT 10 OFFSET 20")]
    [TestCase(SqlDialect.SqlServer, 10, 20, "OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY")]
    public void FormatPagination_LimitAndOffset_ReturnsCorrectSyntax(SqlDialect dialectType, int limit, int offset, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatPagination(dialect, limit, offset);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void FormatPagination_NoLimitOrOffset_ReturnsEmpty(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatPagination(dialect, null, null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [TestCase(SqlDialect.SQLite, 10, 0, "LIMIT 10")]
    [TestCase(SqlDialect.PostgreSQL, 10, 0, "LIMIT 10")]
    [TestCase(SqlDialect.MySQL, 10, 0, "LIMIT 10")]
    [TestCase(SqlDialect.SqlServer, 10, 0, "OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY")]
    public void FormatPagination_ZeroOffset_HandlesCorrectly(SqlDialect dialectType, int limit, int offset, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatPagination(dialect, limit, offset);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Mixed Pagination Tests

    [TestCase(SqlDialect.SQLite, 20, null, null, 0, "LIMIT 20 OFFSET @p0")]
    [TestCase(SqlDialect.PostgreSQL, 20, null, null, 0, "LIMIT 20 OFFSET $1")]
    [TestCase(SqlDialect.MySQL, 20, null, null, 0, "LIMIT 20 OFFSET ?")]
    [TestCase(SqlDialect.SqlServer, 20, null, null, 0, "OFFSET @p0 ROWS FETCH NEXT 20 ROWS ONLY")]
    public void FormatMixedPagination_LiteralLimit_ParamOffset(SqlDialect dialectType, int literalLimit, int? literalOffset, int? limitIdx, int offsetIdx, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatMixedPagination(dialect, literalLimit, limitIdx, literalOffset, offsetIdx);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, null, 5, 0, null, "LIMIT @p0 OFFSET 5")]
    [TestCase(SqlDialect.PostgreSQL, null, 5, 0, null, "LIMIT $1 OFFSET 5")]
    [TestCase(SqlDialect.MySQL, null, 5, 0, null, "LIMIT ? OFFSET 5")]
    [TestCase(SqlDialect.SqlServer, null, 5, 0, null, "OFFSET 5 ROWS FETCH NEXT @p0 ROWS ONLY")]
    public void FormatMixedPagination_ParamLimit_LiteralOffset(SqlDialect dialectType, int? literalLimit, int literalOffset, int limitIdx, int? offsetIdx, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatMixedPagination(dialect, literalLimit, limitIdx, literalOffset, offsetIdx);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, 0, 1, "LIMIT @p0 OFFSET @p1")]
    [TestCase(SqlDialect.PostgreSQL, 0, 1, "LIMIT $1 OFFSET $2")]
    [TestCase(SqlDialect.MySQL, 0, 1, "LIMIT ? OFFSET ?")]
    [TestCase(SqlDialect.SqlServer, 0, 1, "OFFSET @p1 ROWS FETCH NEXT @p0 ROWS ONLY")]
    public void FormatMixedPagination_BothParameterized(SqlDialect dialectType, int limitIdx, int offsetIdx, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatMixedPagination(dialect, null, limitIdx, null, offsetIdx);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void FormatMixedPagination_AllNull_ReturnsEmpty(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatMixedPagination(dialect, null, null, null, null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [TestCase(SqlDialect.SQLite, 0, "OFFSET @p0")]
    [TestCase(SqlDialect.PostgreSQL, 0, "OFFSET $1")]
    [TestCase(SqlDialect.MySQL, 0, "OFFSET ?")]
    [TestCase(SqlDialect.SqlServer, 0, "OFFSET @p0 ROWS")]
    public void FormatMixedPagination_ParamOffsetOnly(SqlDialect dialectType, int offsetIdx, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatMixedPagination(dialect, null, null, null, offsetIdx);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, 0, "LIMIT @p0")]
    [TestCase(SqlDialect.PostgreSQL, 0, "LIMIT $1")]
    [TestCase(SqlDialect.MySQL, 0, "LIMIT ?")]
    [TestCase(SqlDialect.SqlServer, 0, "OFFSET 0 ROWS FETCH NEXT @p0 ROWS ONLY")]
    public void FormatMixedPagination_ParamLimitOnly(SqlDialect dialectType, int limitIdx, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatMixedPagination(dialect, null, limitIdx, null, null);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Identity/Returning Clause Tests

    [TestCase(SqlDialect.SQLite, "id", "RETURNING \"id\"")]
    [TestCase(SqlDialect.PostgreSQL, "id", "RETURNING \"id\"")]
    [TestCase(SqlDialect.MySQL, "id", null)]
    [TestCase(SqlDialect.SqlServer, "id", "OUTPUT INSERTED.[id]")]
    public void FormatReturningClause_ReturnsCorrectSyntax(SqlDialect dialectType, string column, string? expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatReturningClause(dialect, column);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, null)]
    [TestCase(SqlDialect.PostgreSQL, null)]
    [TestCase(SqlDialect.MySQL, "SELECT LAST_INSERT_ID()")]
    [TestCase(SqlDialect.SqlServer, null)]
    public void GetLastInsertIdQuery_ReturnsCorrectQuery(SqlDialect dialectType, string? expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.GetLastInsertIdQuery(dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region String Concatenation Tests

    [TestCase(SqlDialect.SQLite, new[] { "a", "b" }, "a || b")]
    [TestCase(SqlDialect.PostgreSQL, new[] { "a", "b" }, "a || b")]
    [TestCase(SqlDialect.MySQL, new[] { "a", "b" }, "CONCAT(a, b)")]
    [TestCase(SqlDialect.SqlServer, new[] { "a", "b" }, "a + b")]
    public void FormatStringConcat_TwoOperands_ReturnsCorrectSyntax(SqlDialect dialectType, string[] operands, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatStringConcat(dialect, operands);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, new[] { "a", "b", "c" }, "a || b || c")]
    [TestCase(SqlDialect.PostgreSQL, new[] { "a", "b", "c" }, "a || b || c")]
    [TestCase(SqlDialect.MySQL, new[] { "a", "b", "c" }, "CONCAT(a, b, c)")]
    [TestCase(SqlDialect.SqlServer, new[] { "a", "b", "c" }, "a + b + c")]
    public void FormatStringConcat_ThreeOperands_ReturnsCorrectSyntax(SqlDialect dialectType, string[] operands, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatStringConcat(dialect, operands);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, new[] { "a" }, "a")]
    [TestCase(SqlDialect.PostgreSQL, new[] { "a" }, "a")]
    [TestCase(SqlDialect.MySQL, new[] { "a" }, "a")]
    [TestCase(SqlDialect.SqlServer, new[] { "a" }, "a")]
    public void FormatStringConcat_SingleOperand_ReturnsOperand(SqlDialect dialectType, string[] operands, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatStringConcat(dialect, operands);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void FormatStringConcat_EmptyArray_ReturnsEmpty(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.FormatStringConcat(dialect, []);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    #endregion

    #region Identity Syntax Tests

    [TestCase(SqlDialect.SQLite, "AUTOINCREMENT")]
    [TestCase(SqlDialect.PostgreSQL, "GENERATED ALWAYS AS IDENTITY")]
    [TestCase(SqlDialect.MySQL, "AUTO_INCREMENT")]
    [TestCase(SqlDialect.SqlServer, "IDENTITY(1,1)")]
    public void GetIdentitySyntax_ReturnsCorrectSyntax(SqlDialect dialectType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlFormatting.GetIdentitySyntax(dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Factory Tests

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void SqlDialectFactory_GetDialect_ReturnsCorrectValue(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        Assert.That(dialect, Is.EqualTo(dialectType));
    }

    [Test]
    public void SqlDialectFactory_StaticProperties_ReturnCorrectValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SqlDialectFactory.SQLite, Is.EqualTo(SqlDialect.SQLite));
            Assert.That(SqlDialectFactory.PostgreSQL, Is.EqualTo(SqlDialect.PostgreSQL));
            Assert.That(SqlDialectFactory.MySQL, Is.EqualTo(SqlDialect.MySQL));
            Assert.That(SqlDialectFactory.SqlServer, Is.EqualTo(SqlDialect.SqlServer));
        });
    }

    #endregion

    #region Dialect Property Tests

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void Dialect_ReturnsCorrectEnumValue(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        Assert.That(dialect, Is.EqualTo(dialectType));
    }

    [TestCase(SqlDialect.SQLite, '"', '"')]
    [TestCase(SqlDialect.PostgreSQL, '"', '"')]
    [TestCase(SqlDialect.MySQL, '`', '`')]
    [TestCase(SqlDialect.SqlServer, '[', ']')]
    public void IdentifierQuoteCharacters_AreCorrect(SqlDialect dialectType, char expectedStart, char expectedEnd)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var quotes = SqlFormatting.GetIdentifierQuoteChars(dialect);
        Assert.Multiple(() =>
        {
            Assert.That(quotes.Start, Is.EqualTo(expectedStart));
            Assert.That(quotes.End, Is.EqualTo(expectedEnd));
        });
    }

    #endregion

    #region QuoteSqlExpression Tests

    [Test]
    public void QuoteSqlExpression_NullInput_ReturnsNull()
    {
        Assert.That(SqlFormatting.QuoteSqlExpression(null, SqlDialect.SQLite), Is.Null);
    }

    [TestCase(SqlDialect.SQLite, "COUNT(*)", "COUNT(*)")]
    [TestCase(SqlDialect.MySQL, "COUNT(*)", "COUNT(*)")]
    [TestCase(SqlDialect.SqlServer, "COUNT(*)", "COUNT(*)")]
    public void QuoteSqlExpression_NoPlaceholders_PassesThrough(SqlDialect dialect, string input, string expected)
    {
        Assert.That(SqlFormatting.QuoteSqlExpression(input, dialect), Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "SUM({Total})", "SUM(\"Total\")")]
    [TestCase(SqlDialect.PostgreSQL, "SUM({Total})", "SUM(\"Total\")")]
    [TestCase(SqlDialect.MySQL, "SUM({Total})", "SUM(`Total`)")]
    [TestCase(SqlDialect.SqlServer, "SUM({Total})", "SUM([Total])")]
    public void QuoteSqlExpression_SinglePlaceholder_QuotesCorrectly(SqlDialect dialect, string input, string expected)
    {
        Assert.That(SqlFormatting.QuoteSqlExpression(input, dialect), Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "{t0}.{Amount}", "\"t0\".\"Amount\"")]
    [TestCase(SqlDialect.MySQL, "{t0}.{Amount}", "`t0`.`Amount`")]
    [TestCase(SqlDialect.SqlServer, "{t0}.{Amount}", "[t0].[Amount]")]
    public void QuoteSqlExpression_MultiplePlaceholders_QuotesAll(SqlDialect dialect, string input, string expected)
    {
        Assert.That(SqlFormatting.QuoteSqlExpression(input, dialect), Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "SUM({Total}) OVER (ORDER BY {Date})", "SUM(\"Total\") OVER (ORDER BY \"Date\")")]
    [TestCase(SqlDialect.PostgreSQL, "SUM({Total}) OVER (ORDER BY {Date})", "SUM(\"Total\") OVER (ORDER BY \"Date\")")]
    [TestCase(SqlDialect.MySQL, "SUM({Total}) OVER (ORDER BY {Date})", "SUM(`Total`) OVER (ORDER BY `Date`)")]
    [TestCase(SqlDialect.SqlServer, "SUM({Total}) OVER (ORDER BY {Date})", "SUM([Total]) OVER (ORDER BY [Date])")]
    public void QuoteSqlExpression_OverClause_QuotesAllPlaceholders(SqlDialect dialect, string input, string expected)
    {
        Assert.That(SqlFormatting.QuoteSqlExpression(input, dialect), Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "SUM(\"Total\")", "SUM(\"Total\")")]
    [TestCase(SqlDialect.MySQL, "SUM(\"Total\")", "SUM(\"Total\")")]
    public void QuoteSqlExpression_LegacyQuotedIdentifiers_PassesThrough(SqlDialect dialect, string input, string expected)
    {
        // Expressions without {placeholders} should pass through unchanged
        Assert.That(SqlFormatting.QuoteSqlExpression(input, dialect), Is.EqualTo(expected));
    }

    #endregion

    #region ExtractColumnNameFromAggregateSql Tests (via TryResolveAggregateTypeFromSql)

    private static Dictionary<string, ColumnInfo> MakeColumnLookup(string propertyName, string clrType)
    {
        var col = new ColumnInfo(propertyName, propertyName, clrType, clrType,
            isNullable: false, kind: ColumnKind.Standard, referencedEntityName: null,
            modifiers: default!, isValueType: true, readerMethodName: "GetDecimal");
        return new Dictionary<string, ColumnInfo> { [propertyName] = col };
    }

    [TestCase("SUM({Total})", "Total", "decimal")]
    [TestCase("MIN({Total})", "Total", "decimal")]
    [TestCase("AVG({Total})", "Total", "decimal")]
    [TestCase("MAX({Total})", "Total", "decimal")]
    public void ExtractColumnName_SimpleAggregate_ResolvesType(string sqlExpr, string columnName, string expectedType)
    {
        var lookup = MakeColumnLookup(columnName, expectedType);
        var result = ChainAnalyzer.TryResolveAggregateTypeFromSqlPublic(sqlExpr, lookup);
        Assert.That(result, Is.EqualTo(expectedType));
    }

    [TestCase("MIN({t0}.{Total})", "Total", "decimal")]
    public void ExtractColumnName_QualifiedAggregate_ResolvesType(string sqlExpr, string columnName, string expectedType)
    {
        var lookup = MakeColumnLookup(columnName, expectedType);
        var result = ChainAnalyzer.TryResolveAggregateTypeFromSqlPublic(sqlExpr, lookup);
        Assert.That(result, Is.EqualTo(expectedType));
    }

    [Test]
    public void ExtractColumnName_CountStar_ReturnsNull()
    {
        var lookup = MakeColumnLookup("Id", "int");
        var result = ChainAnalyzer.TryResolveAggregateTypeFromSqlPublic("COUNT(*)", lookup);
        Assert.That(result, Is.Null);
    }

    [TestCase("LAG({Total}) OVER (ORDER BY {Date})", "Total", "decimal")]
    public void ExtractColumnName_WindowFunction_ExtractsFromFunctionArgs(string sqlExpr, string columnName, string expectedType)
    {
        var lookup = MakeColumnLookup(columnName, expectedType);
        var result = ChainAnalyzer.TryResolveAggregateTypeFromSqlPublic(sqlExpr, lookup);
        Assert.That(result, Is.EqualTo(expectedType));
    }

    #endregion
}
