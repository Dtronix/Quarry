using System;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Scaffold;

/// <summary>
/// Characterization tests for the shared <see cref="DatabaseSchemaReader"/> helper
/// extracted from ScaffoldCommand (step 1a). Locks the connection-string building
/// behavior so the extraction is provably behavior-preserving.
/// </summary>
public class DatabaseSchemaReaderTests
{
    [Test]
    public void BuildConnectionString_Sqlite_UsesDataSource()
    {
        var cs = DatabaseSchemaReader.BuildConnectionString("sqlite", null, null, null, null, "test.db");
        Assert.That(cs, Does.Contain("Data Source=test.db"));
    }

    [Test]
    public void BuildConnectionString_PostgreSql_IncludesHostPortDatabaseCredentials()
    {
        var cs = DatabaseSchemaReader.BuildConnectionString("postgresql", "db.example.com", "6543", "admin", "secret", "mydb");
        Assert.That(cs, Does.Contain("Host=db.example.com"));
        Assert.That(cs, Does.Contain("Port=6543"));
        Assert.That(cs, Does.Contain("Database=mydb"));
        Assert.That(cs, Does.Contain("Username=admin"));
        Assert.That(cs, Does.Contain("Password=secret"));
    }

    [Test]
    public void BuildConnectionString_PostgreSql_DefaultsHostAndPort()
    {
        var cs = DatabaseSchemaReader.BuildConnectionString("pg", null, null, null, null, "mydb");
        Assert.That(cs, Does.Contain("Host=localhost"));
        Assert.That(cs, Does.Contain("Port=5432"));
    }

    [Test]
    public void BuildConnectionString_MySql_IncludesServerPortDatabase()
    {
        var cs = DatabaseSchemaReader.BuildConnectionString("mysql", "mysql.example.com", "3307", "root", "pw", "shop");
        Assert.That(cs, Does.Contain("Server=mysql.example.com"));
        Assert.That(cs, Does.Contain("Port=3307"));
        Assert.That(cs, Does.Contain("Database=shop"));
        Assert.That(cs, Does.Contain("User ID=root").Or.Contain("UserID=root"));
    }

    [Test]
    public void BuildConnectionString_SqlServer_IncludesCatalogAndTrust()
    {
        var cs = DatabaseSchemaReader.BuildConnectionString("sqlserver", "sql.example.com", "1433", "sa", "pw", "erp");
        Assert.That(cs, Does.Contain("erp"));
        Assert.That(cs, Does.Contain("sql.example.com"));
        Assert.That(cs, Does.Contain("Trust Server Certificate=True").Or.Contain("TrustServerCertificate=True"));
    }

    [Test]
    public void BuildConnectionString_SqlServer_NoUser_UsesIntegratedSecurity()
    {
        var cs = DatabaseSchemaReader.BuildConnectionString("mssql", "sql.example.com", null, null, null, "erp");
        Assert.That(cs, Does.Contain("Integrated Security=True").Or.Contain("IntegratedSecurity=True"));
    }

    [Test]
    public void BuildConnectionString_UnknownDialect_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseSchemaReader.BuildConnectionString("oracle", "h", "1", "u", "p", "d"));
    }
}
