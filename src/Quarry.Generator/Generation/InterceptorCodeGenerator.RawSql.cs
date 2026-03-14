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
    /// <summary>
    /// Generates a placeholder interceptor for unknown kinds.
    /// </summary>
    /// <summary>
    /// Generates a RawSqlAsync&lt;T&gt; interceptor with a typed reader delegate.
    /// </summary>
    private static void GenerateRawSqlAsyncInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName)
    {
        var rawSqlInfo = site.RawSqlTypeInfo;
        if (rawSqlInfo == null)
            return;

        var resultType = rawSqlInfo.ResultTypeName;

        // Generate method signature matching the called overload
        if (rawSqlInfo.HasCancellationToken)
        {
            sb.AppendLine($"    public static Task<List<{resultType}>> {methodName}(");
            sb.AppendLine($"        this QuarryContext self,");
            sb.AppendLine($"        string sql,");
            sb.AppendLine($"        CancellationToken cancellationToken,");
            sb.AppendLine($"        params object?[] parameters)");
        }
        else
        {
            sb.AppendLine($"    public static Task<List<{resultType}>> {methodName}(");
            sb.AppendLine($"        this QuarryContext self,");
            sb.AppendLine($"        string sql,");
            sb.AppendLine($"        params object?[] parameters)");
        }

        sb.AppendLine($"    {{");

        var ctArg = rawSqlInfo.HasCancellationToken ? "cancellationToken" : "CancellationToken.None";

        if (rawSqlInfo.TypeKind == RawSqlTypeKind.Scalar)
        {
            // Scalar T: read reader.GetXxx(0) for each row
            var readerMethod = rawSqlInfo.ScalarReaderMethod ?? "GetValue";
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static r => r.{readerMethod}(0),");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }
        else
        {
            // Entity/DTO T: generate a switch-based reader
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static r =>");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var item = new {resultType}();");
            sb.AppendLine($"                for (var i = 0; i < r.FieldCount; i++)");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    if (r.IsDBNull(i)) continue;");
            sb.AppendLine($"                    switch (r.GetName(i))");
            sb.AppendLine($"                    {{");

            foreach (var prop in rawSqlInfo.Properties)
            {
                var assignment = GenerateRawSqlPropertyAssignment(prop);
                sb.AppendLine($"                        case \"{prop.PropertyName}\": item.{prop.PropertyName} = {assignment}; break;");
            }

            sb.AppendLine($"                    }}");
            sb.AppendLine($"                }}");
            sb.AppendLine($"                return item;");
            sb.AppendLine($"            }},");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a RawSqlScalarAsync&lt;T&gt; interceptor with typed conversion.
    /// </summary>
    private static void GenerateRawSqlScalarAsyncInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName)
    {
        var rawSqlInfo = site.RawSqlTypeInfo;
        if (rawSqlInfo == null)
            return;

        var resultType = rawSqlInfo.ResultTypeName;

        // Generate method signature matching the called overload
        if (rawSqlInfo.HasCancellationToken)
        {
            sb.AppendLine($"    public static Task<{resultType}> {methodName}(");
            sb.AppendLine($"        this QuarryContext self,");
            sb.AppendLine($"        string sql,");
            sb.AppendLine($"        CancellationToken cancellationToken,");
            sb.AppendLine($"        params object?[] parameters)");
        }
        else
        {
            sb.AppendLine($"    public static Task<{resultType}> {methodName}(");
            sb.AppendLine($"        this QuarryContext self,");
            sb.AppendLine($"        string sql,");
            sb.AppendLine($"        params object?[] parameters)");
        }

        sb.AppendLine($"    {{");

        var ctArg = rawSqlInfo.HasCancellationToken ? "cancellationToken" : "CancellationToken.None";

        // Generate typed converter
        var converterExpr = GenerateScalarConverter(resultType);
        sb.AppendLine($"        return self.RawSqlScalarAsyncWithConverter(");
        sb.AppendLine($"            sql,");
        sb.AppendLine($"            static v => {converterExpr},");
        sb.AppendLine($"            {ctArg},");
        sb.AppendLine($"            parameters);");

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a property assignment expression for a RawSql reader column case.
    /// Handles enums, Ref&lt;&gt; FKs, custom type mappings, and standard types.
    /// </summary>
    private static string GenerateRawSqlPropertyAssignment(RawSqlPropertyInfo prop)
    {
        if (prop.CustomTypeMappingClass != null)
        {
            var dbReaderMethod = prop.DbReaderMethodName ?? "GetValue";
            return $"new {prop.CustomTypeMappingClass}().FromDb(r.{dbReaderMethod}(i))";
        }

        if (prop.IsForeignKey && prop.ReferencedEntityName != null)
        {
            return $"new EntityRef<{prop.ReferencedEntityName}, {prop.ClrType}>(r.{prop.ReaderMethodName}(i))";
        }

        if (prop.IsEnum)
        {
            return $"({prop.FullClrType})r.{prop.ReaderMethodName}(i)";
        }

        return $"r.{prop.ReaderMethodName}(i)";
    }

    /// <summary>
    /// Generates a converter expression for a scalar type from object to T.
    /// Handles nullable, enum, and standard types.
    /// </summary>
    private static string GenerateScalarConverter(string resultType)
    {
        // Strip nullable for base type matching
        var baseType = resultType.TrimEnd('?');
        var isNullable = resultType.EndsWith("?");

        return baseType switch
        {
            "int" or "Int32" => isNullable ? "(int?)Convert.ToInt32(v)" : "(int)Convert.ToInt64(v)",
            "long" or "Int64" => isNullable ? "(long?)Convert.ToInt64(v)" : "Convert.ToInt64(v)",
            "short" or "Int16" => isNullable ? "(short?)Convert.ToInt16(v)" : "(short)Convert.ToInt64(v)",
            "byte" or "Byte" => isNullable ? "(byte?)(byte)Convert.ToInt64(v)" : "(byte)Convert.ToInt64(v)",
            "bool" or "Boolean" => isNullable ? "(bool?)Convert.ToBoolean(v)" : "Convert.ToBoolean(v)",
            "float" or "Single" => isNullable ? "(float?)Convert.ToSingle(v)" : "Convert.ToSingle(v)",
            "double" or "Double" => isNullable ? "(double?)Convert.ToDouble(v)" : "Convert.ToDouble(v)",
            "decimal" or "Decimal" => isNullable ? "(decimal?)Convert.ToDecimal(v)" : "Convert.ToDecimal(v)",
            "string" or "String" => "v?.ToString() ?? string.Empty",
            "Guid" => isNullable ? "(Guid?)(Guid)v" : "(Guid)v",
            "DateTime" => isNullable ? "(DateTime?)Convert.ToDateTime(v)" : "Convert.ToDateTime(v)",
            _ => $"({resultType})v"
        };
    }
}
