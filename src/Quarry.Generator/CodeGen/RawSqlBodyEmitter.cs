using System.Text;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor methods for RawSql sites (RawSqlAsync, RawSqlScalarAsync).
/// These bypass the query builder chain — they take raw SQL + parameters directly.
/// </summary>
internal static class RawSqlBodyEmitter
{
    /// <summary>
    /// Emits a RawSqlAsync&lt;T&gt; interceptor with a typed reader delegate.
    /// </summary>
    public static void EmitRawSqlAsync(StringBuilder sb, TranslatedCallSite site, string methodName)
    {
        var rawSqlInfo = site.RawSqlTypeInfo;
        if (rawSqlInfo == null)
            return;

        var resultType = rawSqlInfo.ResultTypeName;

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
            var readerMethod = rawSqlInfo.ScalarReaderMethod ?? "GetValue";
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static r => r.{readerMethod}(0),");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }
        else
        {
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static r =>");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var item = new {resultType}();");

            if (rawSqlInfo.Properties.Count > 0)
            {
                sb.AppendLine($"                for (var i = 0; i < r.FieldCount; i++)");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    if (r.IsDBNull(i)) continue;");
                sb.AppendLine($"                    switch (r.GetName(i))");
                sb.AppendLine($"                    {{");

                foreach (var prop in rawSqlInfo.Properties)
                {
                    var assignment = GeneratePropertyAssignment(prop);
                    sb.AppendLine($"                        case \"{prop.PropertyName}\": item.{prop.PropertyName} = {assignment}; break;");
                }

                sb.AppendLine($"                    }}");
                sb.AppendLine($"                }}");
            }

            sb.AppendLine($"                return item;");
            sb.AppendLine($"            }},");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a RawSqlScalarAsync&lt;T&gt; interceptor with typed conversion.
    /// </summary>
    public static void EmitRawSqlScalarAsync(StringBuilder sb, TranslatedCallSite site, string methodName)
    {
        var rawSqlInfo = site.RawSqlTypeInfo;
        if (rawSqlInfo == null)
            return;

        var resultType = rawSqlInfo.ResultTypeName;

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

        var converterExpr = GenerateScalarConverter(resultType);
        sb.AppendLine($"        return self.RawSqlScalarAsyncWithConverter(");
        sb.AppendLine($"            sql,");
        sb.AppendLine($"            static v => {converterExpr},");
        sb.AppendLine($"            {ctArg},");
        sb.AppendLine($"            parameters);");

        sb.AppendLine($"    }}");
    }

    private static string GeneratePropertyAssignment(RawSqlPropertyInfo prop)
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

    private static string GenerateScalarConverter(string resultType)
    {
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
