using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;

namespace Quarry.Tests.IR;

/// <summary>
/// Negative-equality guardrails for the Stage-5 pipeline models whose
/// <c>IEquatable</c> implementations gate Roslyn incremental caching:
/// <see cref="EntityRegistry"/>, <see cref="AssembledPlan"/>,
/// <see cref="CarrierPlan"/>, <see cref="FileInterceptorGroup"/>.
/// A field silently dropped from equality turns into a stale-cache bug
/// (exactly the shipped EntityRegistry defect that once omitted
/// <c>_allContexts</c>), so each model gets difference-detection coverage:
/// registry constituents directly, and the plan/carrier/group graph via
/// real generator runs over minimally differing sources.
/// </summary>
[TestFixture]
public class PipelineModelEqualityTests
{
    // ── EntityRegistry: targeted constituent variations ──────────────────────

    [Test]
    public void EntityRegistry_SameContexts_Equal_WithSameHashCode()
    {
        var a = EntityRegistry.Build(CreateContexts(), CancellationToken.None);
        var b = EntityRegistry.Build(CreateContexts(), CancellationToken.None);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()),
            "Equal registries must produce equal hash codes");
    }

    /// <summary>
    /// Regression pin for the shipped bug where <c>_allContexts</c> was
    /// omitted from EntityRegistry equality: a context with no entities
    /// leaves the entity map identical, so only the context list
    /// distinguishes the registries.
    /// </summary>
    [Test]
    public void EntityRegistry_ExtraEntityLessContext_NotEqual()
    {
        var baseContexts = CreateContexts();
        var withEmptyContext = baseContexts.Add(new ContextInfo(
            className: "AuditContext",
            @namespace: "TestApp",
            dialectConfig: new GenSqlDialectConfig(GenSqlDialect.SQLite),
            schema: null,
            entities: System.Array.Empty<EntityInfo>(),
            entityMappings: System.Array.Empty<EntityMapping>(),
            location: Location.None));

        var a = EntityRegistry.Build(baseContexts, CancellationToken.None);
        var b = EntityRegistry.Build(withEmptyContext, CancellationToken.None);

        Assert.That(a.Equals(b), Is.False,
            "Registries differing only in the context list must not be equal — " +
            "an entity-less context still affects downstream binding");
    }

    [Test]
    public void EntityRegistry_DifferentColumnType_NotEqual()
    {
        var a = EntityRegistry.Build(CreateContexts(), CancellationToken.None);
        var b = EntityRegistry.Build(CreateContexts(ageColumnType: "long"), CancellationToken.None);

        Assert.That(a.Equals(b), Is.False,
            "A column-type change must invalidate registry equality");
    }

    [Test]
    public void EntityRegistry_DifferentDialect_NotEqual()
    {
        var a = EntityRegistry.Build(CreateContexts(), CancellationToken.None);
        var b = EntityRegistry.Build(
            CreateContexts(dialect: GenSqlDialect.PostgreSQL), CancellationToken.None);

        Assert.That(a.Equals(b), Is.False,
            "A dialect change must invalidate registry equality");
    }

    private static ImmutableArray<ContextInfo> CreateContexts(
        string ageColumnType = "int",
        GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var mods = new ColumnModifiers();
        var userEntity = new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("Name", "name", "string", "string", false, ColumnKind.Standard, null, mods),
                new ColumnInfo("Age", "age", ageColumnType, ageColumnType, false, ColumnKind.Standard, null, mods, isValueType: true)
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);

        var context = new ContextInfo(
            className: "TestContext",
            @namespace: "TestApp",
            dialectConfig: new GenSqlDialectConfig(dialect),
            schema: null,
            entities: new[] { userEntity },
            entityMappings: System.Array.Empty<EntityMapping>(),
            location: Location.None);

        return ImmutableArray.Create(context);
    }

    // ── AssembledPlan / CarrierPlan / FileInterceptorGroup:
    //    real instances harvested from tracked generator runs ────────────────

    private const string SharedSource = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    private const string ServiceTemplate = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public class Service
{
    public async void DoWork(TestDbContext db)
    {
        __PREFIX__await db.Users().Where(u => u.UserId == __WHERE__).Select(u => __PROJECTION__).ExecuteFetchAllAsync();
    }
}
";

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(Schema).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in new[]
        {
            "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll",
            "System.Linq.Expressions.dll", "netstandard.dll", "System.Threading.Tasks.dll",
        })
        {
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, dll)));
        }

        return references;
    }

    /// <summary>
    /// Runs a fresh generator driver over the shared schema plus one service
    /// file and returns that file's <see cref="FileInterceptorGroup"/> from the
    /// tracked Stage-5 outputs.
    /// </summary>
    private static FileInterceptorGroup RunAndGetGroup(
        string whereValue = "1",
        string projection = "(Id: u.UserId, Name: u.UserName)",
        string servicePath = "Service.cs",
        string bodyPrefix = "")
    {
        var serviceSource = ServiceTemplate
            .Replace("__PREFIX__", bodyPrefix)
            .Replace("__WHERE__", whereValue)
            .Replace("__PROJECTION__", projection);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(SharedSource, parseOptions, path: "Shared.cs"),
                CSharpSyntaxTree.ParseText(serviceSource, parseOptions, path: servicePath),
            },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new QuarryGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();

        var crash = result.Diagnostics.Where(d => d.Id == "CS8785").ToList();
        Assert.That(crash, Is.Empty, "Generator must not crash while harvesting groups");

        return result.Results[0].TrackedSteps[TrackingNames.PerFileGroups]
            .SelectMany(s => s.Outputs)
            .Select(o => (FileInterceptorGroup)o.Value)
            .Single(g => g.SourceFilePath == servicePath);
    }

    [Test]
    public void Group_IdenticalIndependentRuns_EqualGraph()
    {
        var a = RunAndGetGroup();
        var b = RunAndGetGroup();

        Assert.That(ReferenceEquals(a, b), Is.False, "Runs must yield independent instances");
        Assert.That(a.Equals(b), Is.True,
            "Independently produced groups over identical source must be equal");
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

        // Drill into the graph so a broken constituent Equals is localized.
        Assert.That(a.AssembledPlans, Has.Count.EqualTo(b.AssembledPlans.Count));
        for (int i = 0; i < a.AssembledPlans.Count; i++)
        {
            Assert.That(a.AssembledPlans[i].Equals(b.AssembledPlans[i]), Is.True,
                $"AssembledPlan[{i}] must be equal across identical runs");
            Assert.That(a.AssembledPlans[i].GetHashCode(), Is.EqualTo(b.AssembledPlans[i].GetHashCode()));
        }

        Assert.That(a.CarrierPlans, Has.Count.EqualTo(b.CarrierPlans.Count));
        Assert.That(a.CarrierPlans, Is.Not.Empty,
            "Fixture chain should produce at least one carrier plan");
        for (int i = 0; i < a.CarrierPlans.Count; i++)
        {
            Assert.That(a.CarrierPlans[i].Equals(b.CarrierPlans[i]), Is.True,
                $"CarrierPlan[{i}] must be equal across identical runs");
            Assert.That(a.CarrierPlans[i].GetHashCode(), Is.EqualTo(b.CarrierPlans[i].GetHashCode()));
        }
    }

    [Test]
    public void Group_DifferentWhereLiteral_PlansNotEqual()
    {
        var a = RunAndGetGroup(whereValue: "1");
        var b = RunAndGetGroup(whereValue: "2");

        Assert.That(a.Equals(b), Is.False,
            "A changed WHERE literal changes the SQL — groups must differ");
        Assert.That(
            a.AssembledPlans.Zip(b.AssembledPlans, (x, y) => x.Equals(y)).All(eq => eq),
            Is.False,
            "At least one AssembledPlan must reflect the changed literal");
    }

    [Test]
    public void Group_DifferentProjection_PlansAndCarriersNotEqual()
    {
        var a = RunAndGetGroup(projection: "(Id: u.UserId, Name: u.UserName)");
        var b = RunAndGetGroup(projection: "u.UserName");

        Assert.That(a.Equals(b), Is.False,
            "A changed projection changes the reader shape — groups must differ");
        Assert.That(
            a.AssembledPlans.Zip(b.AssembledPlans, (x, y) => x.Equals(y)).All(eq => eq),
            Is.False,
            "At least one AssembledPlan must reflect the changed projection");
        // CarrierPlan intentionally excludes the projection: it models clause
        // capture (fields, parameters, mask, extraction), not the reader.
    }

    [Test]
    public void Group_CapturedVariableInsteadOfLiteral_CarriersNotEqual()
    {
        var a = RunAndGetGroup(whereValue: "1");
        var b = RunAndGetGroup(whereValue: "id", bodyPrefix: "var id = 1;\r\n        ");

        Assert.That(a.Equals(b), Is.False,
            "Literal vs captured variable changes parameterization — groups must differ");
        Assert.That(
            a.CarrierPlans.Zip(b.CarrierPlans, (x, y) => x.Equals(y)).All(eq => eq),
            Is.False,
            "At least one CarrierPlan must reflect the captured-variable field/extraction shape");
    }

    [Test]
    public void Group_DifferentSourcePath_NotEqual()
    {
        var a = RunAndGetGroup(servicePath: "Service.cs");
        var b = RunAndGetGroup(servicePath: "Renamed.cs");

        Assert.That(a.Equals(b), Is.False,
            "Groups are keyed per source file — a moved file must not compare equal");
        Assert.That(a.FileTag, Is.Not.EqualTo(b.FileTag));
    }
}
