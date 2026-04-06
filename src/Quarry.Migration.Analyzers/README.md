# <img src="../../docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Structured logging via Logsmith.

---

# Quarry.Migration.Analyzers

Roslyn analyzer and code fix for migrating Dapper calls to Quarry chain API. Provides IDE lightbulb actions to automatically convert Dapper QueryAsync/ExecuteAsync calls.

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| QRM001 | Info | Dapper call can be converted to Quarry chain API |
| QRM002 | Warning | Dapper call converted with Sql.Raw fallback for some expressions |
| QRM003 | Info | Dapper call cannot be converted (non-literal SQL, unknown table) |
