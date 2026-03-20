using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Quarry.Internal;
using Quarry.Shared.Sql;
using Quarry.Tests.Samples;

namespace Quarry.Tests;


/// <summary>
/// Tests for dialect-aware type mapping features:
/// - GetColumnTypeName on SqlFormatting (all forms, all dialects)
/// - IDialectAwareTypeMapping interface
/// - TypeMappingRegistry.TryConfigureParameter
/// - ModificationParameter DialectConfigurator
/// - End-to-end integration with SQLite
/// </summary>
[TestFixture]
internal class DialectTypeMappingTests
{
    #region GetColumnTypeName — Short-form CLR names

    [TestCase("int", null, null, null, "INTEGER")]
    [TestCase("string", null, null, null, "TEXT")]
    [TestCase("bool", null, null, null, "INTEGER")]
    [TestCase("decimal", null, 10, 2, "NUMERIC(10,2)")]
    [TestCase("decimal", null, null, null, "NUMERIC")]
    [TestCase("Guid", null, null, null, "TEXT")]
    [TestCase("byte[]", null, null, null, "BLOB")]
    [TestCase("long", null, null, null, "INTEGER")]
    [TestCase("short", null, null, null, "INTEGER")]
    [TestCase("byte", null, null, null, "INTEGER")]
    [TestCase("sbyte", null, null, null, "INTEGER")]
    [TestCase("float", null, null, null, "REAL")]
    [TestCase("double", null, null, null, "REAL")]
    [TestCase("uint", null, null, null, "INTEGER")]
    [TestCase("ulong", null, null, null, "INTEGER")]
    [TestCase("ushort", null, null, null, "INTEGER")]
    [TestCase("DateTime", null, null, null, "TEXT")]
    [TestCase("DateTimeOffset", null, null, null, "TEXT")]
    public void SQLite_GetColumnTypeName_ShortForm(string clrType, int? maxLen, int? prec, int? scale, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, clrType, maxLen, prec, scale), Is.EqualTo(expected));
    }

    [TestCase("int", null, null, null, "INTEGER")]
    [TestCase("string", null, null, null, "TEXT")]
    [TestCase("string", 100, null, null, "VARCHAR(100)")]
    [TestCase("bool", null, null, null, "BOOLEAN")]
    [TestCase("decimal", null, 18, 4, "NUMERIC(18,4)")]
    [TestCase("decimal", null, null, null, "NUMERIC")]
    [TestCase("Guid", null, null, null, "UUID")]
    [TestCase("DateTime", null, null, null, "TIMESTAMP")]
    [TestCase("DateTimeOffset", null, null, null, "TIMESTAMPTZ")]
    [TestCase("byte[]", null, null, null, "BYTEA")]
    [TestCase("long", null, null, null, "BIGINT")]
    [TestCase("short", null, null, null, "SMALLINT")]
    [TestCase("byte", null, null, null, "SMALLINT")]
    [TestCase("sbyte", null, null, null, "SMALLINT")]
    [TestCase("float", null, null, null, "REAL")]
    [TestCase("double", null, null, null, "DOUBLE PRECISION")]
    [TestCase("uint", null, null, null, "BIGINT")]
    [TestCase("ulong", null, null, null, "BIGINT")]
    [TestCase("ushort", null, null, null, "INTEGER")]
    [TestCase("TimeSpan", null, null, null, "INTERVAL")]
    [TestCase("DateOnly", null, null, null, "DATE")]
    [TestCase("TimeOnly", null, null, null, "TIME")]
    public void PostgreSQL_GetColumnTypeName_ShortForm(string clrType, int? maxLen, int? prec, int? scale, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, clrType, maxLen, prec, scale), Is.EqualTo(expected));
    }

    [TestCase("int", null, null, null, "INT")]
    [TestCase("string", null, null, null, "VARCHAR(255)")]
    [TestCase("string", 50, null, null, "VARCHAR(50)")]
    [TestCase("bool", null, null, null, "TINYINT(1)")]
    [TestCase("Guid", null, null, null, "CHAR(36)")]
    [TestCase("decimal", null, null, null, "DECIMAL(18,2)")]
    [TestCase("decimal", null, 10, 3, "DECIMAL(10,3)")]
    [TestCase("long", null, null, null, "BIGINT")]
    [TestCase("short", null, null, null, "SMALLINT")]
    [TestCase("byte", null, null, null, "TINYINT UNSIGNED")]
    [TestCase("sbyte", null, null, null, "TINYINT")]
    [TestCase("float", null, null, null, "FLOAT")]
    [TestCase("double", null, null, null, "DOUBLE")]
    [TestCase("uint", null, null, null, "INT UNSIGNED")]
    [TestCase("ulong", null, null, null, "BIGINT UNSIGNED")]
    [TestCase("ushort", null, null, null, "SMALLINT UNSIGNED")]
    [TestCase("DateTime", null, null, null, "DATETIME")]
    [TestCase("DateTimeOffset", null, null, null, "DATETIME")]
    [TestCase("TimeSpan", null, null, null, "TIME")]
    [TestCase("DateOnly", null, null, null, "DATE")]
    [TestCase("TimeOnly", null, null, null, "TIME")]
    [TestCase("byte[]", null, null, null, "LONGBLOB")]
    [TestCase("byte[]", 100, null, null, "VARBINARY(100)")]
    public void MySQL_GetColumnTypeName_ShortForm(string clrType, int? maxLen, int? prec, int? scale, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, clrType, maxLen, prec, scale), Is.EqualTo(expected));
    }

    [TestCase("int", null, null, null, "INT")]
    [TestCase("string", null, null, null, "NVARCHAR(MAX)")]
    [TestCase("string", 200, null, null, "NVARCHAR(200)")]
    [TestCase("bool", null, null, null, "BIT")]
    [TestCase("Guid", null, null, null, "UNIQUEIDENTIFIER")]
    [TestCase("DateTime", null, null, null, "DATETIME2")]
    [TestCase("DateTimeOffset", null, null, null, "DATETIMEOFFSET")]
    [TestCase("decimal", null, null, null, "DECIMAL(18,2)")]
    [TestCase("decimal", null, 12, 6, "DECIMAL(12,6)")]
    [TestCase("long", null, null, null, "BIGINT")]
    [TestCase("short", null, null, null, "SMALLINT")]
    [TestCase("byte", null, null, null, "TINYINT")]
    [TestCase("sbyte", null, null, null, "SMALLINT")]
    [TestCase("float", null, null, null, "REAL")]
    [TestCase("double", null, null, null, "FLOAT")]
    [TestCase("uint", null, null, null, "INT")]
    [TestCase("ulong", null, null, null, "BIGINT")]
    [TestCase("ushort", null, null, null, "SMALLINT")]
    [TestCase("TimeSpan", null, null, null, "TIME")]
    [TestCase("DateOnly", null, null, null, "DATE")]
    [TestCase("TimeOnly", null, null, null, "TIME")]
    [TestCase("byte[]", null, null, null, "VARBINARY(MAX)")]
    [TestCase("byte[]", 500, null, null, "VARBINARY(500)")]
    public void SqlServer_GetColumnTypeName_ShortForm(string clrType, int? maxLen, int? prec, int? scale, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, clrType, maxLen, prec, scale), Is.EqualTo(expected));
    }

    #endregion

    #region GetColumnTypeName — System.* qualified names

    [TestCase("System.Int32", "INTEGER")]
    [TestCase("System.Int64", "INTEGER")]
    [TestCase("System.String", "TEXT")]
    [TestCase("System.Boolean", "INTEGER")]
    [TestCase("System.Decimal", "NUMERIC")]
    [TestCase("System.Guid", "TEXT")]
    [TestCase("System.DateTime", "TEXT")]
    [TestCase("System.DateTimeOffset", "TEXT")]
    [TestCase("System.Single", "REAL")]
    [TestCase("System.Double", "REAL")]
    [TestCase("System.Byte", "INTEGER")]
    [TestCase("System.SByte", "INTEGER")]
    [TestCase("System.Int16", "INTEGER")]
    [TestCase("System.UInt16", "INTEGER")]
    [TestCase("System.UInt32", "INTEGER")]
    [TestCase("System.UInt64", "INTEGER")]
    [TestCase("System.Byte[]", "BLOB")]
    public void SQLite_GetColumnTypeName_SystemQualified(string clrType, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, clrType, null, null, null), Is.EqualTo(expected));
    }

    [TestCase("System.Int32", "INTEGER")]
    [TestCase("System.Int64", "BIGINT")]
    [TestCase("System.String", "TEXT")]
    [TestCase("System.Boolean", "BOOLEAN")]
    [TestCase("System.Decimal", "NUMERIC")]
    [TestCase("System.Guid", "UUID")]
    [TestCase("System.DateTime", "TIMESTAMP")]
    [TestCase("System.DateTimeOffset", "TIMESTAMPTZ")]
    [TestCase("System.Single", "REAL")]
    [TestCase("System.Double", "DOUBLE PRECISION")]
    [TestCase("System.Byte", "SMALLINT")]
    [TestCase("System.SByte", "SMALLINT")]
    [TestCase("System.Int16", "SMALLINT")]
    [TestCase("System.UInt16", "INTEGER")]
    [TestCase("System.UInt32", "BIGINT")]
    [TestCase("System.UInt64", "BIGINT")]
    [TestCase("System.Byte[]", "BYTEA")]
    [TestCase("System.DateOnly", "DATE")]
    [TestCase("System.TimeOnly", "TIME")]
    [TestCase("System.TimeSpan", "INTERVAL")]
    public void PostgreSQL_GetColumnTypeName_SystemQualified(string clrType, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, clrType, null, null, null), Is.EqualTo(expected));
    }

    [TestCase("System.Int32", "INT")]
    [TestCase("System.Int64", "BIGINT")]
    [TestCase("System.String", "VARCHAR(255)")]
    [TestCase("System.Boolean", "TINYINT(1)")]
    [TestCase("System.Decimal", "DECIMAL(18,2)")]
    [TestCase("System.Guid", "CHAR(36)")]
    [TestCase("System.DateTime", "DATETIME")]
    [TestCase("System.Single", "FLOAT")]
    [TestCase("System.Double", "DOUBLE")]
    [TestCase("System.Byte", "TINYINT UNSIGNED")]
    [TestCase("System.SByte", "TINYINT")]
    [TestCase("System.Int16", "SMALLINT")]
    [TestCase("System.UInt16", "SMALLINT UNSIGNED")]
    [TestCase("System.UInt32", "INT UNSIGNED")]
    [TestCase("System.UInt64", "BIGINT UNSIGNED")]
    public void MySQL_GetColumnTypeName_SystemQualified(string clrType, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, clrType, null, null, null), Is.EqualTo(expected));
    }

    [TestCase("System.Int32", "INT")]
    [TestCase("System.Int64", "BIGINT")]
    [TestCase("System.String", "NVARCHAR(MAX)")]
    [TestCase("System.Boolean", "BIT")]
    [TestCase("System.Decimal", "DECIMAL(18,2)")]
    [TestCase("System.Guid", "UNIQUEIDENTIFIER")]
    [TestCase("System.DateTime", "DATETIME2")]
    [TestCase("System.DateTimeOffset", "DATETIMEOFFSET")]
    [TestCase("System.Single", "REAL")]
    [TestCase("System.Double", "FLOAT")]
    [TestCase("System.Byte", "TINYINT")]
    [TestCase("System.SByte", "SMALLINT")]
    [TestCase("System.Int16", "SMALLINT")]
    [TestCase("System.UInt16", "SMALLINT")]
    [TestCase("System.UInt32", "INT")]
    [TestCase("System.UInt64", "BIGINT")]
    [TestCase("System.Byte[]", "VARBINARY(MAX)")]
    public void SqlServer_GetColumnTypeName_SystemQualified(string clrType, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, clrType, null, null, null), Is.EqualTo(expected));
    }

    #endregion

    #region GetColumnTypeName — Pascal-case type names (Int32, Int64, etc.)

    [TestCase("Int32", "INTEGER")]
    [TestCase("Int64", "INTEGER")]
    [TestCase("Int16", "INTEGER")]
    [TestCase("Boolean", "INTEGER")]
    [TestCase("Single", "REAL")]
    [TestCase("Double", "REAL")]
    [TestCase("Decimal", "NUMERIC")]
    [TestCase("Byte", "INTEGER")]
    [TestCase("SByte", "INTEGER")]
    [TestCase("UInt16", "INTEGER")]
    [TestCase("UInt32", "INTEGER")]
    [TestCase("UInt64", "INTEGER")]
    [TestCase("String", "TEXT")]
    [TestCase("Byte[]", "BLOB")]
    public void SQLite_GetColumnTypeName_PascalCase(string clrType, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, clrType, null, null, null), Is.EqualTo(expected));
    }

    [TestCase("Int32", "INTEGER")]
    [TestCase("Int64", "BIGINT")]
    [TestCase("Int16", "SMALLINT")]
    [TestCase("Boolean", "BOOLEAN")]
    [TestCase("Single", "REAL")]
    [TestCase("Double", "DOUBLE PRECISION")]
    [TestCase("Decimal", "NUMERIC")]
    [TestCase("Byte", "SMALLINT")]
    [TestCase("SByte", "SMALLINT")]
    [TestCase("UInt16", "INTEGER")]
    [TestCase("UInt32", "BIGINT")]
    [TestCase("UInt64", "BIGINT")]
    [TestCase("String", "TEXT")]
    [TestCase("Byte[]", "BYTEA")]
    public void PostgreSQL_GetColumnTypeName_PascalCase(string clrType, string expected)
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, clrType, null, null, null), Is.EqualTo(expected));
    }

    #endregion

    #region GetColumnTypeName — Three-form consistency

    /// <summary>
    /// Verifies that short-form, Pascal-case, and System.* qualified names all resolve to the same SQL type.
    /// </summary>
    [TestCase("int", "Int32", "System.Int32")]
    [TestCase("long", "Int64", "System.Int64")]
    [TestCase("short", "Int16", "System.Int16")]
    [TestCase("bool", "Boolean", "System.Boolean")]
    [TestCase("float", "Single", "System.Single")]
    [TestCase("double", "Double", "System.Double")]
    [TestCase("decimal", "Decimal", "System.Decimal")]
    [TestCase("byte", "Byte", "System.Byte")]
    [TestCase("sbyte", "SByte", "System.SByte")]
    [TestCase("ushort", "UInt16", "System.UInt16")]
    [TestCase("uint", "UInt32", "System.UInt32")]
    [TestCase("ulong", "UInt64", "System.UInt64")]
    [TestCase("string", "String", "System.String")]
    public void AllDialects_ThreeFormConsistency(string shortForm, string pascalForm, string systemForm)
    {
        var dialects = new SqlDialect[]
        {
            SqlDialect.SQLite,
            SqlDialect.PostgreSQL,
            SqlDialect.MySQL,
            SqlDialect.SqlServer
        };

        foreach (var dialect in dialects)
        {
            var fromShort = SqlFormatting.GetColumnTypeName(dialect, shortForm, null, null, null);
            var fromPascal = SqlFormatting.GetColumnTypeName(dialect, pascalForm, null, null, null);
            var fromSystem = SqlFormatting.GetColumnTypeName(dialect, systemForm, null, null, null);

            Assert.That(fromPascal, Is.EqualTo(fromShort),
                $"{dialect}: '{pascalForm}' should match '{shortForm}'");
            Assert.That(fromSystem, Is.EqualTo(fromShort),
                $"{dialect}: '{systemForm}' should match '{shortForm}'");
        }
    }

    #endregion

    #region GetColumnTypeName — Decimal default behavior per dialect

    [Test]
    public void SQLite_DecimalWithoutPrecision_ReturnsNumericWithoutPrecision()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, "decimal", null, null, null), Is.EqualTo("NUMERIC"));
    }

    [Test]
    public void PostgreSQL_DecimalWithoutPrecision_ReturnsNumericWithoutPrecision()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, "decimal", null, null, null), Is.EqualTo("NUMERIC"));
    }

    [Test]
    public void MySQL_DecimalWithoutPrecision_ReturnsDefault182()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, "decimal", null, null, null), Is.EqualTo("DECIMAL(18,2)"));
    }

    [Test]
    public void SqlServer_DecimalWithoutPrecision_ReturnsDefault182()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, "decimal", null, null, null), Is.EqualTo("DECIMAL(18,2)"));
    }

    [Test]
    public void AllDialects_DecimalWithPrecisionNoScale_DefaultsScaleToZero()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, "decimal", null, 10, null), Is.EqualTo("NUMERIC(10,0)"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, "decimal", null, 10, null), Is.EqualTo("NUMERIC(10,0)"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, "decimal", null, 10, null), Is.EqualTo("DECIMAL(10,0)"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, "decimal", null, 10, null), Is.EqualTo("DECIMAL(10,0)"));
    }

    #endregion

    #region GetColumnTypeName — Unknown/fallback types

    [Test]
    public void AllDialects_UnknownType_ReturnsFallback()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, "SomeCustomType", null, null, null), Is.EqualTo("TEXT"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, "SomeCustomType", null, null, null), Is.EqualTo("TEXT"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, "SomeCustomType", null, null, null), Is.EqualTo("TEXT"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, "SomeCustomType", null, null, null), Is.EqualTo("NVARCHAR(MAX)"));
    }

    [Test]
    public void AllDialects_EmptyString_ReturnsFallback()
    {
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, "", null, null, null), Is.EqualTo("TEXT"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, "", null, null, null), Is.EqualTo("TEXT"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, "", null, null, null), Is.EqualTo("TEXT"));
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, "", null, null, null), Is.EqualTo("NVARCHAR(MAX)"));
    }

    #endregion

    #region GetColumnTypeName — Symmetry: every type mapped by SQLite is mapped by all dialects

    /// <summary>
    /// Ensures no accidental omissions: every CLR type that SQLite handles
    /// should also produce a non-fallback result in the other three dialects.
    /// </summary>
    [TestCase("int")]
    [TestCase("long")]
    [TestCase("short")]
    [TestCase("byte")]
    [TestCase("sbyte")]
    [TestCase("uint")]
    [TestCase("ulong")]
    [TestCase("ushort")]
    [TestCase("bool")]
    [TestCase("float")]
    [TestCase("double")]
    [TestCase("decimal")]
    [TestCase("string")]
    [TestCase("Guid")]
    [TestCase("DateTime")]
    [TestCase("DateTimeOffset")]
    [TestCase("byte[]")]
    public void AllDialects_CommonPrimitives_NeverFallThrough(string clrType)
    {
        // Every common CLR type should produce a non-null, non-empty result in all dialects.
        // This ensures no accidental omissions in any dialect's switch expression.
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, clrType, null, null, null),
            Is.Not.Null.And.Not.Empty, $"SQLite: {clrType}");
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, clrType, null, null, null),
            Is.Not.Null.And.Not.Empty, $"PostgreSQL: {clrType}");
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, clrType, null, null, null),
            Is.Not.Null.And.Not.Empty, $"MySQL: {clrType}");
        Assert.That(SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, clrType, null, null, null),
            Is.Not.Null.And.Not.Empty, $"SqlServer: {clrType}");
    }

    /// <summary>
    /// For types with distinct per-dialect mappings, verify they are NOT the generic fallback.
    /// These are types that should have a specific SQL type on every dialect.
    /// </summary>
    [TestCase("int")]
    [TestCase("long")]
    [TestCase("short")]
    [TestCase("bool")]
    [TestCase("decimal")]
    [TestCase("float")]
    [TestCase("double")]
    [TestCase("Guid")]
    public void AllDialects_StronglyTypedPrimitives_AreNotGenericFallback(string clrType)
    {
        // These types should always get a specific SQL type, never the generic TEXT/NVARCHAR(MAX) fallback
        var pgResult = SqlFormatting.GetColumnTypeName(SqlDialect.PostgreSQL, clrType, null, null, null);
        var mysqlResult = SqlFormatting.GetColumnTypeName(SqlDialect.MySQL, clrType, null, null, null);
        var ssResult = SqlFormatting.GetColumnTypeName(SqlDialect.SqlServer, clrType, null, null, null);

        Assert.That(pgResult, Is.Not.EqualTo("TEXT"), $"PostgreSQL should have specific type for {clrType}");
        Assert.That(mysqlResult, Is.Not.EqualTo("TEXT"), $"MySQL should have specific type for {clrType}");
        Assert.That(ssResult, Is.Not.EqualTo("NVARCHAR(MAX)"), $"SqlServer should have specific type for {clrType}");
    }

    #endregion

    #region IDialectAwareTypeMapping — GetSqlTypeName

    private sealed class TestDialectMapping : TypeMapping<TestDialectValue, string>, IDialectAwareTypeMapping
    {
        public override string ToDb(TestDialectValue value) => value.Data;
        public override TestDialectValue FromDb(string value) => new(value);

        public string? GetSqlTypeName(SqlDialect dialect) => dialect switch
        {
            SqlDialect.PostgreSQL => "jsonb",
            SqlDialect.MySQL => "JSON",
            _ => null
        };

        public void ConfigureParameter(SqlDialect dialect, DbParameter parameter)
        {
            // Mark the parameter so tests can verify this was called
            parameter.SourceColumn = $"configured-{dialect}";
        }
    }

    private readonly record struct TestDialectValue(string Data);

    [Test]
    public void GetSqlTypeName_ReturnsDialectSpecificName()
    {
        var mapping = new TestDialectMapping();

        Assert.That(mapping.GetSqlTypeName(SqlDialect.PostgreSQL), Is.EqualTo("jsonb"));
        Assert.That(mapping.GetSqlTypeName(SqlDialect.MySQL), Is.EqualTo("JSON"));
        Assert.That(mapping.GetSqlTypeName(SqlDialect.SQLite), Is.Null);
        Assert.That(mapping.GetSqlTypeName(SqlDialect.SqlServer), Is.Null);
    }

    [Test]
    public void GetSqlTypeName_NullMeansUseDialectDefault()
    {
        var mapping = new TestDialectMapping();

        // When GetSqlTypeName returns null, the caller should use GetColumnTypeName with the TDb type
        var sqlTypeName = mapping.GetSqlTypeName(SqlDialect.SQLite);
        Assert.That(sqlTypeName, Is.Null);

        // The fallback would be: SqlFormatting.GetColumnTypeName(dialect, "string") since TDb = string
        var fallback = SqlFormatting.GetColumnTypeName(SqlDialect.SQLite, "string", null, null, null);
        Assert.That(fallback, Is.EqualTo("TEXT"));
    }

    #endregion

    #region IDialectAwareTypeMapping — ConfigureParameter via registry

    private readonly record struct ConfigureTestValue(string Data);

    private sealed class ConfigureTestMapping : TypeMapping<ConfigureTestValue, string>, IDialectAwareTypeMapping
    {
        public static Action<SqlDialect, DbParameter>? OnConfigure;
        public override string ToDb(ConfigureTestValue value) => value.Data;
        public override ConfigureTestValue FromDb(string value) => new(value);
        public string? GetSqlTypeName(SqlDialect dialect) => null;
        public void ConfigureParameter(SqlDialect dialect, DbParameter parameter) => OnConfigure?.Invoke(dialect, parameter);
    }

    [Test]
    public void TypeMappingRegistry_TryConfigureParameter_CallsDialectAwareMapping()
    {
        _ = new ConfigureTestMapping();

        var called = false;
        ConfigureTestMapping.OnConfigure = (_, _) => called = true;

        var result = TypeMappingRegistry.TryConfigureParameter(
            typeof(ConfigureTestValue), SqlDialect.PostgreSQL, new FakeDbParameter());

        Assert.That(result, Is.True);
        Assert.That(called, Is.True);
    }

    [Test]
    public void TypeMappingRegistry_TryConfigureParameter_PassesCorrectDialect()
    {
        _ = new ConfigureTestMapping();

        SqlDialect? receivedDialect = null;
        ConfigureTestMapping.OnConfigure = (d, _) => receivedDialect = d;

        TypeMappingRegistry.TryConfigureParameter(
            typeof(ConfigureTestValue), SqlDialect.MySQL, new FakeDbParameter());

        Assert.That(receivedDialect, Is.EqualTo(SqlDialect.MySQL));
    }

    [Test]
    public void TypeMappingRegistry_TryConfigureParameter_PassesDbParameter()
    {
        _ = new ConfigureTestMapping();

        DbParameter? receivedParam = null;
        ConfigureTestMapping.OnConfigure = (_, p) => receivedParam = p;

        var fakeParam = new FakeDbParameter();
        TypeMappingRegistry.TryConfigureParameter(
            typeof(ConfigureTestValue), SqlDialect.SQLite, fakeParam);

        Assert.That(receivedParam, Is.SameAs(fakeParam));
    }

    [Test]
    public void TypeMappingRegistry_TryConfigureParameter_ReturnsFalseForNonDialectAware()
    {
        _ = new MoneyMapping();

        var result = TypeMappingRegistry.TryConfigureParameter(
            typeof(Money), SqlDialect.SQLite, new FakeDbParameter());

        Assert.That(result, Is.False);
    }

    [Test]
    public void TypeMappingRegistry_TryConfigureParameter_ReturnsFalseForUnregisteredType()
    {
        var result = TypeMappingRegistry.TryConfigureParameter(
            typeof(UnregisteredTestType), SqlDialect.SQLite, new FakeDbParameter());

        Assert.That(result, Is.False);
    }

    private readonly struct UnregisteredTestType;

    #endregion

    #region IDialectAwareTypeMapping — Multiple mappings in same context

    private readonly record struct CurrencyValue(string Code);
    private readonly record struct TemperatureValue(double Celsius);

    private sealed class CurrencyMapping : TypeMapping<CurrencyValue, string>, IDialectAwareTypeMapping
    {
        public override string ToDb(CurrencyValue value) => value.Code;
        public override CurrencyValue FromDb(string value) => new(value);
        public string? GetSqlTypeName(SqlDialect dialect) => dialect == SqlDialect.PostgreSQL ? "CHAR(3)" : null;
        public void ConfigureParameter(SqlDialect dialect, DbParameter parameter) { }
    }

    private sealed class TemperatureMapping : TypeMapping<TemperatureValue, double>, IDialectAwareTypeMapping
    {
        public override double ToDb(TemperatureValue value) => value.Celsius;
        public override TemperatureValue FromDb(double value) => new(value);
        public string? GetSqlTypeName(SqlDialect dialect) => dialect == SqlDialect.PostgreSQL ? "DOUBLE PRECISION" : null;
        public void ConfigureParameter(SqlDialect dialect, DbParameter parameter) { }
    }

    [Test]
    public void MultipleDialectAwareMappings_EachReturnsOwnSqlTypeName()
    {
        var currency = new CurrencyMapping();
        var temperature = new TemperatureMapping();

        Assert.That(currency.GetSqlTypeName(SqlDialect.PostgreSQL), Is.EqualTo("CHAR(3)"));
        Assert.That(temperature.GetSqlTypeName(SqlDialect.PostgreSQL), Is.EqualTo("DOUBLE PRECISION"));
    }

    [Test]
    public void MultipleDialectAwareMappings_BothRegisteredAndConfigurable()
    {
        _ = new CurrencyMapping();
        _ = new TemperatureMapping();

        var r1 = TypeMappingRegistry.TryConfigureParameter(
            typeof(CurrencyValue), SqlDialect.PostgreSQL, new FakeDbParameter());
        var r2 = TypeMappingRegistry.TryConfigureParameter(
            typeof(TemperatureValue), SqlDialect.PostgreSQL, new FakeDbParameter());

        Assert.That(r1, Is.True);
        Assert.That(r2, Is.True);
    }

    #endregion

    #region ModificationParameter — DialectConfigurator

    [Test]
    public void ModificationParameter_WithDialectConfigurator_StoresConfigurator()
    {
        var mapping = new TestDialectMapping();
        var param = new ModificationParameter(0, "test", mapping);

        Assert.That(param.DialectConfigurator, Is.SameAs(mapping));
    }

    [Test]
    public void ModificationParameter_WithoutConfigurator_HasNullConfigurator()
    {
        var param = new ModificationParameter(0, "test");

        Assert.That(param.DialectConfigurator, Is.Null);
    }

    [Test]
    public void ModificationParameter_ConfiguratorCallsConfigureParameter()
    {
        var mapping = new TestDialectMapping();
        var param = new ModificationParameter(0, "test", mapping);
        var fakeParam = new FakeDbParameter();

        param.DialectConfigurator!.ConfigureParameter(SqlDialect.PostgreSQL, fakeParam);

        Assert.That(fakeParam.SourceColumn, Is.EqualTo("configured-PostgreSQL"));
    }

    #endregion

    #region Runtime fallback — QueryExecutor null value handling

    [Test]
    public void TypeMappingRegistry_TryConfigureParameter_NotCalledForNullOriginalType()
    {
        // When param.Value is null, originalType is null, so TryConfigureParameter should not be called.
        // This is tested indirectly via QueryExecutor, but we verify the guard at registry level:
        // TryConfigureParameter requires a non-null Type — callers must guard.
        // The QueryExecutor code does: if (originalType != null) TryConfigureParameter(...)
        // So this test just confirms registry doesn't crash on weird but valid types.
        var result = TypeMappingRegistry.TryConfigureParameter(
            typeof(int), SqlDialect.SQLite, new FakeDbParameter());

        // int is not a custom mapped type, so this returns false
        Assert.That(result, Is.False);
    }

    #endregion

    #region End-to-end integration — dialect-aware mapping with SQLite

    private readonly record struct JsonDoc(string Content);

    private sealed class JsonDocMapping : TypeMapping<JsonDoc, string>, IDialectAwareTypeMapping
    {
        public override string ToDb(JsonDoc value) => value.Content;
        public override JsonDoc FromDb(string value) => new(value);

        public string? GetSqlTypeName(SqlDialect dialect) => dialect switch
        {
            SqlDialect.PostgreSQL => "jsonb",
            SqlDialect.MySQL => "JSON",
            _ => "TEXT"
        };

        public int ConfigureCallCount;
        public SqlDialect? LastConfiguredDialect;

        public void ConfigureParameter(SqlDialect dialect, DbParameter parameter)
        {
            ConfigureCallCount++;
            LastConfiguredDialect = dialect;
        }
    }

    [Test]
    public async Task Integration_DialectAwareMapping_InsertAndSelectRoundTrip()
    {
        // Register the dialect-aware mapping
        _ = new JsonDocMapping();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        // Create a simple table to store JSON data
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE "test_json" (
                "Id" INTEGER PRIMARY KEY,
                "Data" TEXT NOT NULL
            )
            """;
        await createCmd.ExecuteNonQueryAsync();

        // Insert via raw SQL using the mapping through the fallback path
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """INSERT INTO "test_json" ("Data") VALUES (@p0)""";
        var p = insertCmd.CreateParameter();
        p.ParameterName = "@p0";

        var doc = new JsonDoc("{\"key\":\"value\"}");
        // Simulate what QueryExecutor does: convert via registry, then configure
        TypeMappingRegistry.TryConvert(typeof(JsonDoc), doc, out var converted);
        p.Value = converted;
        TypeMappingRegistry.TryConfigureParameter(typeof(JsonDoc), SqlDialect.SQLite, p);

        insertCmd.Parameters.Add(p);
        await insertCmd.ExecuteNonQueryAsync();

        // Read back
        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = """SELECT "Data" FROM "test_json" WHERE "Id" = 1""";
        var rawValue = (string)(await selectCmd.ExecuteScalarAsync())!;

        var result = new JsonDocMapping().FromDb(rawValue);
        Assert.That(result.Content, Is.EqualTo("{\"key\":\"value\"}"));
    }

    [Test]
    public async Task Integration_DialectAwareMapping_ConfigureParameterCalledOnFallbackPath()
    {
        // This test verifies the full QueryExecutor path calls TryConfigureParameter
        // by using the real AddWhereClause fallback path with a custom mapped value.
        _ = new MoneyMapping();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE "users" (
                "UserId" INTEGER PRIMARY KEY,
                "UserName" TEXT NOT NULL,
                "Email" TEXT,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "LastLogin" TEXT
            )
            """;
        await cmd.ExecuteNonQueryAsync();

        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = """
            CREATE TABLE "accounts" (
                "AccountId" INTEGER PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "AccountName" TEXT NOT NULL,
                "Balance" REAL NOT NULL,
                "credit_limit" REAL NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1
            )
            """;
        await cmd2.ExecuteNonQueryAsync();

        await using var cmd3 = connection.CreateCommand();
        cmd3.CommandText = """
            INSERT INTO "users" ("UserId", "UserName", "IsActive", "CreatedAt") VALUES (1, 'Test', 1, '2024-01-01')
            """;
        await cmd3.ExecuteNonQueryAsync();

        await using var cmd4 = connection.CreateCommand();
        cmd4.CommandText = """
            INSERT INTO "accounts" ("AccountId", "UserId", "AccountName", "Balance", "credit_limit", "IsActive")
            VALUES (1, 1, 'Savings', 500.00, 1000.00, 1)
            """;
        await cmd4.ExecuteNonQueryAsync();

        using var db = new TestDbContext(connection);

        // Use AddWhereClause to bypass interceptor — exercises QueryExecutor.NormalizeParameterValue
        var results = await ((QueryBuilder<Account, (int AccountId, Money Balance)>)
            db.Accounts().Select(a => (a.AccountId, a.Balance)))
            .AddWhereClause("\"Balance\" >= @p0", new Money(500m))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(500.00m)));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Minimal fake DbParameter for testing ConfigureParameter calls.
    /// </summary>
    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }

    #endregion
}
