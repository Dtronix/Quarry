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

    public static readonly DiagnosticDescriptor DapperQueryWithWarnings = new(
        id: "QRM002",
        title: "Dapper call converted with warnings",
        messageFormat: "This Dapper call was converted with {0} warning(s): {1}",
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

    // EF Core converter diagnostics (QRM01x)

    public static readonly DiagnosticDescriptor EfCoreQueryDetected = new(
        id: "QRM011",
        title: "EF Core query can be converted to Quarry",
        messageFormat: "This EF Core '{0}' query can be converted to a Quarry chain API call",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EfCoreQueryWithWarnings = new(
        id: "QRM012",
        title: "EF Core query converted with warnings",
        messageFormat: "This EF Core query was converted with {0} warning(s): {1}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EfCoreQueryNotConvertible = new(
        id: "QRM013",
        title: "EF Core query cannot be converted",
        messageFormat: "This EF Core query cannot be converted: {0}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // ADO.NET converter diagnostics (QRM02x)

    public static readonly DiagnosticDescriptor AdoNetQueryDetected = new(
        id: "QRM021",
        title: "ADO.NET query can be converted to Quarry",
        messageFormat: "This ADO.NET '{0}' call can be converted to a Quarry chain API call",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AdoNetQueryWithWarnings = new(
        id: "QRM022",
        title: "ADO.NET query converted with warnings",
        messageFormat: "This ADO.NET call was converted with {0} warning(s): {1}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AdoNetQueryNotConvertible = new(
        id: "QRM023",
        title: "ADO.NET query cannot be converted",
        messageFormat: "This ADO.NET call cannot be converted: {0}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // SqlKata converter diagnostics (QRM03x)

    public static readonly DiagnosticDescriptor SqlKataQueryDetected = new(
        id: "QRM031",
        title: "SqlKata query can be converted to Quarry",
        messageFormat: "This SqlKata '{0}' query can be converted to a Quarry chain API call",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SqlKataQueryWithWarnings = new(
        id: "QRM032",
        title: "SqlKata query converted with warnings",
        messageFormat: "This SqlKata query was converted with {0} warning(s): {1}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SqlKataQueryNotConvertible = new(
        id: "QRM033",
        title: "SqlKata query cannot be converted",
        messageFormat: "This SqlKata query cannot be converted: {0}",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
