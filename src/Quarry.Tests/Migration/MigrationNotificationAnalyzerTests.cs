using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class MigrationNotificationAnalyzerTests
{
    private static MigrationStep Step(MigrationStepType type, string table, string? column = null)
    {
        return new MigrationStep(type, MigrationStep.Classify(type), table, null, column, null, null, $"{type} {table}");
    }

    #region SQLite — AlterColumn

    [Test]
    public void SQLite_AlterColumn_WarnsAboutTableRebuild()
    {
        var steps = new[] { Step(MigrationStepType.AlterColumn, "users", "email") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Level, Is.EqualTo(NotificationLevel.Warning));
        Assert.That(notifications[0].Message, Does.Contain("table rebuild"));
        Assert.That(notifications[0].Message, Does.Contain("email"));
        Assert.That(notifications[0].Message, Does.Contain("users"));
    }

    #endregion

    #region SQLite — DropColumn

    [Test]
    public void SQLite_DropColumn_WarnsAboutTableRebuild()
    {
        var steps = new[] { Step(MigrationStepType.DropColumn, "posts", "body") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Level, Is.EqualTo(NotificationLevel.Warning));
        Assert.That(notifications[0].Message, Does.Contain("table rebuild"));
        Assert.That(notifications[0].Message, Does.Contain("body"));
    }

    #endregion

    #region SQLite — AddForeignKey

    [Test]
    public void SQLite_AddForeignKey_ToExistingTable_WarnsAboutTableRebuild()
    {
        // FK on a table NOT created in the same migration
        var steps = new[] { Step(MigrationStepType.AddForeignKey, "posts") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Level, Is.EqualTo(NotificationLevel.Warning));
        Assert.That(notifications[0].Message, Does.Contain("table rebuild"));
        Assert.That(notifications[0].Message, Does.Contain("posts"));
    }

    [Test]
    public void SQLite_AddForeignKey_ToNewTable_NoWarning()
    {
        // FK on a table that IS created in the same migration — will be folded
        var steps = new[]
        {
            Step(MigrationStepType.CreateTable, "posts"),
            Step(MigrationStepType.AddForeignKey, "posts"),
        };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    #endregion

    #region SQLite — DropForeignKey

    [Test]
    public void SQLite_DropForeignKey_WarnsAboutTableRebuild()
    {
        var steps = new[] { Step(MigrationStepType.DropForeignKey, "posts") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Level, Is.EqualTo(NotificationLevel.Warning));
        Assert.That(notifications[0].Message, Does.Contain("table rebuild"));
    }

    #endregion

    #region SQLite — Index rebuild detection

    [Test]
    public void SQLite_DropIndex_FollowedByAddIndex_NotesRebuild()
    {
        var steps = new[]
        {
            Step(MigrationStepType.DropIndex, "users"),
            Step(MigrationStepType.AddIndex, "users"),
        };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Level, Is.EqualTo(NotificationLevel.Info));
        Assert.That(notifications[0].Message, Does.Contain("dropped and recreated"));
    }

    [Test]
    public void SQLite_DropIndex_Alone_NoNotification()
    {
        var steps = new[] { Step(MigrationStepType.DropIndex, "users") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    [Test]
    public void SQLite_DropIndex_FollowedByAddIndexOnDifferentTable_NoNotification()
    {
        var steps = new[]
        {
            Step(MigrationStepType.DropIndex, "users"),
            Step(MigrationStepType.AddIndex, "posts"),
        };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    #endregion

    #region Safe operations — no warnings

    [Test]
    public void SQLite_CreateTable_NoWarning()
    {
        var steps = new[] { Step(MigrationStepType.CreateTable, "users") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    [Test]
    public void SQLite_AddColumn_NoWarning()
    {
        var steps = new[] { Step(MigrationStepType.AddColumn, "users", "email") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    [Test]
    public void SQLite_AddIndex_NoWarning()
    {
        var steps = new[] { Step(MigrationStepType.AddIndex, "users") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    #endregion

    #region Non-SQLite dialects

    [Test]
    public void PostgreSQL_AlterColumn_NoWarning()
    {
        var steps = new[] { Step(MigrationStepType.AlterColumn, "users", "email") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "postgresql");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    [Test]
    public void SqlServer_DropColumn_NoWarning()
    {
        var steps = new[] { Step(MigrationStepType.DropColumn, "users", "email") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlserver");

        Assert.That(notifications, Has.Count.EqualTo(0));
    }

    #endregion

    #region Unknown dialect — falls back to SQLite warnings with prefix

    [Test]
    public void NullDialect_AlterColumn_WarnsWithSQLitePrefix()
    {
        var steps = new[] { Step(MigrationStepType.AlterColumn, "users", "email") };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, null);

        Assert.That(notifications, Has.Count.EqualTo(1));
        Assert.That(notifications[0].Message, Does.StartWith("SQLite: "));
        Assert.That(notifications[0].Message, Does.Contain("table rebuild"));
    }

    #endregion

    #region Multiple steps — produces multiple notifications

    [Test]
    public void SQLite_MultipleProblematicSteps_ProducesMultipleNotifications()
    {
        var steps = new[]
        {
            Step(MigrationStepType.AlterColumn, "users", "email"),
            Step(MigrationStepType.DropColumn, "posts", "body"),
            Step(MigrationStepType.DropForeignKey, "posts"),
        };

        var notifications = MigrationNotificationAnalyzer.Analyze(steps, "sqlite");

        Assert.That(notifications, Has.Count.EqualTo(3));
        Assert.That(notifications[0].StepType, Is.EqualTo(MigrationStepType.AlterColumn));
        Assert.That(notifications[1].StepType, Is.EqualTo(MigrationStepType.DropColumn));
        Assert.That(notifications[2].StepType, Is.EqualTo(MigrationStepType.DropForeignKey));
    }

    #endregion
}
