using System.Text;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for join sites (Join, LeftJoin, RightJoin)
/// and joined builder methods (joined Where, OrderBy, Select).
/// </summary>
/// <remarks>
/// Replaces join generation methods in InterceptorCodeGenerator.Joins.cs.
/// Phase 6A Step 3 ports methods one kind at a time.
/// </remarks>
internal static class JoinBodyEmitter
{
    /// <summary>
    /// Emits a Join/LeftJoin/RightJoin interceptor body.
    /// Appends ON clause and returns joined builder type.
    /// </summary>
    public static void EmitJoin(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateJoinInterceptor
    }

    /// <summary>
    /// Emits a joined Where interceptor (multi-entity lambda resolution).
    /// </summary>
    public static void EmitJoinedWhere(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateJoinedWhereInterceptor
    }

    /// <summary>
    /// Emits a joined Select interceptor (multi-entity projection).
    /// </summary>
    public static void EmitJoinedSelect(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateJoinedSelectInterceptor
    }

    /// <summary>
    /// Emits a joined OrderBy interceptor.
    /// </summary>
    public static void EmitJoinedOrderBy(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateJoinedOrderByInterceptor
    }
}
