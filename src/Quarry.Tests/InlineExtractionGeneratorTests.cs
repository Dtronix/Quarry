using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;

namespace Quarry.Tests;

/// <summary>
/// Tests that verify the generated inline parameter extraction code
/// uses Unsafe.As navigation and cached FieldInfo instead of delegate-based extraction.
/// </summary>
[TestFixture]
public class InlineExtractionGeneratorTests
{
    #region Static Field Declaration Tests

    [Test]
    public void GeneratedCode_CapturedParam_DeclaresFieldInfoStaticField()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("private static FieldInfo? _Where_abc123_p0;"));
    }

    [Test]
    public void GeneratedCode_MultipleCapturedParams_DeclaresMultipleFieldInfoFields()
    {
        var result = GenerateWhereInterceptorWithParams(
            ("Body.Left.Right", 0),
            ("Body.Right.Right", 1));

        Assert.That(result, Does.Contain("private static FieldInfo? _Where_abc123_p0;"));
        Assert.That(result, Does.Contain("private static FieldInfo? _Where_abc123_p1;"));
    }

    [Test]
    public void GeneratedCode_NoCapturedParams_NoStaticFields()
    {
        var result = GenerateWhereInterceptorNoCaptured();

        Assert.That(result, Does.Not.Contain("private static FieldInfo?"));
        Assert.That(result, Does.Not.Contain("Cached Extractors"));
    }

    #endregion

    #region Inline Navigation Tests

    [Test]
    public void GeneratedCode_BodyRight_EmitsCorrectUnsafeAsCast()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("var _n0 = Unsafe.As<BinaryExpression>(expr.Body).Right;"));
        Assert.That(result, Does.Contain("_n0 is UnaryExpression _u0 ? Unsafe.As<MemberExpression>(_u0.Operand) : Unsafe.As<MemberExpression>(_n0)"));
    }

    [Test]
    public void GeneratedCode_BodyLeftRight_EmitsNestedCasts()
    {
        var result = GenerateWhereInterceptor("Body.Left.Right");

        Assert.That(result, Does.Contain("var _n0 = Unsafe.As<BinaryExpression>(Unsafe.As<BinaryExpression>(expr.Body).Left).Right;"));
        Assert.That(result, Does.Contain("_n0 is UnaryExpression _u0 ? Unsafe.As<MemberExpression>(_u0.Operand) : Unsafe.As<MemberExpression>(_n0)"));
    }

    [Test]
    public void GeneratedCode_BodyOperandRight_EmitsUnaryThenBinaryCast()
    {
        var result = GenerateWhereInterceptor("Body.Operand.Right");

        Assert.That(result, Does.Contain("Unsafe.As<UnaryExpression>(expr.Body).Operand"));
        Assert.That(result, Does.Contain("Unsafe.As<BinaryExpression>("));
    }

    [Test]
    public void GeneratedCode_BodyArgument0_EmitsMethodCallCast()
    {
        var result = GenerateWhereInterceptor("Body.Arguments[0]");

        Assert.That(result, Does.Contain("Unsafe.As<MethodCallExpression>(expr.Body).Arguments[0]"));
    }

    [Test]
    public void GeneratedCode_BodyObject_EmitsMethodCallObjectAccess()
    {
        var result = GenerateWhereInterceptor("Body.Object");

        Assert.That(result, Does.Contain("Unsafe.As<MethodCallExpression>(expr.Body).Object!"));
    }

    [Test]
    public void GeneratedCode_BodyTest_EmitsConditionalCast()
    {
        var result = GenerateWhereInterceptor("Body.Test");

        Assert.That(result, Does.Contain("Unsafe.As<ConditionalExpression>(expr.Body).Test"));
    }

    [Test]
    public void GeneratedCode_BodyIfTrue_EmitsConditionalIfTrueCast()
    {
        var result = GenerateWhereInterceptor("Body.IfTrue");

        Assert.That(result, Does.Contain("Unsafe.As<ConditionalExpression>(expr.Body).IfTrue"));
    }

    [Test]
    public void GeneratedCode_BodyIfFalse_EmitsConditionalIfFalseCast()
    {
        var result = GenerateWhereInterceptor("Body.IfFalse");

        Assert.That(result, Does.Contain("Unsafe.As<ConditionalExpression>(expr.Body).IfFalse"));
    }

    [Test]
    public void GeneratedCode_BodyExpression_EmitsMemberExpressionCast()
    {
        var result = GenerateWhereInterceptor("Body.Expression");

        Assert.That(result, Does.Contain("Unsafe.As<MemberExpression>(expr.Body).Expression!"));
    }

    #endregion

    #region FieldInfo Caching Tests

    [Test]
    public void GeneratedCode_CapturedParam_CachesFieldInfoFromMember()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("_Where_abc123_p0 ??= Unsafe.As<FieldInfo>(_m0.Member);"));
    }

    [Test]
    public void GeneratedCode_CapturedParam_CallsGetValueWithClosureInstance()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain(".GetValue(Unsafe.As<ConstantExpression>(_m0.Expression!).Value)"));
    }

    [Test]
    public void GeneratedCode_CapturedParam_AssignsToLocalVariable()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("var p0 = _Where_abc123_p0.GetValue("));
    }

    [Test]
    public void GeneratedCode_CapturedParam_PassesLocalToAddWhereClause()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("return __b.AddWhereClause(@\"\"\"id\"\" > @p0\", p0);"));
    }

    #endregion

    #region Trim Suppression Tests

    [Test]
    public void GeneratedCode_WithCapturedParams_EmitsTrimSuppression()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("[UnconditionalSuppressMessage(\"Trimming\", \"IL2075\""));
    }

    [Test]
    public void GeneratedCode_WithoutCapturedParams_NoTrimSuppression()
    {
        var result = GenerateWhereInterceptorNoCaptured();

        Assert.That(result, Does.Not.Contain("UnconditionalSuppressMessage"));
    }

    #endregion

    #region No ExpressionExtractor References

    [Test]
    public void GeneratedCode_DoesNotReferenceExpressionExtractor()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Not.Contain("ExpressionExtractor"));
        Assert.That(result, Does.Not.Contain("BuildExtractor"));
        Assert.That(result, Does.Not.Contain("BuildCachedExtractor"));
    }

    [Test]
    public void GeneratedCode_DoesNotReferencePathSegment()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Not.Contain("PathSegment"));
    }

    [Test]
    public void GeneratedCode_DoesNotDeclareFuncDelegate()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Not.Contain("Func<LambdaExpression, object?>"));
    }

    #endregion

    #region Required Usings Tests

    [Test]
    public void GeneratedCode_IncludesReflectionUsing()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("using System.Reflection;"));
    }

    [Test]
    public void GeneratedCode_IncludesSuppressMessageUsing()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("using System.Diagnostics.CodeAnalysis;"));
    }

    [Test]
    public void GeneratedCode_IncludesRuntimeCompilerServicesUsing()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("using System.Runtime.CompilerServices;"));
    }

    #endregion

    #region Non-Captured Literal Parameter Tests

    [Test]
    public void GeneratedCode_NonCapturedLiteralParam_EmitsValueInline()
    {
        var result = GenerateWhereInterceptorWithLiteralParam();

        Assert.That(result, Does.Contain("return __b.AddWhereClause(@\"\"\"Name\"\" LIKE '%' || @p0 || '%'\", \"test\");"));
    }

    [Test]
    public void GeneratedCode_NonCapturedLiteralParam_UsesDiscardParamName()
    {
        var result = GenerateWhereInterceptorWithLiteralParam();

        Assert.That(result, Does.Contain("Expression<Func<User, bool>> _)"));
    }

    [Test]
    public void GeneratedCode_MixedCapturedAndLiteralParams_EmitsBothArgs()
    {
        var result = GenerateWhereInterceptorWithMixedParams();

        // Should include both the literal value and the captured extraction
        Assert.That(result, Does.Contain("\"test\""));
        Assert.That(result, Does.Contain("p1"));
        Assert.That(result, Does.Contain("return __b.AddWhereClause("));
    }

    #endregion

    #region Expression Parameter Naming Tests

    [Test]
    public void GeneratedCode_WithCapturedParams_UsesExprParamName()
    {
        var result = GenerateWhereInterceptor("Body.Right");

        Assert.That(result, Does.Contain("Expression<Func<User, bool>> expr)"));
    }

    [Test]
    public void GeneratedCode_WithoutCapturedParams_UsesDiscardParamName()
    {
        var result = GenerateWhereInterceptorNoCaptured();

        Assert.That(result, Does.Contain("Expression<Func<User, bool>> _)"));
    }

    #endregion

    #region Helpers

    private static string GenerateWhereInterceptor(string expressionPath)
    {
        var parameters = new List<ParameterInfo>
        {
            new ParameterInfo(0, "@p0", "object", "capturedValue",
                isCaptured: true, expressionPath: expressionPath)
        };

        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"id\" > @p0",
            parameters);

        return GenerateInterceptorsFile(clauseInfo);
    }

    private static string GenerateWhereInterceptorWithParams(
        params (string path, int index)[] paramSpecs)
    {
        var parameters = paramSpecs.Select(p =>
            new ParameterInfo(p.index, $"@p{p.index}", "object", "capturedValue",
                isCaptured: true, expressionPath: p.path)).ToList();

        var sqlFragment = string.Join(" AND ",
            paramSpecs.Select(p => $"\"col{p.index}\" > @p{p.index}"));

        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, sqlFragment, parameters);
        return GenerateInterceptorsFile(clauseInfo);
    }

    private static string GenerateWhereInterceptorNoCaptured()
    {
        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"is_active\" = true",
            new List<ParameterInfo>());

        return GenerateInterceptorsFile(clauseInfo);
    }

    private static string GenerateWhereInterceptorWithLiteralParam()
    {
        var parameters = new List<ParameterInfo>
        {
            new ParameterInfo(0, "@p0", "string", "\"test\"",
                isCaptured: false)
        };

        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"Name\" LIKE '%' || @p0 || '%'",
            parameters);

        return GenerateInterceptorsFile(clauseInfo);
    }

    private static string GenerateWhereInterceptorWithMixedParams()
    {
        var parameters = new List<ParameterInfo>
        {
            new ParameterInfo(0, "@p0", "string", "\"test\"",
                isCaptured: false),
            new ParameterInfo(1, "@p1", "object", "capturedValue",
                isCaptured: true, expressionPath: "Body.Right")
        };

        var clauseInfo = ClauseInfo.Success(
            ClauseKind.Where,
            "\"Name\" LIKE '%' || @p0 || '%' AND \"id\" > @p1",
            parameters);

        return GenerateInterceptorsFile(clauseInfo);
    }

    private static string GenerateInterceptorsFile(ClauseInfo clauseInfo)
    {
        var site = new UsageSiteInfo(
            methodName: "Where",
            filePath: "Test.cs",
            line: 10,
            column: 5,
            builderTypeName: "IQueryBuilder",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: SyntaxFactory.ParseExpression("test"),
            uniqueId: "abc123",
            clauseInfo: clauseInfo,
            interceptableLocationData: "dGVzdGRhdGE=",
            interceptableLocationVersion: 1);

        return InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext", "TestApp", "test0000", new List<UsageSiteInfo> { site });
    }

    #endregion
}
