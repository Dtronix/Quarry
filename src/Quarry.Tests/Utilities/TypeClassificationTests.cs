using NUnit.Framework;
using Quarry.Generators.Models;
using Quarry.Generators.Utilities;

namespace Quarry.Tests.Utilities;

[TestFixture]
public class TypeClassificationTests
{
    #region IsValueType

    // C# keyword types
    [TestCase("int", ExpectedResult = true)]
    [TestCase("bool", ExpectedResult = true)]
    [TestCase("byte", ExpectedResult = true)]
    [TestCase("sbyte", ExpectedResult = true)]
    [TestCase("short", ExpectedResult = true)]
    [TestCase("ushort", ExpectedResult = true)]
    [TestCase("uint", ExpectedResult = true)]
    [TestCase("long", ExpectedResult = true)]
    [TestCase("ulong", ExpectedResult = true)]
    [TestCase("float", ExpectedResult = true)]
    [TestCase("double", ExpectedResult = true)]
    [TestCase("decimal", ExpectedResult = true)]
    [TestCase("char", ExpectedResult = true)]
    [TestCase("nint", ExpectedResult = true)]
    [TestCase("nuint", ExpectedResult = true)]
    // BCL names
    [TestCase("Int32", ExpectedResult = true)]
    [TestCase("Boolean", ExpectedResult = true)]
    [TestCase("String", ExpectedResult = false)]
    // Qualified names
    [TestCase("System.Int32", ExpectedResult = true)]
    [TestCase("System.String", ExpectedResult = false)]
    [TestCase("System.DateTime", ExpectedResult = true)]
    // Nullable value types
    [TestCase("int?", ExpectedResult = true)]
    [TestCase("DateTime?", ExpectedResult = true)]
    // Reference types
    [TestCase("string", ExpectedResult = false)]
    [TestCase("string?", ExpectedResult = false)]
    // Tuples
    [TestCase("(int, string)", ExpectedResult = true)]
    [TestCase("(int, (bool, decimal))", ExpectedResult = true)]
    // Date/time types
    [TestCase("DateTime", ExpectedResult = true)]
    [TestCase("DateTimeOffset", ExpectedResult = true)]
    [TestCase("TimeSpan", ExpectedResult = true)]
    [TestCase("DateOnly", ExpectedResult = true)]
    [TestCase("TimeOnly", ExpectedResult = true)]
    // Other
    [TestCase("Guid", ExpectedResult = true)]
    [TestCase("byte[]", ExpectedResult = false)]
    [TestCase("object", ExpectedResult = false)]
    // Edge cases
    [TestCase(null, ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    [TestCase("MyStruct", ExpectedResult = false)]
    public bool IsValueType_Tests(string? typeName) => TypeClassification.IsValueType(typeName);

    #endregion

    #region IsReferenceType

    [TestCase("string", ExpectedResult = true)]
    [TestCase("int", ExpectedResult = false)]
    [TestCase("byte[]", ExpectedResult = true)]
    [TestCase("Nullable<int>", ExpectedResult = false)]
    [TestCase("System.Nullable<int>", ExpectedResult = false)]
    [TestCase("(int, string)", ExpectedResult = false)]
    [TestCase("DateTime", ExpectedResult = false)]
    [TestCase("System.Int32", ExpectedResult = false)]
    [TestCase("MyClass", ExpectedResult = true, Description = "Unknown defaults to reference type")]
    [TestCase("string?", ExpectedResult = true)]
    public bool IsReferenceType_Tests(string typeName) => TypeClassification.IsReferenceType(typeName);

    #endregion

    #region IsNonNullableValueType

    [TestCase("int", ExpectedResult = true)]
    [TestCase("int?", ExpectedResult = false)]
    [TestCase("(int, string)", ExpectedResult = true)]
    [TestCase("DateTime", ExpectedResult = true)]
    [TestCase("System.DateTime", ExpectedResult = true)]
    [TestCase("string", ExpectedResult = false)]
    [TestCase("DateTime?", ExpectedResult = false)]
    [TestCase("Guid", ExpectedResult = true)]
    public bool IsNonNullableValueType_Tests(string typeName) => TypeClassification.IsNonNullableValueType(typeName);

    #endregion

    #region GetReaderMethod

    // Signed integers
    [TestCase("int", ExpectedResult = "GetInt32")]
    [TestCase("Int32", ExpectedResult = "GetInt32")]
    [TestCase("System.Int32", ExpectedResult = "GetInt32")]
    [TestCase("short", ExpectedResult = "GetInt16")]
    [TestCase("long", ExpectedResult = "GetInt64")]
    [TestCase("byte", ExpectedResult = "GetByte")]
    // Unsigned with sign cast
    [TestCase("uint", ExpectedResult = "GetInt32")]
    [TestCase("ushort", ExpectedResult = "GetInt16")]
    [TestCase("ulong", ExpectedResult = "GetInt64")]
    [TestCase("sbyte", ExpectedResult = "GetByte")]
    // Float/double/decimal
    [TestCase("float", ExpectedResult = "GetFloat")]
    [TestCase("double", ExpectedResult = "GetDouble")]
    [TestCase("decimal", ExpectedResult = "GetDecimal")]
    // String/bool/char
    [TestCase("string", ExpectedResult = "GetString")]
    [TestCase("bool", ExpectedResult = "GetBoolean")]
    [TestCase("char", ExpectedResult = "GetChar")]
    // Special types
    [TestCase("Guid", ExpectedResult = "GetGuid")]
    [TestCase("DateTime", ExpectedResult = "GetDateTime")]
    // GetFieldValue types
    [TestCase("DateTimeOffset", ExpectedResult = "GetFieldValue<DateTimeOffset>")]
    [TestCase("System.DateTimeOffset", ExpectedResult = "GetFieldValue<DateTimeOffset>")]
    [TestCase("TimeSpan", ExpectedResult = "GetFieldValue<TimeSpan>")]
    [TestCase("DateOnly", ExpectedResult = "GetFieldValue<DateOnly>")]
    [TestCase("TimeOnly", ExpectedResult = "GetFieldValue<TimeOnly>")]
    // Nullable stripping
    [TestCase("int?", ExpectedResult = "GetInt32")]
    [TestCase("DateTimeOffset?", ExpectedResult = "GetFieldValue<DateTimeOffset>")]
    // Fallback
    [TestCase("MyCustomType", ExpectedResult = "GetValue")]
    [TestCase("byte[]", ExpectedResult = "GetValue")]
    public string GetReaderMethod_Tests(string clrType) => TypeClassification.GetReaderMethod(clrType);

    #endregion

    #region NeedsSignCast

    [TestCase("uint", ExpectedResult = true)]
    [TestCase("UInt32", ExpectedResult = true)]
    [TestCase("System.UInt32", ExpectedResult = true)]
    [TestCase("ushort", ExpectedResult = true)]
    [TestCase("ulong", ExpectedResult = true)]
    [TestCase("sbyte", ExpectedResult = true)]
    [TestCase("SByte", ExpectedResult = true)]
    [TestCase("int", ExpectedResult = false)]
    [TestCase("long", ExpectedResult = false)]
    [TestCase("byte", ExpectedResult = false)]
    [TestCase("string", ExpectedResult = false)]
    public bool NeedsSignCast_Tests(string clrType) => TypeClassification.NeedsSignCast(clrType);

    #endregion

    #region IsUnresolvedTypeName (strict)

    [TestCase(null, ExpectedResult = true)]
    [TestCase("", ExpectedResult = true)]
    [TestCase(" ", ExpectedResult = true)]
    [TestCase("?", ExpectedResult = true)]
    [TestCase("object", ExpectedResult = true)]
    [TestCase("? SomeError", ExpectedResult = true)]
    [TestCase("string", ExpectedResult = false)]
    [TestCase("int", ExpectedResult = false)]
    [TestCase("MyType", ExpectedResult = false)]
    public bool IsUnresolvedTypeName_Strict_Tests(string? typeName) => TypeClassification.IsUnresolvedTypeName(typeName);

    #endregion

    #region IsUnresolvedTypeNameLenient

    [TestCase(null, ExpectedResult = true)]
    [TestCase("", ExpectedResult = true)]
    [TestCase(" ", ExpectedResult = true)]
    [TestCase("?", ExpectedResult = true)]
    [TestCase("? SomeError", ExpectedResult = true)]
    [TestCase("object", ExpectedResult = false, Description = "object is valid in lenient mode")]
    [TestCase("string", ExpectedResult = false)]
    [TestCase("int", ExpectedResult = false)]
    public bool IsUnresolvedTypeNameLenient_Tests(string? typeName) => TypeClassification.IsUnresolvedTypeNameLenient(typeName);

    #endregion

    #region IsUnresolvedResultType

    [TestCase(null, ExpectedResult = false, Description = "null means no result type — valid")]
    [TestCase("", ExpectedResult = true)]
    [TestCase("?", ExpectedResult = true)]
    [TestCase("object", ExpectedResult = true)]
    [TestCase("int", ExpectedResult = false)]
    [TestCase("string", ExpectedResult = false)]
    [TestCase("UserDto", ExpectedResult = false)]
    // Simple tuples
    [TestCase("(int, decimal, OrderPriority)", ExpectedResult = false)]
    [TestCase("(object, object, object)", ExpectedResult = true)]
    // Named tuples
    [TestCase("(int OrderId, string UserName)", ExpectedResult = false)]
    [TestCase("(object OrderId, object UserName)", ExpectedResult = true)]
    // Empty type parts
    [TestCase("( OrderId,  Total,  Priority)", ExpectedResult = true)]
    [TestCase("( OrderId)", ExpectedResult = true)]
    // Nested tuples
    [TestCase("(int, (string, object))", ExpectedResult = true)]
    [TestCase("(int, (string, decimal))", ExpectedResult = false)]
    [TestCase("((object, int), string)", ExpectedResult = true)]
    [TestCase("(int, (string, decimal) Named)", ExpectedResult = false)]
    public bool IsUnresolvedResultType_Tests(string? resultTypeName) => TypeClassification.IsUnresolvedResultType(resultTypeName);

    #endregion

    #region BuildTupleTypeName

    [Test]
    public void BuildTupleTypeName_SingleColumn()
    {
        var columns = new[] { MakeColumn("int", "Item1", 0) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns), Is.EqualTo("(int)"));
    }

    [Test]
    public void BuildTupleTypeName_MultiColumn()
    {
        var columns = new[] { MakeColumn("int", "Item1", 0), MakeColumn("string", "Item2", 1) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns), Is.EqualTo("(int, string)"));
    }

    [Test]
    public void BuildTupleTypeName_NullableColumn()
    {
        var columns = new[] { MakeColumn("int", "Item1", 0, isNullable: true) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns), Is.EqualTo("(int?)"));
    }

    [Test]
    public void BuildTupleTypeName_NamedElements()
    {
        var columns = new[] { MakeColumn("int", "OrderId", 0) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns), Is.EqualTo("(int OrderId)"));
    }

    [Test]
    public void BuildTupleTypeName_ItemNElision()
    {
        // Item1 at ordinal 0 should be elided
        var columns = new[] { MakeColumn("int", "Item1", 0), MakeColumn("string", "Name", 1) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns), Is.EqualTo("(int, string Name)"));
    }

    [Test]
    public void BuildTupleTypeName_UnresolvedWithFallback()
    {
        var columns = new[] { MakeColumn("?", "Item1", 0) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns, fallbackToObject: true), Is.EqualTo("(object)"));
    }

    [Test]
    public void BuildTupleTypeName_UnresolvedWithoutFallback()
    {
        var columns = new[] { MakeColumn("?", "Item1", 0) };
        Assert.That(TypeClassification.BuildTupleTypeName(columns, fallbackToObject: false), Is.EqualTo(""));
    }

    private static ProjectedColumn MakeColumn(string clrType, string propertyName, int ordinal,
        bool isNullable = false, string? fullClrType = null)
    {
        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: propertyName,
            clrType: clrType,
            fullClrType: fullClrType ?? clrType,
            isNullable: isNullable,
            ordinal: ordinal);
    }

    #endregion
}
