using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Orchestrates per-file interceptor output by routing each site to the
/// appropriate body emitter based on <see cref="InterceptorRouter.Categorize"/>.
/// </summary>
/// <remarks>
/// Designed to replace <see cref="Quarry.Generators.Generation.InterceptorCodeGenerator.GenerateInterceptorsFile"/>.
/// Phase 6A Step 3 wires emitter methods incrementally.
///
/// Output structure per file:
/// <code>
///   // &lt;auto-generated/&gt;
///   #nullable enable
///   using ...;
///   [InterceptsLocation] attribute
///   namespace {contextNamespace} {
///     // Carrier class declarations (file sealed)
///     file static class {Context}.Interceptors.{FileTag} {
///       // Per-site interceptor methods
///     }
///   }
/// </code>
/// </remarks>
internal sealed class FileEmitter
{
    private readonly string _contextClassName;
    private readonly string? _contextNamespace;
    private readonly string _fileTag;
    private readonly IReadOnlyList<UsageSiteInfo> _sites;
    private readonly IReadOnlyList<PrebuiltChainInfo>? _chains;
    private readonly IReadOnlyList<UsageSiteInfo>? _chainMemberSites;

    public FileEmitter(
        string contextClassName,
        string? contextNamespace,
        string fileTag,
        IReadOnlyList<UsageSiteInfo> sites,
        IReadOnlyList<PrebuiltChainInfo>? chains = null,
        IReadOnlyList<UsageSiteInfo>? chainMemberSites = null)
    {
        _contextClassName = contextClassName;
        _contextNamespace = contextNamespace;
        _fileTag = fileTag;
        _sites = sites;
        _chains = chains;
        _chainMemberSites = chainMemberSites;
    }

    /// <summary>
    /// Emits the complete interceptors file for this (context, file) group.
    /// </summary>
    /// <returns>Generated C# source text.</returns>
    public string Emit()
    {
        // Currently delegates to InterceptorCodeGenerator for backward compatibility.
        // Phase 6A Step 3 migrates emission here using the body emitters.
        return Generation.InterceptorCodeGenerator.GenerateInterceptorsFile(
            _contextClassName,
            _contextNamespace,
            _fileTag,
            _sites,
            _chains);
    }

    /// <summary>
    /// Routes a site to the appropriate body emitter based on its category.
    /// </summary>
    /// <remarks>
    /// Phase 6A Step 3 will implement this dispatch, replacing
    /// InterceptorCodeGenerator.GenerateInterceptorMethod's switch statement.
    /// </remarks>
    internal static EmitterCategory CategorizeAndRoute(
        StringBuilder sb,
        UsageSiteInfo site,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain)
    {
        var category = InterceptorRouter.Categorize(site.Kind);

        // Phase 6A Step 3: Each case delegates to the appropriate body emitter
        // switch (category)
        // {
        //     case EmitterCategory.Clause:
        //         ClauseBodyEmitter.Emit*(sb, site, ...);
        //         break;
        //     case EmitterCategory.Terminal:
        //         TerminalBodyEmitter.Emit*(sb, site, ...);
        //         break;
        //     case EmitterCategory.Join:
        //         JoinBodyEmitter.Emit*(sb, site, ...);
        //         break;
        //     case EmitterCategory.RawSql:
        //         RawSqlBodyEmitter.Emit*(sb, site, ...);
        //         break;
        //     case EmitterCategory.Transition:
        //         CarrierEmitter.EmitNoopTransitionBody(sb, ...);
        //         break;
        //     case EmitterCategory.ChainRoot:
        //         CarrierEmitter.EmitChainRootBody(sb, ...);
        //         break;
        // }

        return category;
    }
}
