namespace Quarry.Generators.Utilities;

/// <summary>
/// Shared CLR type classification helpers used by code-generation emitters.
/// </summary>
internal static class TypeClassification
{
    /// <summary>
    /// Returns true if the CLR type needs an explicit cast from its DbDataReader method
    /// due to a sign mismatch (e.g., GetInt32 → uint, GetByte → sbyte).
    /// </summary>
    public static bool NeedsSignCast(string clrType)
        => clrType is "uint" or "UInt32" or "System.UInt32"
            or "ushort" or "UInt16" or "System.UInt16"
            or "ulong" or "UInt64" or "System.UInt64"
            or "sbyte" or "SByte" or "System.SByte";
}
