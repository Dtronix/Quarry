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
    public FileInterceptorGroup(
        string contextClassName,
        string? contextNamespace,
        string sourceFilePath,
        string fileTag,
        IReadOnlyList<TranslatedCallSite> sites,
        IReadOnlyList<AssembledPlan> assembledPlans,
        IReadOnlyList<TranslatedCallSite> chainMemberSites,
        IReadOnlyList<DiagnosticInfo> diagnostics,
        IReadOnlyList<CarrierPlan> carrierPlans)
    {
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        SourceFilePath = sourceFilePath;
        FileTag = fileTag;
        Sites = sites;
        AssembledPlans = assembledPlans;
        ChainMemberSites = chainMemberSites;
        Diagnostics = diagnostics;
        CarrierPlans = carrierPlans;
    }

    public string ContextClassName { get; }
    public string? ContextNamespace { get; }
    public string SourceFilePath { get; }
    public string FileTag { get; }

    public IReadOnlyList<TranslatedCallSite> Sites { get; }
    public IReadOnlyList<AssembledPlan> AssembledPlans { get; }
    public IReadOnlyList<TranslatedCallSite> ChainMemberSites { get; }
    public IReadOnlyList<CarrierPlan> CarrierPlans { get; }

    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public bool Equals(FileInterceptorGroup? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && SourceFilePath == other.SourceFilePath
            && FileTag == other.FileTag
            && EqualityHelpers.SequenceEqual(Sites, other.Sites)
            && EqualityHelpers.SequenceEqual(AssembledPlans, other.AssembledPlans)
            && EqualityHelpers.SequenceEqual(ChainMemberSites, other.ChainMemberSites)
            && EqualityHelpers.SequenceEqual(CarrierPlans, other.CarrierPlans)
            && EqualityHelpers.SequenceEqual(Diagnostics, other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as FileInterceptorGroup);

    public override int GetHashCode()
        => HashCode.Combine(ContextClassName, SourceFilePath, FileTag, Sites.Count);
}
