using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;

namespace Quarry.Tests.Generation;

/// <summary>
/// Phase 3 tests for <see cref="UsageSiteDiscovery"/>: classification of
/// the four <c>Update().Set(...)</c> forms based on overload-resolved
/// parameter type. The existing UpdateSetPoco and UpdateSetAction paths
/// must remain unchanged; UpdateSetPatch and UpdateSetPatchAction are new.
/// </summary>
[TestFixture]
public class UsageSiteDiscoveryPatchTests
{
    private const string SharedSchema = @"
using Quarry;
using System;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string> Email => Length(200);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    [Test]
    public void Set_NewEntity_ClassifiedAsUpdateSetPoco()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Set(new User { UserName = ""x"" }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}");
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPoco));
    }

    [Test]
    public void Set_AssignmentLambda_ClassifiedAsUpdateSetAction()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Set(u => u.UserName = ""x"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}");
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetAction));
    }

    [Test]
    public void Set_PatchValue_ClassifiedAsUpdateSetPatch()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        var patch = new User.Patch { UserName = ""x"" };
        await _db.Users().Update().Set(patch).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}");
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatch));
    }

    [Test]
    public void Set_PatchActionLambdaExplicit_ClassifiedAsUpdateSetPatchAction()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Set((ref User.Patch p) => p.UserName = ""x"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}");
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatchAction));
    }

    [Test]
    public void Set_PatchValue_ExecutableUpdateBuilder_ClassifiedAsUpdateSetPatch()
    {
        // Same classification when Set is chained after Where (the post-Where
        // builder is IExecutableUpdateBuilder<T>); discovery's containingType.Name
        // check matches both UpdateBuilder forms.
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        var patch = new User.Patch { UserName = ""x"" };
        await _db.Users().Update().Where(u => u.UserId == 1).Set(patch).ExecuteNonQueryAsync();
    }
}");
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatch));
    }

    [Test]
    public void Set_PatchActionLambda_ExecutableUpdateBuilder_ClassifiedAsUpdateSetPatchAction()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Where(u => u.UserId == 1).Set((ref User.Patch p) => p.UserName = ""x"").ExecuteNonQueryAsync();
    }
}");
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatchAction));
    }

    // ── Regression tests: real-generator pipeline (no pre-run) ──────────
    //
    // The real IIncrementalGenerator pipeline runs discovery against the
    // pre-generator compilation — so the SemanticModel doesn't see the
    // generated Entity.Patch struct. Classification must therefore work
    // from argument syntax alone, not from semantic overload resolution.
    // These tests skip RunGeneratorsAndUpdateCompilation to exercise that
    // path exactly.

    [Test]
    public void Set_PatchValue_PreGeneratorCompilation_ClassifiedAsUpdateSetPatch()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        var patch = new User.Patch { UserName = ""x"" };
        await _db.Users().Update().Set(patch).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}", preRunGenerator: false);
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatch));
    }

    [Test]
    public void Set_PatchObjectCreationInline_PreGeneratorCompilation_ClassifiedAsUpdateSetPatch()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Set(new User.Patch { UserName = ""x"" }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}", preRunGenerator: false);
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatch));
    }

    [Test]
    public void Set_PatchActionLambda_PreGeneratorCompilation_ClassifiedAsUpdateSetPatchAction()
    {
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Set((ref User.Patch p) => p.UserName = ""x"").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}", preRunGenerator: false);
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPatchAction));
    }

    [Test]
    public void Set_PocoEntity_PreGeneratorCompilation_ClassifiedAsUpdateSetPoco()
    {
        // Regression: non-Patch Set forms must continue routing correctly when
        // discovery runs against the pre-generator compilation.
        var kind = DiscoverFirstSetKind(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Users().Update().Set(new User { UserName = ""x"" }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
    }
}", preRunGenerator: false);
        Assert.That(kind, Is.EqualTo(InterceptorKind.UpdateSetPoco));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles the schema + user code, locates the first <c>.Set(...)</c>
    /// invocation in the user code, and returns its discovered
    /// <see cref="InterceptorKind"/>. When <paramref name="preRunGenerator"/>
    /// is true (the default), the generator runs once first to merge the
    /// generated <c>Entity.Patch</c> struct into the compilation — this is the
    /// historical harness shape kept for the existing tests. When false,
    /// discovery runs against the pre-generator compilation, exactly mirroring
    /// the real <c>IIncrementalGenerator</c> pipeline.
    /// </summary>
    private static InterceptorKind DiscoverFirstSetKind(string userCode, bool preRunGenerator = true)
    {
        var fullSource = SharedSchema + userCode;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(fullSource, parseOptions) },
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        if (preRunGenerator)
        {
            // Run the generator once to surface the User.Patch nested struct
            // (the user code needs it to overload-resolve Set(User.Patch)).
            var generator = new Quarry.Generators.QuarryGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            compilation = (CSharpCompilation)updatedCompilation;
        }

        // Locate the first .Set(...) invocation across the original source tree.
        SyntaxTree? userTree = null;
        InvocationExpressionSyntax? setInvocation = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!tree.FilePath.Contains(".g.cs"))
                userTree = tree;
            foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Set")
                {
                    setInvocation = inv;
                    break;
                }
            }
            if (setInvocation is not null) break;
        }

        Assert.That(setInvocation, Is.Not.Null, "test source must contain a .Set(...) invocation");
        Assert.That(userTree, Is.Not.Null, "must have located the user source tree");

        var semanticModel = compilation.GetSemanticModel(setInvocation!.SyntaxTree);
        var raw = UsageSiteDiscovery.DiscoverRawCallSite(setInvocation, semanticModel, default);
        Assert.That(raw, Is.Not.Null, "DiscoverRawCallSite returned null for the .Set(...) invocation");
        return raw!.Kind;
    }

    private static List<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(Quarry.Schema).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        return refs;
    }
}
