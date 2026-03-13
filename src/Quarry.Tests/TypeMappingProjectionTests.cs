using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Projection;

namespace Quarry.Tests;

/// <summary>
/// Layer 5: ProjectionAnalyzer propagation and ReaderCodeGenerator FromDb wrapping tests.
/// </summary>
[TestFixture]
public class TypeMappingProjectionTests
{
    private const string MappingFqn = "TestApp.MoneyMapping";

    #region ReaderCodeGenerator – FromDb Wrapping

    [Test]
    public void GenerateReaderDelegate_MappedColumn_WrapsWithFromDb()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "Account",
            new[]
            {
                CreateProjectedColumn("AccountId", "AccountId", "int", 0),
                CreateMappedProjectedColumn("Balance", "Balance", "Money", 1,
                    MappingFqn, "GetDecimal"),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Account");

        Assert.That(readerCode, Does.Contain("_mapper_TestApp_MoneyMapping.FromDb("),
            "Reader delegate should wrap mapped column with FromDb()");
        Assert.That(readerCode, Does.Contain("GetDecimal"),
            "Reader delegate should use GetDecimal for the database type");
    }

    [Test]
    public void GenerateReaderDelegate_NullableMappedColumn_IncludesNullCheck()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "Account",
            new[]
            {
                CreateMappedProjectedColumn("Balance", "Balance", "Money", 0,
                    MappingFqn, "GetDecimal", isNullable: true, isValueType: true),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Account");

        Assert.That(readerCode, Does.Contain("IsDBNull"),
            "Nullable mapped column should include null check");
        Assert.That(readerCode, Does.Contain("_mapper_TestApp_MoneyMapping.FromDb("),
            "Nullable mapped column should still use FromDb()");
    }

    [Test]
    public void GenerateReaderDelegate_NonMappedColumn_DoesNotUseFromDb()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "Account",
            new[]
            {
                CreateProjectedColumn("AccountId", "AccountId", "int", 0),
                CreateProjectedColumn("AccountName", "AccountName", "string", 1),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Account");

        Assert.That(readerCode, Does.Not.Contain("FromDb"),
            "Non-mapped columns should not use FromDb()");
        Assert.That(readerCode, Does.Not.Contain("_mapper_"),
            "Non-mapped columns should not reference mapping fields");
    }

    [Test]
    public void GenerateReaderDelegate_MixedColumns_OnlyWrapsMappedOnes()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "Account",
            new[]
            {
                CreateProjectedColumn("AccountId", "AccountId", "int", 0),
                CreateProjectedColumn("AccountName", "AccountName", "string", 1),
                CreateMappedProjectedColumn("Balance", "Balance", "Money", 2,
                    MappingFqn, "GetDecimal"),
                CreateProjectedColumn("IsActive", "IsActive", "bool", 3),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Account");

        // AccountId uses GetInt32 directly
        Assert.That(readerCode, Does.Contain("GetInt32(0)"));
        // AccountName uses GetString directly
        Assert.That(readerCode, Does.Contain("GetString(1)"));
        // Balance uses FromDb(GetDecimal)
        Assert.That(readerCode, Does.Contain("_mapper_TestApp_MoneyMapping.FromDb(r.GetDecimal(2))"));
        // IsActive uses GetBoolean directly
        Assert.That(readerCode, Does.Contain("GetBoolean(3)"));
    }

    [Test]
    public void GenerateReaderDelegate_TupleWithMappedColumn_WrapsCorrectly()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Tuple,
            "(int, Money)",
            new[]
            {
                CreateProjectedColumn("AccountId", "AccountId", "int", 0),
                CreateMappedProjectedColumn("Balance", "Balance", "Money", 1,
                    MappingFqn, "GetDecimal"),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Account");

        Assert.That(readerCode, Does.Contain("GetInt32(0)"));
        Assert.That(readerCode, Does.Contain("_mapper_TestApp_MoneyMapping.FromDb(r.GetDecimal(1))"));
    }

    #endregion

    #region ProjectedColumn – CustomTypeMapping Propagation

    [Test]
    public void ProjectedColumn_WithCustomTypeMapping_PreservesReaderMethodName()
    {
        var col = CreateMappedProjectedColumn("Balance", "Balance", "Money", 0,
            MappingFqn, "GetDecimal");

        Assert.That(col.CustomTypeMapping, Is.EqualTo(MappingFqn));
        Assert.That(col.ReaderMethodName, Is.EqualTo("GetDecimal"));
    }

    [Test]
    public void ProjectedColumn_WithoutCustomTypeMapping_HasNullMapping()
    {
        var col = CreateProjectedColumn("AccountId", "AccountId", "int", 0);

        Assert.That(col.CustomTypeMapping, Is.Null);
    }

    #endregion

    #region Helper Methods

    private static ProjectedColumn CreateProjectedColumn(
        string propertyName, string columnName, string clrType, int ordinal,
        bool isNullable = false, bool isValueType = false)
    {
        var readerMethod = clrType switch
        {
            "int" => "GetInt32",
            "long" => "GetInt64",
            "string" => "GetString",
            "bool" => "GetBoolean",
            "decimal" => "GetDecimal",
            "double" => "GetDouble",
            "float" => "GetFloat",
            "DateTime" => "GetDateTime",
            "Guid" => "GetGuid",
            _ => "GetValue"
        };

        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: columnName,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            ordinal: ordinal,
            isValueType: isValueType || (clrType != "string"),
            readerMethodName: readerMethod);
    }

    private static ProjectedColumn CreateMappedProjectedColumn(
        string propertyName, string columnName, string clrType, int ordinal,
        string customTypeMapping, string readerMethodName,
        bool isNullable = false, bool isValueType = false)
    {
        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: columnName,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            ordinal: ordinal,
            customTypeMapping: customTypeMapping,
            isValueType: isValueType,
            readerMethodName: readerMethodName);
    }

    #endregion
}
