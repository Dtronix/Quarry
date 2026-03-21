using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for clause sites (Where, OrderBy, Select,
/// GroupBy, Having, Set, Distinct, Limit, Offset, WithTimeout).
/// Handles both carrier-path (field mutation + cast) and non-carrier-path
/// (QueryBuilder delegation) emission.
/// </summary>
/// <remarks>
/// Replaces the clause generation methods in InterceptorCodeGenerator.Query.cs.
/// Phase 6A Step 3 ports methods one kind at a time.
/// </remarks>
internal static class ClauseBodyEmitter
{
    /// <summary>
    /// Emits a Where clause interceptor body.
    /// Non-carrier: appends SQL fragment to QueryBuilder.
    /// Carrier: extracts params to carrier fields, sets mask bit.
    /// </summary>
    public static void EmitWhere(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain,
        bool isFirstInChain,
        int? clauseBit)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateWhereInterceptor
    }

    /// <summary>
    /// Emits an OrderBy/ThenBy clause interceptor body.
    /// </summary>
    public static void EmitOrderBy(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain,
        bool isFirstInChain,
        int? clauseBit)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateOrderByInterceptor
    }

    /// <summary>
    /// Emits a Select clause interceptor body.
    /// </summary>
    public static void EmitSelect(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain,
        bool isFirstInChain,
        int? clauseBit)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateSelectInterceptor
    }

    /// <summary>
    /// Emits a GroupBy clause interceptor body.
    /// </summary>
    public static void EmitGroupBy(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain,
        bool isFirstInChain,
        int? clauseBit)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateGroupByInterceptor
    }

    /// <summary>
    /// Emits a Set/UpdateSet clause interceptor body.
    /// </summary>
    public static void EmitSet(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain,
        bool isFirstInChain,
        int? clauseBit)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateSetInterceptor
    }

    /// <summary>
    /// Emits a Limit or Offset clause interceptor body.
    /// Carrier: stores value on carrier field. Non-carrier: delegates to builder.
    /// </summary>
    public static void EmitPagination(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo? chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateCarrierPaginationInterceptor
    }

    /// <summary>
    /// Emits a Distinct clause interceptor body (always noop on carrier path).
    /// </summary>
    public static void EmitDistinct(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateCarrierDistinctInterceptor
    }

    /// <summary>
    /// Emits a WithTimeout clause interceptor body.
    /// </summary>
    public static void EmitWithTimeout(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateCarrierWithTimeoutInterceptor
    }
}
