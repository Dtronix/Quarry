using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class SqlKataDetectorTests
{
    private static readonly string SqlKataStub = @"
using System.Collections.Generic;

namespace SqlKata
{
    public class Query
    {
        public Query(string table) { }
        public Query Where(string column, string op, object value) => this;
        public Query Where(string column, object value) => this;
        public Query OrWhere(string column, string op, object value) => this;
        public Query WhereNull(string column) => this;
        public Query WhereNotNull(string column) => this;
        public Query WhereIn(string column, IEnumerable<object> values) => this;
        public Query WhereBetween(string column, object lower, object upper) => this;
        public Query WhereRaw(string sql, params object[] bindings) => this;
        public Query OrderBy(params string[] columns) => this;
        public Query OrderByDesc(params string[] columns) => this;
        public Query Select(params string[] columns) => this;
        public Query SelectRaw(string expression, params object[] bindings) => this;
        public Query Join(string table, string first, string second, string op = ""="") => this;
        public Query LeftJoin(string table, string first, string second, string op = ""="") => this;
        public Query RightJoin(string table, string first, string second, string op = ""="") => this;
        public Query CrossJoin(string table) => this;
        public Query Limit(int value) => this;
        public Query Offset(int value) => this;
        public Query Take(int value) => this;
        public Query Skip(int value) => this;
        public Query GroupBy(params string[] columns) => this;
        public Query Having(string column, string op, object value) => this;
        public Query Distinct() => this;
        public Query AsCount(params string[] columns) => this;
        public Query ForPage(int page, int perPage = 15) => this;
        public Query With(string alias, Query query) => this;
    }
}
";

    private static (SemanticModel model, SyntaxNode root) CreateCompilationForDetection(string userCode)
    {
        var userTree = CSharpSyntaxTree.ParseText(userCode);
        var sqlKataTree = CSharpSyntaxTree.ParseText(SqlKataStub);

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, sqlKataTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(userTree);
        return (model, userTree.GetRoot());
    }

    [Test]
    public void Detect_SimpleQuery_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""users"");
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].TableName, Is.EqualTo("users"));
        Assert.That(sites[0].Steps, Is.Empty);
        Assert.That(sites[0].TerminalMethod, Is.Null);
    }

    [Test]
    public void Detect_WhereChain_DetectsSteps()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""users"").Where(""user_id"", "">"", 5);
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Where"));
    }

    [Test]
    public void Detect_MultiStepChain_DetectsAllSteps()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""users"")
            .Where(""user_id"", "">"", 5)
            .OrderBy(""user_name"")
            .Limit(10);
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(3));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Where"));
        Assert.That(sites[0].Steps[1].MethodName, Is.EqualTo("OrderBy"));
        Assert.That(sites[0].Steps[2].MethodName, Is.EqualTo("Limit"));
    }

    [Test]
    public void Detect_UnsupportedWhereRaw_Flagged()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""users"").WhereRaw(""age > ?"", 18);
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].UnsupportedMethods, Contains.Item("WhereRaw"));
    }

    [Test]
    public void Detect_NonSqlKataQuery_NotDetected()
    {
        var (model, root) = CreateCompilationForDetection(@"
public class Query
{
    public Query(string table) { }
}

public class Example
{
    public void Run()
    {
        var query = new Query(""users"");
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Is.Empty);
    }

    [Test]
    public void Detect_SelectAndDistinct_DetectsSteps()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""users"").Select(""user_name"", ""email"").Distinct();
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(2));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Select"));
        Assert.That(sites[0].Steps[1].MethodName, Is.EqualTo("Distinct"));
    }

    [Test]
    public void Detect_JoinChain_DetectsStep()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""orders"")
            .Join(""users"", ""orders.user_id"", ""users.user_id"");
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Join"));
    }

    [Test]
    public void Detect_OrderByDesc_DetectsStep()
    {
        var (model, root) = CreateCompilationForDetection(@"
using SqlKata;

public class Example
{
    public void Run()
    {
        var query = new Query(""users"").OrderByDesc(""created_at"");
    }
}
");

        var detector = new SqlKataDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("OrderByDesc"));
    }
}
