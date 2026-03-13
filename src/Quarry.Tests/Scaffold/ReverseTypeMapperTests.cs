using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class ReverseTypeMapperTests
{
    #region SQLite

    [TestCase("INTEGER", "id", false, "int")]
    [TestCase("TEXT", "name", false, "string")]
    [TestCase("REAL", "price", false, "double")]
    [TestCase("BLOB", "data", false, "byte[]")]
    [TestCase("INT", "count", true, "int")]
    public void MapSqlType_Sqlite_MapsCorrectly(string sqlType, string colName, bool isNullable, string expectedClr)
    {
        var result = ReverseTypeMapper.MapSqlType(sqlType, "sqlite", colName, isNullable, false, false);
        Assert.That(result.ClrType, Is.EqualTo(expectedClr));
        Assert.That(result.IsNullable, Is.EqualTo(isNullable));
    }

    [TestCase("IsActive", "bool")]
    [TestCase("HasAccess", "bool")]
    [TestCase("CanEdit", "bool")]
    public void MapSqlType_SqliteInteger_BooleanHeuristic(string colName, string expectedClr)
    {
        var result = ReverseTypeMapper.MapSqlType("INTEGER", "sqlite", colName, false, false, false);
        Assert.That(result.ClrType, Is.EqualTo(expectedClr));
    }

    #endregion

    #region PostgreSQL

    [TestCase("integer", "int")]
    [TestCase("bigint", "long")]
    [TestCase("smallint", "short")]
    [TestCase("boolean", "bool")]
    [TestCase("text", "string")]
    [TestCase("uuid", "Guid")]
    [TestCase("bytea", "byte[]")]
    [TestCase("timestamp", "DateTime")]
    [TestCase("timestamptz", "DateTimeOffset")]
    [TestCase("interval", "TimeSpan")]
    [TestCase("real", "float")]
    [TestCase("double precision", "double")]
    public void MapSqlType_PostgreSql_MapsCorrectly(string sqlType, string expectedClr)
    {
        var result = ReverseTypeMapper.MapSqlType(sqlType, "postgresql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo(expectedClr));
    }

    [Test]
    public void MapSqlType_PostgreSql_VarcharWithLength()
    {
        var result = ReverseTypeMapper.MapSqlType("varchar(100)", "postgresql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("string"));
        Assert.That(result.MaxLength, Is.EqualTo(100));
    }

    [Test]
    public void MapSqlType_PostgreSql_NumericWithPrecisionScale()
    {
        var result = ReverseTypeMapper.MapSqlType("numeric(18,4)", "postgresql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("decimal"));
        Assert.That(result.Precision, Is.EqualTo(18));
        Assert.That(result.Scale, Is.EqualTo(4));
    }

    [Test]
    public void MapSqlType_PostgreSql_Jsonb_MapsToStringWithWarning()
    {
        var result = ReverseTypeMapper.MapSqlType("jsonb", "postgresql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("string"));
        Assert.That(result.Warning, Is.Not.Null);
    }

    [Test]
    public void MapSqlType_PostgreSql_Serial_MapsToInt()
    {
        var result = ReverseTypeMapper.MapSqlType("serial", "postgresql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("int"));
    }

    #endregion

    #region SQL Server

    [TestCase("INT", "int")]
    [TestCase("BIGINT", "long")]
    [TestCase("SMALLINT", "short")]
    [TestCase("TINYINT", "byte")]
    [TestCase("BIT", "bool")]
    [TestCase("FLOAT", "double")]
    [TestCase("REAL", "float")]
    [TestCase("DATETIME2", "DateTime")]
    [TestCase("DATETIMEOFFSET", "DateTimeOffset")]
    [TestCase("UNIQUEIDENTIFIER", "Guid")]
    [TestCase("TIME", "TimeSpan")]
    public void MapSqlType_SqlServer_MapsCorrectly(string sqlType, string expectedClr)
    {
        var result = ReverseTypeMapper.MapSqlType(sqlType, "sqlserver", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo(expectedClr));
    }

    [Test]
    public void MapSqlType_SqlServer_NvarcharMax_MapsToString()
    {
        var result = ReverseTypeMapper.MapSqlType("NVARCHAR(MAX)", "sqlserver", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("string"));
        Assert.That(result.MaxLength, Is.Null);
    }

    [Test]
    public void MapSqlType_SqlServer_Nvarchar255_MapsToStringWithLength()
    {
        var result = ReverseTypeMapper.MapSqlType("NVARCHAR(255)", "sqlserver", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("string"));
        Assert.That(result.MaxLength, Is.EqualTo(255));
    }

    #endregion

    #region MySQL

    [Test]
    public void MapSqlType_MySql_TinyInt1_MapsToBool()
    {
        var result = ReverseTypeMapper.MapSqlType("TINYINT(1)", "mysql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("bool"));
    }

    [Test]
    public void MapSqlType_MySql_TinyInt3_MapsToByte()
    {
        var result = ReverseTypeMapper.MapSqlType("TINYINT(3)", "mysql", "col", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("byte"));
    }

    [Test]
    public void MapSqlType_MySql_Char36Pk_MapsToGuid()
    {
        var result = ReverseTypeMapper.MapSqlType("CHAR(36)", "mysql", "id", false, false, true);
        Assert.That(result.ClrType, Is.EqualTo("Guid"));
    }

    [Test]
    public void MapSqlType_MySql_Char36NonPk_MapsToString()
    {
        var result = ReverseTypeMapper.MapSqlType("CHAR(36)", "mysql", "code", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("string"));
    }

    [Test]
    public void MapSqlType_MySql_Json_MapsToStringWithWarning()
    {
        var result = ReverseTypeMapper.MapSqlType("JSON", "mysql", "data", false, false, false);
        Assert.That(result.ClrType, Is.EqualTo("string"));
        Assert.That(result.Warning, Is.Not.Null);
    }

    #endregion

    #region ParseTypeComponents

    [Test]
    public void ParseTypeComponents_SimpleType_ReturnsBaseOnly()
    {
        var (baseType, length, precision, scale) = ReverseTypeMapper.ParseTypeComponents("INTEGER");
        Assert.That(baseType, Is.EqualTo("INTEGER"));
        Assert.That(length, Is.Null);
        Assert.That(precision, Is.Null);
        Assert.That(scale, Is.Null);
    }

    [Test]
    public void ParseTypeComponents_WithLength_ReturnsLength()
    {
        var (baseType, length, precision, scale) = ReverseTypeMapper.ParseTypeComponents("VARCHAR(255)");
        Assert.That(baseType, Is.EqualTo("VARCHAR"));
        Assert.That(length, Is.EqualTo(255));
    }

    [Test]
    public void ParseTypeComponents_WithPrecisionScale_ReturnsBoth()
    {
        var (baseType, length, precision, scale) = ReverseTypeMapper.ParseTypeComponents("DECIMAL(18,4)");
        Assert.That(baseType, Is.EqualTo("DECIMAL"));
        Assert.That(precision, Is.EqualTo(18));
        Assert.That(scale, Is.EqualTo(4));
    }

    [Test]
    public void ParseTypeComponents_Max_ReturnsNegativeOne()
    {
        var (baseType, length, _, _) = ReverseTypeMapper.ParseTypeComponents("NVARCHAR(MAX)");
        Assert.That(baseType, Is.EqualTo("NVARCHAR"));
        Assert.That(length, Is.EqualTo(-1));
    }

    #endregion
}
