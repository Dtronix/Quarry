using System;
using System.Collections.Generic;
using Quarry.Generators.CodeGen;
using Quarry.Generators.IR;

namespace Quarry.Generators.Models;

/// <summary>
/// Groups all interceptor data for a single (context, source file) pair.
/// Output of the SelectMany transform — one instance per source file per context.
/// Must have value equality for Roslyn incremental caching.
/// </summary>
internal sealed class FileInterceptorGroup : IEquatable<FileInterceptorGroup>
{
    /// <summary>
    /// Legacy constructor for old pipeline (UsageSiteInfo + PrebuiltChainInfo).
    /// </summary>
    public FileInterceptorGroup(
        string contextClassName,
        string? contextNamespace,
        string sourceFilePath,
        string fileTag,
        IReadOnlyList<UsageSiteInfo> sites,
        IReadOnlyList<PrebuiltChainInfo> chains,
        IReadOnlyList<UsageSiteInfo> chainMemberSites,
        IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        SourceFilePath = sourceFilePath;
        FileTag = fileTag;
        Sites = sites;
        Chains = chains;
        ChainMemberSites = chainMemberSites;
        Diagnostics = diagnostics;
        // New pipeline fields default to empty
        TranslatedSites = Array.Empty<TranslatedCallSite>();
        AssembledPlans = Array.Empty<AssembledPlan>();
        TranslatedChainMemberSites = Array.Empty<TranslatedCallSite>();
        CarrierPlans = Array.Empty<CarrierPlan>();
    }

    /// <summary>
    /// New pipeline constructor (TranslatedCallSite + AssembledPlan + CarrierPlan).
    /// </summary>
    public FileInterceptorGroup(
        string contextClassName,
        string? contextNamespace,
        string sourceFilePath,
        string fileTag,
        IReadOnlyList<TranslatedCallSite> translatedSites,
        IReadOnlyList<AssembledPlan> assembledPlans,
        IReadOnlyList<TranslatedCallSite> translatedChainMemberSites,
        IReadOnlyList<DiagnosticInfo> diagnostics,
        IReadOnlyList<CarrierPlan> carrierPlans)
    {
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        SourceFilePath = sourceFilePath;
        FileTag = fileTag;
        TranslatedSites = translatedSites;
        AssembledPlans = assembledPlans;
        TranslatedChainMemberSites = translatedChainMemberSites;
        Diagnostics = diagnostics;
        CarrierPlans = carrierPlans;
        // Legacy fields default to empty
        Sites = Array.Empty<UsageSiteInfo>();
        Chains = Array.Empty<PrebuiltChainInfo>();
        ChainMemberSites = Array.Empty<UsageSiteInfo>();
    }

    public string ContextClassName { get; }
    public string? ContextNamespace { get; }
    public string SourceFilePath { get; }
    public string FileTag { get; }

    // Legacy pipeline types
    public IReadOnlyList<UsageSiteInfo> Sites { get; }
    public IReadOnlyList<PrebuiltChainInfo> Chains { get; }
    public IReadOnlyList<UsageSiteInfo> ChainMemberSites { get; }

    // New pipeline types
    public IReadOnlyList<TranslatedCallSite> TranslatedSites { get; }
    public IReadOnlyList<AssembledPlan> AssembledPlans { get; }
    public IReadOnlyList<TranslatedCallSite> TranslatedChainMemberSites { get; }
    public IReadOnlyList<CarrierPlan> CarrierPlans { get; }

    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    /// <summary>
    /// Returns true if this group was created by the new pipeline.
    /// </summary>
    public bool IsNewPipeline => TranslatedSites.Count > 0 || AssembledPlans.Count > 0;

    public bool Equals(FileInterceptorGroup? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && SourceFilePath == other.SourceFilePath
            && FileTag == other.FileTag
            && EqualityHelpers.SequenceEqual(Sites, other.Sites)
            && EqualityHelpers.SequenceEqual(Chains, other.Chains)
            && EqualityHelpers.SequenceEqual(ChainMemberSites, other.ChainMemberSites)
            && EqualityHelpers.SequenceEqual(TranslatedSites, other.TranslatedSites)
            && EqualityHelpers.SequenceEqual(AssembledPlans, other.AssembledPlans)
            && EqualityHelpers.SequenceEqual(TranslatedChainMemberSites, other.TranslatedChainMemberSites)
            && EqualityHelpers.SequenceEqual(CarrierPlans, other.CarrierPlans)
            && EqualityHelpers.SequenceEqual(Diagnostics, other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as FileInterceptorGroup);

    public override int GetHashCode()
        => HashCode.Combine(ContextClassName, SourceFilePath, FileTag, Sites.Count, TranslatedSites.Count);
}
