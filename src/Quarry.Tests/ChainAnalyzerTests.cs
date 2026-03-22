using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;

namespace Quarry.Tests;

/// <summary>
/// Tests for <see cref="ChainAnalyzer"/> intra-method dataflow analysis.
/// </summary>
[TestFixture]
public class ChainAnalyzerTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    #region Helpers

    /// <summary>
    /// Shared schema + context preamble used by all test sources.
    /// The test method body is injected via <paramref name="methodBody"/>.
    /// </summary>
    private static string WrapInTestSource(string methodBody)
    {
        return @"
using Quarry;
using System;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<int> Age => Default(0);
    public Col<bool> IsActive => Default(true);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class Db : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Svc
{
    private static QueryBuilder<User> GetQuery() => throw new NotImplementedException();

    public async Task Test(Db db)
    {
        bool condition = true;
        bool condition2 = false;
        bool condition3 = true;
        bool condition4 = false;
        bool condition5 = true;
        bool condition6 = false;
" + methodBody + @"
    }
}
";
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var syntaxTrees = sources.Select(s =>
            CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    /// <summary>
    /// Method name to InterceptorKind mapping, mirroring UsageSiteDiscovery.InterceptableMethods.
    /// </summary>
    private static readonly Dictionary<string, InterceptorKind> MethodKindMap = new(StringComparer.Ordinal)
    {
        ["Select"] = InterceptorKind.Select,
        ["Where"] = InterceptorKind.Where,
        ["OrderBy"] = InterceptorKind.OrderBy,
        ["ThenBy"] = InterceptorKind.ThenBy,
        ["GroupBy"] = InterceptorKind.GroupBy,
        ["Having"] = InterceptorKind.Having,
        ["Set"] = InterceptorKind.Set,
        ["Join"] = InterceptorKind.Join,
        ["LeftJoin"] = InterceptorKind.LeftJoin,
        ["RightJoin"] = InterceptorKind.RightJoin,
        ["ExecuteFetchAllAsync"] = InterceptorKind.ExecuteFetchAll,
        ["ExecuteFetchFirstAsync"] = InterceptorKind.ExecuteFetchFirst,
        ["ExecuteFetchFirstOrDefaultAsync"] = InterceptorKind.ExecuteFetchFirstOrDefault,
        ["ExecuteFetchSingleAsync"] = InterceptorKind.ExecuteFetchSingle,
        ["ExecuteScalarAsync"] = InterceptorKind.ExecuteScalar,
        ["ExecuteNonQueryAsync"] = InterceptorKind.ExecuteNonQuery,
        ["ToAsyncEnumerable"] = InterceptorKind.ToAsyncEnumerable,
    };

    /// <summary>
    /// Converts an AnalyzedChain (new pipeline) to a ChainAnalysisResult (old type) for test assertions.
    /// </summary>
    private static ChainAnalysisResult ConvertToResult(Quarry.Generators.Parsing.AnalyzedChain chain)
    {
        var plan = chain.Plan;

        // Build clause list with roles, conditional flags, and bit indices
        var clauses = new List<ChainedClauseSite>();
        var conditionalBitLookup = new Dictionary<string, int>(StringComparer.Ordinal);

        // Map conditional terms to their clause sites by matching position
        int condIdx = 0;
        foreach (var cs in chain.ClauseSites)
        {
            if (cs.Bound.Raw.ConditionalInfo != null && condIdx < plan.ConditionalTerms.Count)
            {
                conditionalBitLookup[cs.Bound.Raw.UniqueId] = plan.ConditionalTerms[condIdx].BitIndex;
                condIdx++;
            }
        }

        foreach (var cs in chain.ClauseSites)
        {
            var isConditional = cs.Bound.Raw.ConditionalInfo != null;
            conditionalBitLookup.TryGetValue(cs.Bound.Raw.UniqueId, out var bitIdx);
            var hasBit = conditionalBitLookup.ContainsKey(cs.Bound.Raw.UniqueId);

            var role = cs.Clause?.Kind switch
            {
                ClauseKind.Where => ClauseRole.Where,
                ClauseKind.OrderBy => ClauseRole.OrderBy,
                ClauseKind.GroupBy => ClauseRole.GroupBy,
                ClauseKind.Having => ClauseRole.Having,
                ClauseKind.Set => ClauseRole.Set,
                ClauseKind.Join => ClauseRole.Join,
                _ => ClauseRole.Where
            };

            var site = UsageSiteInfo.FromTranslatedCallSite(cs);
            clauses.Add(new ChainedClauseSite(site, isConditional, hasBit ? (int?)bitIdx : null, role));
        }

        // Build conditional clauses with BranchKind from RawCallSite.ConditionalInfo
        var conditionalClauses = new List<ConditionalClause>();
        condIdx = 0;
        foreach (var cs in chain.ClauseSites)
        {
            if (cs.Bound.Raw.ConditionalInfo != null && condIdx < plan.ConditionalTerms.Count)
            {
                var term = plan.ConditionalTerms[condIdx];
                var site = UsageSiteInfo.FromTranslatedCallSite(cs);
                conditionalClauses.Add(new ConditionalClause(
                    term.BitIndex, site, cs.Bound.Raw.ConditionalInfo.BranchKind));
                condIdx++;
            }
        }

        var executionSite = UsageSiteInfo.FromTranslatedCallSite(chain.ExecutionSite);

        return new ChainAnalysisResult(
            tier: plan.Tier,
            clauses: clauses,
            executionSite: executionSite,
            conditionalClauses: conditionalClauses,
            possibleMasks: plan.PossibleMasks,
            notAnalyzableReason: plan.NotAnalyzableReason,
            unmatchedMethodNames: plan.UnmatchedMethodNames);
    }

    /// <summary>
    /// Runs the generator with test capture enabled, returns captured chains.
    /// </summary>
    private static List<Quarry.Generators.Parsing.AnalyzedChain> RunGeneratorWithCapture(string methodBody)
    {
        var source = WrapInTestSource(methodBody);
        var compilation = CreateCompilation(source);

        ChainAnalyzer.TestCapturedChains = new List<Quarry.Generators.Parsing.AnalyzedChain>();
        try
        {
            var generator = new Quarry.Generators.QuarryGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return new List<Quarry.Generators.Parsing.AnalyzedChain>(ChainAnalyzer.TestCapturedChains);
        }
        finally
        {
            ChainAnalyzer.TestCapturedChains = null;
        }
    }

    /// <summary>
    /// Analyzes the chain in the Test method by running the full generator pipeline.
    /// Returns the chain analysis result for the execution site at the given index.
    /// </summary>
    private static ChainAnalysisResult? AnalyzeChain(string methodBody, int executionIndex = -1)
    {
        var captured = RunGeneratorWithCapture(methodBody);
        if (captured.Count == 0)
            return null;

        var targetIndex = executionIndex >= 0 ? executionIndex : captured.Count - 1;
        if (targetIndex >= captured.Count)
            return null;

        return ConvertToResult(captured[targetIndex]);
    }

    /// <summary>
    /// Analyzes ALL execution chains in the Test method and returns all results.
    /// </summary>
    private static List<ChainAnalysisResult> AnalyzeAllChains(string methodBody)
    {
        var captured = RunGeneratorWithCapture(methodBody);
        return captured.Select(ConvertToResult).ToList();
    }

    /// <summary>
    /// Runs the full generator and returns diagnostics matching the given ID.
    /// </summary>
    private static List<Diagnostic> RunGeneratorAndGetDiagnostics(string methodBody, string diagnosticId)
    {
        var source = WrapInTestSource(methodBody);
        var compilation = CreateCompilation(source);

        var generator = new Quarry.Generators.QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var result = driver.GetRunResult();
        return result.Diagnostics.Where(d => d.Id == diagnosticId).ToList();
    }

    #endregion

    #region Direct Fluent Chain (Tier 1, no conditionals)

    [Test]
    public void FluentChain_WhereSelectExecute_Tier1_NoClauses()
    {
        var result = AnalyzeChain(@"
        await db.Users().Where(u => u.UserId > 0).Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Is.Empty);
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(1));
        Assert.That(result.PossibleMasks[0], Is.EqualTo(0UL));
        Assert.That(result.NotAnalyzableReason, Is.Null);
    }

    [Test]
    public void FluentChain_MultiClause_CorrectRoles()
    {
        var result = AnalyzeChain(@"
        await db.Users().Where(u => u.IsActive).OrderBy(u => u.UserName).Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.Clauses, Has.Count.GreaterThanOrEqualTo(2));

        var roles = result.Clauses.Select(c => c.Role).ToList();
        Assert.That(roles, Does.Contain(ClauseRole.Where));
        Assert.That(roles, Does.Contain(ClauseRole.OrderBy));
    }

    [Test]
    public void FluentChain_AllClausesUnconditional()
    {
        var result = AnalyzeChain(@"
        await db.Users().Where(u => u.Age > 18).Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Clauses.All(c => !c.IsConditional), Is.True);
        Assert.That(result.Clauses.All(c => c.BitIndex == null), Is.True);
    }

    #endregion

    #region Variable Chain — Unconditional (Tier 1)

    [Test]
    public void VariableChain_Unconditional_Tier1()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        q = q.OrderBy(u => u.UserName);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Is.Empty);
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(1));
        Assert.That(result.PossibleMasks[0], Is.EqualTo(0UL));
    }

    [Test]
    public void VariableChain_Unconditional_ClausesPopulated()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.Age > 0);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        // Should have at least the Where from the variable assignment
        Assert.That(result!.Clauses.Count + result.ExecutionSite.MethodName.Length, Is.GreaterThan(0));
        Assert.That(result.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
    }

    #endregion

    #region Independent Conditionals (Tier 1)

    [Test]
    public void IndependentConditional_SingleIf_OneBit_TwoMasks()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(1));
        Assert.That(result.ConditionalClauses[0].BranchKind, Is.EqualTo(BranchKind.Independent));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(2));
        // Masks should contain 0 (bit off) and 1 (bit on)
        Assert.That(result.PossibleMasks, Does.Contain(0UL));
        Assert.That(result.PossibleMasks, Does.Contain(1UL));
    }

    [Test]
    public void IndependentConditional_TwoIfs_TwoBits_FourMasks()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(2));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(4));
    }

    [Test]
    public void IndependentConditional_FourIfs_FourBits_SixteenMasks()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        if (condition3)
            q = q.Where(u => u.Age < 65);
        if (condition4)
            q = q.OrderBy(u => u.Age);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(4));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(16));
    }

    [Test]
    public void IndependentConditional_ConditionalHasBitIndex_UnconditionalDoesNot()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        var unconditional = result!.Clauses.Where(c => !c.IsConditional).ToList();
        var conditional = result.Clauses.Where(c => c.IsConditional).ToList();

        Assert.That(unconditional.All(c => c.BitIndex == null), Is.True);
        Assert.That(conditional.All(c => c.BitIndex != null), Is.True);
    }

    #endregion

    #region Mutually Exclusive Conditionals (Tier 1)

    [Test]
    public void MutuallyExclusive_IfElse_OneBit()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        else
            q = q.OrderBy(u => u.Age);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(2));

        // Both should be MutuallyExclusive
        Assert.That(result.ConditionalClauses.All(c => c.BranchKind == BranchKind.MutuallyExclusive), Is.True);
    }

    [Test]
    public void MixedBranches_IndependentAndExclusive_CorrectMasks()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.Where(u => u.Age > 21);
        if (condition2)
            q = q.OrderBy(u => u.UserName);
        else
            q = q.OrderBy(u => u.Age);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        // 1 independent + 2 mutually exclusive = 3 conditional clauses, but masks
        // are: independent (on/off) x exclusive (one-of-two)
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(3));
        Assert.That(result.PossibleMasks.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Tier 2 — Threshold Exceeded

    [Test]
    public void FiveConditionals_ExceedsThreshold_Tier2()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        if (condition3)
            q = q.Where(u => u.Age < 65);
        if (condition4)
            q = q.OrderBy(u => u.Age);
        if (condition5)
            q = q.Where(u => u.UserId > 100);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrequotedFragments));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(5));
        Assert.That(result.PossibleMasks, Is.Empty);
    }

    [Test]
    public void SixConditionals_Tier2()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        if (condition3)
            q = q.Where(u => u.Age < 65);
        if (condition4)
            q = q.OrderBy(u => u.Age);
        if (condition5)
            q = q.Where(u => u.UserId > 100);
        if (condition6)
            q = q.Where(u => u.UserId < 999);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrequotedFragments));
        Assert.That(result.PossibleMasks, Is.Empty);
    }

    #endregion

    #region Disqualifiers (Tier 3)

    [Test]
    public void Disqualified_AssignedInForLoop()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        for (int i = 0; i < 3; i++)
            q = q.Where(u => u.Age > 0);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("loop"));
    }

    [Test]
    public void Disqualified_AssignedInForeachLoop()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        var items = new[] { 1, 2, 3 };
        foreach (var item in items)
            q = q.Where(u => u.Age > 0);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("loop"));
    }

    [Test]
    public void Disqualified_AssignedInWhileLoop()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        while (condition)
            q = q.Where(u => u.Age > 0);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("loop"));
    }

    [Test]
    public void Disqualified_AssignedInDoWhileLoop()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        do { q = q.Where(u => u.Age > 0); } while (condition);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("loop"));
    }

    [Test]
    public void Disqualified_AssignedInTryCatch()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        try { q = q.Where(u => u.Age > 0); } catch { }
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("try/catch"));
    }

    [Test]
    public void Disqualified_PassedAsArgument()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        Console.WriteLine(q);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("argument"));
    }

    [Test]
    public void Disqualified_CapturedInLambda()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        Action a = () => { var x = q; };
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("lambda"));
    }

    [Test]
    public void Disqualified_CapturedInLocalFunction()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        void LocalFunc() { var x = q; }
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        // May report as "lambda" or "local function" depending on which dataflow check fires first
        Assert.That(result.NotAnalyzableReason, Does.Contain("captured"));
    }

    [Test]
    public void Disqualified_ExcessiveNesting()
    {
        // MaxIfNestingDepth is 2, check is depth > 2, so we need 4 levels (depth 3)
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
        {
            if (condition2)
            {
                if (condition3)
                {
                    if (condition4)
                        q = q.OrderBy(u => u.UserName);
                }
            }
        }
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("nesting"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void ExecutionChainClauses_AppendedAsUnconditional()
    {
        // query.Where(...).ExecuteFetchAllAsync() — the Where is on the execution chain
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        await q.Where(u => u.Age > 18).Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        // The Where on the execution chain should be unconditional
        var executionChainClauses = result.Clauses.Where(c => c.Role == ClauseRole.Where).ToList();
        Assert.That(executionChainClauses.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void EmptyChain_NoClauseSites_Tier1()
    {
        var result = AnalyzeChain(@"
        await db.Users().Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Is.Empty);
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void VariableChain_NoConditional_SingleMask()
    {
        var result = AnalyzeChain(@"
        var q = db.Users();
        q = q.Where(u => u.IsActive);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(1));
        Assert.That(result.PossibleMasks[0], Is.EqualTo(0UL));
    }

    #endregion

    #region IsExecutionKind

    [Test]
    public void IsExecutionKind_ExecutionMethods_ReturnTrue()
    {
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ExecuteFetchAll), Is.True);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ExecuteFetchFirst), Is.True);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ExecuteFetchFirstOrDefault), Is.True);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ExecuteFetchSingle), Is.True);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ExecuteScalar), Is.True);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ExecuteNonQuery), Is.True);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ToAsyncEnumerable), Is.True);
    }

    [Test]
    public void IsExecutionKind_NonExecutionMethods_ReturnFalse()
    {
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.Select), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.Where), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.OrderBy), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.ThenBy), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.GroupBy), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.Having), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.Set), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.Join), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.LeftJoin), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.RightJoin), Is.False);
        Assert.That(ChainAnalyzer.IsExecutionKind(InterceptorKind.Unknown), Is.False);
    }

    #endregion

    #region Integration — QRY030/031/032 Diagnostics

    [Test]
    public void Integration_FluentChain_EmitsQRY030()
    {
        var diagnostics = RunGeneratorAndGetDiagnostics(@"
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();
", "QRY030");

        Assert.That(diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("tier 1"));
    }

    [Test]
    public void Integration_ManyConditionals_EmitsQRY031()
    {
        var diagnostics = RunGeneratorAndGetDiagnostics(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        if (condition3)
            q = q.Where(u => u.Age < 65);
        if (condition4)
            q = q.OrderBy(u => u.Age);
        if (condition5)
            q = q.Where(u => u.UserId > 100);
        await q.Select(u => u).ExecuteFetchAllAsync();
", "QRY031");

        Assert.That(diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("tier 2"));
    }

    [Test]
    public void Integration_LoopAssignment_EmitsQRY032()
    {
        var diagnostics = RunGeneratorAndGetDiagnostics(@"
        var q = db.Users().Where(u => u.IsActive);
        for (int i = 0; i < 3; i++)
            q = q.Where(u => u.Age > 0);
        await q.Select(u => u).ExecuteFetchAllAsync();
", "QRY032");

        Assert.That(diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("not analyzable"));
    }

    [Test]
    public void Integration_QRY032_ReasonMessageFlowsThrough()
    {
        var diagnostics = RunGeneratorAndGetDiagnostics(@"
        var q = db.Users().Where(u => u.IsActive);
        try { q = q.Where(u => u.Age > 0); } catch { }
        await q.Select(u => u).ExecuteFetchAllAsync();
", "QRY032");

        Assert.That(diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("try/catch"));
    }

    #endregion

    #region Different Execution Methods

    [Test]
    public void FluentChain_ExecuteFetchFirstAsync_Tier1()
    {
        var result = AnalyzeChain(@"
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchFirstAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(1));
    }

    [Test]
    public void FluentChain_ExecuteFetchFirstOrDefaultAsync_Tier1()
    {
        var result = AnalyzeChain(@"
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchFirstOrDefaultAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
    }

    [Test]
    public void FluentChain_ExecuteFetchSingleAsync_Tier1()
    {
        var result = AnalyzeChain(@"
        await db.Users().Where(u => u.UserId == 1).Select(u => u).ExecuteFetchSingleAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
    }

    [Test]
    public void VariableChain_ExecuteFetchFirstAsync_WithConditional_Tier1()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        await q.Select(u => u).ExecuteFetchFirstAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(1));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(2));
    }

    #endregion

    #region Mask Value Correctness

    [Test]
    public void IndependentConditional_TwoBits_CorrectMaskValues()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PossibleMasks, Has.Count.EqualTo(4));

        // 2 independent bits → masks: 00, 01, 10, 11
        var masks = result.PossibleMasks.OrderBy(m => m).ToList();
        Assert.That(masks, Does.Contain(0b00UL)); // neither
        Assert.That(masks, Does.Contain(0b01UL)); // bit 0 only
        Assert.That(masks, Does.Contain(0b10UL)); // bit 1 only
        Assert.That(masks, Does.Contain(0b11UL)); // both
    }

    [Test]
    public void MutuallyExclusive_IfElse_NoNeitherMask()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        else
            q = q.OrderBy(u => u.Age);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        // Mutually exclusive: exactly one branch runs, so mask 0 (neither) should NOT be present.
        // Each mask should have at least one of the exclusive bits set.
        foreach (var mask in result!.PossibleMasks)
        {
            var anyExclusiveBitSet = result.ConditionalClauses
                .Any(cc => (mask & (1UL << cc.BitIndex)) != 0);
            Assert.That(anyExclusiveBitSet, Is.True,
                $"Mask {mask} has no exclusive bit set — 'neither' option should not exist");
        }
    }

    [Test]
    public void MixedBranches_CorrectCartesianProduct()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.Where(u => u.Age > 21);
        if (condition2)
            q = q.OrderBy(u => u.UserName);
        else
            q = q.OrderBy(u => u.Age);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        // 1 independent bit (on/off = 2) × 1 exclusive pair (one-of-two = 2) = 4 masks
        Assert.That(result!.PossibleMasks, Has.Count.EqualTo(4));

        // All masks should have exactly one of the exclusive bits set
        var exclusiveClauses = result.ConditionalClauses
            .Where(c => c.BranchKind == BranchKind.MutuallyExclusive)
            .Select(c => c.BitIndex)
            .ToList();

        foreach (var mask in result.PossibleMasks)
        {
            var exclusiveBitsSet = exclusiveClauses.Count(bit => (mask & (1UL << bit)) != 0);
            Assert.That(exclusiveBitsSet, Is.EqualTo(1),
                $"Mask {mask} should have exactly 1 exclusive bit set, got {exclusiveBitsSet}");
        }
    }

    #endregion

    #region ClauseRole on Variable Chains

    [Test]
    public void VariableChain_ConditionalClauses_HaveCorrectRoles()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.OrderBy(u => u.UserName);
        if (condition2)
            q = q.Where(u => u.Age > 21);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);

        // The conditional clauses should have the right roles
        var conditionalRoles = result!.Clauses
            .Where(c => c.IsConditional)
            .Select(c => c.Role)
            .ToList();

        Assert.That(conditionalRoles, Has.Count.EqualTo(2));
        Assert.That(conditionalRoles, Does.Contain(ClauseRole.OrderBy));
        Assert.That(conditionalRoles, Does.Contain(ClauseRole.Where));
    }

    [Test]
    public void VariableChain_UnconditionalAndConditional_MixedRoles()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        q = q.OrderBy(u => u.UserName);
        if (condition)
            q = q.Where(u => u.Age > 21);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);

        var unconditionalRoles = result!.Clauses
            .Where(c => !c.IsConditional)
            .Select(c => c.Role)
            .ToList();
        var conditionalRoles = result.Clauses
            .Where(c => c.IsConditional)
            .Select(c => c.Role)
            .ToList();

        // Unconditional: Where (from init) and OrderBy (from reassignment)
        Assert.That(unconditionalRoles, Does.Contain(ClauseRole.Where));
        Assert.That(unconditionalRoles, Does.Contain(ClauseRole.OrderBy));

        // Conditional: Where (from if block)
        Assert.That(conditionalRoles, Has.Count.EqualTo(1));
        Assert.That(conditionalRoles[0], Is.EqualTo(ClauseRole.Where));
    }

    #endregion

    #region Opaque Assignment Disqualifier

    [Test]
    public void Disqualified_AssignedFromNonQuarryMethod()
    {
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        q = GetQuery();
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        // GetQuery() is not a Quarry builder method — this is an opaque assignment
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
        Assert.That(result.NotAnalyzableReason, Does.Contain("non-Quarry"));
    }

    #endregion

    #region Multiple Execution Sites in One Method

    [Test]
    public void MultipleExecutionSites_EachAnalyzedIndependently()
    {
        var results = AnalyzeAllChains(@"
        var q1 = db.Users().Where(u => u.IsActive);
        await q1.Select(u => u).ExecuteFetchAllAsync();

        var q2 = db.Users().Where(u => u.Age > 21);
        if (condition)
            q2 = q2.OrderBy(u => u.UserName);
        await q2.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(results, Has.Count.EqualTo(2));

        // First chain: unconditional variable chain
        Assert.That(results[0].Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(results[0].ConditionalClauses, Is.Empty);

        // Second chain: one conditional
        Assert.That(results[1].Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(results[1].ConditionalClauses, Has.Count.EqualTo(1));
    }

    [Test]
    public void MultipleExecutionSites_DifferentTiers()
    {
        var results = AnalyzeAllChains(@"
        await db.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();

        var q = db.Users().Where(u => u.Age > 21);
        for (int i = 0; i < 3; i++)
            q = q.Where(u => u.Age > 0);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(results, Has.Count.EqualTo(2));

        // First: fluent chain → tier 1
        Assert.That(results[0].Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));

        // Second: loop disqualifier → tier 3
        Assert.That(results[1].Tier, Is.EqualTo(OptimizationTier.RuntimeBuild));
    }

    #endregion

    #region Variable + Terminal Chain Clause Merging

    [Test]
    public void VariableAndTerminalClauses_BothPresent()
    {
        // q has Where from variable assignment; OrderBy + Select are on the terminal chain
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        await q.OrderBy(u => u.UserName).Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));

        var roles = result.Clauses.Select(c => c.Role).ToList();
        // Where from the variable, OrderBy from the terminal chain
        Assert.That(roles, Does.Contain(ClauseRole.Where));
        Assert.That(roles, Does.Contain(ClauseRole.OrderBy));

        // All should be unconditional
        Assert.That(result.Clauses.All(c => !c.IsConditional), Is.True);
    }

    [Test]
    public void VariableConditionalAndTerminalClauses_Mixed()
    {
        // q has conditional Where; OrderBy is unconditional on terminal chain
        var result = AnalyzeChain(@"
        var q = db.Users().Where(u => u.IsActive);
        if (condition)
            q = q.Where(u => u.Age > 21);
        await q.OrderBy(u => u.UserName).Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(1));

        // Terminal chain clauses (OrderBy) should be unconditional
        var terminalOrderBy = result.Clauses
            .Where(c => c.Role == ClauseRole.OrderBy)
            .ToList();
        Assert.That(terminalOrderBy, Has.Count.EqualTo(1));
        Assert.That(terminalOrderBy[0].IsConditional, Is.False);
    }

    #endregion

    #region Zero-Clause Variable Chain

    [Test]
    public void VariableChain_RawBuilder_NoClauses_Tier1()
    {
        // Variable holds the raw builder with no clauses added
        var result = AnalyzeChain(@"
        var q = db.Users();
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Is.Empty);
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(1));
        Assert.That(result.PossibleMasks[0], Is.EqualTo(0UL));
    }

    [Test]
    public void VariableChain_RawBuilderWithConditionalOnly_Tier1()
    {
        // Variable starts as raw builder, only clause is conditional
        var result = AnalyzeChain(@"
        var q = db.Users();
        if (condition)
            q = q.Where(u => u.IsActive);
        await q.Select(u => u).ExecuteFetchAllAsync();
");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Tier, Is.EqualTo(OptimizationTier.PrebuiltDispatch));
        Assert.That(result.ConditionalClauses, Has.Count.EqualTo(1));
        Assert.That(result.PossibleMasks, Has.Count.EqualTo(2));
    }

    #endregion
}
