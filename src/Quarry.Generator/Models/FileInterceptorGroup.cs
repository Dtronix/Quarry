using System;
using System.Collections.Generic;

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
    }

    /// <summary>
    /// Gets the context class name.
    /// </summary>
    public string ContextClassName { get; }

    /// <summary>
    /// Gets the context namespace.
    /// </summary>
    public string? ContextNamespace { get; }

    /// <summary>
    /// Gets the source file path that all sites in this group originate from.
    /// </summary>
    public string SourceFilePath { get; }

    /// <summary>
    /// Gets the sanitized file tag derived from the source path, used in output filename and class name.
    /// </summary>
    public string FileTag { get; }

    /// <summary>
    /// Gets all analyzable usage sites from this file for this context.
    /// </summary>
    public IReadOnlyList<UsageSiteInfo> Sites { get; }

    /// <summary>
    /// Gets pre-built chains whose execution terminal is in this file.
    /// </summary>
    public IReadOnlyList<PrebuiltChainInfo> Chains { get; }

    /// <summary>
    /// Gets non-analyzable clause sites pulled in by chain analysis
    /// (conditional clause sites that would otherwise be excluded).
    /// </summary>
    public IReadOnlyList<UsageSiteInfo> ChainMemberSites { get; }

    /// <summary>
    /// Gets diagnostics discovered during grouping/analysis for deferred reporting.
    /// </summary>
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
            && EqualityHelpers.SequenceEqual(Chains, other.Chains)
            && EqualityHelpers.SequenceEqual(ChainMemberSites, other.ChainMemberSites)
            && EqualityHelpers.SequenceEqual(Diagnostics, other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as FileInterceptorGroup);

    public override int GetHashCode()
        => HashCode.Combine(ContextClassName, SourceFilePath, FileTag, Sites.Count);
}
