using NUnit.Framework;
using Quarry.Generators.CodeGen;

namespace Quarry.Tests.IR;

[TestFixture]
public class CarrierAnalyzerTests
{
    #region NormalizeFieldType

    [TestCase("int", ExpectedResult = "int")]
    [TestCase("long", ExpectedResult = "long")]
    [TestCase("byte", ExpectedResult = "byte")]
    [TestCase("decimal", ExpectedResult = "decimal")]
    [TestCase("bool", ExpectedResult = "bool")]
    [TestCase("DateTime", ExpectedResult = "DateTime")]
    [TestCase("Guid", ExpectedResult = "Guid")]
    [TestCase("TimeSpan", ExpectedResult = "TimeSpan")]
    [TestCase("DateOnly", ExpectedResult = "DateOnly")]
    [TestCase("TimeOnly", ExpectedResult = "TimeOnly")]
    public string NormalizeFieldType_ValueTypes_Unchanged(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("string", ExpectedResult = "string?")]
    [TestCase("object", ExpectedResult = "object?")]
    [TestCase("?", ExpectedResult = "object?")]
    public string NormalizeFieldType_SimpleReferenceTypes_AppendNullable(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("byte[]", ExpectedResult = "byte[]?")]
    [TestCase("int[]", ExpectedResult = "int[]?")]
    [TestCase("string[]", ExpectedResult = "string[]?")]
    public string NormalizeFieldType_Arrays_AppendNullable(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("IReadOnlyList<int>", ExpectedResult = "IReadOnlyList<int>?")]
    [TestCase("List<string>", ExpectedResult = "List<string>?")]
    [TestCase("Dictionary<string, int>", ExpectedResult = "Dictionary<string, int>?")]
    public string NormalizeFieldType_GenericReferenceTypes_AppendNullable(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("System.Int32", ExpectedResult = "System.Int32")]
    [TestCase("System.DateTime", ExpectedResult = "System.DateTime")]
    [TestCase("System.Guid", ExpectedResult = "System.Guid")]
    public string NormalizeFieldType_QualifiedValueTypes_Unchanged(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("System.String", ExpectedResult = "System.String?")]
    [TestCase("MyApp.Models.User", ExpectedResult = "MyApp.Models.User?")]
    public string NormalizeFieldType_QualifiedReferenceTypes_AppendNullable(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("int?", ExpectedResult = "int?")]
    [TestCase("string?", ExpectedResult = "string?")]
    [TestCase("byte[]?", ExpectedResult = "byte[]?")]
    [TestCase("DateTime?", ExpectedResult = "DateTime?")]
    public string NormalizeFieldType_AlreadyNullable_Unchanged(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    [TestCase("Nullable<int>", ExpectedResult = "int?")]
    [TestCase("System.Nullable<DateTime>", ExpectedResult = "DateTime?")]
    public string NormalizeFieldType_ExplicitNullable_Normalized(string typeName)
    {
        return CarrierAnalyzer.NormalizeFieldType(typeName);
    }

    #endregion

    #region IsReferenceTypeName

    [TestCase("string", ExpectedResult = true)]
    [TestCase("byte[]", ExpectedResult = true)]
    [TestCase("int[]", ExpectedResult = true)]
    [TestCase("IReadOnlyList<int>", ExpectedResult = true)]
    [TestCase("System.String", ExpectedResult = true)]
    [TestCase("MyApp.User", ExpectedResult = true)]
    public bool IsReferenceTypeName_ReferenceTypes_ReturnsTrue(string typeName)
    {
        return CarrierAnalyzer.IsReferenceTypeName(typeName);
    }

    [TestCase("string?", ExpectedResult = true)]
    [TestCase("byte[]?", ExpectedResult = true)]
    public bool IsReferenceTypeName_NullableReferenceTypes_ReturnsTrue(string typeName)
    {
        return CarrierAnalyzer.IsReferenceTypeName(typeName);
    }

    [TestCase("int", ExpectedResult = false)]
    [TestCase("byte", ExpectedResult = false)]
    [TestCase("decimal", ExpectedResult = false)]
    [TestCase("bool", ExpectedResult = false)]
    [TestCase("DateTime", ExpectedResult = false)]
    [TestCase("Guid", ExpectedResult = false)]
    [TestCase("TimeSpan", ExpectedResult = false)]
    [TestCase("System.Int32", ExpectedResult = false)]
    [TestCase("System.DateTime", ExpectedResult = false)]
    public bool IsReferenceTypeName_ValueTypes_ReturnsFalse(string typeName)
    {
        return CarrierAnalyzer.IsReferenceTypeName(typeName);
    }

    [TestCase("int?", ExpectedResult = false)]
    [TestCase("DateTime?", ExpectedResult = false)]
    [TestCase("Nullable<int>", ExpectedResult = false)]
    [TestCase("System.Nullable<DateTime>", ExpectedResult = false)]
    public bool IsReferenceTypeName_NullableValueTypes_ReturnsFalse(string typeName)
    {
        return CarrierAnalyzer.IsReferenceTypeName(typeName);
    }

    #endregion
}
