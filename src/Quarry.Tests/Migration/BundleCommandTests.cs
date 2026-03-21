using Quarry.Tool.Commands;

namespace Quarry.Tests.Migration;

public class BundleCommandTests
{
    // --- GenerateBundleProgram tests ---

    [Test]
    public void GenerateBundleProgram_SingleMigration_EmitsCorrectStructure()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "InitialCreate", "M0001_InitialCreate", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("MigrationRunner.RunAsync"));
        Assert.That(code, Does.Contain("M0001_InitialCreate.Upgrade"));
        Assert.That(code, Does.Contain("M0001_InitialCreate.Downgrade"));
        Assert.That(code, Does.Contain("M0001_InitialCreate.Backup"));
        Assert.That(code, Does.Contain("using MyApp.Migrations;"));
    }

    [Test]
    public void GenerateBundleProgram_MultipleMigrations_SortedByVersion()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "InitialCreate", "M0001_InitialCreate", "MyApp.Migrations"),
            new(2, "AddUsers", "M0002_AddUsers", "MyApp.Migrations"),
            new(3, "AddPosts", "M0003_AddPosts", "MyApp.Migrations"),
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        var idx1 = code.IndexOf("M0001_InitialCreate.Upgrade");
        var idx2 = code.IndexOf("M0002_AddUsers.Upgrade");
        var idx3 = code.IndexOf("M0003_AddPosts.Upgrade");
        Assert.That(idx1, Is.GreaterThan(-1));
        Assert.That(idx2, Is.GreaterThan(idx1));
        Assert.That(idx3, Is.GreaterThan(idx2));
    }

    [Test]
    public void GenerateBundleProgram_WithDefaultDialect_EmbedsDefault()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, "postgresql");

        Assert.That(code, Does.Contain("\"postgresql\""));
        // Should not require dialect as a mandatory argument
        Assert.That(code, Does.Not.Contain("Dialect required"));
    }

    [Test]
    public void GenerateBundleProgram_WithoutDefaultDialect_RequiresDialect()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("Dialect required"));
    }

    [Test]
    public void GenerateBundleProgram_EmitsAllDbProviderUsings()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("using Microsoft.Data.Sqlite;"));
        Assert.That(code, Does.Contain("using Npgsql;"));
        Assert.That(code, Does.Contain("using MySqlConnector;"));
        Assert.That(code, Does.Contain("using Microsoft.Data.SqlClient;"));
    }

    [Test]
    public void GenerateBundleProgram_EmitsConnectionStringFromEnvVar()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("QUARRY_CONNECTION"));
    }

    [Test]
    public void GenerateBundleProgram_EmitsCLIOptionsSupport()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("dry-run"));
        Assert.That(code, Does.Contain("direction"));
        Assert.That(code, Does.Contain("target"));
        Assert.That(code, Does.Contain("idempotent"));
        Assert.That(code, Does.Contain("ignore-incomplete"));
    }

    [Test]
    public void GenerateBundleProgram_EmitsExitCodeHandling()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("return 0;"));
        Assert.That(code, Does.Contain("return 1;"));
    }

    [Test]
    public void GenerateBundleProgram_EmitsDialectParserWithAllDialects()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("\"sqlite\""));
        Assert.That(code, Does.Contain("\"postgresql\""));
        Assert.That(code, Does.Contain("\"mysql\""));
        Assert.That(code, Does.Contain("\"sqlserver\""));
        Assert.That(code, Does.Contain("\"postgres\""));
        Assert.That(code, Does.Contain("\"mssql\""));
    }

    [Test]
    public void GenerateBundleProgram_EmitsHelpFlag()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("PrintUsage"));
        Assert.That(code, Does.Contain("--help"));
    }

    [Test]
    public void GenerateBundleProgram_MultipleNamespaces_EmitsAllUsings()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations"),
            new(2, "Seed", "M0002_Seed", "MyApp.Seeds"),
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("using MyApp.Migrations;"));
        Assert.That(code, Does.Contain("using MyApp.Seeds;"));
    }

    [Test]
    public void GenerateBundleProgram_MigrationNameWithSpecialChars_Escaped()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Add \"Users\" Table", "M0001_AddUsersTable", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        // Should escape quotes in migration name
        Assert.That(code, Does.Contain("Add \\\"Users\\\" Table"));
    }

    [Test]
    public void GenerateBundleProgram_EmitsDbConnectionCreationForAllDialects()
    {
        var migrations = new List<CommandHelpers.MigrationInfo>
        {
            new(1, "Init", "M0001_Init", "MyApp.Migrations")
        };

        var code = BundleCommand.GenerateBundleProgram(migrations, null);

        Assert.That(code, Does.Contain("new SqliteConnection("));
        Assert.That(code, Does.Contain("new NpgsqlConnection("));
        Assert.That(code, Does.Contain("new MySqlConnection("));
        Assert.That(code, Does.Contain("new SqlConnection("));
    }

    // --- GenerateBundleCsproj tests ---

    [Test]
    public void GenerateBundleCsproj_ProjectReference_EmitsProjectReference()
    {
        var quarryRef = new BundleCommand.QuarryReference
        {
            IsProjectReference = true,
            Path = "/src/Quarry/Quarry.csproj"
        };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: false, runtime: null);

        Assert.That(csproj, Does.Contain("<ProjectReference Include=\"/src/Quarry/Quarry.csproj\""));
        Assert.That(csproj, Does.Contain("<SelfContained>false</SelfContained>"));
        Assert.That(csproj, Does.Contain("<PublishSingleFile>true</PublishSingleFile>"));
    }

    [Test]
    public void GenerateBundleCsproj_PackageReference_EmitsPackageReference()
    {
        var quarryRef = new BundleCommand.QuarryReference
        {
            IsPackageReference = true,
            PackageName = "Quarry",
            Version = "1.0.0"
        };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: false, runtime: null);

        Assert.That(csproj, Does.Contain("<PackageReference Include=\"Quarry\" Version=\"1.0.0\""));
    }

    [Test]
    public void GenerateBundleCsproj_DllReference_EmitsHintPath()
    {
        var quarryRef = new BundleCommand.QuarryReference
        {
            Path = "/lib/Quarry.dll"
        };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: false, runtime: null);

        Assert.That(csproj, Does.Contain("<HintPath>/lib/Quarry.dll</HintPath>"));
    }

    [Test]
    public void GenerateBundleCsproj_SelfContained_EnablesTrimming()
    {
        var quarryRef = new BundleCommand.QuarryReference { IsProjectReference = true, Path = "Quarry.csproj" };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: true, runtime: null);

        Assert.That(csproj, Does.Contain("<SelfContained>true</SelfContained>"));
        Assert.That(csproj, Does.Contain("<PublishTrimmed>true</PublishTrimmed>"));
    }

    [Test]
    public void GenerateBundleCsproj_WithRuntime_EmitsRuntimeIdentifier()
    {
        var quarryRef = new BundleCommand.QuarryReference { IsProjectReference = true, Path = "Quarry.csproj" };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: false, runtime: "linux-x64");

        Assert.That(csproj, Does.Contain("<RuntimeIdentifier>linux-x64</RuntimeIdentifier>"));
    }

    [Test]
    public void GenerateBundleCsproj_IncludesAllDbProviders()
    {
        var quarryRef = new BundleCommand.QuarryReference { IsProjectReference = true, Path = "Quarry.csproj" };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: false, runtime: null);

        Assert.That(csproj, Does.Contain("Microsoft.Data.Sqlite"));
        Assert.That(csproj, Does.Contain("Npgsql"));
        Assert.That(csproj, Does.Contain("MySqlConnector"));
        Assert.That(csproj, Does.Contain("Microsoft.Data.SqlClient"));
    }

    [Test]
    public void GenerateBundleCsproj_TargetsNet10()
    {
        var quarryRef = new BundleCommand.QuarryReference { IsProjectReference = true, Path = "Quarry.csproj" };

        var csproj = BundleCommand.GenerateBundleCsproj(quarryRef, selfContained: false, runtime: null);

        Assert.That(csproj, Does.Contain("<TargetFramework>net10.0</TargetFramework>"));
    }

    // --- FindQuarryReference tests ---

    [Test]
    public void FindQuarryReference_ProjectReference_Detected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"quarry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = Path.Combine(tempDir, "Test.csproj");
            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../Quarry/Quarry.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var result = BundleCommand.FindQuarryReference(csproj);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.IsProjectReference, Is.True);
            Assert.That(result.Path, Does.Contain("Quarry.csproj"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void FindQuarryReference_PackageReference_Detected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"quarry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = Path.Combine(tempDir, "Test.csproj");
            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Quarry" Version="1.2.3" />
                  </ItemGroup>
                </Project>
                """);

            var result = BundleCommand.FindQuarryReference(csproj);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.IsPackageReference, Is.True);
            Assert.That(result.PackageName, Is.EqualTo("Quarry"));
            Assert.That(result.Version, Is.EqualTo("1.2.3"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void FindQuarryReference_GeneratorReference_Ignored()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"quarry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = Path.Combine(tempDir, "Test.csproj");
            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../Quarry.Generator/Quarry.Generator.csproj" OutputItemType="Analyzer" />
                  </ItemGroup>
                </Project>
                """);

            var result = BundleCommand.FindQuarryReference(csproj);

            // Should fall back to assembly-based detection, not match the Generator reference
            Assert.That(result == null || !result.IsProjectReference || !result.Path!.Contains("Generator"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
