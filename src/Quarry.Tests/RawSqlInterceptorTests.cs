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

        // Assert — struct-based row reader
        Assert.That(result, Does.Contain("IAsyncEnumerable<UserDto>"));
        Assert.That(result, Does.Contain("this QuarryContext self"));
        Assert.That(result, Does.Contain("RawSqlAsyncWithReader<UserDto, RawSqlReader_UserDto_0>"));
        Assert.That(result, Does.Contain("file struct RawSqlReader_UserDto_0 : IRowReader<UserDto>"));
        Assert.That(result, Does.Contain("var item = new UserDto()"));
        // Resolve: ordinal discovery
        Assert.That(result, Does.Contain("case \"name\": _ord0 = i; break;"));
        Assert.That(result, Does.Contain("case \"email\": _ord1 = i; break;"));
        // Read: non-nullable Name has no IsDBNull guard
        Assert.That(result, Does.Contain("if (_ord0 >= 0) item.Name = r.GetString(_ord0);"));
        // Read: nullable Email has IsDBNull guard
        Assert.That(result, Does.Contain("if (_ord1 >= 0 && !r.IsDBNull(_ord1)) item.Email = r.GetString(_ord1);"));
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
        Assert.That(result, Does.Contain("IAsyncEnumerable<int>"));
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
        Assert.That(result, Does.Contain("IAsyncEnumerable<string>"));
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

        // Assert — enum cast uses cached ordinal in struct Read method
        Assert.That(result, Does.Contain("(UserStatus)r.GetInt32(_ord1)"));
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

        // Assert — FK wrapper uses cached ordinal in struct Read method
        Assert.That(result, Does.Contain("new EntityRef<User, int>(r.GetInt32(_ord1))"));
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

        // Assert — custom type mapping uses cached ordinal in struct Read method
        Assert.That(result, Does.Contain("new MoneyMapping().FromDb(r.GetDecimal(_ord1))"));
    }

    [Test]
    public void RawSqlAsync_EntityType_WithByteArrayProperty_GeneratesGetFieldValue()
    {
        // Arrange
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "Package",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Id", "long", "GetInt64", false),
                new RawSqlPropertyInfo("Password", "byte[]", "GetFieldValue<byte[]>", true,
                    fullClrType: "byte[]")
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "Package", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert — should use typed GetFieldValue with cached ordinal, not bare GetValue
        Assert.That(result, Does.Contain("r.GetFieldValue<byte[]>(_ord1)"));
        Assert.That(result, Does.Not.Contain("r.GetValue(_ord"));
    }

    [Test]
    public void RawSqlAsync_EntityType_WithNullableByteArrayProperty_GeneratesGetFieldValue()
    {
        // Arrange — nullable byte[]? column (e.g., Col<byte[]?> Password)
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "Package",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Id", "long", "GetInt64", false),
                new RawSqlPropertyInfo("Password", "byte[]", "GetFieldValue<byte[]>", true,
                    fullClrType: "byte[]")
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "Package", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert — nullable byte[]? should use GetFieldValue with IsDBNull guard in struct Read method
        Assert.That(result, Does.Contain("if (_ord1 >= 0 && !r.IsDBNull(_ord1)) item.Password = r.GetFieldValue<byte[]>(_ord1);"));
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

        // Assert — struct Read method: non-nullable has no IsDBNull, nullable has IsDBNull guard
        Assert.That(result, Does.Contain("if (_ord0 >= 0) item.Name = r.GetString(_ord0);"));
        Assert.That(result, Does.Contain("if (_ord1 >= 0 && !r.IsDBNull(_ord1)) item.MiddleName = r.GetString(_ord1);"));
        Assert.That(result, Does.Contain("if (_ord2 >= 0 && !r.IsDBNull(_ord2)) item.Age = r.GetInt32(_ord2);"));
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

    [Test]
    public void GenerateInterceptorsFile_MultipleDtoSites_GeneratesUniqueStructNames()
    {
        // Arrange — two DTO sites for different types in the same file
        var userSite = CreateRawSqlCallSite(
            InterceptorKind.RawSqlAsync, "UserDto",
            new RawSqlTypeInfo("UserDto", RawSqlTypeKind.Dto,
                new[] { new RawSqlPropertyInfo("Name", "string", "GetString", false) }),
            uniqueId: "site_user");

        var orderSite = CreateRawSqlCallSite(
            InterceptorKind.RawSqlAsync, "OrderDto",
            new RawSqlTypeInfo("OrderDto", RawSqlTypeKind.Dto,
                new[] { new RawSqlPropertyInfo("Total", "decimal", "GetDecimal", false) }),
            uniqueId: "site_order");

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { userSite, orderSite });

        // Assert — each DTO site gets a uniquely named struct
        Assert.That(result, Does.Contain("file struct RawSqlReader_UserDto_0 : IRowReader<UserDto>"));
        Assert.That(result, Does.Contain("file struct RawSqlReader_OrderDto_1 : IRowReader<OrderDto>"));
        Assert.That(result, Does.Contain("RawSqlAsyncWithReader<UserDto, RawSqlReader_UserDto_0>"));
        Assert.That(result, Does.Contain("RawSqlAsyncWithReader<OrderDto, RawSqlReader_OrderDto_1>"));
    }

    [Test]
    public void RawSqlAsync_AllNonNullableDto_NoIsDBNullGuards()
    {
        // Arrange — all properties are non-nullable
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "StrictDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Id", "int", "GetInt32", false),
                new RawSqlPropertyInfo("Name", "string", "GetString", false),
                new RawSqlPropertyInfo("Value", "decimal", "GetDecimal", false)
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "StrictDto", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert — no IsDBNull guards since all properties are non-nullable
        Assert.That(result, Does.Not.Contain("IsDBNull"));
        Assert.That(result, Does.Contain("if (_ord0 >= 0) item.Id = r.GetInt32(_ord0);"));
        Assert.That(result, Does.Contain("if (_ord1 >= 0) item.Name = r.GetString(_ord1);"));
        Assert.That(result, Does.Contain("if (_ord2 >= 0) item.Value = r.GetDecimal(_ord2);"));
    }

    [Test]
    public void RawSqlAsync_DtoType_WithGetValueFallback_GeneratesCast()
    {
        // Arrange — simulate an unknown type that falls through to GetValue
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "CustomDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Data", "SomeCustomType", "GetValue", false,
                    fullClrType: "MyNamespace.SomeCustomType")
            });

        var site = CreateRawSqlCallSite(InterceptorKind.RawSqlAsync, "CustomDto", rawSqlTypeInfo);

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        // Assert — GetValue result must be cast to the target type, using cached ordinal
        Assert.That(result, Does.Contain("(MyNamespace.SomeCustomType)r.GetValue(_ord0)"));
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
        Assert.That(result, Does.Not.Contain("switch (r.GetName(i).ToLowerInvariant())"));
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

        // Struct Resolve: ordinal discovery for all properties
        Assert.That(result, Does.Contain("case \"id\": _ord0 = i; break;"));
        Assert.That(result, Does.Contain("case \"name\": _ord1 = i; break;"));
        Assert.That(result, Does.Contain("case \"createdat\": _ord2 = i; break;"));
        Assert.That(result, Does.Contain("case \"isactive\": _ord3 = i; break;"));
        Assert.That(result, Does.Contain("case \"balance\": _ord4 = i; break;"));
        Assert.That(result, Does.Contain("case \"notes\": _ord5 = i; break;"));
        // Struct Read: typed reads with cached ordinals (Notes is nullable, gets IsDBNull)
        Assert.That(result, Does.Contain("item.Id = r.GetInt32(_ord0)"));
        Assert.That(result, Does.Contain("item.Name = r.GetString(_ord1)"));
        Assert.That(result, Does.Contain("item.CreatedAt = r.GetDateTime(_ord2)"));
        Assert.That(result, Does.Contain("item.IsActive = r.GetBoolean(_ord3)"));
        Assert.That(result, Does.Contain("item.Balance = r.GetDecimal(_ord4)"));
        Assert.That(result, Does.Contain("!r.IsDBNull(_ord5)) item.Notes = r.GetString(_ord5)"));
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
