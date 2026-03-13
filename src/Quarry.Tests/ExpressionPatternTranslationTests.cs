using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Projection;
using Quarry.Shared.Migration;
using Quarry.Generators.Translation;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for expression pattern translations: navigation join ON-condition generation,
/// collection Contains → IN clause inlining, and string method → SQL projection translation.
/// Cross-dialect coverage verifies identifier quoting and SQL syntax across all four dialects.
/// </summary>
[TestFixture]
public class ExpressionPatternTranslationTests
{
    #region Navigation Join Translation

    [Test]
    public void NavigationJoin_TranslateFromEntityInfo_GeneratesOnCondition()
    {
        var userEntity = CreateUserEntityInfo();
        var orderEntity = CreateOrderEntityInfo();
        var joinInvocation = ParseJoinInvocation("u => u.Orders");

        var result = ClauseTranslator.TranslateNavigationJoin(
            joinInvocation, userEntity, orderEntity,
            GenSqlDialect.PostgreSQL, JoinClauseKind.Inner);

        Assert.That(result, Is.Not.Null, "Navigation join translation returned null");
        Assert.That(result!.IsSuccess, Is.True);
        var joinInfo = (JoinClauseInfo)result;
        Assert.That(joinInfo.JoinedEntityName, Is.EqualTo("Order"));
        Assert.That(joinInfo.OnConditionSql, Does.Contain("t0."));
        Assert.That(joinInfo.OnConditionSql, Does.Contain("t1."));
    }

    [Test]
    public void NavigationJoin_LeftJoin_GeneratesOnCondition()
    {
        var userEntity = CreateUserEntityInfo();
        var orderEntity = CreateOrderEntityInfo();
        var joinInvocation = ParseJoinInvocation("u => u.Orders", "LeftJoin");

        var result = ClauseTranslator.TranslateNavigationJoin(
            joinInvocation, userEntity, orderEntity,
            GenSqlDialect.PostgreSQL, JoinClauseKind.Left);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsSuccess, Is.True);
        Assert.That(result, Is.InstanceOf<JoinClauseInfo>());
    }

    [Test]
    public void NavigationJoin_UnknownNavigation_ReturnsNull()
    {
        var userEntity = CreateUserEntityInfo();
        var orderEntity = CreateOrderEntityInfo();
        var joinInvocation = ParseJoinInvocation("u => u.Invoices");

        var result = ClauseTranslator.TranslateNavigationJoin(
            joinInvocation, userEntity, orderEntity,
            GenSqlDialect.PostgreSQL, JoinClauseKind.Inner);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region Navigation Join — Cross-Dialect

    [TestCase(0, "\"UserId\"", "\"UserId\"")] // SQLite
    [TestCase(1, "\"UserId\"", "\"UserId\"")] // PostgreSQL
    [TestCase(2, "`UserId`", "`UserId`")] // MySQL
    [TestCase(3, "[UserId]", "[UserId]")] // SqlServer
    public void NavigationJoin_QuotesIdentifiersPerDialect(
        int dialectInt, string expectedPkQuoted, string expectedFkQuoted)
    {
        var dialect = (GenSqlDialect)dialectInt;
        var userEntity = CreateUserEntityInfo();
        var orderEntity = CreateOrderEntityInfo();
        var joinInvocation = ParseJoinInvocation("u => u.Orders");

        var result = ClauseTranslator.TranslateNavigationJoin(
            joinInvocation, userEntity, orderEntity,
            dialect, JoinClauseKind.Inner);

        Assert.That(result, Is.Not.Null);
        var joinInfo = (JoinClauseInfo)result!;
        Assert.That(joinInfo.OnConditionSql, Does.Contain($"t0.{expectedPkQuoted}"));
        Assert.That(joinInfo.OnConditionSql, Does.Contain($"t1.{expectedFkQuoted}"));
    }

    [TestCase(0)] // SQLite
    [TestCase(1)] // PostgreSQL
    [TestCase(2)] // MySQL
    [TestCase(3)] // SqlServer
    public void NavigationJoin_LeftJoin_SucceedsAcrossDialects(int dialectInt)
    {
        var dialect = (GenSqlDialect)dialectInt;
        var userEntity = CreateUserEntityInfo();
        var orderEntity = CreateOrderEntityInfo();
        var joinInvocation = ParseJoinInvocation("u => u.Orders", "LeftJoin");

        var result = ClauseTranslator.TranslateNavigationJoin(
            joinInvocation, userEntity, orderEntity,
            dialect, JoinClauseKind.Left);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsSuccess, Is.True);
    }

    #endregion

    #region IN Clause (Collection.Contains)

    [Test]
    public void Contains_VariableArray_InlinesLiterals()
    {
        var result = TranslateLambda("u => ids.Contains(u.Id)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"id\" IN (1, 2, 3)"));
    }

    [Test]
    public void Contains_InlineArray_InlinesLiterals()
    {
        var result = TranslateLambda("u => new[] { 10, 20, 30 }.Contains(u.Id)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"id\" IN (10, 20, 30)"));
    }

    [Test]
    public void Contains_StringArray_InlinesQuotedStrings()
    {
        var result = TranslateLambda("u => new[] { \"a\", \"b\" }.Contains(u.Name)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" IN ('a', 'b')"));
    }

    [Test]
    public void Contains_VariableStringArray_InlinesLiterals()
    {
        var result = TranslateLambda("u => names.Contains(u.Name)");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo("\"name\" IN ('alice', 'bob')"));
    }

    #endregion

    #region IN Clause — Cross-Dialect

    [TestCase(0, "\"id\" IN (1, 2, 3)")] // SQLite
    [TestCase(1, "\"id\" IN (1, 2, 3)")] // PostgreSQL
    [TestCase(2, "`id` IN (1, 2, 3)")] // MySQL
    [TestCase(3, "[id] IN (1, 2, 3)")] // SqlServer
    public void Contains_VariableArray_QuotesColumnPerDialect(int dialectInt, string expectedSql)
    {
        var result = TranslateLambda("u => ids.Contains(u.Id)", (GenSqlDialect)dialectInt);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo(expectedSql));
    }

    [TestCase(0, "\"name\" IN ('alice', 'bob')")] // SQLite
    [TestCase(1, "\"name\" IN ('alice', 'bob')")] // PostgreSQL
    [TestCase(2, "`name` IN ('alice', 'bob')")] // MySQL
    [TestCase(3, "[name] IN ('alice', 'bob')")] // SqlServer
    public void Contains_VariableStringArray_QuotesColumnPerDialect(int dialectInt, string expectedSql)
    {
        var result = TranslateLambda("u => names.Contains(u.Name)", (GenSqlDialect)dialectInt);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo(expectedSql));
    }

    [TestCase(0, "\"id\" IN (10, 20, 30)")] // SQLite
    [TestCase(1, "\"id\" IN (10, 20, 30)")] // PostgreSQL
    [TestCase(2, "`id` IN (10, 20, 30)")] // MySQL
    [TestCase(3, "[id] IN (10, 20, 30)")] // SqlServer
    public void Contains_InlineArray_QuotesColumnPerDialect(int dialectInt, string expectedSql)
    {
        var result = TranslateLambda("u => new[] { 10, 20, 30 }.Contains(u.Id)", (GenSqlDialect)dialectInt);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Sql, Is.EqualTo(expectedSql));
    }

    #endregion

    #region String Methods in Select Projection

    [Test]
    public void Select_Substring_TranslatesToSqlSubstring()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.Substring(0, 3))");

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Kind, Is.EqualTo(ProjectionKind.SingleColumn));
        Assert.That(projection.Columns.Count, Is.EqualTo(1));

        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Is.Not.Null);
        Assert.That(column.SqlExpression, Does.Contain("SUBSTRING"));
        Assert.That(column.SqlExpression, Does.Contain("(0 + 1)"));
        Assert.That(column.SqlExpression, Does.Contain("3"));
    }

    [Test]
    public void Select_ToLower_TranslatesToSqlLower()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToLower())");

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns.Count, Is.EqualTo(1));
        Assert.That(projection.Columns[0].SqlExpression, Is.Not.Null);
    }

    [Test]
    public void Select_ToUpper_TranslatesToSqlUpper()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToUpper())");

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns.Count, Is.EqualTo(1));
        Assert.That(projection.Columns[0].SqlExpression, Is.Not.Null);
    }

    [Test]
    public void Select_Trim_TranslatesToSqlTrim()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.Trim())");

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns.Count, Is.EqualTo(1));
        Assert.That(projection.Columns[0].SqlExpression, Is.Not.Null);
    }

    [Test]
    public void Select_StringMethodReturnsStringType()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToLower())");

        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("string"));
        Assert.That(projection.Columns[0].ReaderMethodName, Is.EqualTo("GetString"));
    }

    #endregion

    #region String Method Projections — Cross-Dialect

    [TestCase(0, "SUBSTRING(\"Name\", (0 + 1), 3)")] // SQLite
    [TestCase(1, "SUBSTRING(\"Name\", (0 + 1), 3)")] // PostgreSQL
    [TestCase(2, "SUBSTRING(`Name`, (0 + 1), 3)")] // MySQL
    [TestCase(3, "SUBSTRING([Name], (0 + 1), 3)")] // SqlServer
    public void Select_SubstringTwoArgs_TranslatesPerDialect(int dialectInt, string expectedSql)
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.Substring(0, 3))", (GenSqlDialect)dialectInt);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Is.EqualTo(expectedSql));
    }

    [TestCase(0, "LOWER(\"Name\")")] // SQLite
    [TestCase(1, "LOWER(\"Name\")")] // PostgreSQL
    [TestCase(2, "LOWER(`Name`)")] // MySQL
    [TestCase(3, "LOWER([Name])")] // SqlServer
    public void Select_ToLower_QuotesColumnPerDialect(int dialectInt, string expectedSql)
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToLower())", (GenSqlDialect)dialectInt);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns[0].SqlExpression, Is.EqualTo(expectedSql));
    }

    [TestCase(0, "UPPER(\"Name\")")] // SQLite
    [TestCase(1, "UPPER(\"Name\")")] // PostgreSQL
    [TestCase(2, "UPPER(`Name`)")] // MySQL
    [TestCase(3, "UPPER([Name])")] // SqlServer
    public void Select_ToUpper_QuotesColumnPerDialect(int dialectInt, string expectedSql)
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToUpper())", (GenSqlDialect)dialectInt);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns[0].SqlExpression, Is.EqualTo(expectedSql));
    }

    [TestCase(0, "TRIM(\"Name\")")] // SQLite
    [TestCase(1, "TRIM(\"Name\")")] // PostgreSQL
    [TestCase(2, "TRIM(`Name`)")] // MySQL
    [TestCase(3, "TRIM([Name])")] // SqlServer
    public void Select_Trim_QuotesColumnPerDialect(int dialectInt, string expectedSql)
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.Trim())", (GenSqlDialect)dialectInt);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns[0].SqlExpression, Is.EqualTo(expectedSql));
    }

    [Test]
    public void Select_SubstringOneArg_PostgreSQL_UsesFromSyntax()
    {
        var projection = AnalyzeSelectExpression(
            "Select(u => u.Name.Substring(2))", GenSqlDialect.PostgreSQL);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Does.Contain("SUBSTRING"));
        Assert.That(column.SqlExpression, Does.Contain("FROM"));
        Assert.That(column.SqlExpression, Does.Not.Contain("LEN"));
    }

    [Test]
    public void Select_SubstringOneArg_SQLite_UsesFromSyntax()
    {
        var projection = AnalyzeSelectExpression(
            "Select(u => u.Name.Substring(2))", GenSqlDialect.SQLite);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Does.Contain("SUBSTRING"));
        Assert.That(column.SqlExpression, Does.Contain("FROM"));
    }

    [Test]
    public void Select_SubstringOneArg_MySQL_UsesFromSyntax()
    {
        var projection = AnalyzeSelectExpression(
            "Select(u => u.Name.Substring(2))", GenSqlDialect.MySQL);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Does.Contain("SUBSTRING"));
        Assert.That(column.SqlExpression, Does.Contain("FROM"));
    }

    [Test]
    public void Select_SubstringOneArg_SqlServer_UsesLenFallback()
    {
        var projection = AnalyzeSelectExpression(
            "Select(u => u.Name.Substring(2))", GenSqlDialect.SqlServer);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Does.Contain("SUBSTRING"));
        Assert.That(column.SqlExpression, Does.Contain("LEN"));
        Assert.That(column.SqlExpression, Does.Not.Contain("FROM"));
    }

    [TestCase(0)] // SQLite
    [TestCase(1)] // PostgreSQL
    [TestCase(2)] // MySQL
    [TestCase(3)] // SqlServer
    public void Select_ToLowerInvariant_TranslatesAcrossDialects(int dialectInt)
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToLowerInvariant())", (GenSqlDialect)dialectInt);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns[0].SqlExpression, Does.Contain("LOWER("));
    }

    [TestCase(0)] // SQLite
    [TestCase(1)] // PostgreSQL
    [TestCase(2)] // MySQL
    [TestCase(3)] // SqlServer
    public void Select_ToUpperInvariant_TranslatesAcrossDialects(int dialectInt)
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name.ToUpperInvariant())", (GenSqlDialect)dialectInt);

        Assert.That(projection.IsOptimalPath, Is.True, projection.NonOptimalReason);
        Assert.That(projection.Columns[0].SqlExpression, Does.Contain("UPPER("));
    }

    #endregion

    #region Helper Methods

    private static InvocationExpressionSyntax ParseJoinInvocation(
        string navExpression, string methodName = "Join")
    {
        var compilation = CreateNavigationJoinCompilation(navExpression, methodName);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();

        var invocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == methodName);

        Assert.That(invocation, Is.Not.Null, $"{methodName} invocation not found in source");
        return invocation!;
    }

    private static ExpressionTranslationResult TranslateLambda(
        string lambdaExpression,
        GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateExpressionCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var lambdaNode = root.DescendantNodes()
            .OfType<SimpleLambdaExpressionSyntax>()
            .FirstOrDefault();

        if (lambdaNode == null)
            return ExpressionTranslationResult.Failure("Lambda not found");

        var body = lambdaNode.Body as ExpressionSyntax;
        if (body == null)
            return ExpressionTranslationResult.Failure("Lambda body is not an expression");

        var entityInfo = CreateTestEntityInfo(dialect);
        var parameterName = lambdaNode.Parameter.Identifier.Text;
        var context = new ExpressionTranslationContext(semanticModel, entityInfo, dialect, parameterName);

        return ExpressionSyntaxTranslator.Translate(body, context);
    }

    private static ProjectionInfo AnalyzeSelectExpression(
        string selectExpression,
        GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateSelectCompilation(selectExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var selectInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "Select");

        if (selectInvocation == null)
            throw new InvalidOperationException("No Select() invocation found in source");

        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
            throw new InvalidOperationException("User type not found");

        return ProjectionAnalyzer.AnalyzeFromTypeSymbol(
            selectInvocation, semanticModel, userTypeSymbol, dialect);
    }

    /// <summary>
    /// Creates an EntityInfo for expression translation tests.
    /// Uses SnakeCase naming (column names differ from property names)
    /// to verify the translator resolves column names, not property names.
    /// </summary>
    private static EntityInfo CreateTestEntityInfo(GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var columns = new List<ColumnInfo>
        {
            new("Id", "id", "int", "System.Int32", false, ColumnKind.PrimaryKey, null,
                new ColumnModifiers(isIdentity: true)),
            new("Name", "name", "string", "System.String", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
            new("Email", "email", "string", "System.String", true, ColumnKind.Standard, null,
                new ColumnModifiers()),
            new("IsActive", "is_active", "bool", "System.Boolean", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
            new("Age", "age", "int", "System.Int32", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
        };

        return new EntityInfo(
            "TestEntity", "TestEntitySchema", "TestApp", "test_entities",
            NamingStyleKind.SnakeCase, columns,
            new List<NavigationInfo>(), Array.Empty<IndexInfo>(), Location.None);
    }

    private static EntityInfo CreateUserEntityInfo()
    {
        var columns = new List<ColumnInfo>
        {
            new("UserId", "UserId", "int", "System.Int32", false, ColumnKind.PrimaryKey, null,
                new ColumnModifiers(isIdentity: true)),
            new("UserName", "UserName", "string", "System.String", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
        };

        var navigations = new List<NavigationInfo>
        {
            new("Orders", "Order", "UserId"),
        };

        return new EntityInfo(
            "User", "UserSchema", "TestApp", "users",
            NamingStyleKind.Exact, columns, navigations, Array.Empty<IndexInfo>(), Location.None);
    }

    private static EntityInfo CreateOrderEntityInfo()
    {
        var columns = new List<ColumnInfo>
        {
            new("OrderId", "OrderId", "int", "System.Int32", false, ColumnKind.PrimaryKey, null,
                new ColumnModifiers(isIdentity: true)),
            new("UserId", "UserId", "int", "System.Int32", false, ColumnKind.ForeignKey, "User",
                new ColumnModifiers()),
            new("Total", "Total", "decimal", "System.Decimal", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
        };

        return new EntityInfo(
            "Order", "OrderSchema", "TestApp", "orders",
            NamingStyleKind.Exact, columns,
            new List<NavigationInfo>(), Array.Empty<IndexInfo>(), Location.None);
    }

    #endregion

    #region Compilation Helpers

    private static CSharpCompilation CreateExpressionCompilation(string lambdaExpression)
    {
        var source = $@"
using System;
using System.Linq;
using System.Linq.Expressions;

namespace TestApp
{{
    public class TestEntity
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public string? Email {{ get; set; }}
        public bool IsActive {{ get; set; }}
        public int Age {{ get; set; }}
    }}

    public class TestClass
    {{
        public void Method()
        {{
            var ids = new[] {{ 1, 2, 3 }};
            var names = new[] {{ ""alice"", ""bob"" }};
            Expression<Func<TestEntity, bool>> predicate = {lambdaExpression};
        }}
    }}
}}
";
        return CreateCompilation(source);
    }

    private static CSharpCompilation CreateSelectCompilation(string selectExpression)
    {
        var source = $@"
using System;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int UserId {{ get; set; }}
        public string Name {{ get; set; }}
        public string? Email {{ get; set; }}
        public bool IsActive {{ get; set; }}
        public int Age {{ get; set; }}
        public decimal Balance {{ get; set; }}
    }}

    public class QueryBuilder<T>
    {{
        public QueryBuilder<T, TResult> Select<TResult>(Expression<Func<T, TResult>> selector) => null!;
    }}

    public class QueryBuilder<T, TResult> {{ }}

    public class TestClass
    {{
        public void Method()
        {{
            var builder = new QueryBuilder<User>();
            var result = builder.{selectExpression};
        }}
    }}
}}
";
        return CreateCompilation(source);
    }

    private static CSharpCompilation CreateNavigationJoinCompilation(string navExpression, string methodName = "Join")
    {
        var source = $@"
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace TestApp
{{
    public class NavigationList<T> : List<T> {{ }}

    public class User
    {{
        public int UserId {{ get; set; }}
        public string UserName {{ get; set; }}
        public NavigationList<Order> Orders {{ get; set; }}
        public NavigationList<Order> Invoices {{ get; set; }}
    }}

    public class Order
    {{
        public int OrderId {{ get; set; }}
        public int UserId {{ get; set; }}
        public decimal Total {{ get; set; }}
    }}

    public class QueryBuilder<T>
    {{
        public JoinedQueryBuilder<T, TJoined> {methodName}<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) => null!;
    }}

    public class JoinedQueryBuilder<T1, T2> {{ }}

    public class TestClass
    {{
        public void Method()
        {{
            var builder = new QueryBuilder<User>();
            var result = builder.{methodName}<Order>({navExpression});
        }}
    }}
}}
";
        return CreateCompilation(source);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    #endregion
}
