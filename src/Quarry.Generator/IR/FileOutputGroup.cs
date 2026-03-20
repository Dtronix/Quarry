using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Grouped output for a single (context, file) combination.
/// Contains the translated call sites and assembled plans needed
/// for code generation. Replaces FileInterceptorGroup.
/// </summary>
internal sealed class FileOutputGroup : IEquatable<FileOutputGroup>
{
    public FileOutputGroup(
        string contextClassName,
        string contextNamespace,
        string filePath,
        SqlDialect dialect,
        string? schemaName,
        IReadOnlyList<TranslatedCallSite> sites,
        IReadOnlyList<AssembledPlan> plans,
        IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        FilePath = filePath;
        Dialect = dialect;
        SchemaName = schemaName;
        Sites = sites;
        Plans = plans;
        Diagnostics = diagnostics;
    }

    public string ContextClassName { get; }
    public string ContextNamespace { get; }
    public string FilePath { get; }
    public SqlDialect Dialect { get; }
    public string? SchemaName { get; }
    public IReadOnlyList<TranslatedCallSite> Sites { get; }
    public IReadOnlyList<AssembledPlan> Plans { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public bool Equals(FileOutputGroup? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && FilePath == other.FilePath
            && Dialect == other.Dialect
            && SchemaName == other.SchemaName
            && EqualityHelpers.SequenceEqual(Sites, other.Sites)
            && EqualityHelpers.SequenceEqual(Plans, other.Plans)
            && EqualityHelpers.SequenceEqual(Diagnostics, other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as FileOutputGroup);

    public override int GetHashCode()
    {
        return HashCode.Combine(ContextClassName, FilePath, Dialect);
    }
}
