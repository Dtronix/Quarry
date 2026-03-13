using System.Collections.Generic;
using NUnit.Framework;
using Quarry.Internal;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using RuntimeSqlDialect = Quarry.SqlDialect;
using RtSqlFormatting = Quarry.Shared.Sql.SqlFormatting;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Verifies that the compile-time SQL builder produces identical output to
/// the runtime SQL builder for the same queries across all 4 SQL dialects.
/// </summary>
[TestFixture]
public class CompileTimeRuntimeEquivalenceTests
{
    private static GenSqlDialect ToGen(RuntimeSqlDialect d) => (GenSqlDialect)(int)d;

    /// <summary>
    /// Quotes an identifier using the runtime dialect, returning a string usable
    /// in both the runtime WHERE fragment and the compile-time clause fragment.
    /// Both paths accept pre-quoted identifiers.
    /// </summary>
    private static string Q(RuntimeSqlDialect d, string id) => RtSqlFormatting.QuoteIdentifier(d, id);

    /// <summary>
    /// Formats a parameter placeholder using the canonical @pN format used by the
    /// compile-time path. The template engine remaps these to dialect-specific forms.
    /// </summary>
    private static string CanonicalParam(int idx) => $"@p{idx}";

    private static readonly RuntimeSqlDialect[] AllDialects =
    {
        RuntimeSqlDialect.SQLite,
        RuntimeSqlDialect.PostgreSQL,
        RuntimeSqlDialect.MySQL,
        RuntimeSqlDialect.SqlServer,
    };

    // ───────────────────────────────────────────────────────────────
    // SELECT — simple (no WHERE, no clauses)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectAll_Equivalence(RuntimeSqlDialect dialect)
    {
        var state = new QueryState(dialect, "Users", null);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>();
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, ToGen(dialect), "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"SELECT * equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with schema-qualified table
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectAll_WithSchema_Equivalence(RuntimeSqlDialect dialect)
    {
        var state = new QueryState(dialect, "Users", "dbo");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>();
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, ToGen(dialect), "Users", "dbo");

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Schema-qualified SELECT equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with single WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithSingleWhere_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        // Runtime: uses dialect-quoted identifier + dialect parameter
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        // Compile-time: uses dialect-quoted identifier + canonical @pN
        // The template engine remaps @p0 to the dialect-specific form
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false)
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Single WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with multiple WHERE (AND-joined)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithMultipleWheres_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);

        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}")
            .WithWhere($"{qAge} > {p1}");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Multiple WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with WHERE (no params) — static condition
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithStaticWhere_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qCol = Q(dialect, "IsActive");

        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qCol} = 1");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qCol} = 1", System.Array.Empty<ParameterInfo>(), isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Static WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with ORDER BY ASC
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithOrderByAsc_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qCol = Q(dialect, "Name");

        var state = new QueryState(dialect, "Users", null)
            .WithOrderBy(qCol, Direction.Ascending);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause(qCol, isDescending: false, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"ORDER BY ASC equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with ORDER BY DESC
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithOrderByDesc_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qCol = Q(dialect, "CreatedAt");

        var state = new QueryState(dialect, "Users", null)
            .WithOrderBy(qCol, Direction.Descending);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause(qCol, isDescending: true, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"ORDER BY DESC equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with WHERE + ORDER BY combined
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithWhereAndOrderBy_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}")
            .WithOrderBy(qAge, Direction.Ascending);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause(qAge, isDescending: false, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"WHERE + ORDER BY equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with DISTINCT
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithDistinct_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var state = new QueryState(dialect, "Users", null)
            .WithDistinct();
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause(),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"DISTINCT equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with multiple ORDER BY (OrderBy + ThenBy)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithMultipleOrderBy_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");

        var state = new QueryState(dialect, "Users", null)
            .WithOrderBy(qName, Direction.Ascending)
            .WithOrderBy(qAge, Direction.Descending);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause(qName, isDescending: false, isConditional: false),
            MakeThenByClause(qAge, isDescending: true, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Multiple ORDER BY equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with DISTINCT + WHERE + ORDER BY combined
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithDistinctWhereOrderBy_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var state = new QueryState(dialect, "Users", null)
            .WithDistinct()
            .WithWhere($"{qName} = {p0}")
            .WithOrderBy(qAge, Direction.Descending);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeDistinctClause(),
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeOrderByClause(qAge, isDescending: true, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"DISTINCT + WHERE + ORDER BY equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // DELETE without WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void DeleteWithoutWhere_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var deleteState = new DeleteState(dialect, "Users", null, null);
        var runtimeSql = SqlModificationBuilder.BuildDeleteSql(deleteState);

        var clauses = new List<ChainedClauseSite>();
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"DELETE without WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // DELETE with WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void DeleteWithWhere_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qId = Q(dialect, "Id");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var deleteState = new DeleteState(dialect, "Users", null, null);
        deleteState.WhereConditions.Add($"{qId} = {p0}");
        var runtimeSql = SqlModificationBuilder.BuildDeleteSql(deleteState);

        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere, $"{qId} = @p0",
                new[] { MakeParam(0, "@p0") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"DELETE WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // DELETE with schema + WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void DeleteWithSchema_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qId = Q(dialect, "Id");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var deleteState = new DeleteState(dialect, "Users", "dbo", null);
        deleteState.WhereConditions.Add($"{qId} = {p0}");
        var runtimeSql = SqlModificationBuilder.BuildDeleteSql(deleteState);

        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere, $"{qId} = @p0",
                new[] { MakeParam(0, "@p0") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, genDialect, "Users", "dbo");

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"DELETE with schema equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // DELETE with multiple WHERE conditions
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void DeleteWithMultipleWheres_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qId = Q(dialect, "Id");
        var qActive = Q(dialect, "IsActive");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        // Runtime: DELETE WHERE joins conditions with " AND " (string.Join)
        var deleteState = new DeleteState(dialect, "Users", null, null);
        deleteState.WhereConditions.Add($"{qId} = {p0}");
        deleteState.WhereConditions.Add($"{qActive} = 1");
        var runtimeSql = SqlModificationBuilder.BuildDeleteSql(deleteState);

        // Compile-time: also uses " AND " for multiple DELETE WHERE
        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere, $"{qId} = @p0",
                new[] { MakeParam(0, "@p0") }, isConditional: false),
            MakeClause(ClauseRole.DeleteWhere, $"{qActive} = 1",
                System.Array.Empty<ParameterInfo>(), isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"DELETE multiple WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // UPDATE with SET + WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void UpdateWithSetAndWhere_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qId = Q(dialect, "Id");
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);

        // Runtime
        var updateState = new UpdateState(dialect, "Users", null, null);
        updateState.SetClauses.Add(new SetClause(qName, 0));
        updateState.WhereConditions.Add($"{qId} = {p1}");
        var runtimeSql = SqlModificationBuilder.BuildUpdateSql(updateState);

        // Compile-time
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause(qName, 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere, $"{qId} = @p1",
                new[] { MakeParam(1, "@p1") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"UPDATE SET + WHERE equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // UPDATE with multiple SET + WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void UpdateWithMultipleSets_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var qId = Q(dialect, "Id");
        var p2 = RtSqlFormatting.FormatParameter(dialect, 2);

        // Runtime
        var updateState = new UpdateState(dialect, "Users", null, null);
        updateState.SetClauses.Add(new SetClause(qName, 0));
        updateState.SetClauses.Add(new SetClause(qAge, 1));
        updateState.WhereConditions.Add($"{qId} = {p2}");
        var runtimeSql = SqlModificationBuilder.BuildUpdateSql(updateState);

        // Compile-time
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause(qName, 0, isConditional: false),
            MakeSetClause(qAge, 1, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere, $"{qId} = @p2",
                new[] { MakeParam(2, "@p2") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            0UL, clauses, templates, genDialect, "Users", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"UPDATE multiple SET equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // UPDATE with schema
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void UpdateWithSchema_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qId = Q(dialect, "Id");
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);

        // Runtime
        var updateState = new UpdateState(dialect, "Users", "dbo", null);
        updateState.SetClauses.Add(new SetClause(qName, 0));
        updateState.WhereConditions.Add($"{qId} = {p1}");
        var runtimeSql = SqlModificationBuilder.BuildUpdateSql(updateState);

        // Compile-time
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause(qName, 0, isConditional: false),
            MakeClause(ClauseRole.UpdateWhere, $"{qId} = @p1",
                new[] { MakeParam(1, "@p1") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            0UL, clauses, templates, genDialect, "Users", "dbo");

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"UPDATE with schema equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // INSERT
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void Insert_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");

        // Runtime
        var insertState = new InsertState(dialect, "Users", null, null);
        insertState.Columns.Add(qName);
        insertState.Columns.Add(qAge);
        var runtimeSql = SqlModificationBuilder.BuildInsertSql(insertState, rowCount: 1);

        // Compile-time
        var columns = new List<string> { qName, qAge };
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            genDialect, "Users", null, columns, parameterCount: 2, identityColumn: null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"INSERT equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // INSERT with identity column
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void InsertWithIdentity_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");

        // Runtime
        var insertState = new InsertState(dialect, "Users", null, null);
        insertState.Columns.Add(qName);
        insertState.IdentityColumn = "Id";
        var runtimeSql = SqlModificationBuilder.BuildInsertSql(insertState, rowCount: 1);

        // Compile-time
        var columns = new List<string> { qName };
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            genDialect, "Users", null, columns, parameterCount: 1, identityColumn: "Id");

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"INSERT with identity equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // INSERT with schema
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void InsertWithSchema_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");

        // Runtime
        var insertState = new InsertState(dialect, "Users", "dbo", null);
        insertState.Columns.Add(qName);
        insertState.Columns.Add(qAge);
        var runtimeSql = SqlModificationBuilder.BuildInsertSql(insertState, rowCount: 1);

        // Compile-time
        var columns = new List<string> { qName, qAge };
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            genDialect, "Users", "dbo", columns, parameterCount: 2, identityColumn: null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"INSERT with schema equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT pagination — compile-time uses parameterized pagination
    // while runtime uses literal values. Verify the compile-time path
    // matches the parameterized pagination format.
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithLimitOffset_CompileTimeProducesParameterizedPagination(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(isConditional: false),
            MakeOffsetClause(isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        // Table portion should match
        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        Assert.That(result.Sql, Does.StartWith($"SELECT * FROM {tableRef}"),
            $"Pagination SELECT preamble mismatch for {dialect}");

        // Pagination portion uses parameterized format
        var expectedPagination = RtSqlFormatting.FormatParameterizedPagination(
            dialect, limitParamIndex: 0, offsetParamIndex: 1);
        Assert.That(result.Sql, Does.EndWith(expectedPagination),
            $"Pagination parameterized format mismatch for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // SELECT with ORDER BY + pagination — structural equivalence
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void SelectWithOrderByAndPagination_StructureEquivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");

        // Runtime uses literal pagination
        var state = new QueryState(dialect, "Users", null)
            .WithOrderBy(qName, Direction.Ascending)
            .WithLimit(10)
            .WithOffset(5);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        // Compile-time uses parameterized pagination
        var clauses = new List<ChainedClauseSite>
        {
            MakeOrderByClause(qName, isDescending: false, isConditional: false),
            MakeLimitClause(isConditional: false),
            MakeOffsetClause(isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var compileResult = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        // Both should share the same SELECT...ORDER BY prefix
        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        var expectedPrefix = $"SELECT * FROM {tableRef} ORDER BY {qName} ASC";
        Assert.That(runtimeSql, Does.StartWith(expectedPrefix),
            $"Runtime ORDER BY structure for {dialect}");
        Assert.That(compileResult.Sql, Does.StartWith(expectedPrefix),
            $"Compile-time ORDER BY structure for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional branching — single conditional WHERE (1 bit, 2 variants)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalSingleWhere_Mask0_NoWhere(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");

        // Bit 0 = conditional WHERE. mask=0 → WHERE not applied.
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        // mask=0 → no WHERE clause
        var state = new QueryState(dialect, "Users", null);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);
        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Conditional WHERE mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalSingleWhere_Mask1_WithWhere(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        // Bit 0 = conditional WHERE. mask=1 → WHERE applied.
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Users", null);

        // mask=1 → WHERE present
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);
        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"Conditional WHERE mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional branching — two conditional WHEREs (2 bits, 4 variants)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalTwoWheres_Mask0_NoWheres(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0b00 → neither WHERE
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null);
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"mask=0b00 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalTwoWheres_Mask1_FirstWhereOnly(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0b01 → first WHERE only; parameter index is @p0 since second WHERE inactive
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"mask=0b01 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalTwoWheres_Mask2_SecondWhereOnly(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        // When only second WHERE is active, its parameter becomes @p0 (first active param)
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0b10 → second WHERE only; its param remapped to @p0
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            2UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qAge} > {p0}");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"mask=0b10 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalTwoWheres_Mask3_BothWheres(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: true, bitIndex: 0),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 1),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0b11 → both WHEREs active; parameters @p0, @p1
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            3UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}")
            .WithWhere($"{qAge} > {p1}");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"mask=0b11 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Mixed unconditional + conditional clauses
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void MixedUnconditionalAndConditional_Mask0_OnlyUnconditional(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        // First WHERE is unconditional, second is conditional (bit 0)
        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0 → only unconditional WHERE
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Mixed mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void MixedUnconditionalAndConditional_Mask1_BothWheres(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false),
            MakeWhereClause($"{qAge} > @p1", new[] { MakeParam(1, "@p1") },
                isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=1 → both WHEREs active
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}")
            .WithWhere($"{qAge} > {p1}");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Mixed mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional OrderBy with unconditional WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalOrderBy_Mask0_NoOrderBy(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false),
            MakeOrderByClause(qAge, isDescending: false, isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0 → WHERE only, no ORDER BY
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional OrderBy mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalOrderBy_Mask1_WithOrderBy(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause($"{qName} = @p0", new[] { MakeParam(0, "@p0") },
                isConditional: false),
            MakeOrderByClause(qAge, isDescending: false, isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=1 → WHERE + ORDER BY
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Users", null);
        var state = new QueryState(dialect, "Users", null)
            .WithWhere($"{qName} = {p0}")
            .WithOrderBy(qAge, Direction.Ascending);
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional OrderBy mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional DELETE WHERE (1 bit, 2 variants)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalDeleteWhere_Mask0_NoWhere(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qId = Q(dialect, "Id");

        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere, $"{qId} = @p0",
                new[] { MakeParam(0, "@p0") }, isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, genDialect, "Users", null);

        // mask=0 → no WHERE
        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        Assert.That(result.Sql, Is.EqualTo($"DELETE FROM {tableRef}"),
            $"Conditional DELETE WHERE mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalDeleteWhere_Mask1_WithWhere(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qId = Q(dialect, "Id");
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var clauses = new List<ChainedClauseSite>
        {
            MakeClause(ClauseRole.DeleteWhere, $"{qId} = @p0",
                new[] { MakeParam(0, "@p0") }, isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            1UL, clauses, templates, genDialect, "Users", null);

        // mask=1 → WHERE present
        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        Assert.That(result.Sql, Is.EqualTo($"DELETE FROM {tableRef} WHERE {qId} = {p0}"),
            $"Conditional DELETE WHERE mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional UPDATE SET (1 bit) + unconditional WHERE
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalUpdateSet_Mask0_OnlyRequiredSet(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var qId = Q(dialect, "Id");

        // SET Name is unconditional, SET Age is conditional (bit 0), WHERE is unconditional
        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause(qName, 0, isConditional: false),
            MakeSetClause(qAge, 1, isConditional: true, bitIndex: 0),
            MakeClause(ClauseRole.UpdateWhere, $"{qId} = @p2",
                new[] { MakeParam(2, "@p2") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0 → only Name SET + WHERE
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            0UL, clauses, templates, genDialect, "Users", null);

        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);
        Assert.That(result.Sql,
            Is.EqualTo($"UPDATE {tableRef} SET {qName} = {p0} WHERE {qId} = {p1}"),
            $"Conditional UPDATE SET mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalUpdateSet_Mask1_BothSets(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var qName = Q(dialect, "Name");
        var qAge = Q(dialect, "Age");
        var qId = Q(dialect, "Id");

        var clauses = new List<ChainedClauseSite>
        {
            MakeSetClause(qName, 0, isConditional: false),
            MakeSetClause(qAge, 1, isConditional: true, bitIndex: 0),
            MakeClause(ClauseRole.UpdateWhere, $"{qId} = @p2",
                new[] { MakeParam(2, "@p2") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=1 → both SET + WHERE
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            1UL, clauses, templates, genDialect, "Users", null);

        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);
        var p1 = RtSqlFormatting.FormatParameter(dialect, 1);
        var p2 = RtSqlFormatting.FormatParameter(dialect, 2);
        Assert.That(result.Sql,
            Is.EqualTo($"UPDATE {tableRef} SET {qName} = {p0}, {qAge} = {p1} WHERE {qId} = {p2}"),
            $"Conditional UPDATE SET mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional pagination — LIMIT/OFFSET conditional (1 bit)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalPagination_Mask0_NoPagination(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(isConditional: true, bitIndex: 0),
            MakeOffsetClause(isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0 → no pagination
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Users", null);

        var state = new QueryState(dialect, "Users", null);
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional pagination mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalPagination_Mask1_WithPagination(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeLimitClause(isConditional: true, bitIndex: 0),
            MakeOffsetClause(isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=1 → pagination present
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Users", null);

        var tableRef = RtSqlFormatting.FormatTableName(dialect, "Users", null);
        Assert.That(result.Sql, Does.StartWith($"SELECT * FROM {tableRef}"),
            $"Conditional pagination mask=1 preamble failed for {dialect}");

        var expectedPagination = RtSqlFormatting.FormatParameterizedPagination(
            dialect, limitParamIndex: 0, offsetParamIndex: 1);
        Assert.That(result.Sql, Does.EndWith(expectedPagination),
            $"Conditional pagination mask=1 suffix failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // GROUP BY — simple single column
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void GroupBy_SingleColumn_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"));
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"GROUP BY single column equivalence failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void GroupBy_MultipleColumns_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId") + ", " + Q(dialect, "Status"));
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId") + ", " + Q(dialect, "Status"), isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"GROUP BY multiple columns equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // HAVING — simple aggregate filter
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void Having_SingleCondition_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"))
            .WithHaving("COUNT(*) > 5");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
            MakeHavingClause("COUNT(*) > 5", null, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"HAVING single condition equivalence failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void Having_WithParameter_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);
        var p0 = RtSqlFormatting.FormatParameter(dialect, 0);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"))
            .WithHaving("COUNT(*) > " + p0);
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
            MakeHavingClause("COUNT(*) > @p0", new[] { MakeParam(0, "@p0") }, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"HAVING with parameter equivalence failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void Having_MultipleConditions_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"))
            .WithHaving("COUNT(*) > 5")
            .WithHaving("SUM(" + Q(dialect, "Total") + ") > 100");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
            MakeHavingClause("COUNT(*) > 5", null, isConditional: false),
            MakeHavingClause("SUM(" + Q(dialect, "Total") + ") > 100", null, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"HAVING multiple conditions equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // GROUP BY + HAVING + WHERE combination
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void GroupBy_Having_Where_Combined_Equivalence(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var state = new QueryState(dialect, "Orders", null)
            .WithWhere("(" + Q(dialect, "IsActive") + " = 1)")
            .WithGroupByFragment(Q(dialect, "UserId"))
            .WithHaving("COUNT(*) > 5");
        var runtimeSql = SqlBuilder.BuildSelectSql(state);

        var clauses = new List<ChainedClauseSite>
        {
            MakeWhereClause("(" + Q(dialect, "IsActive") + " = 1)",
                System.Array.Empty<ParameterInfo>(), isConditional: false),
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
            MakeHavingClause("COUNT(*) > 5", null, isConditional: false),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"GROUP BY + HAVING + WHERE combined equivalence failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional GROUP BY (1 bit)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalGroupBy_Mask0_NoGroupBy(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0 → no GROUP BY
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        var state = new QueryState(dialect, "Orders", null);
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional GROUP BY mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalGroupBy_Mask1_WithGroupBy(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=1 → GROUP BY present
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Orders", null);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"));
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional GROUP BY mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Conditional HAVING (1 bit)
    // ───────────────────────────────────────────────────────────────

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalHaving_Mask0_GroupByOnly(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
            MakeHavingClause("COUNT(*) > 5", null, isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=0 → GROUP BY but no HAVING
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, genDialect, "Orders", null);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"));
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional HAVING mask=0 failed for {dialect}");
    }

    [TestCaseSource(nameof(AllDialects))]
    public void ConditionalHaving_Mask1_GroupByAndHaving(RuntimeSqlDialect dialect)
    {
        var genDialect = ToGen(dialect);

        var clauses = new List<ChainedClauseSite>
        {
            MakeGroupByClause(Q(dialect, "UserId"), isConditional: false),
            MakeHavingClause("COUNT(*) > 5", null, isConditional: true, bitIndex: 0),
        };
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);

        // mask=1 → both GROUP BY and HAVING
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            1UL, clauses, templates, genDialect, "Orders", null);

        var state = new QueryState(dialect, "Orders", null)
            .WithGroupByFragment(Q(dialect, "UserId"))
            .WithHaving("COUNT(*) > 5");
        Assert.That(result.Sql, Is.EqualTo(SqlBuilder.BuildSelectSql(state)),
            $"Conditional HAVING mask=1 failed for {dialect}");
    }

    // ───────────────────────────────────────────────────────────────
    // Helper factories
    // ───────────────────────────────────────────────────────────────

    private static ParameterInfo MakeParam(int index, string name)
    {
        return new ParameterInfo(index, name, "string", "value");
    }

    private static ChainedClauseSite MakeGroupByClause(
        string groupBySql,
        bool isConditional,
        int? bitIndex = null)
    {
        var clauseInfo = ClauseInfo.Success(ClauseKind.GroupBy, groupBySql,
            System.Array.Empty<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "GroupBy",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.GroupBy,
            invocationSyntax: null!,
            uniqueId: $"test_groupby_{groupBySql}",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.GroupBy);
    }

    private static ChainedClauseSite MakeHavingClause(
        string havingSql,
        IReadOnlyList<ParameterInfo>? parameters,
        bool isConditional,
        int? bitIndex = null)
    {
        var parms = parameters ?? System.Array.Empty<ParameterInfo>();
        var clauseInfo = ClauseInfo.Success(ClauseKind.Having, havingSql, parms);
        var site = new UsageSiteInfo(
            methodName: "Having",
            filePath: "test.cs",
            line: 1,
            column: 1,
            builderTypeName: "Quarry.QueryBuilder`1",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Having,
            invocationSyntax: null!,
            uniqueId: $"test_having_{havingSql}",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.Having);
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
            uniqueId: $"test_{role}_{sqlFragment.GetHashCode()}",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, role);
    }

    private static ChainedClauseSite MakeOrderByClause(
        string columnSql,
        bool isDescending,
        bool isConditional,
        int? bitIndex = null)
    {
        var clauseInfo = new OrderByClauseInfo(columnSql, isDescending,
            System.Array.Empty<ParameterInfo>());
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
            uniqueId: $"test_orderby_{columnSql}",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.OrderBy);
    }

    private static ChainedClauseSite MakeThenByClause(
        string columnSql,
        bool isDescending,
        bool isConditional,
        int? bitIndex = null)
    {
        var clauseInfo = new OrderByClauseInfo(columnSql, isDescending,
            System.Array.Empty<ParameterInfo>());
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
            uniqueId: $"test_thenby_{columnSql}",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.ThenBy);
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
            uniqueId: $"test_set_{columnSql}",
            clauseInfo: clauseInfo);

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.UpdateSet);
    }

    private static ChainedClauseSite MakeDistinctClause()
    {
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
            uniqueId: "test_distinct");

        return new ChainedClauseSite(site, isConditional: false, bitIndex: null, ClauseRole.Distinct);
    }

    private static ChainedClauseSite MakeLimitClause(bool isConditional, int? bitIndex = null)
    {
        var parameters = new[] { MakeParam(0, "@p0") };
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, "@p0", parameters);
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

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.Limit);
    }

    private static ChainedClauseSite MakeOffsetClause(bool isConditional, int? bitIndex = null)
    {
        var parameters = new[] { MakeParam(0, "@p0") };
        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, "@p0", parameters);
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

        return new ChainedClauseSite(site, isConditional, bitIndex, ClauseRole.Offset);
    }
}
