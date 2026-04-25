using System.Collections.Generic;
using NUnit.Framework;
using Quarry.Generators.CodeGen;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

namespace Quarry.Tests.IR;

/// <summary>
/// Pins the dedup invariant for <see cref="FileEmitter.CarrierStructuralKey"/>: same-shape carriers
/// MUST NOT merge when their captured-variable extractors differ in <c>VariableName</c>
/// or <c>DisplayClassName</c>. Relaxing the key to types-only would re-introduce
/// issue #268 (chained-<c>With&lt;&gt;</c> dispatch routing to the wrong closure-field
/// extractor).
/// </summary>
[TestFixture]
public class CarrierStructuralKeyTests
{
    private const string DisplayClassA = "TestApp.Outer+<>c__DisplayClass3_0";
    private const string DisplayClassB = "TestApp.Outer+<>c__DisplayClass4_0";

    private static IReadOnlyList<CarrierField> SharedFields() => new[]
    {
        new CarrierField("P0", "decimal", FieldRole.Parameter),
        new CarrierField("P1", "bool", FieldRole.Parameter),
    };

    private static Dictionary<int, AssembledSqlVariant> SharedSqlVariants() => new()
    {
        [0] = new AssembledSqlVariant("SELECT 1", 0),
    };

    private static string[] SharedInterfaces() => new[]
    {
        "IEntityAccessor<Foo>",
        "IQueryBuilder<Foo>",
    };

    private static CapturedVariableExtractor MakeExtractor(
        string variableName,
        string variableType,
        string displayClassName,
        int clauseIndex)
    {
        return new CapturedVariableExtractor(
            methodName: $"__ExtractVar_{variableName}_{clauseIndex}",
            variableName: variableName,
            variableType: variableType,
            displayClassName: displayClassName,
            captureKind: CaptureKind.ClosureCapture,
            isStaticField: false);
    }

    private static FileEmitter.CarrierStructuralKey BuildKey(IReadOnlyList<CapturedVariableExtractor> extractors)
    {
        return new FileEmitter.CarrierStructuralKey(
            fields: SharedFields(),
            maskType: null,
            maskBitCount: 0,
            extractors: extractors,
            sqlVariants: SharedSqlVariants(),
            readerDelegateCode: null,
            hasCollectionParam: false,
            interfaces: SharedInterfaces());
    }

    [Test]
    public void DifferentVariableNames_DoNotMerge()
    {
        var keyA = BuildKey(new[]
        {
            MakeExtractor("cutoff", "decimal", DisplayClassA, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassA, 1),
        });
        var keyB = BuildKey(new[]
        {
            MakeExtractor("orderCutoff", "decimal", DisplayClassA, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassA, 1),
        });

        Assert.That(keyA.Equals(keyB), Is.False,
            "Carriers whose first extractor differs only in variable name (cutoff vs orderCutoff) "
            + "must produce distinct dedup keys; otherwise the second carrier's interceptor would "
            + "reference an [UnsafeAccessor] reading 'cutoff' from a closure that has 'orderCutoff' "
            + "(issue #268).");
    }

    [Test]
    public void DifferentDisplayClassNames_DoNotMerge()
    {
        var keyA = BuildKey(new[]
        {
            MakeExtractor("orderCutoff", "decimal", DisplayClassA, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassA, 1),
        });
        var keyB = BuildKey(new[]
        {
            MakeExtractor("orderCutoff", "decimal", DisplayClassB, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassB, 1),
        });

        Assert.That(keyA.Equals(keyB), Is.False,
            "Carriers whose extractors target different compiler-generated display classes must "
            + "produce distinct dedup keys; otherwise the second carrier's [UnsafeAccessor] would "
            + "be typed against a foreign closure type.");
    }

    [Test]
    public void DifferentVariableTypes_DoNotMerge()
    {
        var keyA = BuildKey(new[]
        {
            MakeExtractor("orderCutoff", "decimal", DisplayClassA, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassA, 1),
        });
        var keyB = BuildKey(new[]
        {
            MakeExtractor("orderCutoff", "int", DisplayClassA, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassA, 1),
        });

        Assert.That(keyA.Equals(keyB), Is.False,
            "Carriers whose extractors share name + display class but differ in CLR type "
            + "(decimal vs int) must produce distinct dedup keys; the carrier P0 field "
            + "type and the [UnsafeAccessor] return type both depend on it, so a merge "
            + "would emit an extractor with the wrong return signature for the second site.");
    }

    [Test]
    public void IdenticalCarriers_DoMerge()
    {
        var extractors = new[]
        {
            MakeExtractor("orderCutoff", "decimal", DisplayClassA, 0),
            MakeExtractor("activeFilter", "bool", DisplayClassA, 1),
        };

        var keyA = BuildKey(extractors);
        var keyB = BuildKey(extractors);

        Assert.That(keyA.Equals(keyB), Is.True,
            "Two byte-identical carrier inputs must produce equal dedup keys; this confirms the "
            + "above non-merge results aren't false positives from unrelated state.");
        Assert.That(keyA.GetHashCode(), Is.EqualTo(keyB.GetHashCode()));
    }
}
