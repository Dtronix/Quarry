using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class SqlTypeMapperTests
{
    #region Basic CLR type mappings

    [TestCase(SqlDialect.SQLite, "int", "INTEGER")]
    [TestCase(SqlDialect.PostgreSQL, "int", "integer")]
    [TestCase(SqlDialect.MySQL, "int", "INT")]
    [TestCase(SqlDialect.SqlServer, "int", "INT")]
    [TestCase(SqlDialect.SQLite, "long", "INTEGER")]
    [TestCase(SqlDialect.PostgreSQL, "long", "bigint")]
    [TestCase(SqlDialect.MySQL, "long", "BIGINT")]
    [TestCase(SqlDialect.SqlServer, "long", "BIGINT")]
    [TestCase(SqlDialect.SQLite, "bool", "INTEGER")]
    [TestCase(SqlDialect.PostgreSQL, "bool", "boolean")]
    [TestCase(SqlDialect.MySQL, "bool", "TINYINT(1)")]
    [TestCase(SqlDialect.SqlServer, "bool", "BIT")]
    [TestCase(SqlDialect.SQLite, "decimal", "REAL")]
    [TestCase(SqlDialect.PostgreSQL, "decimal", "numeric")]
    [TestCase(SqlDialect.SQLite, "Guid", "TEXT")]
    [TestCase(SqlDialect.PostgreSQL, "Guid", "uuid")]
    [TestCase(SqlDialect.MySQL, "Guid", "CHAR(36)")]
    [TestCase(SqlDialect.SqlServer, "Guid", "UNIQUEIDENTIFIER")]
    [TestCase(SqlDialect.SQLite, "byte[]", "BLOB")]
    [TestCase(SqlDialect.PostgreSQL, "byte[]", "bytea")]
    public void MapClrType_BasicTypes(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region String with length

    [TestCase(SqlDialect.PostgreSQL, 100, "varchar(100)")]
    [TestCase(SqlDialect.MySQL, 255, "VARCHAR(255)")]
    [TestCase(SqlDialect.SqlServer, 200, "NVARCHAR(200)")]
    public void MapClrType_StringWithLength(SqlDialect dialectType, int length, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType("string", dialect, length: length);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "TEXT")]
    [TestCase(SqlDialect.PostgreSQL, "text")]
    [TestCase(SqlDialect.SqlServer, "NVARCHAR(MAX)")]
    public void MapClrType_StringWithoutLength(SqlDialect dialectType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType("string", dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Decimal with precision

    [TestCase(SqlDialect.PostgreSQL, 18, 2, "numeric(18,2)")]
    [TestCase(SqlDialect.MySQL, 10, 4, "DECIMAL(10,4)")]
    [TestCase(SqlDialect.SqlServer, 18, 2, "DECIMAL(18,2)")]
    public void MapClrType_DecimalWithPrecision(SqlDialect dialectType, int precision, int scale, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType("decimal", dialect, precision: precision, scale: scale);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Nullable types

    [TestCase(SqlDialect.PostgreSQL, "int?", "integer")]
    [TestCase(SqlDialect.PostgreSQL, "bool?", "boolean")]
    public void MapClrType_NullableTypes_StripsQuestionMark(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Extended coverage

    [TestCase(SqlDialect.PostgreSQL, "System.Int32", "integer")]
    [TestCase(SqlDialect.SqlServer, "System.Int32", "INT")]
    [TestCase(SqlDialect.MySQL, "System.Int32", "INT")]
    public void MapClrType_SystemPrefix_StripsCorrectly(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.PostgreSQL, "DateTime?", "timestamp")]
    [TestCase(SqlDialect.SqlServer, "DateTime?", "DATETIME2")]
    public void MapClrType_NullableDateTime_Strips(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void MapClrType_UnknownType_FallsBackToText(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType("SomeCustomType", dialect);
        // Should return some text-based fallback
        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(SqlDialect.PostgreSQL, "byte", "smallint")]
    [TestCase(SqlDialect.PostgreSQL, "short", "smallint")]
    [TestCase(SqlDialect.SqlServer, "byte", "TINYINT")]
    [TestCase(SqlDialect.SqlServer, "short", "SMALLINT")]
    public void MapClrType_ByteAndShort(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "TEXT")]
    [TestCase(SqlDialect.PostgreSQL, "interval")]
    [TestCase(SqlDialect.SqlServer, "TIME")]
    public void MapClrType_TimeSpan(SqlDialect dialectType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType("TimeSpan", dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "TEXT")]
    [TestCase(SqlDialect.PostgreSQL, "timestamptz")]
    [TestCase(SqlDialect.SqlServer, "DATETIMEOFFSET")]
    public void MapClrType_DateTimeOffset(SqlDialect dialectType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType("DateTimeOffset", dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Float and Double

    [TestCase(SqlDialect.PostgreSQL, "float", "real")]
    [TestCase(SqlDialect.MySQL, "float", "FLOAT")]
    [TestCase(SqlDialect.SqlServer, "float", "REAL")]
    [TestCase(SqlDialect.SQLite, "float", "REAL")]
    public void MapClrType_Float(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.PostgreSQL, "double", "double precision")]
    [TestCase(SqlDialect.MySQL, "double", "DOUBLE")]
    [TestCase(SqlDialect.SqlServer, "double", "FLOAT")]
    [TestCase(SqlDialect.SQLite, "double", "REAL")]
    public void MapClrType_Double(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.PostgreSQL, "System.Boolean", "boolean")]
    [TestCase(SqlDialect.SqlServer, "System.Boolean", "BIT")]
    public void MapClrType_SystemBoolean_StripsNamespace(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.PostgreSQL, "DateTime", "timestamp")]
    [TestCase(SqlDialect.MySQL, "DateTime", "DATETIME")]
    [TestCase(SqlDialect.SqlServer, "DateTime", "DATETIME2")]
    [TestCase(SqlDialect.SQLite, "DateTime", "TEXT")]
    public void MapClrType_DateTime_AllDialects(SqlDialect dialectType, string clrType, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var result = SqlTypeMapper.MapClrType(clrType, dialect);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void MapClrType_StringWithLength_SQLite_IgnoresLength()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var result = SqlTypeMapper.MapClrType("string", dialect, length: 100);
        Assert.That(result, Is.EqualTo("TEXT"));
    }

    #endregion
}
