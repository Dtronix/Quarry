using System.Collections.Generic;
using NUnit.Framework;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for <see cref="CompileTimeSqlBuilder"/> and <see cref="SqlFragmentTemplate"/>.
/// Verifies that compile-time SQL building produces correct, dialect-specific SQL
/// for all tier 1 dispatch variants.
/// </summary>
[TestFixture]
public class CompileTimeSqlBuilderTests
{
    // ───────────────────────────────────────────────────────────────
    // Dialect formatting helpers
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void QuoteIdentifier_PerDialect()
    {
        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.SQLite, "Users"), Is.EqualTo("\"Users\""));
        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.PostgreSQL, "Users"), Is.EqualTo("\"Users\""));
        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.MySQL, "Users"), Is.EqualTo("`Users`"));
        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.SqlServer, "Users"), Is.EqualTo("[Users]"));
    }

    [Test]
    public void FormatTableName_PerDialect()
    {
        Assert.That(CompileTimeSqlBuilder.FormatTableName(GenSqlDialect.SQLite, "Users", null), Is.EqualTo("\"Users\""));
        Assert.That(CompileTimeSqlBuilder.FormatTableName(GenSqlDialect.PostgreSQL, "Users", "dbo"), Is.EqualTo("\"dbo\".\"Users\""));
        Assert.That(CompileTimeSqlBuilder.FormatTableName(GenSqlDialect.MySQL, "Users", "mydb"), Is.EqualTo("`mydb`.`Users`"));
        Assert.That(CompileTimeSqlBuilder.FormatTableName(GenSqlDialect.SqlServer, "Users", "dbo"), Is.EqualTo("[dbo].[Users]"));
    }

    [Test]
    public void FormatParameter_PerDialect()
    {
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.SQLite, 0), Is.EqualTo("@p0"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.SQLite, 3), Is.EqualTo("@p3"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.PostgreSQL, 0), Is.EqualTo("$1"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.PostgreSQL, 2), Is.EqualTo("$3"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.MySQL, 0), Is.EqualTo("?"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.MySQL, 5), Is.EqualTo("?"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.SqlServer, 0), Is.EqualTo("@p0"));
        Assert.That(CompileTimeSqlBuilder.FormatParameter(GenSqlDialect.SqlServer, 1), Is.EqualTo("@p1"));
    }

    [Test]
    public void FormatReturningClause_PerDialect()
    {
        Assert.That(CompileTimeSqlBuilder.FormatReturningClause(GenSqlDialect.SQLite, "id"), Is.EqualTo("RETURNING \"id\""));
        Assert.That(CompileTimeSqlBuilder.FormatReturningClause(GenSqlDialect.PostgreSQL, "id"), Is.EqualTo("RETURNING \"id\""));
        Assert.That(CompileTimeSqlBuilder.FormatReturningClause(GenSqlDialect.SqlServer, "id"), Is.EqualTo("OUTPUT INSERTED.[id]"));
        Assert.That(CompileTimeSqlBuilder.FormatReturningClause(GenSqlDialect.MySQL, "id"), Is.Null);
    }

    [Test]
    public void FormatBoolean_PerDialect()
    {
        Assert.That(CompileTimeSqlBuilder.FormatBoolean(GenSqlDialect.PostgreSQL, true), Is.EqualTo("TRUE"));
        Assert.That(CompileTimeSqlBuilder.FormatBoolean(GenSqlDialect.PostgreSQL, false), Is.EqualTo("FALSE"));
        Assert.That(CompileTimeSqlBuilder.FormatBoolean(GenSqlDialect.SQLite, true), Is.EqualTo("1"));
        Assert.That(CompileTimeSqlBuilder.FormatBoolean(GenSqlDialect.SqlServer, false), Is.EqualTo("0"));
    }

    [Test]
    public void QuoteIdentifier_EscapesQuoteCharacters()
    {
        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.SQLite, "user\"name"),
            Is.EqualTo("\"user\"\"name\""));

        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.MySQL, "user`name"),
            Is.EqualTo("`user``name`"));

        Assert.That(CompileTimeSqlBuilder.QuoteIdentifier(GenSqlDialect.SqlServer, "user]name"),
            Is.EqualTo("[user]]name]"));
    }

    // ───────────────────────────────────────────────────────────────
    // SqlFragmentTemplate
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void SqlFragmentTemplate_Static_NoParameters()
    {
        var template = SqlFragmentTemplate.Static("\"Name\"");
        Assert.That(template.HasParameters, Is.False);
        Assert.That(template.ParameterCount, Is.Zero);
        Assert.That(template.Render(GenSqlDialect.SQLite, 0), Is.EqualTo("\"Name\""));
    }

    [Test]
    public void SqlFragmentTemplate_FromClauseInfo_SingleParameter()
    {
        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"Name\" = @p0",
            new[] { MakeParam(0, "@p0") });

        var template = SqlFragmentTemplate.FromClauseInfo(clauseInfo);

        Assert.That(template.HasParameters, Is.True);
        Assert.That(template.ParameterCount, Is.EqualTo(1));

        // Render with base 0 for SQLite
        Assert.That(template.Render(GenSqlDialect.SQLite, 0),
            Is.EqualTo("\"Name\" = @p0"));

        // Render with base 2 for SQLite — parameter shifts to @p2
        Assert.That(template.Render(GenSqlDialect.SQLite, 2),
            Is.EqualTo("\"Name\" = @p2"));

        // Render for PostgreSQL — uses $N format
        Assert.That(template.Render(GenSqlDialect.PostgreSQL, 0),
            Is.EqualTo("\"Name\" = $1"));

        Assert.That(template.Render(GenSqlDialect.PostgreSQL, 3),
            Is.EqualTo("\"Name\" = $4"));

        // Render for MySQL — always ?
        Assert.That(template.Render(GenSqlDialect.MySQL, 0),
            Is.EqualTo("\"Name\" = ?"));
    }

    [Test]
    public void SqlFragmentTemplate_FromClauseInfo_MultipleParameters()
    {
        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"Age\" BETWEEN @p0 AND @p1",
            new[] { MakeParam(0, "@p0"), MakeParam(1, "@p1") });

        var template = SqlFragmentTemplate.FromClauseInfo(clauseInfo);

        Assert.That(template.ParameterCount, Is.EqualTo(2));

        // Base 0 for SQLite
        Assert.That(template.Render(GenSqlDialect.SQLite, 0),
            Is.EqualTo("\"Age\" BETWEEN @p0 AND @p1"));

        // Base 3 — shifts to @p3, @p4
        Assert.That(template.Render(GenSqlDialect.SQLite, 3),
            Is.EqualTo("\"Age\" BETWEEN @p3 AND @p4"));

        // PostgreSQL with base 1 — $2, $3
        Assert.That(template.Render(GenSqlDialect.PostgreSQL, 1),
            Is.EqualTo("\"Age\" BETWEEN $2 AND $3"));
    }

    [Test]
    public void SqlFragmentTemplate_FromClauseInfo_NoParameters()
    {
        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"IsActive\" = 1",
            System.Array.Empty<ParameterInfo>());

        var template = SqlFragmentTemplate.FromClauseInfo(clauseInfo);

        Assert.That(template.HasParameters, Is.False);
        Assert.That(template.Render(GenSqlDialect.SQLite, 0),
            Is.EqualTo("\"IsActive\" = 1"));
    }

    [Test]
    public void SqlFragmentTemplate_DoesNotMatchLongerParameterName()
    {
        // @p1 must not match inside @p10
        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"A\" = @p1 AND \"B\" = @p10",
            new[] { MakeParam(1, "@p1"), MakeParam(10, "@p10") });

        var template = SqlFragmentTemplate.FromClauseInfo(clauseInfo);

        Assert.That(template.ParameterCount, Is.EqualTo(2));

        // Base 0: slots are clause-local 0 and 1
        Assert.That(template.Render(GenSqlDialect.SQLite, 0),
            Is.EqualTo("\"A\" = @p0 AND \"B\" = @p1"));

        // Base 5 for PostgreSQL
        Assert.That(template.Render(GenSqlDialect.PostgreSQL, 5),
            Is.EqualTo("\"A\" = $6 AND \"B\" = $7"));
    }

    // ───────────────────────────────────────────────────────────────
    // Pagination formatting
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void FormatParameterizedPagination_LimitOffset_SQLite()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.SQLite, limitParamIndex: 0, offsetParamIndex: 1);
        Assert.That(result, Is.EqualTo("LIMIT @p0 OFFSET @p1"));
    }

    [Test]
    public void FormatParameterizedPagination_LimitOnly_PostgreSQL()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.PostgreSQL, limitParamIndex: 2, offsetParamIndex: null);
        Assert.That(result, Is.EqualTo("LIMIT $3"));
    }

    [Test]
    public void FormatParameterizedPagination_OffsetOnly_MySQL()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.MySQL, limitParamIndex: null, offsetParamIndex: 0);
        Assert.That(result, Is.EqualTo("OFFSET ?"));
    }

    [Test]
    public void FormatParameterizedPagination_SqlServer_OffsetFetch()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.SqlServer, limitParamIndex: 1, offsetParamIndex: 0);
        Assert.That(result, Is.EqualTo("OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY"));
    }

    [Test]
    public void FormatParameterizedPagination_SqlServer_OffsetOnly()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.SqlServer, limitParamIndex: null, offsetParamIndex: 2);
        Assert.That(result, Is.EqualTo("OFFSET @p2 ROWS"));
    }

    [Test]
    public void FormatParameterizedPagination_SqlServer_LimitOnly_DefaultsOffset0()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.SqlServer, limitParamIndex: 0, offsetParamIndex: null);
        Assert.That(result, Is.EqualTo("OFFSET 0 ROWS FETCH NEXT @p0 ROWS ONLY"));
    }

    [Test]
    public void FormatParameterizedPagination_None_ReturnsEmpty()
    {
        var result = CompileTimeSqlBuilder.FormatParameterizedPagination(
            GenSqlDialect.SQLite, limitParamIndex: null, offsetParamIndex: null);
        Assert.That(result, Is.Empty);
    }

    // ───────────────────────────────────────────────────────────────
    // INSERT SQL building
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildInsertSql_SQLite_WithReturning()
    {
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            GenSqlDialect.SQLite, "Users", null,
            new[] { "\"Name\"", "\"Age\"" },
            parameterCount: 2,
            identityColumn: "Id");

        Assert.That(result.Sql,
            Is.EqualTo("INSERT INTO \"Users\" (\"Name\", \"Age\") VALUES (@p0, @p1) RETURNING \"Id\""));
        Assert.That(result.LastInsertIdQuery, Is.Null);
    }

    [Test]
    public void BuildInsertSql_PostgreSQL()
    {
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            GenSqlDialect.PostgreSQL, "Users", "public",
            new[] { "\"Name\"", "\"Age\"" },
            parameterCount: 2,
            identityColumn: "Id");

        Assert.That(result.Sql,
            Is.EqualTo("INSERT INTO \"public\".\"Users\" (\"Name\", \"Age\") VALUES ($1, $2) RETURNING \"Id\""));
    }

    [Test]
    public void BuildInsertSql_MySQL_WithLastInsertId()
    {
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            GenSqlDialect.MySQL, "Users", null,
            new[] { "`Name`", "`Age`" },
            parameterCount: 2,
            identityColumn: "Id");

        Assert.That(result.Sql,
            Is.EqualTo("INSERT INTO `Users` (`Name`, `Age`) VALUES (?, ?)"));
        Assert.That(result.LastInsertIdQuery, Is.EqualTo("SELECT LAST_INSERT_ID()"));
    }

    [Test]
    public void BuildInsertSql_SqlServer_WithOutput()
    {
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            GenSqlDialect.SqlServer, "Users", "dbo",
            new[] { "[Name]", "[Age]" },
            parameterCount: 2,
            identityColumn: "Id");

        Assert.That(result.Sql,
            Is.EqualTo("INSERT INTO [dbo].[Users] ([Name], [Age]) VALUES (@p0, @p1) OUTPUT INSERTED.[Id]"));
    }

    [Test]
    public void BuildInsertSql_NoIdentity()
    {
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            GenSqlDialect.SQLite, "Logs", null,
            new[] { "\"Message\"" },
            parameterCount: 1,
            identityColumn: null);

        Assert.That(result.Sql,
            Is.EqualTo("INSERT INTO \"Logs\" (\"Message\") VALUES (@p0)"));
        Assert.That(result.LastInsertIdQuery, Is.Null);
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT SQL building — non-branching (single mask)
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_SimpleSelectAll_NoConditions()
    {
        var clauses = new List<ChainedClauseSite>();
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\""));
        Assert.That(result.ParameterCount, Is.Zero);
    }

    [Test]
    public void BuildSelectSql_WithUnconditionalWhere_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Name\" = @p0"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_WithUnconditionalWhere_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Name\" = $1"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_MultipleUnconditionalWheres_JoinedWithAND()
    {
        // Matches runtime SqlBuilder: (cond1) AND (cond2)
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeWhereClause("\"Age\" > @p1", new[] { MakeParam(1, "@p1") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" WHERE (\"Name\" = @p0) AND (\"Age\" > @p1)"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT SQL building — conditional branches (dispatch table)
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_ConditionalWhere_ActiveBit_IncludesClause()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Bit 0 set → clause active
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"IsActive\" = @p0"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_ConditionalWhere_InactiveBit_ExcludesClause()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Bit 0 not set → clause excluded
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\""));
        Assert.That(result.ParameterCount, Is.Zero);
    }

    [Test]
    public void BuildSelectSql_TwoConditionalWheres_ParameterIndicesReindex()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause("\"Age\" > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Mask 0b00 — neither active → no WHERE, no params
        var r00 = CompileTimeSqlBuilder.BuildSelectSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r00.Sql, Is.EqualTo("SELECT * FROM \"Users\""));
        Assert.That(r00.ParameterCount, Is.EqualTo(0));

        // Mask 0b01 — only clause A → @p0
        var r01 = CompileTimeSqlBuilder.BuildSelectSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r01.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Name\" = @p0"));
        Assert.That(r01.ParameterCount, Is.EqualTo(1));

        // Mask 0b10 — only clause B → re-indexed to @p0
        var r10 = CompileTimeSqlBuilder.BuildSelectSql(2UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r10.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Age\" > @p0"));
        Assert.That(r10.ParameterCount, Is.EqualTo(1));

        // Mask 0b11 — both active → @p0, @p1
        var r11 = CompileTimeSqlBuilder.BuildSelectSql(3UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r11.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" WHERE (\"Name\" = @p0) AND (\"Age\" > @p1)"));
        Assert.That(r11.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildSelectSql_ConditionalWhere_PostgreSQL_CorrectDialect()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause("\"Age\" > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Only clause B active → parameter at $1 (PostgreSQL base 0 → $1)
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            2UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Age\" > $1"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_UnconditionalAndConditional_MixedMask()
    {
        // Unconditional WHERE + conditional WHERE at bit 0
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"IsActive\" = 1", System.Array.Empty<ParameterInfo>(), isConditional: false),
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Bit 0 not set → only unconditional WHERE
        var r0 = CompileTimeSqlBuilder.BuildSelectSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r0.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"IsActive\" = 1"));
        Assert.That(r0.ParameterCount, Is.Zero);

        // Bit 0 set → both WHEREs joined with AND
        var r1 = CompileTimeSqlBuilder.BuildSelectSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" WHERE (\"IsActive\" = 1) AND (\"Name\" = @p0)"));
        Assert.That(r1.ParameterCount, Is.EqualTo(1));
    }

    // ───────────────────────────────────────────────────────────────
    // BuildSelectSqlMap — full dispatch table
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSqlMap_ProducesAllVariants()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"A\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause("\"B\" = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1)
        };

        var possibleMasks = new List<ulong> { 0UL, 1UL, 2UL, 3UL };
        var map = CompileTimeSqlBuilder.BuildSelectSqlMap(
            possibleMasks, clauses, GenSqlDialect.SQLite, "T", null);

        Assert.That(map.Count, Is.EqualTo(4));
        Assert.That(map[0].Sql, Is.EqualTo("SELECT * FROM \"T\""));
        Assert.That(map[1].Sql, Is.EqualTo("SELECT * FROM \"T\" WHERE \"A\" = @p0"));
        Assert.That(map[2].Sql, Is.EqualTo("SELECT * FROM \"T\" WHERE \"B\" = @p0"));
        Assert.That(map[3].Sql, Is.EqualTo("SELECT * FROM \"T\" WHERE (\"A\" = @p0) AND (\"B\" = @p1)"));
    }

    // ───────────────────────────────────────────────────────────────
    // DELETE SQL building
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildDeleteSql_WithConditionalWhere()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "\"Id\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Active
        var r1 = CompileTimeSqlBuilder.BuildDeleteSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql, Is.EqualTo("DELETE FROM \"Users\" WHERE \"Id\" = @p0"));
        Assert.That(r1.ParameterCount, Is.EqualTo(1));

        // Inactive
        var r0 = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r0.Sql, Is.EqualTo("DELETE FROM \"Users\""));
        Assert.That(r0.ParameterCount, Is.Zero);
    }

    [Test]
    public void BuildDeleteSql_PostgreSQL_DialectParameters()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "\"Id\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.PostgreSQL, "Users", "public");
        Assert.That(result.Sql, Is.EqualTo("DELETE FROM \"public\".\"Users\" WHERE \"Id\" = $1"));
    }

    // ───────────────────────────────────────────────────────────────
    // UPDATE SQL building
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildUpdateSql_SetAndWhere_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("\"Name\"", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "\"Id\" = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE \"Users\" SET \"Name\" = @p0 WHERE \"Id\" = @p1"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildUpdateSql_PostgreSQL_DialectParameters()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("\"Name\"", 0, isConditional: false),
            MakeSetClause("\"Age\"", 1, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "\"Id\" = @p2", new[] { MakeParam(2, "@p2") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.PostgreSQL, "Users", null);
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE \"Users\" SET \"Name\" = $1, \"Age\" = $2 WHERE \"Id\" = $3"));
        Assert.That(result.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildUpdateSql_ConditionalSet_ParameterReindex()
    {
        // Conditional SET at bit 0, unconditional WHERE
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("\"Name\"", 0, isConditional: true, bitIndex: 0),
            MakeClause(ClauseRole.UpdateWhere,
                "\"Id\" = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // SET active (bit 0 set) — SET @p0, WHERE @p1
        var r1 = CompileTimeSqlBuilder.BuildUpdateSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql,
            Is.EqualTo("UPDATE \"Users\" SET \"Name\" = @p0 WHERE \"Id\" = @p1"));
        Assert.That(r1.ParameterCount, Is.EqualTo(2));

        // SET inactive (bit 0 not set) — no SET clause, WHERE gets @p0
        var r0 = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r0.Sql,
            Is.EqualTo("UPDATE \"Users\" WHERE \"Id\" = @p0"));
        Assert.That(r0.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildUpdateSql_ConditionalWhere_ParameterReindex()
    {
        // Unconditional SET + unconditional WHERE (no param) + conditional WHERE (1 param)
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("\"UserName\"", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "\"UserId\" = 1", Array.Empty<ParameterInfo>(),
                isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Conditional WHERE active — SET @p0, WHERE @p1
        var r1 = CompileTimeSqlBuilder.BuildUpdateSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql,
            Is.EqualTo("UPDATE \"Users\" SET \"UserName\" = @p0 WHERE (\"UserId\" = 1) AND (\"IsActive\" = @p1)"));
        Assert.That(r1.ParameterCount, Is.EqualTo(2));

        // Conditional WHERE inactive — SET @p0, WHERE no param
        var r0 = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r0.Sql,
            Is.EqualTo("UPDATE \"Users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1"));
        Assert.That(r0.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildUpdateSql_ConditionalWhere_SetWithoutParams_ParameterReindex()
    {
        // Regression: SET clause with 0 params in ClauseInfo (enrichment fallback loses
        // SetClauseInfo). The synthetic template in BuildTemplates must still account for
        // the SET parameter so conditional WHERE parameters are offset correctly.
        var setClauseInfo = ClauseInfo.Success(ClauseKind.Set, "\"UserName\"", Array.Empty<ParameterInfo>());
        var setSite = new UsageSiteInfo(
            methodName: "Set",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.ExecutableUpdateBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.UpdateSet,
            invocationSyntax: null!,
            uniqueId: "test_set",
            clauseInfo: setClauseInfo);

        var clauses = new List<ChainedClauseSite>
        {
            new ChainedClauseSite(setSite, isConditional: false, bitIndex: null, ClauseRole.UpdateSet),
            MakeClause(ClauseRole.UpdateWhere,
                "\"UserId\" = 1", Array.Empty<ParameterInfo>(),
                isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Conditional WHERE active — SET @p0 (from synthetic template), WHERE @p1
        var r1 = CompileTimeSqlBuilder.BuildUpdateSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql,
            Is.EqualTo("UPDATE \"Users\" SET \"UserName\" = @p0 WHERE (\"UserId\" = 1) AND (\"IsActive\" = @p1)"));
        Assert.That(r1.ParameterCount, Is.EqualTo(2));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with ORDER BY across all 4 dialects
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_OrderBy_Ascending_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("\"Name\"", isDescending: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" ORDER BY \"Name\" ASC"));
        Assert.That(result.ParameterCount, Is.Zero);
    }

    [Test]
    public void BuildSelectSql_OrderBy_Descending_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("\"CreatedAt\"", isDescending: true)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" ORDER BY \"CreatedAt\" DESC"));
        Assert.That(result.ParameterCount, Is.Zero);
    }

    [Test]
    public void BuildSelectSql_OrderBy_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("`Age`", isDescending: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM `Users` ORDER BY `Age` ASC"));
    }

    [Test]
    public void BuildSelectSql_OrderBy_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("[Name]", isDescending: true)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "dbo");

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM [dbo].[Users] ORDER BY [Name] DESC"));
    }

    [Test]
    public void BuildSelectSql_OrderByThenBy_AllDirections()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("\"LastName\"", isDescending: false),
            MakeThenByClause("\"FirstName\"", isDescending: true)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" ORDER BY \"LastName\" ASC, \"FirstName\" DESC"));
    }

    [Test]
    public void BuildSelectSql_ConditionalOrderBy_ActiveBit()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("\"Name\"", isDescending: false, isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Bit 0 set — ORDER BY present
        var r1 = CompileTimeSqlBuilder.BuildSelectSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql, Is.EqualTo("SELECT * FROM \"Users\" ORDER BY \"Name\" ASC"));

        // Bit 0 not set — no ORDER BY
        var r0 = CompileTimeSqlBuilder.BuildSelectSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r0.Sql, Is.EqualTo("SELECT * FROM \"Users\""));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with JOIN across all 4 dialects
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_InnerJoin_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeJoinClause(JoinClauseKind.Inner, "Orders", "\"Users\".\"Id\" = \"Orders\".\"UserId\"")
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" INNER JOIN \"Orders\" ON \"Users\".\"Id\" = \"Orders\".\"UserId\""));
    }

    [Test]
    public void BuildSelectSql_LeftJoin_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeJoinClause(JoinClauseKind.Left, "Orders", "\"Users\".\"Id\" = \"Orders\".\"UserId\"")
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" LEFT JOIN \"Orders\" ON \"Users\".\"Id\" = \"Orders\".\"UserId\""));
    }

    [Test]
    public void BuildSelectSql_RightJoin_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeJoinClause(JoinClauseKind.Right, "Orders", "`Users`.`Id` = `Orders`.`UserId`")
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM `Users` RIGHT JOIN `Orders` ON `Users`.`Id` = `Orders`.`UserId`"));
    }

    [Test]
    public void BuildSelectSql_InnerJoin_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeJoinClause(JoinClauseKind.Inner, "Orders", "[Users].[Id] = [Orders].[UserId]")
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "dbo");

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [dbo].[Users] INNER JOIN [Orders] ON [Users].[Id] = [Orders].[UserId]"));
    }

    [Test]
    public void BuildSelectSql_JoinWithParameters_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeJoinClause(JoinClauseKind.Inner, "Orders",
                "\"Users\".\"Id\" = \"Orders\".\"UserId\" AND \"Orders\".\"Status\" = @p0",
                new[] { MakeParam(0, "@p0") })
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" INNER JOIN \"Orders\" ON \"Users\".\"Id\" = \"Orders\".\"UserId\" AND \"Orders\".\"Status\" = $1"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with HAVING across all 4 dialects
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_Having_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.Having,
                "COUNT(*) > @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Orders\" HAVING COUNT(*) > @p0"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_Having_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.Having,
                "COUNT(*) > @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Orders\" HAVING COUNT(*) > $1"));
    }

    [Test]
    public void BuildSelectSql_Having_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.Having,
                "COUNT(*) > @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM `Orders` HAVING COUNT(*) > ?"));
    }

    [Test]
    public void BuildSelectSql_Having_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.Having,
                "COUNT(*) > @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Orders", "dbo");

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM [dbo].[Orders] HAVING COUNT(*) > @p0"));
    }

    [Test]
    public void BuildSelectSql_MultipleHavings_JoinedWithAND()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.Having,
                "COUNT(*) > @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false),
            MakeClause(ClauseRole.Having,
                "SUM(\"Total\") > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Orders", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Orders\" HAVING (COUNT(*) > @p0) AND (SUM(\"Total\") > @p1)"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with DISTINCT
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_Distinct_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause()
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT DISTINCT * FROM \"Users\""));
    }

    [Test]
    public void BuildSelectSql_Distinct_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause()
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", "public");

        Assert.That(result.Sql, Is.EqualTo("SELECT DISTINCT * FROM \"public\".\"Users\""));
    }

    [Test]
    public void BuildSelectSql_Distinct_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause()
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT DISTINCT * FROM `Users`"));
    }

    [Test]
    public void BuildSelectSql_Distinct_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause()
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "dbo");

        Assert.That(result.Sql, Is.EqualTo("SELECT DISTINCT * FROM [dbo].[Users]"));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with parameterized Limit/Offset across all 4 dialects
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_LimitOffset_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT @p0 OFFSET @p1"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildSelectSql_LimitOffset_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT $1 OFFSET $2"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildSelectSql_LimitOffset_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM `Users` LIMIT ? OFFSET ?"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildSelectSql_LimitOffset_SqlServer_OffsetFetch()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("[Name]", isDescending: false),
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "dbo");

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [dbo].[Users] ORDER BY [Name] ASC OFFSET @p1 ROWS FETCH NEXT @p0 ROWS ONLY"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildSelectSql_LimitOnly_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT @p0"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_LimitOnly_SqlServer_DefaultsOffset0()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("[Id]", isDescending: false),
            MakeLimitClause(0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [Users] ORDER BY [Id] ASC OFFSET 0 ROWS FETCH NEXT @p0 ROWS ONLY"));
    }

    [Test]
    public void BuildSelectSql_LimitOffset_SqlServer_NoOrderBy_InjectsSelectNull()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [Users] ORDER BY (SELECT NULL) OFFSET @p1 ROWS FETCH NEXT @p0 ROWS ONLY"));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with literal Limit/Offset (ToSql prebuilt chains)
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_LiteralLimitOffset_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null,
            literalLimit: 10, literalOffset: 20);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT 10 OFFSET 20"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralLimitOffset_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null,
            literalLimit: 10, literalOffset: 20);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT 10 OFFSET 20"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralLimitOffset_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null,
            literalLimit: 10, literalOffset: 20);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM `Users` LIMIT 10 OFFSET 20"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralLimitOffset_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("[Name]", isDescending: false),
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", null,
            literalLimit: 10, literalOffset: 20);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [Users] ORDER BY [Name] ASC OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralLimitOnly_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null,
            literalLimit: 50);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT 50"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralOffsetOnly_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOffsetClause(0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null,
            literalOffset: 30);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" OFFSET 30"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralOffsetOnly_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause("[Id]", isDescending: false),
            MakeOffsetClause(0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", null,
            literalOffset: 15);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [Users] ORDER BY [Id] ASC OFFSET 15 ROWS"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralLimitOffset_SqlServer_NoOrderBy_InjectsSelectNull()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(0),
            MakeOffsetClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", null,
            literalLimit: 10, literalOffset: 20);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [Users] ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"));
        Assert.That(result.ParameterCount, Is.EqualTo(0));
    }

    [Test]
    public void BuildSelectSql_LiteralLimit_WithWhereParams_CorrectParamCount()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeLimitClause(1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses, skipPaginationTemplates: true);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null,
            literalLimit: 10);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"IsActive\" = @p0 LIMIT 10"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSqlMap_LiteralLimitOffset_AllVariants()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeLimitClause(1)
        };

        var masks = new List<ulong> { 0UL, 1UL };
        var map = CompileTimeSqlBuilder.BuildSelectSqlMap(
            masks, clauses, GenSqlDialect.SQLite, "Users", null,
            literalLimit: 25);

        // Mask 0: WHERE inactive — only LIMIT
        Assert.That(map[0UL].Sql, Is.EqualTo("SELECT * FROM \"Users\" LIMIT 25"));
        Assert.That(map[0UL].ParameterCount, Is.EqualTo(0));

        // Mask 1: WHERE active — WHERE + LIMIT, @p0 for WHERE param only
        Assert.That(map[1UL].Sql, Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Name\" = @p0 LIMIT 25"));
        Assert.That(map[1UL].ParameterCount, Is.EqualTo(1));
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with WHERE across MySQL and SqlServer
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_WithUnconditionalWhere_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("`Name` = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null);

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM `Users` WHERE `Name` = ?"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_WithUnconditionalWhere_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("[Name] = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "dbo");

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM [dbo].[Users] WHERE [Name] = @p0"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_ConditionalWhere_MySQL_ParameterReindex()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("`Name` = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause("`Age` > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Both active — MySQL uses ? for all params
        var r11 = CompileTimeSqlBuilder.BuildSelectSql(3UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(r11.Sql,
            Is.EqualTo("SELECT * FROM `Users` WHERE (`Name` = ?) AND (`Age` > ?)"));
        Assert.That(r11.ParameterCount, Is.EqualTo(2));

        // Only clause B active
        var r10 = CompileTimeSqlBuilder.BuildSelectSql(2UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(r10.Sql, Is.EqualTo("SELECT * FROM `Users` WHERE `Age` > ?"));
        Assert.That(r10.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_ConditionalWhere_SqlServer_ParameterReindex()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("[Name] = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause("[Age] > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // Only clause B → re-indexed to @p0
        var r10 = CompileTimeSqlBuilder.BuildSelectSql(2UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(r10.Sql, Is.EqualTo("SELECT * FROM [dbo].[Users] WHERE [Age] > @p0"));
        Assert.That(r10.ParameterCount, Is.EqualTo(1));

        // Both active
        var r11 = CompileTimeSqlBuilder.BuildSelectSql(3UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(r11.Sql,
            Is.EqualTo("SELECT * FROM [dbo].[Users] WHERE ([Name] = @p0) AND ([Age] > @p1)"));
        Assert.That(r11.ParameterCount, Is.EqualTo(2));
    }

    // ───────────────────────────────────────────────────────────────
    // DELETE across MySQL and SqlServer
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildDeleteSql_MySQL_DialectParameters()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "`Id` = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(result.Sql, Is.EqualTo("DELETE FROM `Users` WHERE `Id` = ?"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildDeleteSql_SqlServer_DialectParameters()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "[Id] = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(result.Sql, Is.EqualTo("DELETE FROM [dbo].[Users] WHERE [Id] = @p0"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildDeleteSql_MySQL_ConditionalWhere()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "`Status` = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var r1 = CompileTimeSqlBuilder.BuildDeleteSql(1UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(r1.Sql, Is.EqualTo("DELETE FROM `Users` WHERE `Status` = ?"));

        var r0 = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(r0.Sql, Is.EqualTo("DELETE FROM `Users`"));
    }

    [Test]
    public void BuildDeleteSql_SqlServer_ConditionalWhere()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "[Status] = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var r1 = CompileTimeSqlBuilder.BuildDeleteSql(1UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(r1.Sql, Is.EqualTo("DELETE FROM [dbo].[Users] WHERE [Status] = @p0"));

        var r0 = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(r0.Sql, Is.EqualTo("DELETE FROM [dbo].[Users]"));
    }

    // ───────────────────────────────────────────────────────────────
    // UPDATE across MySQL and SqlServer
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildUpdateSql_SetAndWhere_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("`Name`", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "`Id` = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE `Users` SET `Name` = ? WHERE `Id` = ?"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildUpdateSql_SetAndWhere_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("[Name]", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "[Id] = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE [dbo].[Users] SET [Name] = @p0 WHERE [Id] = @p1"));
        Assert.That(result.ParameterCount, Is.EqualTo(2));
    }

    [Test]
    public void BuildUpdateSql_MultipleSets_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("`Name`", 0, isConditional: false),
            MakeSetClause("`Age`", 1, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "`Id` = @p2", new[] { MakeParam(2, "@p2") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.MySQL, "Users", null);
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE `Users` SET `Name` = ?, `Age` = ? WHERE `Id` = ?"));
        Assert.That(result.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildUpdateSql_ConditionalSet_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("[Name]", 0, isConditional: true, bitIndex: 0),
            MakeClause(ClauseRole.UpdateWhere,
                "[Id] = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // SET active
        var r1 = CompileTimeSqlBuilder.BuildUpdateSql(1UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(r1.Sql,
            Is.EqualTo("UPDATE [dbo].[Users] SET [Name] = @p0 WHERE [Id] = @p1"));
        Assert.That(r1.ParameterCount, Is.EqualTo(2));

        // SET inactive — WHERE gets @p0
        var r0 = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "dbo");
        Assert.That(r0.Sql,
            Is.EqualTo("UPDATE [dbo].[Users] WHERE [Id] = @p0"));
        Assert.That(r0.ParameterCount, Is.EqualTo(1));
    }

    // ───────────────────────────────────────────────────────────────
    // Complex multi-clause SELECT across all 4 dialects
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_WhereOrderByPagination_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause("\"Name\"", isDescending: false),
            MakeLimitClause(1),
            MakeOffsetClause(2)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" WHERE \"IsActive\" = @p0 ORDER BY \"Name\" ASC LIMIT @p1 OFFSET @p2"));
        Assert.That(result.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildSelectSql_WhereOrderByPagination_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"IsActive\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause("\"Name\"", isDescending: true),
            MakeLimitClause(1),
            MakeOffsetClause(2)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", "public");

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"public\".\"Users\" WHERE \"IsActive\" = $1 ORDER BY \"Name\" DESC LIMIT $2 OFFSET $3"));
        Assert.That(result.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildSelectSql_WhereOrderByPagination_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("`IsActive` = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause("`Name`", isDescending: false),
            MakeLimitClause(1),
            MakeOffsetClause(2)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM `Users` WHERE `IsActive` = ? ORDER BY `Name` ASC LIMIT ? OFFSET ?"));
        Assert.That(result.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildSelectSql_WhereOrderByPagination_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("[IsActive] = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause("[Name]", isDescending: false),
            MakeLimitClause(1),
            MakeOffsetClause(2)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "dbo");

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM [dbo].[Users] WHERE [IsActive] = @p0 ORDER BY [Name] ASC OFFSET @p2 ROWS FETCH NEXT @p1 ROWS ONLY"));
        Assert.That(result.ParameterCount, Is.EqualTo(3));
    }

    [Test]
    public void BuildSelectSql_JoinWhereOrderBy_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeJoinClause(JoinClauseKind.Inner, "Orders", "\"Users\".\"Id\" = \"Orders\".\"UserId\""),
            MakeWhereClause("\"Orders\".\"Total\" > @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause("\"Orders\".\"Total\"", isDescending: true)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" INNER JOIN \"Orders\" ON \"Users\".\"Id\" = \"Orders\".\"UserId\" WHERE \"Orders\".\"Total\" > $1 ORDER BY \"Orders\".\"Total\" DESC"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_DistinctWhereOrderBy_SQLite()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause(),
            MakeWhereClause("\"Age\" > @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause("\"Name\"", isDescending: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SQLite, "Users", null);

        Assert.That(result.Sql,
            Is.EqualTo("SELECT DISTINCT * FROM \"Users\" WHERE \"Age\" > @p0 ORDER BY \"Name\" ASC"));
        Assert.That(result.ParameterCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildSelectSql_ConditionalWhereAndOrderBy_ParameterReindex()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeOrderByClause("\"Age\"", isDescending: false),
            MakeLimitClause(1),
            MakeOffsetClause(2)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // WHERE active — @p0 for WHERE, @p1 for LIMIT, @p2 for OFFSET
        var r1 = CompileTimeSqlBuilder.BuildSelectSql(1UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r1.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" WHERE \"Name\" = @p0 ORDER BY \"Age\" ASC LIMIT @p1 OFFSET @p2"));
        Assert.That(r1.ParameterCount, Is.EqualTo(3));

        // WHERE inactive — LIMIT gets @p0, OFFSET gets @p1
        var r0 = CompileTimeSqlBuilder.BuildSelectSql(0UL, clauses, templates, GenSqlDialect.SQLite, "Users", null);
        Assert.That(r0.Sql,
            Is.EqualTo("SELECT * FROM \"Users\" ORDER BY \"Age\" ASC LIMIT @p0 OFFSET @p1"));
        Assert.That(r0.ParameterCount, Is.EqualTo(2));
    }

    // ───────────────────────────────────────────────────────────────
    // Schema-qualified operations across SqlServer/PostgreSQL/MySQL
    // ───────────────────────────────────────────────────────────────

    [Test]
    public void BuildSelectSql_SchemaQualified_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("[Name] = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.SqlServer, "Users", "hr");

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM [hr].[Users] WHERE [Name] = @p0"));
    }

    [Test]
    public void BuildSelectSql_SchemaQualified_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("\"Name\" = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.PostgreSQL, "Users", "hr");

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM \"hr\".\"Users\" WHERE \"Name\" = $1"));
    }

    [Test]
    public void BuildSelectSql_SchemaQualified_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("`Name` = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates,
            GenSqlDialect.MySQL, "Users", "mydb");

        Assert.That(result.Sql, Is.EqualTo("SELECT * FROM `mydb`.`Users` WHERE `Name` = ?"));
    }

    [Test]
    public void BuildUpdateSql_SchemaQualified_PostgreSQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("\"Name\"", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "\"Id\" = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.PostgreSQL, "Users", "hr");
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE \"hr\".\"Users\" SET \"Name\" = $1 WHERE \"Id\" = $2"));
    }

    [Test]
    public void BuildUpdateSql_SchemaQualified_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("`Name`", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "`Id` = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.MySQL, "Users", "mydb");
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE `mydb`.`Users` SET `Name` = ? WHERE `Id` = ?"));
    }

    [Test]
    public void BuildUpdateSql_SchemaQualified_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause("[Name]", 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere,
                "[Id] = @p1", new[] { MakeParam(1, "@p1") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildUpdateSql(0UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "hr");
        Assert.That(result.Sql,
            Is.EqualTo("UPDATE [hr].[Users] SET [Name] = @p0 WHERE [Id] = @p1"));
    }

    [Test]
    public void BuildDeleteSql_SchemaQualified_SqlServer()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "[Id] = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.SqlServer, "Users", "hr");
        Assert.That(result.Sql, Is.EqualTo("DELETE FROM [hr].[Users] WHERE [Id] = @p0"));
    }

    [Test]
    public void BuildDeleteSql_SchemaQualified_MySQL()
    {
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere,
                "`Id` = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(0UL, clauses, templates, GenSqlDialect.MySQL, "Users", "mydb");
        Assert.That(result.Sql, Is.EqualTo("DELETE FROM `mydb`.`Users` WHERE `Id` = ?"));
    }

    // ───────────────────────────────────────────────────────────────
    // Helper factories
    // ───────────────────────────────────────────────────────────────

    private static ParameterInfo MakeParam(int index, string name)
    {
        return new ParameterInfo(index, name, "string", "value");
    }

    private static ChainedClauseSite MakeWhereClause(
        string sqlFragment,
        IReadOnlyList<ParameterInfo> parameters,
        bool isConditional,
        int? bitIndex = null)
    {
        return MakeClause(ClauseRole.Where, sqlFragment, parameters, isConditional, bitIndex);
    }

    private static ChainedClauseSite MakeClause(
        ClauseRole role,
        string sqlFragment,
        IReadOnlyList<ParameterInfo> parameters,
        bool isConditional,
        int? bitIndex = null)
    {
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, sqlFragment, parameters);
        var site = new UsageSiteInfo(
            methodName: "Where",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: null!,
            uniqueId: "test",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, role);
    }

    private static ChainedClauseSite MakeSetClause(
        string columnSql,
        int parameterIndex,
        bool isConditional,
        int? bitIndex = null)
    {
        var parameters = new[] { MakeParam(parameterIndex, $"@p{parameterIndex}") };
        var clauseInfo = new SetClauseInfo(columnSql, parameterIndex, parameters);
        var site = new UsageSiteInfo(
            methodName: "Set",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.ExecutableUpdateBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.UpdateSet,
            invocationSyntax: null!,
            uniqueId: "test_set",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.UpdateSet);
    }

    private static ChainedClauseSite MakeOrderByClause(
        string columnSql,
        bool isDescending,
        bool isConditional = false,
        int? bitIndex = null)
    {
        var clauseInfo = new OrderByClauseInfo(columnSql, isDescending, System.Array.Empty<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "OrderBy",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.OrderBy,
            invocationSyntax: null!,
            uniqueId: "test_orderby",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.OrderBy);
    }

    private static ChainedClauseSite MakeThenByClause(
        string columnSql,
        bool isDescending)
    {
        var clauseInfo = new OrderByClauseInfo(columnSql, isDescending, System.Array.Empty<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "ThenBy",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.ThenBy,
            invocationSyntax: null!,
            uniqueId: "test_thenby",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, false, null, ClauseRole.ThenBy);
    }

    private static ChainedClauseSite MakeJoinClause(
        JoinClauseKind joinKind,
        string joinedTableName,
        string onConditionSql,
        IReadOnlyList<ParameterInfo>? parameters = null)
    {
        parameters ??= System.Array.Empty<ParameterInfo>();
        var clauseInfo = new JoinClauseInfo(joinKind, joinedTableName, joinedTableName, onConditionSql, parameters);
        var site = new UsageSiteInfo(
            methodName: "Join",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Join,
            invocationSyntax: null!,
            uniqueId: "test_join",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, false, null, ClauseRole.Join);
    }

    private static ChainedClauseSite MakeDistinctClause()
    {
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, "", System.Array.Empty<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "Distinct",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: null!,
            uniqueId: "test_distinct",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, false, null, ClauseRole.Distinct);
    }

    private static ChainedClauseSite MakeLimitClause(int parameterIndex)
    {
        var parameters = new[] { MakeParam(parameterIndex, $"@p{parameterIndex}") };
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, $"@p{parameterIndex}", parameters);
        var site = new UsageSiteInfo(
            methodName: "Limit",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: null!,
            uniqueId: "test_limit",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, false, null, ClauseRole.Limit);
    }

    private static ChainedClauseSite MakeOffsetClause(int parameterIndex)
    {
        var parameters = new[] { MakeParam(parameterIndex, $"@p{parameterIndex}") };
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, $"@p{parameterIndex}", parameters);
        var site = new UsageSiteInfo(
            methodName: "Offset",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: null!,
            uniqueId: "test_offset",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, false, null, ClauseRole.Offset);
    }
}
