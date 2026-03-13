using Quarry;
using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class BackupGeneratorTests
{
    private static TableDef BuildTable(string name, params ColumnDef[] columns) =>
        new(name, null, NamingStyleKind.Exact, columns, Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>());

    private static MigrationStep DropTableStep(string table) =>
        new(MigrationStepType.DropTable, StepClassification.Destructive, table, null, null, null, null, $"Drop {table}");

    private static MigrationStep DropColumnStep(string table, string column) =>
        new(MigrationStepType.DropColumn, StepClassification.Destructive, table, null, column, null, null, $"Drop {column}");

    private static MigrationStep AddColumnStep(string table, string column) =>
        new(MigrationStepType.AddColumn, StepClassification.Safe, table, null, column, null, null, $"Add {column}");

    private static MigrationStep AlterColumnStep(string table, string column) =>
        new(MigrationStepType.AlterColumn, StepClassification.Cautious, table, null, column, null, null, $"Alter {column}");

    #region GenerateBackupSql

    [Test]
    public void GenerateBackupSql_DropTable_SQLite_CreatesAsSelect()
    {
        var step = DropTableStep("users");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        var sql = BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("AS SELECT * FROM"));
        Assert.That(sql, Does.Contain("__quarry_backup_users"));
    }

    [Test]
    public void GenerateBackupSql_DropTable_SqlServer_UsesSelectInto()
    {
        var step = DropTableStep("users");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        var sql = BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("SELECT * INTO"));
        Assert.That(sql, Does.Contain("__quarry_backup_users"));
    }

    [Test]
    public void GenerateBackupSql_DropTable_PostgreSQL_CreatesAsSelect()
    {
        var step = DropTableStep("users");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        var sql = BackupGenerator.GenerateBackupSql(step, table, SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("AS SELECT * FROM"));
    }

    [Test]
    public void GenerateBackupSql_DropColumn_WithPK_BackupsPKAndColumn()
    {
        var step = DropColumnStep("users", "email");
        var table = BuildTable("users",
            new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
            new ColumnDef("email", "string", true, ColumnKind.Standard));
        var sql = BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("__quarry_backup_users_email"));
        Assert.That(sql, Does.Contain("SELECT"));
    }

    [Test]
    public void GenerateBackupSql_DropColumn_NoPK_FallsBackToFullBackup()
    {
        var step = DropColumnStep("users", "email");
        var table = BuildTable("users",
            new ColumnDef("id", "int", false, ColumnKind.Standard),
            new ColumnDef("email", "string", true, ColumnKind.Standard));
        var sql = BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("__quarry_backup_users"));
        Assert.That(sql, Does.Contain("SELECT * FROM"));
    }

    [Test]
    public void GenerateBackupSql_NonDestructiveStep_ReturnsNull()
    {
        var step = new MigrationStep(MigrationStepType.CreateTable, StepClassification.Safe, "users", null, null, null, null, "Create");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        Assert.That(BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SQLite), Is.Null);
    }

    [Test]
    public void GenerateBackupSql_AddColumn_ReturnsNull()
    {
        var step = AddColumnStep("users", "email");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        Assert.That(BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SQLite), Is.Null);
    }

    [Test]
    public void GenerateBackupSql_AlterColumn_ReturnsNull()
    {
        var step = AlterColumnStep("users", "name");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        Assert.That(BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SQLite), Is.Null);
    }

    #endregion

    #region GenerateRestoreSql

    [Test]
    public void GenerateRestoreSql_DropTable_EmitsInsertAndDrop()
    {
        var step = DropTableStep("users");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        var sql = BackupGenerator.GenerateRestoreSql(step, table, SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("DROP TABLE"));
        Assert.That(sql, Does.Contain("__quarry_backup_users"));
    }

    [Test]
    public void GenerateRestoreSql_DropColumn_WithPK_EmitsUpdateAndDrop()
    {
        var step = DropColumnStep("users", "email");
        var table = BuildTable("users",
            new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
            new ColumnDef("email", "string", true, ColumnKind.Standard));
        var sql = BackupGenerator.GenerateRestoreSql(step, table, SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("DROP TABLE"));
    }

    [Test]
    public void GenerateRestoreSql_DropColumn_NoPK_ReturnsNull()
    {
        var step = DropColumnStep("users", "email");
        var table = BuildTable("users",
            new ColumnDef("id", "int", false, ColumnKind.Standard),
            new ColumnDef("email", "string", true, ColumnKind.Standard));
        Assert.That(BackupGenerator.GenerateRestoreSql(step, table, SqlDialect.SQLite), Is.Null);
    }

    [Test]
    public void GenerateRestoreSql_NonDestructiveStep_ReturnsNull()
    {
        var step = AddColumnStep("users", "email");
        var table = BuildTable("users", new ColumnDef("id", "int", false, ColumnKind.PrimaryKey));
        Assert.That(BackupGenerator.GenerateRestoreSql(step, table, SqlDialect.SQLite), Is.Null);
    }

    #endregion

    #region GetBackupTableName

    [Test]
    public void GetBackupTableName_DropTable_ReturnsCorrectName()
    {
        var step = DropTableStep("users");
        Assert.That(BackupGenerator.GetBackupTableName(step), Is.EqualTo("__quarry_backup_users"));
    }

    [Test]
    public void GetBackupTableName_DropColumn_ReturnsCorrectName()
    {
        var step = DropColumnStep("users", "email");
        Assert.That(BackupGenerator.GetBackupTableName(step), Is.EqualTo("__quarry_backup_users_email"));
    }

    #endregion

    #region SqlServer DropColumn with PK

    [Test]
    public void GenerateBackupSql_DropColumn_SqlServer_UsesSelectInto()
    {
        var step = DropColumnStep("users", "email");
        var table = BuildTable("users",
            new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
            new ColumnDef("email", "string", true, ColumnKind.Standard));
        var sql = BackupGenerator.GenerateBackupSql(step, table, SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("INTO"));
        Assert.That(sql, Does.Contain("__quarry_backup_users_email"));
    }

    #endregion
}
