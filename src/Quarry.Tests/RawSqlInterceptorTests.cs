using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Tests.Testing;

namespace Quarry.Tests;

/// <summary>
/// Tests for RawSqlAsync and RawSqlScalarAsync interceptor generation.
/// </summary>
[TestFixture]
public class RawSqlInterceptorTests
{
    #region RawSqlAsync DTO Interceptor Tests

    [Test]
    public void RawSqlAsync_DtoType_GeneratesTypedReader()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "UserDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Name", "string", "GetString", false),
                new RawSqlPropertyInfo("Email", "string", "GetString", true)
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "UserDto", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("Task<List<UserDto>>"));
        Assert.That(result, Does.Contain("this QuarryContext self"));
        Assert.That(result, Does.Contain("RawSqlAsyncWithReader"));
        Assert.That(result, Does.Contain("var item = new UserDto()"));
        Assert.That(result, Does.Contain("case \"Name\": item.Name = r.GetString(i); break;"));
        Assert.That(result, Does.Contain("case \"Email\": item.Email = r.GetString(i); break;"));
    }

    [Test]
    public void RawSqlAsync_DtoType_WithCancellationToken_GeneratesCorrectSignature()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "UserDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Name", "string", "GetString", false)
            },
            hasCancellationToken: true);

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "UserDto", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("CancellationToken cancellationToken,"));
        Assert.That(result, Does.Contain("cancellationToken,"));
        Assert.That(result, Does.Not.Contain("CancellationToken.None"));
    }

    [Test]
    public void RawSqlAsync_DtoType_WithoutCancellationToken_UsesCancellationTokenNone()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "UserDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Name", "string", "GetString", false)
            },
            hasCancellationToken: false);

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "UserDto", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("CancellationToken.None"));
    }

    #endregion

    #region RawSqlAsync Scalar Interceptor Tests

    [Test]
    public void RawSqlAsync_ScalarInt_GeneratesScalarReader()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "int",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetInt32");

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "int", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("Task<List<int>>"));
        Assert.That(result, Does.Contain("r.GetInt32(0)"));
        Assert.That(result, Does.Not.Contain("new int()"));
    }

    [Test]
    public void RawSqlAsync_ScalarString_GeneratesScalarReader()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "string",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetString");

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "string", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("Task<List<string>>"));
        Assert.That(result, Does.Contain("r.GetString(0)"));
    }

    #endregion

    #region RawSqlScalarAsync Interceptor Tests

    [Test]
    public void RawSqlScalarAsync_Int_GeneratesTypedConverter()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "int",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetInt32");

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlScalarAsync, "int", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("Task<int>"));
        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"));
        Assert.That(result, Does.Not.Contain("Convert.ChangeType"));
    }

    [Test]
    public void RawSqlScalarAsync_String_GeneratesTypedConverter()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "string",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetString");

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlScalarAsync, "string", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("Task<string>"));
        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"));
    }

    [Test]
    public void RawSqlScalarAsync_WithCancellationToken_GeneratesCorrectSignature()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "long",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            hasCancellationToken: true,
            scalarReaderMethod: "GetInt64");

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlScalarAsync, "long", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("CancellationToken cancellationToken,"));
        Assert.That(result, Does.Contain("cancellationToken,"));
    }

    #endregion

    #region Entity Type Enrichment Tests

    [Test]
    public void RawSqlAsync_EntityType_WithEnumProperty_GeneratesEnumCast()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "User",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("UserId", "int", "GetInt32", false),
                new RawSqlPropertyInfo("Status", "int", "GetInt32", false,
                    isEnum: true, fullClrType: "UserStatus")
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "User", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("(UserStatus)r.GetInt32(i)"));
    }

    [Test]
    public void RawSqlAsync_EntityType_WithRefForeignKey_GeneratesRefWrapper()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "Order",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("OrderId", "int", "GetInt32", false),
                new RawSqlPropertyInfo("UserId", "int", "GetInt32", false,
                    isForeignKey: true, referencedEntityName: "User")
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "Order", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("new EntityRef<User, int>(r.GetInt32(i))"));
    }

    [Test]
    public void RawSqlAsync_EntityType_WithCustomTypeMapping_GeneratesFromDb()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "Product",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("ProductId", "int", "GetInt32", false),
                new RawSqlPropertyInfo("Price", "decimal", "GetDecimal", false,
                    customTypeMappingClass: "MoneyMapping", dbReaderMethodName: "GetDecimal")
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "Product", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert
        Assert.That(result, Does.Contain("new MoneyMapping().FromDb(r.GetDecimal(i))"));
    }

    #endregion

    #region Nullable Property Tests

    [Test]
    public void RawSqlAsync_DtoWithNullableProperties_GeneratesCorrectReader()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "UserDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Name", "string", "GetString", false),
                new RawSqlPropertyInfo("MiddleName", "string", "GetString", true),
                new RawSqlPropertyInfo("Age", "int", "GetInt32", true)
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "UserDto", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert - all properties handled (nullable check is via IsDBNull in the for loop)
        Assert.That(result, Does.Contain("case \"Name\": item.Name = r.GetString(i); break;"));
        Assert.That(result, Does.Contain("case \"MiddleName\": item.MiddleName = r.GetString(i); break;"));
        Assert.That(result, Does.Contain("case \"Age\": item.Age = r.GetInt32(i); break;"));
    }

    #endregion

    #region Multiple RawSql Sites Tests

    [Test]
    public void GenerateInterceptorsFile_MultipleRawSqlSites_GeneratesAllInterceptors()
    {
        // Arrange
        var dtoSite = CreateRawSqlCallSite(
            InterceptorKind.RawSqlAsync, "UserDto",
            new RawSqlTypeInfo("UserDto", RawSqlTypeKind.Dto,
                new[] { new RawSqlPropertyInfo("Name", "string", "GetString", false) }),
            uniqueId: "site1");

        var scalarSite = CreateRawSqlCallSite(
            InterceptorKind.RawSqlScalarAsync, "int",
            new RawSqlTypeInfo("int", RawSqlTypeKind.Scalar,
                System.Array.Empty<RawSqlPropertyInfo>(), scalarReaderMethod: "GetInt32"),
            uniqueId: "site2");

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { dtoSite, scalarSite });

        // Assert
        Assert.That(result, Does.Contain("Standalone Interceptors"));
        Assert.That(result, Does.Contain("RawSqlAsyncWithReader"));
        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"));
    }

    #endregion

    #region Edge Case: DTO with Zero Properties

    [Test]
    public void RawSqlAsync_DtoWithZeroProperties_OmitsSwitch()
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "EmptyDto",
            RawSqlTypeKind.Dto,
            System.Array.Empty<RawSqlPropertyInfo>());

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "EmptyDto", rawSqlTypeInfo);

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Should emit a simple one-liner lambda discarding the reader parameter
        Assert.That(result, Does.Contain("static _ => new EmptyDto()"));
        Assert.That(result, Does.Not.Contain("switch (r.GetName(i))"));
        Assert.That(result, Does.Not.Contain("case \""));
    }

    #endregion

    #region Edge Case: All Standard CLR Scalar Types

    [Test]
    [TestCase("int", "GetInt32")]
    [TestCase("long", "GetInt64")]
    [TestCase("string", "GetString")]
    [TestCase("bool", "GetBoolean")]
    [TestCase("decimal", "GetDecimal")]
    [TestCase("double", "GetDouble")]
    [TestCase("float", "GetFloat")]
    [TestCase("byte", "GetByte")]
    [TestCase("short", "GetInt16")]
    [TestCase("DateTime", "GetDateTime")]
    [TestCase("Guid", "GetGuid")]
    public void RawSqlScalarAsync_StandardClrType_GeneratesTypedConverter(string clrType, string readerMethod)
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            clrType,
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: readerMethod);

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlScalarAsync, clrType, rawSqlTypeInfo);

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"),
            $"Should generate converter for {clrType}");
        Assert.That(result, Does.Not.Contain("Convert.ChangeType"),
            $"Should not use Convert.ChangeType for {clrType}");
    }

    [Test]
    [TestCase("int", "GetInt32")]
    [TestCase("long", "GetInt64")]
    [TestCase("string", "GetString")]
    [TestCase("bool", "GetBoolean")]
    [TestCase("decimal", "GetDecimal")]
    [TestCase("double", "GetDouble")]
    [TestCase("DateTime", "GetDateTime")]
    [TestCase("Guid", "GetGuid")]
    [TestCase("byte", "GetByte")]
    public void RawSqlAsync_StandardClrScalar_GeneratesScalarReader(string clrType, string readerMethod)
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            clrType,
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: readerMethod);

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, clrType, rawSqlTypeInfo);

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain($"r.{readerMethod}(0)"),
            $"Should generate r.{readerMethod}(0) for scalar {clrType}");
    }

    #endregion

    #region Edge Case: Nullable Scalar Converter

    [Test]
    public void RawSqlScalarAsync_NullableInt_GeneratesTypedConverter()
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "int?",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetInt32");

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlScalarAsync, "int?", rawSqlTypeInfo);

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"),
            "Should generate converter for nullable int");
        Assert.That(result, Does.Contain("Task<int?>"),
            "Return type should be Task<int?>");
    }

    #endregion

    #region Edge Case: Mixed Property Types in Single DTO

    [Test]
    public void RawSqlAsync_MixedPropertyTypes_GeneratesAllReaderMethods()
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "MixedDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Id", "int", "GetInt32", false),
                new RawSqlPropertyInfo("Name", "string", "GetString", false),
                new RawSqlPropertyInfo("CreatedAt", "DateTime", "GetDateTime", false),
                new RawSqlPropertyInfo("IsActive", "bool", "GetBoolean", false),
                new RawSqlPropertyInfo("Balance", "decimal", "GetDecimal", false),
                new RawSqlPropertyInfo("Notes", "string", "GetString", true),
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "MixedDto", rawSqlTypeInfo);

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("case \"Id\": item.Id = r.GetInt32(i); break;"));
        Assert.That(result, Does.Contain("case \"Name\": item.Name = r.GetString(i); break;"));
        Assert.That(result, Does.Contain("case \"CreatedAt\": item.CreatedAt = r.GetDateTime(i); break;"));
        Assert.That(result, Does.Contain("case \"IsActive\": item.IsActive = r.GetBoolean(i); break;"));
        Assert.That(result, Does.Contain("case \"Balance\": item.Balance = r.GetDecimal(i); break;"));
        Assert.That(result, Does.Contain("case \"Notes\": item.Notes = r.GetString(i); break;"));
    }

    #endregion

    #region RawCallSite Equality Tests

    [Test]
    public void RawCallSite_Equals_IgnoresRawSqlTypeInfo()
    {
        // RawSqlTypeInfo is a mutable enrichment property set by DisplayClassEnricher,
        // not a discovery-time identity field. It must NOT be part of Equals to prevent
        // perpetual cache misses in the incremental pipeline.
        var site1 = new RawCallSite(
            methodName: "RawSqlAsync",
            filePath: "Test.cs",
            line: 10,
            column: 5,
            uniqueId: "test-eq-1",
            kind: InterceptorKind.RawSqlAsync,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: "User",
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 10, 5, default));

        var site2 = new RawCallSite(
            methodName: "RawSqlAsync",
            filePath: "Test.cs",
            line: 10,
            column: 5,
            uniqueId: "test-eq-1",
            kind: InterceptorKind.RawSqlAsync,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: "User",
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 10, 5, default));

        // Set RawSqlTypeInfo on one but not the other
        site1.RawSqlTypeInfo = new RawSqlTypeInfo(
            "User", RawSqlTypeKind.Dto,
            new[] { new RawSqlPropertyInfo("UserId", "int", "GetInt32", false) });

        Assert.That(site1.Equals(site2), Is.True,
            "RawCallSite.Equals should ignore RawSqlTypeInfo differences");
    }

    #endregion

    #region Helper Methods

    private static TranslatedCallSite CreateRawSqlCallSite(
        InterceptorKind kind,
        string resultType,
        RawSqlTypeInfo rawSqlTypeInfo,
        string uniqueId = "test123")
    {
        return new TestCallSiteBuilder()
            .WithMethodName(kind == InterceptorKind.RawSqlAsync ? "RawSqlAsync" : "RawSqlScalarAsync")
            .WithKind(kind)
            .WithEntityType(resultType)
            .WithRawSqlTypeInfo(rawSqlTypeInfo)
            .WithUniqueId(uniqueId)
            .Build();
    }

    #endregion
}
