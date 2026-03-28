using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Projection;
using Quarry.Shared.Migration;
using Quarry.Tests.Testing;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for EntityReader&lt;T&gt; custom entity materialization support.
/// Covers: ReaderCodeGenerator delegation, InterceptorCodeGenerator field emission,
/// and ProjectionInfo propagation.
/// </summary>
[TestFixture]
public class EntityReaderTests
{
    private const string ReaderFqn = "TestApp.UserReader";

    #region ReaderCodeGenerator – Custom EntityReader Delegation

    [Test]
    public void GenerateReaderDelegate_WithCustomEntityReader_DelegatesToReaderInstance()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
                CreateProjectedColumn("UserName", "user_name", "string", 1),
            },
            customEntityReaderClass: ReaderFqn);

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Contain("_entityReader_TestApp_UserReader.Read(r)"),
            "Reader delegate should delegate to the custom entity reader's Read() method");
        Assert.That(readerCode, Does.StartWith("static (DbDataReader r) =>"),
            "Reader delegate should be a static lambda");
    }

    [Test]
    public void GenerateReaderDelegate_WithCustomEntityReader_DoesNotGenerateInlineReader()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
                CreateProjectedColumn("UserName", "user_name", "string", 1),
            },
            customEntityReaderClass: ReaderFqn);

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Not.Contain("new User"),
            "Should not generate inline object initializer when custom reader is active");
        Assert.That(readerCode, Does.Not.Contain("GetInt32"),
            "Should not contain ordinal-based reads when custom reader is active");
    }

    [Test]
    public void GenerateReaderDelegate_WithoutCustomEntityReader_GeneratesInlineReader()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
                CreateProjectedColumn("UserName", "user_name", "string", 1),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Contain("new User"),
            "Should generate inline reader when no custom entity reader");
        Assert.That(readerCode, Does.Contain("GetInt32(0)"));
        Assert.That(readerCode, Does.Contain("GetString(1)"));
    }

    [Test]
    public void GenerateReaderDelegate_DtoProjection_IgnoresCustomEntityReader()
    {
        // Custom entity reader should NOT apply to DTO projections
        var projection = new ProjectionInfo(
            ProjectionKind.Dto,
            "UserDto",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
            },
            customEntityReaderClass: ReaderFqn);

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Not.Contain("_entityReader_"),
            "DTO projection should not use custom entity reader");
        Assert.That(readerCode, Does.Contain("new UserDto"),
            "DTO projection should generate standard inline reader");
    }

    [Test]
    public void GenerateReaderDelegate_TupleProjection_IgnoresCustomEntityReader()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Tuple,
            "(int, string)",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
                CreateProjectedColumn("UserName", "user_name", "string", 1),
            },
            customEntityReaderClass: ReaderFqn);

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Not.Contain("_entityReader_"),
            "Tuple projection should not use custom entity reader");
    }

    [Test]
    public void GenerateReaderDelegate_NamedTupleElements_IncludesElementNames()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Tuple,
            "(string ProductName, string CategoryName)",
            new[]
            {
                CreateProjectedColumn("ProductName", "name", "string", 0),
                CreateProjectedColumn("CategoryName", "name", "string", 1),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Product");

        Assert.That(readerCode, Does.Contain("ProductName: r.GetString(0)"),
            "Named tuple elements should include element name prefix");
        Assert.That(readerCode, Does.Contain("CategoryName: r.GetString(1)"),
            "Named tuple elements should include element name prefix");
    }

    [Test]
    public void GenerateReaderDelegate_DefaultItemNames_OmitsElementNames()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Tuple,
            "(int, string)",
            new[]
            {
                CreateProjectedColumn("Item1", "user_id", "int", 0),
                CreateProjectedColumn("Item2", "user_name", "string", 1),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Not.Contain("Item1:"),
            "Default ItemN names should be omitted");
        Assert.That(readerCode, Does.Not.Contain("Item2:"),
            "Default ItemN names should be omitted");
        Assert.That(readerCode, Does.Contain("r.GetInt32(0), r.GetString(1)"));
    }

    [Test]
    public void GenerateReaderDelegate_SingleColumnProjection_IgnoresCustomEntityReader()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.SingleColumn,
            "string",
            new[]
            {
                CreateProjectedColumn("UserName", "user_name", "string", 0),
            },
            customEntityReaderClass: ReaderFqn);

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Not.Contain("_entityReader_"),
            "Single column projection should not use custom entity reader");
    }

    #endregion

    #region InterceptorCodeGenerator – Field Name Generation

    [Test]
    public void GetEntityReaderFieldName_ProducesCorrectFieldName()
    {
        var fieldName = InterceptorCodeGenerator.GetEntityReaderFieldName("TestApp.UserReader");
        Assert.That(fieldName, Is.EqualTo("_entityReader_TestApp_UserReader"));
    }

    [Test]
    public void GetEntityReaderFieldName_HandlesNestedClass()
    {
        var fieldName = InterceptorCodeGenerator.GetEntityReaderFieldName("TestApp.Readers+UserReader");
        Assert.That(fieldName, Is.EqualTo("_entityReader_TestApp_Readers_UserReader"));
    }

    #endregion

    #region InterceptorCodeGenerator – Cached Instance Emission

    [Test]
    public void GenerateInterceptorsFile_WithCustomEntityReader_EmitsCachedReaderField()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
            },
            customEntityReaderClass: ReaderFqn);

        var callSite = TestCallSiteBuilder.CreateSelectSite("User", "User", projection,
            customEntityReaderClass: ReaderFqn);
        var code = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestDb", "TestApp", "test0000", new[] { callSite });

        Assert.That(code, Does.Contain($"private static readonly {ReaderFqn} _entityReader_TestApp_UserReader = new();"),
            "Should emit cached EntityReader instance as static readonly field");
    }

    [Test]
    public void GenerateInterceptorsFile_WithoutCustomEntityReader_DoesNotEmitReaderField()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            new[]
            {
                CreateProjectedColumn("UserId", "user_id", "int", 0),
            });

        var callSite = TestCallSiteBuilder.CreateSelectSite("User", "User", projection);
        var code = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestDb", "TestApp", "test0000", new[] { callSite });

        Assert.That(code, Does.Not.Contain("_entityReader_"),
            "Should not emit EntityReader field when no custom reader is configured");
    }

    #endregion

    #region ProjectionInfo – CustomEntityReaderClass Propagation

    [Test]
    public void ProjectionInfo_WithCustomEntityReader_StoresReaderClass()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            Array.Empty<ProjectedColumn>(),
            customEntityReaderClass: ReaderFqn);

        Assert.That(projection.CustomEntityReaderClass, Is.EqualTo(ReaderFqn));
    }

    [Test]
    public void ProjectionInfo_WithoutCustomEntityReader_HasNullReaderClass()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "User",
            Array.Empty<ProjectedColumn>());

        Assert.That(projection.CustomEntityReaderClass, Is.Null);
    }

    #endregion

    #region EntityInfo – CustomEntityReaderClass Storage

    [Test]
    public void EntityInfo_WithCustomEntityReader_StoresReaderClass()
    {
        var entity = new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: Array.Empty<ColumnInfo>(),
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Microsoft.CodeAnalysis.Location.None,
            customEntityReaderClass: ReaderFqn);

        Assert.That(entity.CustomEntityReaderClass, Is.EqualTo(ReaderFqn));
        Assert.That(entity.InvalidEntityReaderClass, Is.Null);
    }

    [Test]
    public void EntityInfo_WithInvalidEntityReader_StoresInvalidClass()
    {
        var entity = new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: Array.Empty<ColumnInfo>(),
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Microsoft.CodeAnalysis.Location.None,
            invalidEntityReaderClass: "TestApp.BadReader");

        Assert.That(entity.CustomEntityReaderClass, Is.Null);
        Assert.That(entity.InvalidEntityReaderClass, Is.EqualTo("TestApp.BadReader"));
    }

    #endregion

    #region Helper Methods

    private static ProjectedColumn CreateProjectedColumn(
        string propertyName, string columnName, string clrType, int ordinal,
        bool isNullable = false)
    {
        var readerMethod = clrType switch
        {
            "int" => "GetInt32",
            "string" => "GetString",
            "bool" => "GetBoolean",
            "decimal" => "GetDecimal",
            _ => "GetValue"
        };

        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: columnName,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            ordinal: ordinal,
            isValueType: clrType != "string",
            readerMethodName: readerMethod);
    }

    #endregion
}
