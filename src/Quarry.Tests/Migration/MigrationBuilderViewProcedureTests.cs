using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationBuilderViewProcedureTests
{
    #region CreateView

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void CreateView_GeneratesCreateViewDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateView("active_users",
            "SELECT user_id, user_name, email FROM users WHERE is_active = 1");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE VIEW"));
        Assert.That(sql, Does.Contain("active_users").IgnoreCase);
        Assert.That(sql, Does.Contain("SELECT user_id"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void CreateView_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateView("active_users",
            "SELECT user_id FROM users WHERE is_active = 1",
            schema: "reporting");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE VIEW"));
        Assert.That(sql, Does.Contain("reporting").IgnoreCase);
    }

    #endregion

    #region DropView

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropView_GeneratesDropViewDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropView("active_users");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP VIEW"));
        Assert.That(sql, Does.Contain("active_users").IgnoreCase);
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropView_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropView("active_users", schema: "reporting");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP VIEW"));
        Assert.That(sql, Does.Contain("reporting").IgnoreCase);
    }

    #endregion

    #region AlterView

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    public void AlterView_PostgreSQL_MySQL_EmitsCreateOrReplace(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AlterView("active_users",
            "SELECT user_id, user_name FROM users WHERE is_active = 1 AND verified = 1");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE OR REPLACE VIEW"));
        Assert.That(sql, Does.Contain("active_users").IgnoreCase);
    }

    [Test]
    public void AlterView_SQLite_EmitsDropAndCreate()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.AlterView("active_users",
            "SELECT user_id, user_name FROM users WHERE is_active = 1 AND verified = 1");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP VIEW IF EXISTS"));
        Assert.That(sql, Does.Contain("CREATE VIEW"));
        Assert.That(sql, Does.Not.Contain("CREATE OR REPLACE"));
    }

    [Test]
    public void AlterView_SqlServer_EmitsAlterView()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.AlterView("active_users",
            "SELECT user_id, user_name FROM users WHERE is_active = 1 AND verified = 1");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER VIEW"));
        Assert.That(sql, Does.Not.Contain("CREATE OR REPLACE"));
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AlterView_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AlterView("active_users",
            "SELECT user_id FROM users",
            schema: "reporting");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("reporting").IgnoreCase);
    }

    #endregion

    #region CreateProcedure

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void CreateProcedure_GeneratesCreateProcedureDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateProcedure("deactivate_user",
            "AS BEGIN UPDATE users SET is_active = 0 WHERE user_id = @user_id END");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE PROCEDURE"));
        Assert.That(sql, Does.Contain("deactivate_user").IgnoreCase);
    }

    [Test]
    public void CreateProcedure_SQLite_ThrowsNotSupportedException()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.CreateProcedure("deactivate_user", "AS BEGIN END");

        Assert.Throws<NotSupportedException>(() => builder.BuildSql(dialect));
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void CreateProcedure_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateProcedure("deactivate_user",
            "AS BEGIN UPDATE users SET is_active = 0 END",
            schema: "admin");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE PROCEDURE"));
        Assert.That(sql, Does.Contain("admin").IgnoreCase);
    }

    #endregion

    #region DropProcedure

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropProcedure_GeneratesDropProcedureDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropProcedure("deactivate_user");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP PROCEDURE"));
        Assert.That(sql, Does.Contain("deactivate_user").IgnoreCase);
    }

    [Test]
    public void DropProcedure_SQLite_ThrowsNotSupportedException()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.DropProcedure("deactivate_user");

        Assert.Throws<NotSupportedException>(() => builder.BuildSql(dialect));
    }

    #endregion

    #region AlterProcedure

    [Test]
    public void AlterProcedure_PostgreSQL_EmitsCreateOrReplace()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.AlterProcedure("deactivate_user",
            "AS BEGIN UPDATE users SET is_active = 0 END");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE OR REPLACE PROCEDURE"));
    }

    [Test]
    public void AlterProcedure_MySQL_EmitsDropAndCreate()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();

        builder.AlterProcedure("deactivate_user",
            "AS BEGIN UPDATE users SET is_active = 0 END");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP PROCEDURE IF EXISTS"));
        Assert.That(sql, Does.Contain("CREATE PROCEDURE"));
    }

    [Test]
    public void AlterProcedure_SqlServer_EmitsAlterProcedure()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.AlterProcedure("deactivate_user",
            "AS BEGIN UPDATE users SET is_active = 0 END");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER PROCEDURE"));
        Assert.That(sql, Does.Not.Contain("CREATE OR REPLACE"));
    }

    [Test]
    public void AlterProcedure_SQLite_ThrowsNotSupportedException()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.AlterProcedure("deactivate_user", "AS BEGIN END");

        Assert.Throws<NotSupportedException>(() => builder.BuildSql(dialect));
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AlterProcedure_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AlterProcedure("deactivate_user",
            "AS BEGIN UPDATE users SET is_active = 0 END",
            schema: "admin");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("admin").IgnoreCase);
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropProcedure_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropProcedure("deactivate_user", schema: "admin");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP PROCEDURE"));
        Assert.That(sql, Does.Contain("admin").IgnoreCase);
    }

    #endregion

    #region Idempotent Views

    [Test]
    public void Idempotent_CreateView_SQLite_EmitsIfNotExists()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.CreateView("active_users", "SELECT 1 FROM users");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("CREATE VIEW IF NOT EXISTS"));
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    public void Idempotent_CreateView_PostgreSQL_MySQL_EmitsCreateOrReplace(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateView("active_users", "SELECT 1 FROM users");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("CREATE OR REPLACE VIEW"));
    }

    [Test]
    public void Idempotent_CreateView_SqlServer_EmitsCreateOrAlter()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.CreateView("active_users", "SELECT 1 FROM users");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("CREATE OR ALTER VIEW"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    public void Idempotent_DropView_NonSqlServer_EmitsIfExists(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropView("active_users");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("DROP VIEW IF EXISTS"));
    }

    [Test]
    public void Idempotent_DropView_SqlServer_EmitsSysViewsCheck()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.DropView("active_users");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("IF EXISTS"));
        Assert.That(sql, Does.Contain("sys.views"));
    }

    #endregion

    #region Idempotent Procedures

    [Test]
    public void Idempotent_CreateProcedure_PostgreSQL_EmitsCreateOrReplace()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.CreateProcedure("deactivate_user", "AS BEGIN END");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("CREATE OR REPLACE PROCEDURE"));
    }

    [Test]
    public void Idempotent_CreateProcedure_SqlServer_EmitsCreateOrAlter()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.CreateProcedure("deactivate_user", "AS BEGIN END");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("CREATE OR ALTER PROCEDURE"));
    }

    [Test]
    public void Idempotent_CreateProcedure_MySQL_EmitsDropIfExistsThenCreate()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();

        builder.CreateProcedure("deactivate_user", "AS BEGIN END");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("DROP PROCEDURE IF EXISTS"));
        Assert.That(sql, Does.Contain("CREATE PROCEDURE"));
    }

    [Test]
    public void Idempotent_DropProcedure_PostgreSQL_EmitsIfExists()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.DropProcedure("deactivate_user");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("DROP PROCEDURE IF EXISTS"));
    }

    [Test]
    public void Idempotent_DropProcedure_SqlServer_EmitsSysProceduresCheck()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.DropProcedure("deactivate_user");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("IF EXISTS"));
        Assert.That(sql, Does.Contain("sys.procedures"));
    }

    #endregion

    #region Fluent Chaining

    [Test]
    public void FluentChaining_ViewsAndProcedures_Works()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        var result = builder
            .CreateView("active_users", "SELECT user_id FROM users WHERE is_active = true")
            .CreateProcedure("deactivate_user", "AS BEGIN UPDATE users SET is_active = false END")
            .DropView("old_view")
            .DropProcedure("old_proc");

        Assert.That(result, Is.SameAs(builder));

        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("CREATE VIEW"));
        Assert.That(sql, Does.Contain("CREATE PROCEDURE"));
        Assert.That(sql, Does.Contain("DROP VIEW"));
        Assert.That(sql, Does.Contain("DROP PROCEDURE"));
    }

    [Test]
    public void FluentChaining_ViewsWithTables_Works()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        var result = builder
            .CreateTable("users", null, t =>
            {
                t.Column("id", c => c.ClrType("int").Identity().NotNull());
                t.Column("is_active", c => c.ClrType("bool").NotNull());
                t.PrimaryKey("PK_users", "id");
            })
            .CreateView("active_users", "SELECT id FROM users WHERE is_active = true");

        Assert.That(result, Is.SameAs(builder));

        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("CREATE VIEW"));
    }

    #endregion

    #region SuppressTransaction

    [Test]
    public void SuppressTransaction_OnViewOperation_SetsFlagOnLastOperation()
    {
        var builder = new MigrationBuilder();
        builder.CreateView("active_users", "SELECT 1 FROM users")
               .SuppressTransaction();

        var ops = builder.GetOperations();
        Assert.That(ops[0].SuppressTransaction, Is.True);
    }

    [Test]
    public void SuppressTransaction_OnProcedureOperation_SetsFlagOnLastOperation()
    {
        var builder = new MigrationBuilder();
        builder.CreateProcedure("deactivate_user", "AS BEGIN END")
               .SuppressTransaction();

        var ops = builder.GetOperations();
        Assert.That(ops[0].SuppressTransaction, Is.True);
    }

    #endregion
}
