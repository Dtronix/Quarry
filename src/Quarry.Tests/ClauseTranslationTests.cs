using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Translation;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for clause expression translation (Where, OrderBy, GroupBy, Having, Set).
/// </summary>
[TestFixture]
public class ClauseTranslationTests
{
    #region Where Clause Tests

    [Test]
    public void Where_SimpleEquality_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Id == 5");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.Kind, Is.EqualTo(ClauseKind.Where));
        Assert.That(clause.SqlFragment, Does.Contain("\"Id\""));
        Assert.That(clause.SqlFragment, Does.Contain("="));
        Assert.That(clause.SqlFragment, Does.Contain("5"));
    }

    [Test]
    public void Where_StringProperty_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Name == \"John\"");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("\"Name\""));
        Assert.That(clause.SqlFragment, Does.Contain("'John'"));
    }

    [Test]
    public void Where_BooleanProperty_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.IsActive");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void Where_AndCondition_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.IsActive && u.Age > 18");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("AND"));
        Assert.That(clause.SqlFragment, Does.Contain("\"IsActive\""));
        Assert.That(clause.SqlFragment, Does.Contain("\"Age\""));
    }

    [Test]
    public void Where_OrCondition_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.IsActive || u.Age > 65");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("OR"));
    }

    [Test]
    public void Where_NullCheck_IsNull_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Email == null");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("IS NULL"));
    }

    [Test]
    public void Where_NullCheck_IsNotNull_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Email != null");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("IS NOT NULL"));
    }

    [Test]
    public void Where_NullPattern_IsNull_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Email is null");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("IS NULL"));
    }

    [Test]
    public void Where_NullPattern_IsNotNull_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Email is not null");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("IS NOT NULL"));
    }

    [Test]
    public void Where_LessThan_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Age < 30");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("<"));
        Assert.That(clause.SqlFragment, Does.Contain("30"));
    }

    [Test]
    public void Where_GreaterThanOrEqual_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Age >= 21");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain(">="));
        Assert.That(clause.SqlFragment, Does.Contain("21"));
    }

    [Test]
    public void Where_NotEquals_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Status != 0");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("<>"));
    }

    [Test]
    public void Where_StringContains_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Name.Contains(\"john\")");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("LIKE"));
        Assert.That(clause.SqlFragment, Does.Contain("@p0"));
    }

    [Test]
    public void Where_StringStartsWith_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Name.StartsWith(\"A\")");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("LIKE"));
        Assert.That(clause.SqlFragment, Does.Contain("@p0"));
    }

    [Test]
    public void Where_StringEndsWith_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Name.EndsWith(\"son\")");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("LIKE"));
        Assert.That(clause.SqlFragment, Does.Contain("@p0"));
    }

    [Test]
    public void Where_NotExpression_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => !u.IsActive");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("NOT"));
    }

    [Test]
    public void Where_ArithmeticExpression_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => u.Age + 5 > 30");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("+"));
        Assert.That(clause.SqlFragment, Does.Contain("5"));
        Assert.That(clause.SqlFragment, Does.Contain(">"));
    }

    [Test]
    public void Where_BitwiseAnd_TranslatesCorrectly()
    {
        var clause = AnalyzeWhereExpression("u => (u.Flags & 1) == 1");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("&"));
    }

    #endregion

    #region OrderBy Clause Tests

    [Test]
    public void OrderBy_SimpleColumn_TranslatesCorrectly()
    {
        var clause = AnalyzeOrderByExpression("u => u.Name");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("\"Name\""));
    }

    [Test]
    public void OrderBy_IntColumn_TranslatesCorrectly()
    {
        var clause = AnalyzeOrderByExpression("u => u.Age");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("\"Age\""));
    }

    [Test]
    public void OrderBy_ReturnsOrderByClauseInfo()
    {
        var clause = AnalyzeOrderByExpression("u => u.Name");

        Assert.That(clause, Is.InstanceOf<OrderByClauseInfo>());
        var orderBy = (OrderByClauseInfo)clause;
        Assert.That(orderBy.ColumnSql, Does.Contain("\"Name\""));
    }

    #endregion

    #region GroupBy Clause Tests

    [Test]
    public void GroupBy_SingleColumn_TranslatesCorrectly()
    {
        var clause = AnalyzeGroupByExpression("u => u.Status");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.Kind, Is.EqualTo(ClauseKind.GroupBy));
        Assert.That(clause.SqlFragment, Does.Contain("\"Status\""));
    }

    [Test]
    public void GroupBy_TupleColumns_TranslatesCorrectly()
    {
        var clause = AnalyzeGroupByExpression("u => (u.Status, u.IsActive)");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("\"Status\""));
        Assert.That(clause.SqlFragment, Does.Contain("\"IsActive\""));
    }

    #endregion

    #region Having Clause Tests

    [Test]
    public void Having_SimpleCondition_TranslatesCorrectly()
    {
        var clause = AnalyzeHavingExpression("u => u.Age > 25");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.Kind, Is.EqualTo(ClauseKind.Having));
        Assert.That(clause.SqlFragment, Does.Contain("\"Age\""));
        Assert.That(clause.SqlFragment, Does.Contain(">"));
    }

    #endregion

    #region Set Clause Tests

    [Test]
    public void Set_SimpleColumn_TranslatesCorrectly()
    {
        var clause = AnalyzeSetExpression("u => u.Name", "\"NewName\"");

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.Kind, Is.EqualTo(ClauseKind.Set));
        Assert.That(clause, Is.InstanceOf<SetClauseInfo>());

        var setInfo = (SetClauseInfo)clause;
        Assert.That(setInfo.ColumnSql, Does.Contain("\"Name\""));
    }

    [Test]
    public void Set_IntColumn_TranslatesCorrectly()
    {
        var clause = AnalyzeSetExpression("u => u.Age", "30");

        Assert.That(clause.IsSuccess, Is.True);
        var setInfo = (SetClauseInfo)clause;
        Assert.That(setInfo.ColumnSql, Does.Contain("\"Age\""));
        Assert.That(setInfo.ParameterIndex, Is.EqualTo(0));
    }

    #endregion

    #region Dialect-Specific Tests

    [Test]
    public void Where_MySqlDialect_UsesBackticks()
    {
        var clause = AnalyzeWhereExpression("u => u.Name == \"John\"", GenSqlDialect.MySQL);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("`Name`"));
    }

    [Test]
    public void Where_SqlServerDialect_UsesBrackets()
    {
        var clause = AnalyzeWhereExpression("u => u.Name == \"John\"", GenSqlDialect.SqlServer);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("[Name]"));
    }

    [Test]
    public void Where_PostgreSqlDialect_UsesDoubleQuotes()
    {
        var clause = AnalyzeWhereExpression("u => u.Name == \"John\"", GenSqlDialect.PostgreSQL);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("\"Name\""));
    }

    [Test]
    public void Where_BooleanLiteral_PostgreSql_UsesTRUE()
    {
        var clause = AnalyzeWhereExpression("u => u.IsActive == true", GenSqlDialect.PostgreSQL);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("TRUE"));
    }

    [Test]
    public void Where_BooleanLiteral_SqlServer_Uses1()
    {
        var clause = AnalyzeWhereExpression("u => u.IsActive == true", GenSqlDialect.SqlServer);

        Assert.That(clause.IsSuccess, Is.True);
        Assert.That(clause.SqlFragment, Does.Contain("1"));
    }

    #endregion

    #region Helper Methods

    private static ClauseInfo AnalyzeWhereExpression(string lambdaExpression, GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateWhereCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var whereInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "Where");

        if (whereInvocation == null)
        {
            return ClauseInfo.Failure(ClauseKind.Where, "Where invocation not found");
        }

        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
        {
            return ClauseInfo.Failure(ClauseKind.Where, "User type not found");
        }

        return ClauseTranslator.TranslateWhere(whereInvocation, semanticModel, userTypeSymbol, dialect);
    }

    private static ClauseInfo AnalyzeOrderByExpression(string lambdaExpression, GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateOrderByCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var orderByInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "OrderBy");

        if (orderByInvocation == null)
        {
            return ClauseInfo.Failure(ClauseKind.OrderBy, "OrderBy invocation not found");
        }

        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
        {
            return ClauseInfo.Failure(ClauseKind.OrderBy, "User type not found");
        }

        return ClauseTranslator.TranslateOrderBy(orderByInvocation, semanticModel, userTypeSymbol, dialect);
    }

    private static ClauseInfo AnalyzeGroupByExpression(string lambdaExpression, GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateGroupByCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var groupByInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "GroupBy");

        if (groupByInvocation == null)
        {
            return ClauseInfo.Failure(ClauseKind.GroupBy, "GroupBy invocation not found");
        }

        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
        {
            return ClauseInfo.Failure(ClauseKind.GroupBy, "User type not found");
        }

        return ClauseTranslator.TranslateGroupBy(groupByInvocation, semanticModel, userTypeSymbol, dialect);
    }

    private static ClauseInfo AnalyzeHavingExpression(string lambdaExpression, GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateHavingCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var havingInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "Having");

        if (havingInvocation == null)
        {
            return ClauseInfo.Failure(ClauseKind.Having, "Having invocation not found");
        }

        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
        {
            return ClauseInfo.Failure(ClauseKind.Having, "User type not found");
        }

        return ClauseTranslator.TranslateHaving(havingInvocation, semanticModel, userTypeSymbol, dialect);
    }

    private static ClauseInfo AnalyzeSetExpression(string columnLambda, string valueExpression, GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateSetCompilation(columnLambda, valueExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var setInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "Set");

        if (setInvocation == null)
        {
            return ClauseInfo.Failure(ClauseKind.Set, "Set invocation not found");
        }

        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
        {
            return ClauseInfo.Failure(ClauseKind.Set, "User type not found");
        }

        return ClauseTranslator.TranslateSet(setInvocation, semanticModel, userTypeSymbol, dialect, existingParameterCount: 0);
    }

    private static Compilation CreateWhereCompilation(string lambdaExpression)
    {
        var code = $@"
using System;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public string? Email {{ get; set; }}
        public int Age {{ get; set; }}
        public bool IsActive {{ get; set; }}
        public int Status {{ get; set; }}
        public int Flags {{ get; set; }}
    }}

    public class QueryBuilder<T>
    {{
        public QueryBuilder<T> Where(Expression<Func<T, bool>> predicate) => this;
    }}

    public class TestClass
    {{
        public void Test()
        {{
            var builder = new QueryBuilder<User>();
            builder.Where({lambdaExpression});
        }}
    }}
}}";
        return CreateCompilation(code);
    }

    private static Compilation CreateOrderByCompilation(string lambdaExpression)
    {
        var code = $@"
using System;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public int Age {{ get; set; }}
    }}

    public class QueryBuilder<T>
    {{
        public QueryBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector) => this;
    }}

    public class TestClass
    {{
        public void Test()
        {{
            var builder = new QueryBuilder<User>();
            builder.OrderBy({lambdaExpression});
        }}
    }}
}}";
        return CreateCompilation(code);
    }

    private static Compilation CreateGroupByCompilation(string lambdaExpression)
    {
        var code = $@"
using System;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public int Status {{ get; set; }}
        public bool IsActive {{ get; set; }}
    }}

    public class QueryBuilder<T>
    {{
        public QueryBuilder<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector) => this;
    }}

    public class TestClass
    {{
        public void Test()
        {{
            var builder = new QueryBuilder<User>();
            builder.GroupBy({lambdaExpression});
        }}
    }}
}}";
        return CreateCompilation(code);
    }

    private static Compilation CreateHavingCompilation(string lambdaExpression)
    {
        var code = $@"
using System;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int Id {{ get; set; }}
        public int Age {{ get; set; }}
    }}

    public class QueryBuilder<T>
    {{
        public QueryBuilder<T> Having(Expression<Func<T, bool>> predicate) => this;
    }}

    public class TestClass
    {{
        public void Test()
        {{
            var builder = new QueryBuilder<User>();
            builder.Having({lambdaExpression});
        }}
    }}
}}";
        return CreateCompilation(code);
    }

    private static Compilation CreateSetCompilation(string columnLambda, string valueExpression)
    {
        var code = $@"
using System;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public int Age {{ get; set; }}
    }}

    public class UpdateBuilder<T>
    {{
        public UpdateBuilder<T> Set<TValue>(Expression<Func<T, TValue>> column, TValue value) => this;
    }}

    public class TestClass
    {{
        public void Test()
        {{
            var builder = new UpdateBuilder<User>();
            builder.Set({columnLambda}, {valueExpression});
        }}
    }}
}}";
        return CreateCompilation(code);
    }

    private static Compilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.Unsafe).Assembly.Location),
        };

        // Add runtime references
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRefs = new[]
        {
            System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"),
            System.IO.Path.Combine(runtimeDir, "System.Linq.Expressions.dll"),
        };

        var allReferences = references.Concat(
            runtimeRefs.Where(System.IO.File.Exists).Select(p => MetadataReference.CreateFromFile(p)));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            allReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #endregion
}
