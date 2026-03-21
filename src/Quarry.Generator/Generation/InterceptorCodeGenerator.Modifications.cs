using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Projection;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    // ModificationWhere, Set, UpdateSet, UpdateSetAction, UpdateSetPoco methods moved to CodeGen.ClauseBodyEmitter

    /// <summary>
    /// Generates an InsertBuilder ExecuteNonQueryAsync() interceptor.
    /// </summary>
    private static void GenerateInsertExecuteNonQueryInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo;

        sb.AppendLine($"    public static Task<int> {methodName}(");
        sb.AppendLine($"        this IInsertBuilder<{entityType}> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default)");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            // Carrier-optimized path
            if (carrier != null && prebuiltChain != null)
            {
                EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                    "ExecuteCarrierNonQueryWithCommandAsync");
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<InsertBuilder<{entityType}>>(builder);");

            EmitInsertColumnSetup(sb, insertInfo);

            // Extract values from each entity
            sb.AppendLine($"        foreach (var entity in __b.Entities)");
            sb.AppendLine($"        {{");
            EmitInsertEntityBindings(sb, insertInfo, "entity", "__b", "            ");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            sb.AppendLine($"        return Quarry.Internal.ModificationExecutor.ExecuteInsertNonQueryAsync(__b.State, __b.Entities, cancellationToken);");
        }
        else
        {
            // Non-translatable — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates an InsertBuilder ExecuteScalarAsync() interceptor for identity return.
    /// </summary>
    private static void GenerateInsertExecuteScalarInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo;

        // ExecuteScalarAsync<TKey> is a generic method on generic class IInsertBuilder<T>.
        // Interceptors must match the combined arity: <T, TKey> (CS9177).
        // T is constrained to class to match IInsertBuilder<T> where T : class.
        sb.AppendLine($"    public static Task<TKey> {methodName}<T, TKey>(");
        sb.AppendLine($"        this IInsertBuilder<T> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default) where T : class");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            // Carrier-optimized path
            if (carrier != null && prebuiltChain != null)
            {
                EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                    "ExecuteCarrierScalarWithCommandAsync<TKey>", isScalar: true);
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<InsertBuilder<T>>(builder);");

            EmitInsertColumnSetup(sb, insertInfo);

            // Set identity column if present
            if (!string.IsNullOrEmpty(insertInfo.IdentityColumnName))
            {
                sb.AppendLine($"        __b.SetIdentityColumn(@\"{EscapeStringLiteral(insertInfo.IdentityColumnName!)}\");");
                sb.AppendLine();
            }

            // Validate single entity insert
            sb.AppendLine($"        if (__b.Entities.Count != 1)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            throw new InvalidOperationException(");
            sb.AppendLine($"                \"ExecuteScalarAsync can only be used for single entity inserts. \" +");
            sb.AppendLine($"                \"For batch inserts, use ExecuteNonQueryAsync() instead.\");");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            // Cast entity to concrete type for property access.
            // At the call site T is always the concrete entity type (e.g. User),
            // so this cast is safe.
            sb.AppendLine($"        var entity = Unsafe.As<{entityType}>(__b.Entities[0]);");
            EmitInsertEntityBindings(sb, insertInfo, "entity", "__b", "        ");
            sb.AppendLine();

            sb.AppendLine($"        return Quarry.Internal.ModificationExecutor.ExecuteInsertScalarAsync<T, TKey>(__b.State, __b.Entities[0], cancellationToken);");
        }
        else
        {
            // Non-translatable — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    #region Modification Helpers

    /// <summary>
    /// Gets the value expression for an entity column property, handling FK navigation and type mapping.
    /// Used by Insert, Update POCO, and other entity property extraction code.
    /// </summary>
    internal static string GetColumnValueExpression(string entityVar, string propertyName, bool isForeignKey, string? customTypeMappingClass)
    {
        var valueExpr = isForeignKey
            ? $"{entityVar}.{propertyName}.Id"
            : $"{entityVar}.{propertyName}";
        if (customTypeMappingClass != null)
            valueExpr = $"{GetMappingFieldName(customTypeMappingClass)}.ToDb({valueExpr})";
        return valueExpr;
    }

    /// <summary>
    /// Emits the column setup code shared by all insert interceptors.
    /// </summary>
    private static void EmitInsertColumnSetup(StringBuilder sb, InsertInfo insertInfo)
    {
        var columnNames = string.Join(", ", insertInfo.Columns.Select(c => $"@\"{EscapeStringLiteral(c.QuotedColumnName)}\""));
        sb.AppendLine($"        __b.SetColumns(new[] {{ {columnNames} }});");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits entity property extraction and parameter binding for insert operations.
    /// </summary>
    private static void EmitInsertEntityBindings(StringBuilder sb, InsertInfo insertInfo, string entityVar, string builderVar, string indent)
    {
        sb.AppendLine($"{indent}var paramIndices = new List<int>({insertInfo.Columns.Count});");

        foreach (var column in insertInfo.Columns)
        {
            var valueExpr = GetColumnValueExpression(entityVar, column.PropertyName, column.IsForeignKey, column.CustomTypeMappingClass);
            var sensitiveArg = column.IsSensitive ? ", isSensitive: true" : "";
            sb.AppendLine($"{indent}paramIndices.Add({builderVar}.AddParameter({valueExpr}{sensitiveArg}));");
        }

        sb.AppendLine($"{indent}{builderVar}.AddRow(paramIndices);");
    }

    #endregion
}
