using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationRunnerLockTimeoutTests
{
    [Test]
    public void GetLockTimeoutSql_SqlServer_ReturnsSetLockTimeoutInMilliseconds()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromSeconds(5) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.SqlServer, options);

        Assert.That(sql, Is.EqualTo("SET LOCK_TIMEOUT 5000;"));
    }

    [Test]
    public void GetLockTimeoutSql_PostgreSQL_ReturnsSetStatementTimeoutInMilliseconds()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromSeconds(10) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.PostgreSQL, options);

        Assert.That(sql, Is.EqualTo("SET statement_timeout = '10000ms';"));
    }

    [Test]
    public void GetLockTimeoutSql_MySQL_ReturnsSetInnodbLockWaitTimeoutInSeconds()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromSeconds(30) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.MySQL, options);

        Assert.That(sql, Is.EqualTo("SET innodb_lock_wait_timeout = 30;"));
    }

    [Test]
    public void GetLockTimeoutSql_SQLite_ReturnsNull()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromSeconds(5) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.SQLite, options);

        Assert.That(sql, Is.Null);
    }

    [Test]
    public void GetLockTimeoutSql_NullTimeout_ReturnsNull()
    {
        var options = new MigrationOptions { LockTimeout = null };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.SqlServer, options);

        Assert.That(sql, Is.Null);
    }

    [Test]
    public void GetLockTimeoutSql_SubSecondTimeout_SqlServer_UsesMilliseconds()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromMilliseconds(500) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.SqlServer, options);

        Assert.That(sql, Is.EqualTo("SET LOCK_TIMEOUT 500;"));
    }

    [Test]
    public void GetLockTimeoutSql_SubSecondTimeout_MySQL_TruncatesToSeconds()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromMilliseconds(500) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.MySQL, options);

        // MySQL innodb_lock_wait_timeout is integer seconds; sub-second truncates to 0
        Assert.That(sql, Is.EqualTo("SET innodb_lock_wait_timeout = 0;"));
    }

    [Test]
    public void GetLockTimeoutSql_LargeTimeout_SqlServer_FormatsCorrectly()
    {
        var options = new MigrationOptions { LockTimeout = TimeSpan.FromMinutes(5) };

        var sql = MigrationRunner.GetLockTimeoutSql(SqlDialect.SqlServer, options);

        Assert.That(sql, Is.EqualTo("SET LOCK_TIMEOUT 300000;"));
    }
}
