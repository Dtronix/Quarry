using Quarry.Migration;
using ForeignKeyAction = Quarry.Migration.ForeignKeyAction;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests dialect-specific DDL rendering branches not covered by CrossDialectDdlTests.
/// </summary>
public class DdlRendererDialectTests
{
    #region RenameTable

    [Test]
    public void RenameTable_SqlServer_EmitsSpRename()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.RenameTable("old_table", "new_table");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("sp_rename"));
        Assert.That(sql, Does.Contain("old_table"));
        Assert.That(sql, Does.Contain("new_table"));
    }

    [Test]
    public void RenameTable_MySQL_EmitsRenameTable()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.RenameTable("old_table", "new_table");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("RENAME TABLE"));
    }

    [Test]
    public void RenameTable_PostgreSQL_EmitsAlterTableRenameTo()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.RenameTable("old_table", "new_table");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("RENAME TO"));
    }

    [Test]
    public void RenameTable_SQLite_EmitsAlterTableRenameTo()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.RenameTable("old_table", "new_table");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("RENAME TO"));
    }

    #endregion

    #region RenameColumn

    [Test]
    public void RenameColumn_SqlServer_EmitsSpRenameColumn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.RenameColumn("users", "old_col", "new_col");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("sp_rename"));
        Assert.That(sql, Does.Contain("COLUMN").IgnoreCase);
    }

    [Test]
    public void RenameColumn_MySQL_EmitsRenameColumn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.RenameColumn("users", "old_col", "new_col");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("RENAME COLUMN"));
    }

    [Test]
    public void RenameColumn_PostgreSQL_EmitsAlterTableRenameColumn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.RenameColumn("users", "old_col", "new_col");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("RENAME COLUMN"));
    }

    [Test]
    public void RenameColumn_SQLite_EmitsAlterTableRenameColumn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.RenameColumn("users", "old_col", "new_col");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("RENAME COLUMN"));
    }

    #endregion

    #region DropIndex

    [Test]
    public void DropIndex_SqlServer_EmitsDropIndexOnTable()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DROP INDEX"));
        Assert.That(sql, Does.Contain("ON"));
    }

    [Test]
    public void DropIndex_MySQL_EmitsDropIndexOnTable()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DROP INDEX"));
        Assert.That(sql, Does.Contain("ON"));
    }

    [Test]
    public void DropIndex_PostgreSQL_EmitsDropIndex()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DROP INDEX"));
        Assert.That(sql, Does.Not.Contain(" ON "));
    }

    [Test]
    public void DropIndex_SQLite_EmitsDropIndex()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DROP INDEX"));
        Assert.That(sql, Does.Not.Contain(" ON "));
    }

    #endregion

    #region AlterColumn PostgreSQL Nullability

    [Test]
    public void AlterColumn_PostgreSQL_NullableTrue_EmitsDropNotNull()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "email", c => c.ClrType("string").Nullable());
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DROP NOT NULL"));
    }

    [Test]
    public void AlterColumn_PostgreSQL_NullableFalse_EmitsSetNotNull()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "email", c => c.ClrType("string").NotNull());
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("SET NOT NULL"));
    }

    #endregion

    #region AddColumn with Defaults

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddColumn_WithDefaultExpression_EmitsDefault(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "created_at", c => c.ClrType("DateTime").DefaultExpression("GETDATE()"));
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DEFAULT"));
        Assert.That(sql, Does.Contain("GETDATE()"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddColumn_WithDefaultValue_EmitsDefault(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "status", c => c.ClrType("string").DefaultValue("'active'"));
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DEFAULT"));
        Assert.That(sql, Does.Contain("'active'"));
    }

    #endregion

    #region SQLite AlterColumn Rebuild

    [Test]
    public void AlterColumn_SQLite_WithSourceTable_EmitsRebuild()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200));
        builder.WithSourceTable(t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.Column("name", c => c.ClrType("string").Length(100));
            t.PrimaryKey("PK_users", "id");
        });
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("RENAME TO"));
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("DROP TABLE"));
    }

    [Test]
    public void AlterColumn_SQLite_WithoutSourceTable_EmitsComment()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200));
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("-- SQLite does not natively support ALTER COLUMN"));
    }

    #endregion

    #region DropForeignKey SQLite

    [Test]
    public void DropForeignKey_SQLite_EmitsComment_NotAlterTable()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.DropForeignKey("FK_posts_users", "posts");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("-- SQLite does not support DROP/ADD CONSTRAINT"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
    }

    #endregion

    #region Batched

    [Test]
    public void Batched_SetsPropertyOnLastOperation()
    {
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "age", c => c.ClrType("int").Nullable());
        builder.Batched(1000);
        var ops = builder.GetOperations();
        Assert.That(ops[^1].BatchSize, Is.EqualTo(1000));
    }

    [Test]
    public void BatchedRawSql_SqlServer_EmitsWhileLoop()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.Sql("UPDATE TOP (5000) users SET status = 'active' WHERE status IS NULL");
        builder.Batched(5000);
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("WHILE 1 = 1"));
        Assert.That(sql, Does.Contain("BEGIN"));
        Assert.That(sql, Does.Contain("UPDATE TOP (5000)"));
        Assert.That(sql, Does.Contain("IF @@ROWCOUNT = 0 BREAK;"));
        Assert.That(sql, Does.Contain("END"));
    }

    [Test]
    public void BatchedRawSql_PostgreSQL_EmitsDoBlock()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.Sql("DELETE FROM logs WHERE created_at < '2020-01-01' LIMIT 10000");
        builder.Batched(10000);
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("DO $$ DECLARE rows_affected INT;"));
        Assert.That(sql, Does.Contain("BEGIN LOOP"));
        Assert.That(sql, Does.Contain("DELETE FROM logs"));
        Assert.That(sql, Does.Contain("GET DIAGNOSTICS rows_affected = ROW_COUNT;"));
        Assert.That(sql, Does.Contain("EXIT WHEN rows_affected = 0;"));
        Assert.That(sql, Does.Contain("END LOOP; END $$;"));
    }

    [Test]
    public void BatchedRawSql_MySQL_EmitsLoop()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.Sql("UPDATE users SET status = 'active' WHERE status IS NULL LIMIT 5000");
        builder.Batched(5000);
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("repeat_loop: LOOP"));
        Assert.That(sql, Does.Contain("UPDATE users"));
        Assert.That(sql, Does.Contain("IF ROW_COUNT() = 0 THEN LEAVE repeat_loop; END IF;"));
        Assert.That(sql, Does.Contain("END LOOP;"));
    }

    [Test]
    public void BatchedRawSql_SQLite_EmitsWarningComment()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();
        builder.Sql("UPDATE users SET status = 'active' WHERE status IS NULL");
        builder.Batched(5000);
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("-- Batched execution is not supported for SQLite"));
        Assert.That(sql, Does.Contain("UPDATE users SET status = 'active'"));
    }

    [Test]
    public void BatchedInsertData_SplitsRowsIntoBatches()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.InsertData("users", new object[]
        {
            new { id = 0, name = "user0" },
            new { id = 1, name = "user1" },
            new { id = 2, name = "user2" },
            new { id = 3, name = "user3" },
            new { id = 4, name = "user4" },
        });
        builder.Batched(2);
        var sql = builder.BuildSql(dialect);
        // Should produce 3 INSERT statements: 2 rows, 2 rows, 1 row
        var insertCount = System.Text.RegularExpressions.Regex.Matches(sql, "INSERT INTO").Count;
        Assert.That(insertCount, Is.EqualTo(3));
    }

    [Test]
    public void BatchedInsertData_SingleBatch_WhenRowsLessThanBatchSize()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.InsertData("users", new object[]
        {
            new { id = 1, name = "a" },
            new { id = 2, name = "b" },
        });
        builder.Batched(10);
        var sql = builder.BuildSql(dialect);
        var insertCount = System.Text.RegularExpressions.Regex.Matches(sql, "INSERT INTO").Count;
        Assert.That(insertCount, Is.EqualTo(1));
    }

    [Test]
    public void BatchedRawSql_WithoutBatchSize_RendersNormally()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.Sql("UPDATE users SET status = 'active' WHERE status IS NULL");
        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Not.Contain("WHILE"));
        Assert.That(sql, Does.Contain("UPDATE users"));
    }

    #endregion

    #region Idempotent DDL

    [Test]
    public void Idempotent_CreateTable_SQLite_EmitsIfNotExists()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_users", "id");
        });
        var sql = builder.BuildIdempotentSql(SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("CREATE TABLE IF NOT EXISTS"));
    }

    [Test]
    public void Idempotent_CreateTable_PostgreSQL_EmitsIfNotExists()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_users", "id");
        });
        var sql = builder.BuildIdempotentSql(SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("CREATE TABLE IF NOT EXISTS"));
    }

    [Test]
    public void Idempotent_CreateTable_SqlServer_EmitsInformationSchemaCheck()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_users", "id");
        });
        var sql = builder.BuildIdempotentSql(SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("IF NOT EXISTS"));
        Assert.That(sql, Does.Contain("INFORMATION_SCHEMA.TABLES"));
    }

    [Test]
    public void Idempotent_DropTable_SQLite_EmitsIfExists()
    {
        var builder = new MigrationBuilder();
        builder.DropTable("users");
        var sql = builder.BuildIdempotentSql(SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("DROP TABLE IF EXISTS"));
    }

    [Test]
    public void Idempotent_DropTable_SqlServer_EmitsInformationSchemaCheck()
    {
        var builder = new MigrationBuilder();
        builder.DropTable("users");
        var sql = builder.BuildIdempotentSql(SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("IF EXISTS"));
        Assert.That(sql, Does.Contain("INFORMATION_SCHEMA.TABLES"));
    }

    [Test]
    public void Idempotent_AddColumn_PostgreSQL_EmitsIfNotExists()
    {
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(200).Nullable());
        var sql = builder.BuildIdempotentSql(SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("ADD COLUMN IF NOT EXISTS"));
    }

    [Test]
    public void Idempotent_AddColumn_SqlServer_EmitsSysColumnsCheck()
    {
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(200).Nullable());
        var sql = builder.BuildIdempotentSql(SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("IF NOT EXISTS"));
        Assert.That(sql, Does.Contain("sys.columns"));
    }

    [Test]
    public void Idempotent_CreateIndex_SQLite_EmitsIfNotExists()
    {
        var builder = new MigrationBuilder();
        builder.AddIndex("IX_users_email", "users", new[] { "email" });
        var sql = builder.BuildIdempotentSql(SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("IF NOT EXISTS"));
    }

    [Test]
    public void Idempotent_CreateIndex_PostgreSQL_EmitsIfNotExists()
    {
        var builder = new MigrationBuilder();
        builder.AddIndex("IX_users_email", "users", new[] { "email" });
        var sql = builder.BuildIdempotentSql(SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("IF NOT EXISTS"));
    }

    [Test]
    public void Idempotent_CreateIndex_SqlServer_EmitsSysIndexesCheck()
    {
        var builder = new MigrationBuilder();
        builder.AddIndex("IX_users_email", "users", new[] { "email" });
        var sql = builder.BuildIdempotentSql(SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("IF NOT EXISTS"));
        Assert.That(sql, Does.Contain("sys.indexes"));
    }

    [Test]
    public void Idempotent_DropIndex_SQLite_EmitsIfExists()
    {
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildIdempotentSql(SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("DROP INDEX IF EXISTS"));
    }

    [Test]
    public void Idempotent_DropIndex_PostgreSQL_EmitsIfExists()
    {
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildIdempotentSql(SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("DROP INDEX IF EXISTS"));
    }

    [Test]
    public void Idempotent_DropIndex_SqlServer_EmitsSysIndexesCheck()
    {
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");
        var sql = builder.BuildIdempotentSql(SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("IF EXISTS"));
        Assert.That(sql, Does.Contain("sys.indexes"));
    }

    [Test]
    public void Idempotent_DropColumn_SqlServer_EmitsSysColumnsCheck()
    {
        var builder = new MigrationBuilder();
        builder.DropColumn("users", "email");
        var sql = builder.BuildIdempotentSql(SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("IF EXISTS"));
        Assert.That(sql, Does.Contain("sys.columns"));
    }

    [Test]
    public void NonIdempotent_CreateTable_DoesNotEmitIfNotExists()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_users", "id");
        });
        var sql = builder.BuildSql(SqlDialect.SQLite);
        Assert.That(sql, Does.Not.Contain("IF NOT EXISTS"));
    }

    #endregion
}
