using Quarry.Generators.Sql.Parser;
using GenDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

[TestFixture]
public class SqlParserDmlTests
{
    private static GenDialect D(SqlDialect d) => (GenDialect)(int)d;

    // ─── DELETE ─────────────────────────────────────────

    [Test]
    public void Delete_SimpleDeleteFrom()
    {
        var result = SqlParser.Parse("DELETE FROM users", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement, Is.TypeOf<SqlDeleteStatement>());
        var del = (SqlDeleteStatement)result.Statement!;
        Assert.That(del.Table.TableName, Is.EqualTo("users"));
        Assert.That(del.Where, Is.Null);
    }

    [Test]
    public void Delete_WithWhere()
    {
        var result = SqlParser.Parse("DELETE FROM users WHERE user_id = 1", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var del = (SqlDeleteStatement)result.Statement!;
        Assert.That(del.Table.TableName, Is.EqualTo("users"));
        Assert.That(del.Where, Is.Not.Null);
        var bin = (SqlBinaryExpr)del.Where!;
        Assert.That(((SqlColumnRef)bin.Left).ColumnName, Is.EqualTo("user_id"));
        Assert.That(bin.Operator, Is.EqualTo(SqlBinaryOp.Equal));
    }

    [Test]
    public void Delete_WithParameter()
    {
        var result = SqlParser.Parse("DELETE FROM users WHERE user_id = @id", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var del = (SqlDeleteStatement)result.Statement!;
        var bin = (SqlBinaryExpr)del.Where!;
        Assert.That(bin.Right, Is.TypeOf<SqlParameter>());
        Assert.That(((SqlParameter)bin.Right).RawText, Is.EqualTo("@id"));
    }

    [Test]
    public void Delete_WithComplexWhere()
    {
        var result = SqlParser.Parse("DELETE FROM users WHERE is_active = 0 AND created_at < @cutoff", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var del = (SqlDeleteStatement)result.Statement!;
        Assert.That(del.Where, Is.TypeOf<SqlBinaryExpr>());
        var and = (SqlBinaryExpr)del.Where!;
        Assert.That(and.Operator, Is.EqualTo(SqlBinaryOp.And));
    }

    [Test]
    public void Delete_WithTrailingSemicolon()
    {
        var result = SqlParser.Parse("DELETE FROM users WHERE id = 1;", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement, Is.TypeOf<SqlDeleteStatement>());
    }

    // ─── UPDATE ─────────────────────────────────────────

    [Test]
    public void Update_SingleColumn()
    {
        var result = SqlParser.Parse("UPDATE users SET is_active = 0", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement, Is.TypeOf<SqlUpdateStatement>());
        var upd = (SqlUpdateStatement)result.Statement!;
        Assert.That(upd.Table.TableName, Is.EqualTo("users"));
        Assert.That(upd.Assignments, Has.Count.EqualTo(1));
        Assert.That(upd.Assignments[0].Column.ColumnName, Is.EqualTo("is_active"));
        Assert.That(upd.Assignments[0].Value, Is.TypeOf<SqlLiteral>());
        Assert.That(upd.Where, Is.Null);
    }

    [Test]
    public void Update_MultipleColumns()
    {
        var result = SqlParser.Parse("UPDATE users SET is_active = 0, email = @email, user_name = @name", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var upd = (SqlUpdateStatement)result.Statement!;
        Assert.That(upd.Assignments, Has.Count.EqualTo(3));
        Assert.That(upd.Assignments[0].Column.ColumnName, Is.EqualTo("is_active"));
        Assert.That(upd.Assignments[1].Column.ColumnName, Is.EqualTo("email"));
        Assert.That(upd.Assignments[2].Column.ColumnName, Is.EqualTo("user_name"));
    }

    [Test]
    public void Update_WithWhere()
    {
        var result = SqlParser.Parse("UPDATE users SET is_active = 0 WHERE user_id = @id", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var upd = (SqlUpdateStatement)result.Statement!;
        Assert.That(upd.Assignments, Has.Count.EqualTo(1));
        Assert.That(upd.Where, Is.Not.Null);
        var bin = (SqlBinaryExpr)upd.Where!;
        Assert.That(((SqlColumnRef)bin.Left).ColumnName, Is.EqualTo("user_id"));
    }

    [Test]
    public void Update_WithParameters()
    {
        var result = SqlParser.Parse("UPDATE users SET email = @email WHERE user_id = @id", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var upd = (SqlUpdateStatement)result.Statement!;
        Assert.That(upd.Assignments[0].Value, Is.TypeOf<SqlParameter>());
        Assert.That(((SqlParameter)upd.Assignments[0].Value).RawText, Is.EqualTo("@email"));
        var bin = (SqlBinaryExpr)upd.Where!;
        Assert.That(bin.Right, Is.TypeOf<SqlParameter>());
    }

    [Test]
    public void Update_QualifiedColumn()
    {
        var result = SqlParser.Parse("UPDATE users u SET u.is_active = 0 WHERE u.user_id = @id", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var upd = (SqlUpdateStatement)result.Statement!;
        Assert.That(upd.Assignments[0].Column.TableAlias, Is.EqualTo("u"));
        Assert.That(upd.Assignments[0].Column.ColumnName, Is.EqualTo("is_active"));
    }

    [Test]
    public void Update_WithComplexWhere()
    {
        var result = SqlParser.Parse("UPDATE users SET salary = @salary WHERE dept = @dept AND is_active = 1", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var upd = (SqlUpdateStatement)result.Statement!;
        Assert.That(upd.Assignments, Has.Count.EqualTo(1));
        var and = (SqlBinaryExpr)upd.Where!;
        Assert.That(and.Operator, Is.EqualTo(SqlBinaryOp.And));
    }

    // ─── INSERT ─────────────────────────────────────────

    [Test]
    public void Insert_WithColumnsAndValues()
    {
        var result = SqlParser.Parse("INSERT INTO users (user_name, email) VALUES (@name, @email)", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement, Is.TypeOf<SqlInsertStatement>());
        var ins = (SqlInsertStatement)result.Statement!;
        Assert.That(ins.Table.TableName, Is.EqualTo("users"));
        Assert.That(ins.Columns, Has.Count.EqualTo(2));
        Assert.That(ins.Columns![0].ColumnName, Is.EqualTo("user_name"));
        Assert.That(ins.Columns![1].ColumnName, Is.EqualTo("email"));
        Assert.That(ins.ValueRows, Has.Count.EqualTo(1));
        Assert.That(ins.ValueRows[0], Has.Count.EqualTo(2));
    }

    [Test]
    public void Insert_WithoutColumnList()
    {
        var result = SqlParser.Parse("INSERT INTO users VALUES (@name, @email, 1)", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var ins = (SqlInsertStatement)result.Statement!;
        Assert.That(ins.Columns, Is.Null);
        Assert.That(ins.ValueRows, Has.Count.EqualTo(1));
        Assert.That(ins.ValueRows[0], Has.Count.EqualTo(3));
    }

    [Test]
    public void Insert_MultipleRows()
    {
        var result = SqlParser.Parse("INSERT INTO users (user_name) VALUES ('a'), ('b'), ('c')", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var ins = (SqlInsertStatement)result.Statement!;
        Assert.That(ins.ValueRows, Has.Count.EqualTo(3));
        Assert.That(ins.ValueRows[0], Has.Count.EqualTo(1));
    }

    [Test]
    public void Insert_WithParameters()
    {
        var result = SqlParser.Parse("INSERT INTO users (user_id, user_name) VALUES (@id, @name)", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var ins = (SqlInsertStatement)result.Statement!;
        Assert.That(ins.ValueRows[0][0], Is.TypeOf<SqlParameter>());
        Assert.That(((SqlParameter)ins.ValueRows[0][0]).RawText, Is.EqualTo("@id"));
        Assert.That(ins.ValueRows[0][1], Is.TypeOf<SqlParameter>());
    }

    [Test]
    public void Insert_WithLiteralValues()
    {
        var result = SqlParser.Parse("INSERT INTO users (user_name, is_active) VALUES ('test', 1)", D(SqlDialect.SQLite));
        Assert.That(result.Success, Is.True);
        var ins = (SqlInsertStatement)result.Statement!;
        Assert.That(ins.ValueRows[0][0], Is.TypeOf<SqlLiteral>());
        Assert.That(((SqlLiteral)ins.ValueRows[0][0]).LiteralKind, Is.EqualTo(SqlLiteralKind.String));
        Assert.That(ins.ValueRows[0][1], Is.TypeOf<SqlLiteral>());
        Assert.That(((SqlLiteral)ins.ValueRows[0][1]).LiteralKind, Is.EqualTo(SqlLiteralKind.Number));
    }

    // ─── Error cases ────────────────────────────────────

    [Test]
    public void Delete_MissingFrom_HasDiagnostic()
    {
        var result = SqlParser.Parse("DELETE users", D(SqlDialect.SQLite));
        // Parser expects FROM after DELETE — assert we get the specific diagnostic for the
        // missing FROM, not just "any diagnostic" (which could mask an unrelated error).
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Expected 'FROM'")), Is.True,
            $"Expected diagnostic mentioning 'FROM'. Got: {string.Join(" | ", result.Diagnostics.Select(d => d.Message))}");
    }

    [Test]
    public void Update_MissingSet_HasDiagnostic()
    {
        var result = SqlParser.Parse("UPDATE users WHERE id = 1", D(SqlDialect.SQLite));
        // Parser expects SET after table — assert the specific diagnostic.
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Expected 'SET'")), Is.True,
            $"Expected diagnostic mentioning 'SET'. Got: {string.Join(" | ", result.Diagnostics.Select(d => d.Message))}");
    }

    [Test]
    public void Insert_MissingValues_HasDiagnostic()
    {
        var result = SqlParser.Parse("INSERT INTO users (name)", D(SqlDialect.SQLite));
        // Parser expects VALUES after column list — assert the specific diagnostic.
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Expected 'VALUES'")), Is.True,
            $"Expected diagnostic mentioning 'VALUES'. Got: {string.Join(" | ", result.Diagnostics.Select(d => d.Message))}");
    }
}
