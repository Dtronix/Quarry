namespace Quarry.Generators;

/// <summary>
/// Stable names applied to incremental pipeline nodes via <c>WithTrackingName</c>.
/// Tests enable <c>trackIncrementalGeneratorSteps</c> and assert per-stage
/// run reasons (New/Modified/Cached/Unchanged) through
/// <c>GeneratorRunResult.TrackedSteps[name]</c>.
/// </summary>
public static class TrackingNames
{
    /// <summary>Per-context [QuarryContext] discovery (Pipeline 1 root).</summary>
    public const string ContextDeclarations = nameof(ContextDeclarations);

    /// <summary>Collected-context EntityRegistry barrier feeding all interceptor stages.</summary>
    public const string EntityRegistry = nameof(EntityRegistry);

    /// <summary>Stage 2: raw call-site discovery.</summary>
    public const string RawCallSites = nameof(RawCallSites);

    /// <summary>Stage 2.5: batch display-class enrichment.</summary>
    public const string EnrichedCallSites = nameof(EnrichedCallSites);

    /// <summary>Stage 3: per-site bind results (success or BindFailure).</summary>
    public const string BindResults = nameof(BindResults);

    /// <summary>Stage 4: per-site translation.</summary>
    public const string TranslatedCallSites = nameof(TranslatedCallSites);

    /// <summary>Stage 5: collected analysis grouped per source file.</summary>
    public const string PerFileGroups = nameof(PerFileGroups);
}
