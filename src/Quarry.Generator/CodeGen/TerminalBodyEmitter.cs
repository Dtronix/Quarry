using System.Text;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for execution terminal sites (FetchAll,
/// FetchFirst, FetchFirstOrDefault, FetchSingle, ExecuteScalar,
/// ExecuteNonQuery, ToAsyncEnumerable, ToDiagnostics).
/// Handles both carrier-path (direct DbCommand) and non-carrier-path
/// (prebuilt SQL dispatch) emission.
/// </summary>
/// <remarks>
/// Replaces terminal generation methods in InterceptorCodeGenerator.Execution.cs
/// and carrier terminal methods in InterceptorCodeGenerator.Carrier.cs.
/// Phase 6A Step 3 ports methods one kind at a time.
/// </remarks>
internal static class TerminalBodyEmitter
{
    /// <summary>
    /// Emits a FetchAll/FetchFirst/FetchSingle reader terminal.
    /// Carrier: extracts params from carrier, creates DbCommand, executes reader.
    /// Non-carrier: dispatches prebuilt SQL, binds params, executes reader.
    /// </summary>
    public static void EmitReaderTerminal(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GeneratePrebuiltSelectExecutionInterceptor
        // and InterceptorCodeGenerator.EmitCarrierExecutionTerminal
    }

    /// <summary>
    /// Emits an ExecuteScalar terminal.
    /// </summary>
    public static void EmitScalarTerminal(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator scalar terminal
    }

    /// <summary>
    /// Emits an ExecuteNonQuery terminal (DELETE/UPDATE).
    /// </summary>
    public static void EmitNonQueryTerminal(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GeneratePrebuiltNonQueryExecutionInterceptor
    }

    /// <summary>
    /// Emits an Insert terminal (InsertExecuteNonQuery/InsertExecuteScalar/InsertToDiagnostics).
    /// </summary>
    public static void EmitInsertTerminal(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator insert terminal
    }

    /// <summary>
    /// Emits a ToDiagnostics terminal (returns SQL string + parameters).
    /// </summary>
    public static void EmitDiagnosticsTerminal(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator diagnostics terminal
    }

    /// <summary>
    /// Emits a ToAsyncEnumerable terminal.
    /// </summary>
    public static void EmitAsyncEnumerableTerminal(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType,
        CarrierStrategy? carrier,
        string? carrierClassName,
        PrebuiltChainInfo chain)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator async enumerable terminal
    }
}
