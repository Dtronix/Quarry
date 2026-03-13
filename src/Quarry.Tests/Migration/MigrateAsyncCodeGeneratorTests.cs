using Quarry.Generators.Generation;
using Quarry.Generators.Models;

namespace Quarry.Tests.Migration;

public class MigrateAsyncCodeGeneratorTests
{
    [Test]
    public void Generate_SingleMigration_EmitsCorrectStructure()
    {
        var migrations = new List<MigrationInfo>
        {
            new(1, "InitialCreate", "M0001_InitialCreate", "MyApp.Migrations")
        };

        var code = MigrateAsyncCodeGenerator.Generate("AppDbContext", "MyApp", migrations);

        Assert.That(code, Does.Contain("partial class AppDbContext"));
        Assert.That(code, Does.Contain("namespace MyApp;"));
        Assert.That(code, Does.Contain("MigrateAsync"));
        Assert.That(code, Does.Contain("M0001_InitialCreate.Upgrade"));
        Assert.That(code, Does.Contain("M0001_InitialCreate.Downgrade"));
        Assert.That(code, Does.Contain("M0001_InitialCreate.Backup"));
        Assert.That(code, Does.Contain("MigrationRunner.RunAsync"));
    }

    [Test]
    public void Generate_MultipleMigrations_SortedByVersion()
    {
        var migrations = new List<MigrationInfo>
        {
            new(3, "AddPosts", "M0003_AddPosts", "MyApp.Migrations"),
            new(1, "InitialCreate", "M0001_InitialCreate", "MyApp.Migrations"),
            new(2, "AddUsers", "M0002_AddUsers", "MyApp.Migrations"),
        };

        var code = MigrateAsyncCodeGenerator.Generate("AppDbContext", "MyApp", migrations);

        var idx1 = code.IndexOf("M0001_InitialCreate");
        var idx2 = code.IndexOf("M0002_AddUsers");
        var idx3 = code.IndexOf("M0003_AddPosts");
        Assert.That(idx1, Is.LessThan(idx2));
        Assert.That(idx2, Is.LessThan(idx3));
    }

    [Test]
    public void Generate_EmptyMigrations_EmitsEmptyArray()
    {
        var code = MigrateAsyncCodeGenerator.Generate("AppDbContext", "MyApp", new List<MigrationInfo>());

        Assert.That(code, Does.Contain("MigrateAsync"));
        Assert.That(code, Does.Contain("MigrationRunner.RunAsync"));
    }

    [Test]
    public void Generate_DifferentNamespaces_EmitsUsings()
    {
        var migrations = new List<MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations"),
            new(2, "Seed", "M0002_Seed", "MyApp.Seeds"),
        };

        var code = MigrateAsyncCodeGenerator.Generate("AppDbContext", "MyApp", migrations);

        Assert.That(code, Does.Contain("using MyApp.Migrations;"));
        Assert.That(code, Does.Contain("using MyApp.Seeds;"));
    }

    [Test]
    public void Generate_MigrationNames_PreservedInTuples()
    {
        var migrations = new List<MigrationInfo>
        {
            new(1, "Initial Create", "M0001_InitialCreate", "MyApp.Migrations"),
        };

        var code = MigrateAsyncCodeGenerator.Generate("AppDbContext", "MyApp", migrations);

        Assert.That(code, Does.Contain("\"Initial Create\""));
    }
}
