# <img src="../../docs/images/logo-128.png" height="48"> Quarry

Type-safe SQL builder for .NET 10. Source generators + C# 12 interceptors emit all SQL at compile time. AOT compatible. Structured logging via Logsmith.

---

# Quarry.Migration

Core library for migrating from other data access libraries (Dapper, ADO.NET, etc.) to Quarry. Provides SQL parsing, schema resolution, Dapper call site detection, and SQL-to-Quarry chain translation.

## Features

- **SchemaResolver** - Discovers Quarry entity schemas via Roslyn and maps SQL table/column names to entity types and properties
- **DapperDetector** - Finds Dapper call sites (QueryAsync, ExecuteAsync, etc.) and extracts SQL strings, parameters, and result types
- **ChainEmitter** - Translates parsed SQL AST to Quarry chain API C# source code
- **DapperConverter** - Public facade for programmatic access to the full conversion pipeline
