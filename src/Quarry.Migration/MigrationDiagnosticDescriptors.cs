using Microsoft.CodeAnalysis;

namespace Quarry.Migration;

internal static class MigrationDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor DapperQueryDetected = new(
        id: "QRM001",
        title: "Dapper call can be converted to Quarry",
        messageFormat: "This Dapper '{0}' call can be converted to a Quarry chain API call",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DapperQueryWithRawFallback = new(
        id: "QRM002",
        title: "Dapper call converted with Sql.Raw fallback",
        messageFormat: "This Dapper call was converted but uses Sql.Raw for {0} expression(s) that could not be fully translated",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DapperQueryNotConvertible = new(
        id: "QRM003",
        title: "Dapper call cannot be converted",
        messageFormat: "This Dapper call cannot be converted: {0}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
