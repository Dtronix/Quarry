using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Projection;
using Quarry.Tests.Testing;

namespace Quarry.Tests;

/// <summary>
/// Tests that generated reader code emits explicit casts for types whose
/// DbDataReader method returns a differently-signed CLR type:
///   uint  ← GetInt32   (signed → unsigned)
///   ushort ← GetInt16  (signed → unsigned)
///   ulong ← GetInt64   (signed → unsigned)
///   sbyte ← GetByte    (unsigned → signed)
/// </summary>
[TestFixture]
public class SignCastReaderTests
{
    #region ReaderCodeGenerator — Entity / DTO Projections

    [TestCase("uint", "GetInt32")]
    [TestCase("ushort", "GetInt16")]
    [TestCase("ulong", "GetInt64")]
    [TestCase("sbyte", "GetByte")]
    public void EntityReader_SignMismatchColumn_EmitsCast(string clrType, string readerMethod)
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "TestEntity",
            new[]
            {
                new ProjectedColumn(
                    propertyName: "Value",
                    columnName: "value",
                    clrType: clrType,
                    fullClrType: clrType,
                    isNullable: false,
                    ordinal: 0,
                    isValueType: true,
                    readerMethodName: readerMethod)
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "TestEntity");

        Assert.That(readerCode, Does.Contain($"({clrType})r.{readerMethod}(0)"),
            $"Should cast {readerMethod}() result to {clrType}");
    }

    [TestCase("uint", "GetInt32")]
    [TestCase("ushort", "GetInt16")]
    [TestCase("ulong", "GetInt64")]
    [TestCase("sbyte", "GetByte")]
    public void EntityReader_NullableSignMismatchColumn_EmitsCastWithNullCheck(string clrType, string readerMethod)
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "TestEntity",
            new[]
            {
                new ProjectedColumn(
                    propertyName: "Value",
                    columnName: "value",
                    clrType: clrType,
                    fullClrType: clrType,
                    isNullable: true,
                    ordinal: 0,
                    isValueType: true,
                    readerMethodName: readerMethod)
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "TestEntity");

        Assert.That(readerCode, Does.Contain($"({clrType})r.{readerMethod}(0)"),
            $"Should cast {readerMethod}() result to {clrType} even for nullable");
        Assert.That(readerCode, Does.Contain($"r.IsDBNull(0)"),
            "Should include null check for nullable column");
        Assert.That(readerCode, Does.Contain($"default({clrType}?)"),
            "Should use nullable default for null case");
    }

    [Test]
    public void EntityReader_SignedColumn_DoesNotEmitCast()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Entity,
            "TestEntity",
            new[]
            {
                new ProjectedColumn(
                    propertyName: "Value",
                    columnName: "value",
                    clrType: "int",
                    fullClrType: "int",
                    isNullable: false,
                    ordinal: 0,
                    isValueType: true,
                    readerMethodName: "GetInt32")
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "TestEntity");

        Assert.That(readerCode, Does.Contain("r.GetInt32(0)"));
        Assert.That(readerCode, Does.Not.Contain("(int)r.GetInt32(0)"),
            "Signed types should not get an unnecessary cast");
    }

    #endregion

    #region ReaderCodeGenerator — SingleColumn Projections

    [TestCase("uint", "GetInt32")]
    [TestCase("ushort", "GetInt16")]
    [TestCase("ulong", "GetInt64")]
    [TestCase("sbyte", "GetByte")]
    public void SingleColumnReader_SignMismatchColumn_EmitsCast(string clrType, string readerMethod)
    {
        var projection = new ProjectionInfo(
            ProjectionKind.SingleColumn,
            clrType,
            new[]
            {
                new ProjectedColumn(
                    propertyName: "Value",
                    columnName: "value",
                    clrType: clrType,
                    fullClrType: clrType,
                    isNullable: false,
                    ordinal: 0,
                    isValueType: true,
                    readerMethodName: readerMethod)
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "TestEntity");

        Assert.That(readerCode, Does.Contain($"({clrType})r.{readerMethod}(0)"),
            $"Single column reader should cast {readerMethod}() to {clrType}");
    }

    #endregion

    #region ReaderCodeGenerator — Tuple Projections

    [Test]
    public void TupleReader_MixedSignTypes_EmitsCastsWhereNeeded()
    {
        var projection = new ProjectionInfo(
            ProjectionKind.Tuple,
            "(uint, int)",
            new[]
            {
                new ProjectedColumn("Item1", "unsigned_col", "uint", "uint",
                    isNullable: false, ordinal: 0, isValueType: true, readerMethodName: "GetInt32"),
                new ProjectedColumn("Item2", "signed_col", "int", "int",
                    isNullable: false, ordinal: 1, isValueType: true, readerMethodName: "GetInt32"),
            });

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "TestEntity");

        Assert.That(readerCode, Does.Contain("(uint)r.GetInt32(0)"),
            "First tuple element (uint) should have cast");
        Assert.That(readerCode, Does.Contain("r.GetInt32(1)"),
            "Second tuple element (int) should not have cast");
        Assert.That(readerCode, Does.Not.Contain("(int)r.GetInt32(1)"),
            "Signed int should not get unnecessary cast");
    }

    #endregion

    #region RawSqlBodyEmitter — Scalar Reader Cast

    [TestCase("uint", "GetInt32")]
    [TestCase("ushort", "GetInt16")]
    [TestCase("ulong", "GetInt64")]
    [TestCase("sbyte", "GetByte")]
    public void RawSqlAsync_ScalarSignMismatch_EmitsCast(string clrType, string readerMethod)
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            clrType,
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: readerMethod);

        var site = new TestCallSiteBuilder()
            .WithMethodName("RawSqlAsync")
            .WithKind(InterceptorKind.RawSqlAsync)
            .WithEntityType(clrType)
            .WithRawSqlTypeInfo(rawSqlTypeInfo)
            .WithUniqueId("test_uint")
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain($"({clrType})r.{readerMethod}(0)"),
            $"RawSqlAsync scalar reader should cast {readerMethod}() to {clrType}");
    }

    [Test]
    public void RawSqlAsync_ScalarInt_DoesNotEmitCast()
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "int",
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetInt32");

        var site = new TestCallSiteBuilder()
            .WithMethodName("RawSqlAsync")
            .WithKind(InterceptorKind.RawSqlAsync)
            .WithEntityType("int")
            .WithRawSqlTypeInfo(rawSqlTypeInfo)
            .WithUniqueId("test_int")
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("r.GetInt32(0)"));
        Assert.That(result, Does.Not.Contain("(int)r.GetInt32(0)"),
            "Signed int scalar should not get unnecessary cast");
    }

    #endregion

    #region RawSqlBodyEmitter — DTO Property Cast

    [Test]
    public void RawSqlAsync_DtoWithUintProperty_EmitsCast()
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            "NetworkDto",
            RawSqlTypeKind.Dto,
            new[]
            {
                new RawSqlPropertyInfo("Ipv4", "uint", "GetInt32", false),
                new RawSqlPropertyInfo("Name", "string", "GetString", false),
            });

        var site = new TestCallSiteBuilder()
            .WithMethodName("RawSqlAsync")
            .WithKind(InterceptorKind.RawSqlAsync)
            .WithEntityType("NetworkDto")
            .WithRawSqlTypeInfo(rawSqlTypeInfo)
            .WithUniqueId("test_dto_uint")
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("case \"Ipv4\": item.Ipv4 = (uint)r.GetInt32(i); break;"),
            "uint DTO property should have cast from GetInt32");
        Assert.That(result, Does.Contain("case \"Name\": item.Name = r.GetString(i); break;"),
            "string property should not have cast");
    }

    #endregion

    #region RawSqlScalarAsync — Converter for Unsigned Types

    [TestCase("uint", "(uint)Convert.ToInt64")]
    [TestCase("ushort", "(ushort)Convert.ToInt64")]
    [TestCase("ulong", "(ulong)Convert.ToInt64")]
    public void RawSqlScalarAsync_UnsignedType_NonNullable_GeneratesCorrectConverter(string clrType, string expectedConvert)
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            clrType,
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetInt32");

        var site = new TestCallSiteBuilder()
            .WithMethodName("RawSqlScalarAsync")
            .WithKind(InterceptorKind.RawSqlScalarAsync)
            .WithEntityType(clrType)
            .WithRawSqlTypeInfo(rawSqlTypeInfo)
            .WithUniqueId("test_scalar_uint")
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"));
        Assert.That(result, Does.Contain(expectedConvert),
            $"Scalar converter for {clrType} should use {expectedConvert}");
    }

    [TestCase("uint?", "(uint?)Convert.ToUInt32")]
    [TestCase("ushort?", "(ushort?)Convert.ToUInt16")]
    [TestCase("ulong?", "(ulong?)Convert.ToUInt64")]
    public void RawSqlScalarAsync_UnsignedType_Nullable_GeneratesCorrectConverter(string clrType, string expectedConvert)
    {
        var rawSqlTypeInfo = new RawSqlTypeInfo(
            clrType,
            RawSqlTypeKind.Scalar,
            System.Array.Empty<RawSqlPropertyInfo>(),
            scalarReaderMethod: "GetInt32");

        var site = new TestCallSiteBuilder()
            .WithMethodName("RawSqlScalarAsync")
            .WithKind(InterceptorKind.RawSqlScalarAsync)
            .WithEntityType(clrType)
            .WithRawSqlTypeInfo(rawSqlTypeInfo)
            .WithUniqueId("test_scalar_nullable_uint")
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new[] { site });

        Assert.That(result, Does.Contain("RawSqlScalarAsyncWithConverter"));
        Assert.That(result, Does.Contain(expectedConvert),
            $"Nullable scalar converter for {clrType} should use {expectedConvert}");
    }

    #endregion
}
