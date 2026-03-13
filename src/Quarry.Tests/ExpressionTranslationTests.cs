using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for the ExpressionSyntaxTranslator compile-time expression translation.
/// </summary>
[TestFixture]
public class ExpressionTranslationTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    /// <summary>
    /// Creates a compilation with the given lambda expression source.
    /// </summary>
    private static CSharpCompilation CreateCompilation(string lambdaExpression)
    {
        var source = $@"
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestApp
{{
    public class TestEntity
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public string? Email {{ get; set; }}
        public bool IsActive {{ get; set; }}
        public int Age {{ get; set; }}
        public decimal Balance {{ get; set; }}
        public DateTime CreatedAt {{ get; set; }}
        public int? ParentId {{ get; set; }}
        public int Flags {{ get; set; }}
    }}

    public class TestClass
    {{
        public int capturedVar = 42;
        public string capturedString = ""test"";

        public void Method()
        {{
            var localVar = 100;
            var ids = new[] {{ 1, 2, 3 }};
            System.Func<TestEntity, bool> predicate = {lambdaExpression};
        }}
    }}
}}
";

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Core.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    /// <summary>
    /// Creates entity info for TestEntity.
    /// </summary>
    private static EntityInfo CreateTestEntityInfo()
    {
        var columns = new List<ColumnInfo>
        {
            new("Id", "id", "int", "System.Int32", false, ColumnKind.PrimaryKey, null, new ColumnModifiers(isIdentity: true)),
            new("Name", "name", "string", "System.String", false, ColumnKind.Standard, null, new ColumnModifiers(maxLength: 100)),
            new("Email", "email", "string", "System.String", true, ColumnKind.Standard, null, new ColumnModifiers()),
            new("IsActive", "is_active", "bool", "System.Boolean", false, ColumnKind.Standard, null, new ColumnModifiers()),
            new("Age", "age", "int", "System.Int32", false, ColumnKind.Standard, null, new ColumnModifiers()),
            new("Balance", "balance", "decimal", "System.Decimal", false, ColumnKind.Standard, null, new ColumnModifiers(precision: 18, scale: 2)),
            new("CreatedAt", "created_at", "DateTime", "System.DateTime", false, ColumnKind.Standard, null, new ColumnModifiers()),
            new("ParentId", "parent_id", "int", "System.Int32", true, ColumnKind.Standard, null, new ColumnModifiers()),
            new("Flags", "flags", "int", "System.Int32", false, ColumnKind.Standard, null, new ColumnModifiers())
        };

        return new EntityInfo(
            "TestEntity",
            "TestEntitySchema",
            "TestApp",
            "test_entities",
            NamingStyleKind.SnakeCase,
            columns,
            new List<NavigationInfo>(),
            Array.Empty<IndexInfo>(),
            Location.None);
    }

    /// <summary>
    /// Translates a lambda expression to SQL.
    /// </summary>
    private static ExpressionTranslationResult TranslateLambda(
        string lambdaExpression,
        GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the lambda expression
        var lambda = root.DescendantNodes()
            .OfType<SimpleLambdaExpressionSyntax>()
            .FirstOrDefault();

        if (lambda == null)
        {
            // Try parenthesized lambda
            var parenthesizedLambda = root.DescendantNodes()
                .OfType<ParenthesizedLambdaExpressionSyntax>()
                .FirstOrDefault();

            if (parenthesizedLambda != null)
            {
                var entityInfo = CreateTestEntityInfo();
                var paramName = parenthesizedLambda.ParameterList.Parameters.First().Identifier.Text;
                var context = new ExpressionTranslationContext(semanticModel, entityInfo, dialect, paramName);
                return ExpressionSyntaxTranslator.Translate(parenthesizedLambda.Body as ExpressionSyntax ?? throw new InvalidOperationException(), context);
            }

            throw new InvalidOperationException("No lambda expression found in source");
        }

        var entityInfoSimple = CreateTestEntityInfo();
        var lambdaParamName = lambda.Parameter.Identifier.Text;
        var contextSimple = new ExpressionTranslationContext(semanticModel, entityInfoSimple, dialect, lambdaParamName);

        return ExpressionSyntaxTranslator.Translate(lambda.Body as ExpressionSyntax ?? throw new InvalidOperationException(), contextSimple);
    }

    #region Member Access Tests

    [Test]
    public void Translate_PropertyAccess_ReturnsQuotedColumnName()
    {
        var result = TranslateLambda("u => u.Name == \"test\"");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("\"name\""));
    }

    [Test]
    public void Translate_PrimaryKeyProperty_ReturnsQuotedColumnName()
    {
        var result = TranslateLambda("u => u.Id == 1");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("\"id\""));
    }

    #endregion

    #region Binary Expression Tests

    [Test]
    public void Translate_EqualsOperator_ReturnsEqualsSign()
    {
        var result = TranslateLambda("u => u.Id == 1");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"id\" = 1"));
    }

    [Test]
    public void Translate_NotEqualsOperator_ReturnsNotEquals()
    {
        var result = TranslateLambda("u => u.Id != 1");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"id\" <> 1"));
    }

    [Test]
    public void Translate_LessThan_ReturnsLessThan()
    {
        var result = TranslateLambda("u => u.Age < 18");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" < 18"));
    }

    [Test]
    public void Translate_LessThanOrEqual_ReturnsLessOrEqual()
    {
        var result = TranslateLambda("u => u.Age <= 18");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" <= 18"));
    }

    [Test]
    public void Translate_GreaterThan_ReturnsGreaterThan()
    {
        var result = TranslateLambda("u => u.Age > 18");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" > 18"));
    }

    [Test]
    public void Translate_GreaterThanOrEqual_ReturnsGreaterOrEqual()
    {
        var result = TranslateLambda("u => u.Age >= 18");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" >= 18"));
    }

    [Test]
    public void Translate_LogicalAnd_ReturnsAnd()
    {
        var result = TranslateLambda("u => u.IsActive && u.Age > 18");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        // Boolean columns can be used directly in WHERE clauses (valid SQL)
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" AND \"age\" > 18"));
    }

    [Test]
    public void Translate_LogicalOr_ReturnsOr()
    {
        var result = TranslateLambda("u => u.IsActive || u.Age > 18");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        // Boolean columns can be used directly in WHERE clauses (valid SQL)
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" OR \"age\" > 18"));
    }

    [Test]
    public void Translate_ComplexCondition_ReturnsCombinedConditions()
    {
        var result = TranslateLambda("u => u.IsActive && (u.Age > 18 || u.Age < 10)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        // Boolean columns can be used directly in WHERE clauses (valid SQL)
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" AND (\"age\" > 18 OR \"age\" < 10)"));
    }

    #endregion

    #region Null Pattern Tests

    [Test]
    public void Translate_EqualsNull_ReturnsIsNull()
    {
        var result = TranslateLambda("u => u.Email == null");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"email\" IS NULL"));
    }

    [Test]
    public void Translate_NotEqualsNull_ReturnsIsNotNull()
    {
        var result = TranslateLambda("u => u.Email != null");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"email\" IS NOT NULL"));
    }

    [Test]
    public void Translate_IsNullPattern_ReturnsIsNull()
    {
        var result = TranslateLambda("u => u.Email is null");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"email\" IS NULL"));
    }

    [Test]
    public void Translate_IsNotNullPattern_ReturnsIsNotNull()
    {
        var result = TranslateLambda("u => u.Email is not null");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"email\" IS NOT NULL"));
    }

    [Test]
    public void Translate_NullOnLeftSide_ReturnsIsNull()
    {
        var result = TranslateLambda("u => null == u.Email");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"email\" IS NULL"));
    }

    #endregion

    #region Arithmetic Operator Tests

    [Test]
    public void Translate_Addition_ReturnsPlus()
    {
        var result = TranslateLambda("u => u.Age + 10 > 30");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" + 10 > 30"));
    }

    [Test]
    public void Translate_Subtraction_ReturnsMinus()
    {
        var result = TranslateLambda("u => u.Age - 10 < 30");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" - 10 < 30"));
    }

    [Test]
    public void Translate_Multiplication_ReturnsAsterisk()
    {
        var result = TranslateLambda("u => u.Age * 2 > 40");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" * 2 > 40"));
    }

    [Test]
    public void Translate_Division_ReturnsSlash()
    {
        var result = TranslateLambda("u => u.Age / 2 > 10");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" / 2 > 10"));
    }

    [Test]
    public void Translate_Modulo_ReturnsPercent()
    {
        var result = TranslateLambda("u => u.Age % 2 == 0");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"age\" % 2 = 0"));
    }

    #endregion

    #region Bitwise Operator Tests

    [Test]
    public void Translate_BitwiseAnd_ReturnsAmpersand()
    {
        var result = TranslateLambda("u => (u.Flags & 1) == 1");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("(\"flags\" & 1) = 1"));
    }

    [Test]
    public void Translate_BitwiseOr_ReturnsPipe()
    {
        var result = TranslateLambda("u => (u.Flags | 2) > 0");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("(\"flags\" | 2) > 0"));
    }

    [Test]
    public void Translate_BitwiseXor_ReturnsCaret()
    {
        var result = TranslateLambda("u => (u.Flags ^ 3) == 0");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("(\"flags\" ^ 3) = 0"));
    }

    #endregion

    #region String Method Tests

    [Test]
    public void Translate_StringContains_ReturnsLikeWithWildcards()
    {
        var result = TranslateLambda("u => u.Name.Contains(\"john\")");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" LIKE '%' || @p0 || '%'"));
        Assert.That(result.Parameters, Has.Count.EqualTo(1));
    }

    [Test]
    public void Translate_StringStartsWith_ReturnsLikeWithSuffix()
    {
        var result = TranslateLambda("u => u.Name.StartsWith(\"john\")");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" LIKE @p0 || '%'"));
        Assert.That(result.Parameters, Has.Count.EqualTo(1));
    }

    [Test]
    public void Translate_StringEndsWith_ReturnsLikeWithPrefix()
    {
        var result = TranslateLambda("u => u.Name.EndsWith(\"son\")");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" LIKE '%' || @p0"));
        Assert.That(result.Parameters, Has.Count.EqualTo(1));
    }

    [Test]
    public void Translate_ToLower_ReturnsLowerFunction()
    {
        var result = TranslateLambda("u => u.Name.ToLower() == \"john\"");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("LOWER(\"name\")"));
    }

    [Test]
    public void Translate_ToUpper_ReturnsUpperFunction()
    {
        var result = TranslateLambda("u => u.Name.ToUpper() == \"JOHN\"");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("UPPER(\"name\")"));
    }

    [Test]
    public void Translate_Trim_ReturnsTrimFunction()
    {
        var result = TranslateLambda("u => u.Name.Trim() == \"john\"");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("TRIM(\"name\")"));
    }

    #endregion

    #region Collection Contains Tests

    [Test]
    public void Translate_ArrayContains_ReturnsInClause()
    {
        var result = TranslateLambda("u => new[] { 1, 2, 3 }.Contains(u.Id)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"id\" IN (1, 2, 3)"));
    }

    [Test]
    public void Translate_ArrayContainsWithStrings_ReturnsInClauseWithQuotedStrings()
    {
        var result = TranslateLambda("u => new[] { \"a\", \"b\", \"c\" }.Contains(u.Name)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" IN ('a', 'b', 'c')"));
    }

    [Test]
    public void Translate_VariableContains_ResolvesToInlineLiterals()
    {
        // When the variable initializer is a literal array, the values are inlined
        var result = TranslateLambda("u => ids.Contains(u.Id)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"id\" IN (1, 2, 3)"));
    }

    #endregion

    #region Literal Tests

    [Test]
    public void Translate_StringLiteral_ReturnsQuotedString()
    {
        var result = TranslateLambda("u => u.Name == \"john\"");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" = 'john'"));
    }

    [Test]
    public void Translate_StringWithQuotes_EscapesQuotes()
    {
        var result = TranslateLambda("u => u.Name == \"O'Brien\"");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" = 'O''Brien'"));
    }

    [Test]
    public void Translate_BooleanTrue_ReturnsTrueForPostgreSQL()
    {
        var result = TranslateLambda("u => u.IsActive == true", GenSqlDialect.PostgreSQL);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" = TRUE"));
    }

    [Test]
    public void Translate_BooleanFalse_ReturnsFalseForPostgreSQL()
    {
        var result = TranslateLambda("u => u.IsActive == false", GenSqlDialect.PostgreSQL);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" = FALSE"));
    }

    [Test]
    public void Translate_BooleanTrue_Returns1ForSQLite()
    {
        var result = TranslateLambda("u => u.IsActive == true", GenSqlDialect.SQLite);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" = 1"));
    }

    [Test]
    public void Translate_BooleanFalse_Returns0ForSQLite()
    {
        var result = TranslateLambda("u => u.IsActive == false", GenSqlDialect.SQLite);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"is_active\" = 0"));
    }

    #endregion

    #region Dialect-Specific Tests

    [Test]
    public void Translate_PropertyAccess_PostgreSQL_UsesDoubleQuotes()
    {
        var result = TranslateLambda("u => u.Id == 1", GenSqlDialect.PostgreSQL);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("\"id\""));
    }

    [Test]
    public void Translate_PropertyAccess_MySQL_UsesBackticks()
    {
        var result = TranslateLambda("u => u.Id == 1", GenSqlDialect.MySQL);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("`id`"));
    }

    [Test]
    public void Translate_PropertyAccess_SqlServer_UsesBrackets()
    {
        var result = TranslateLambda("u => u.Id == 1", GenSqlDialect.SqlServer);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("[id]"));
    }

    [Test]
    public void Translate_PropertyAccess_SQLite_UsesDoubleQuotes()
    {
        var result = TranslateLambda("u => u.Id == 1", GenSqlDialect.SQLite);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("\"id\""));
    }

    #endregion

    #region Prefix Unary Tests

    [Test]
    public void Translate_LogicalNot_ReturnsNot()
    {
        var result = TranslateLambda("u => !u.IsActive");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("NOT"));
    }

    [Test]
    public void Translate_UnaryMinus_ReturnsNegation()
    {
        var result = TranslateLambda("u => -u.Age < 0");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("-\"age\""));
    }

    #endregion

    #region Captured Variable Tests

    [Test]
    public void Translate_CapturedLocalVariable_ReturnsParameter()
    {
        var result = TranslateLambda("u => u.Id == localVar");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("@p"));
        Assert.That(result.Parameters.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Translate_CapturedFieldVariable_ReturnsParameter()
    {
        var result = TranslateLambda("u => u.Id == capturedVar");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("@p"));
        Assert.That(result.Parameters.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Boolean Property Access Tests

    [Test]
    public void Translate_BooleanProperty_DirectAccess()
    {
        // When a boolean property is accessed directly (not in comparison),
        // we need to compare it with true
        var result = TranslateLambda("u => u.IsActive");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Does.Contain("\"is_active\""));
    }

    #endregion
}
