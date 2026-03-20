using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class UpdateTests
{
    [Test]
    public void SingleSet()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0"));
    }

    [Test]
    public void MultipleSet()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.SetClauses.Add(new SetClause("\"Email\"", 1));
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0, \"Email\" = @p1"));
    }

    [Test]
    public void WithWhere()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.WhereConditions.Add("\"UserId\" = @p1");
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0 WHERE \"UserId\" = @p1"));
    }

    [Test]
    public void MultipleWhere_NoParens()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.WhereConditions.Add("\"UserId\" = @p1");
        state.WhereConditions.Add("\"IsActive\" = 1");
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0 WHERE \"UserId\" = @p1 AND \"IsActive\" = 1"));
    }

    [Test]
    public void All_NoWhere()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.AllowAll = true;
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0"));
    }

    [Test]
    public void SchemaQualified()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", "public", null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"public\".\"users\" SET \"Name\" = @p0"));
    }

    [TestCase(SqlDialect.SQLite, "@p0", "@p1")]
    [TestCase(SqlDialect.PostgreSQL, "$1", "$2")]
    [TestCase(SqlDialect.MySQL, "?", "?")]
    [TestCase(SqlDialect.SqlServer, "@p0", "@p1")]
    public void AllDialects_ParameterFormat(SqlDialect dialect, string setParam, string whereParam)
    {
        var state = new UpdateState(dialect, "users", null, null);
        state.SetClauses.Add(new SetClause(SqlFormatting.QuoteIdentifier(dialect, "Name"), 0));
        state.WhereConditions.Add($"{SqlFormatting.QuoteIdentifier(dialect, "UserId")} = {SqlFormatting.FormatParameter(dialect, 1)}");
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Does.Contain($"SET {SqlFormatting.QuoteIdentifier(dialect, "Name")} = {setParam}"));
        Assert.That(sql, Does.Contain($"WHERE {SqlFormatting.QuoteIdentifier(dialect, "UserId")} = {whereParam}"));
    }

    #region ClauseMask Bit Masking

    [Test]
    public void ClauseMask_InitiallyZero()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        Assert.That(state.ClauseMask, Is.EqualTo(0UL));
    }

    [Test]
    public void SetClauseBit_SetsSingleBit()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(0);
        Assert.That(state.ClauseMask, Is.EqualTo(1UL));
    }

    [Test]
    public void SetClauseBit_SetsHigherBit()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(3);
        Assert.That(state.ClauseMask, Is.EqualTo(0b1000UL));
    }

    [Test]
    public void SetClauseBit_MultipleBits_AccumulateViaOr()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(0);
        state.SetClauseBit(2);
        Assert.That(state.ClauseMask, Is.EqualTo(0b101UL));
    }

    [Test]
    public void SetClauseBit_Idempotent_SettingSameBitTwice()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(1);
        state.SetClauseBit(1);
        Assert.That(state.ClauseMask, Is.EqualTo(0b10UL));
    }

    [Test]
    public void SetClauseBit_AllLowBits_ProducesExpectedMask()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(0);
        state.SetClauseBit(1);
        state.SetClauseBit(2);
        state.SetClauseBit(3);
        Assert.That(state.ClauseMask, Is.EqualTo(0b1111UL));
    }

    [Test]
    public void SetClauseBit_SparseHighBits()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(0);
        state.SetClauseBit(7);
        Assert.That(state.ClauseMask, Is.EqualTo((1UL << 0) | (1UL << 7)));
    }

    [Test]
    public void SetClauseBit_Bit63_DoesNotOverflow()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(63);
        Assert.That(state.ClauseMask, Is.EqualTo(1UL << 63));
    }

    [TestCase(0, 1UL)]
    [TestCase(1, 2UL)]
    [TestCase(4, 16UL)]
    [TestCase(7, 128UL)]
    [TestCase(15, 32768UL)]
    public void SetClauseBit_IndividualBitPositions(int bit, ulong expected)
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauseBit(bit);
        Assert.That(state.ClauseMask, Is.EqualTo(expected));
    }

    [Test]
    public void ClauseMask_CanDistinguishConditionalSetVariants()
    {
        // Simulates: unconditional Set(Name), conditional Set(IsActive) at bit 0,
        // conditional Set(Age) at bit 1 — four possible SQL variants via mask 0..3
        var stateNeither = new UpdateState(SqlDialect.SQLite, "users", null, null);
        var stateActiveOnly = new UpdateState(SqlDialect.SQLite, "users", null, null);
        var stateAgeOnly = new UpdateState(SqlDialect.SQLite, "users", null, null);
        var stateBoth = new UpdateState(SqlDialect.SQLite, "users", null, null);

        stateActiveOnly.SetClauseBit(0);
        stateAgeOnly.SetClauseBit(1);
        stateBoth.SetClauseBit(0);
        stateBoth.SetClauseBit(1);

        Assert.That(stateNeither.ClauseMask, Is.EqualTo(0b00UL));
        Assert.That(stateActiveOnly.ClauseMask, Is.EqualTo(0b01UL));
        Assert.That(stateAgeOnly.ClauseMask, Is.EqualTo(0b10UL));
        Assert.That(stateBoth.ClauseMask, Is.EqualTo(0b11UL));

        // All four masks are distinct — ensures dispatch can select unique SQL variant
        var masks = new[] { stateNeither.ClauseMask, stateActiveOnly.ClauseMask, stateAgeOnly.ClauseMask, stateBoth.ClauseMask };
        Assert.That(masks, Is.Unique);
    }

    #endregion
}
