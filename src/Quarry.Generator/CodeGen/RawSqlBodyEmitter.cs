using System.Text;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Utilities;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor methods for RawSql sites (RawSqlAsync, RawSqlScalarAsync).
/// These bypass the query builder chain — they take raw SQL + parameters directly.
/// </summary>
internal static class RawSqlBodyEmitter
{
    /// <summary>
    /// Emits a file struct implementing IRowReader&lt;T&gt; at namespace scope.
    /// The struct resolves column ordinals once and caches them for per-row reads.
    /// </summary>
    public static void EmitRowReaderStruct(StringBuilder sb, RawSqlTypeInfo rawSqlInfo, string structName)
    {
        var resultType = rawSqlInfo.ResultTypeName;
        var props = rawSqlInfo.Properties;

        sb.AppendLine($"file struct {structName} : IRowReader<{resultType}>");
        sb.AppendLine("{");

        // Ordinal fields
        for (int i = 0; i < props.Count; i++)
        {
            sb.AppendLine($"    int _ord{i};");
        }

        sb.AppendLine();

        // Resolve method — discover ordinals once
        sb.AppendLine($"    public void Resolve(DbDataReader r)");
        sb.AppendLine($"    {{");
        for (int i = 0; i < props.Count; i++)
        {
            sb.AppendLine($"        _ord{i} = -1;");
        }
        sb.AppendLine($"        for (var i = 0; i < r.FieldCount; i++)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            switch (r.GetName(i).ToLowerInvariant())");
        sb.AppendLine($"            {{");
        for (int i = 0; i < props.Count; i++)
        {
            sb.AppendLine($"                case \"{props[i].PropertyName.ToLowerInvariant()}\": _ord{i} = i; break;");
        }
        sb.AppendLine($"            }}");
        sb.AppendLine($"        }}");
        sb.AppendLine($"    }}");

        sb.AppendLine();

        // Read method — per-row materialization using cached ordinals
        sb.AppendLine($"    public {resultType} Read(DbDataReader r)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var item = new {resultType}();");
        for (int i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            var assignment = GeneratePropertyAssignment(prop, $"_ord{i}");
            if (prop.IsNullable)
            {
                sb.AppendLine($"        if (_ord{i} >= 0 && !r.IsDBNull(_ord{i})) item.{prop.PropertyName} = {assignment};");
            }
            else
            {
                sb.AppendLine($"        if (_ord{i} >= 0) item.{prop.PropertyName} = {assignment};");
            }
        }
        sb.AppendLine($"        return item;");
        sb.AppendLine($"    }}");

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits a RawSqlAsync&lt;T&gt; interceptor with a typed reader delegate or struct reader.
    /// </summary>
    public static void EmitRawSqlAsync(StringBuilder sb, TranslatedCallSite site, string methodName, string? structName = null)
    {
        var rawSqlInfo = site.RawSqlTypeInfo;
        if (rawSqlInfo == null)
            return;

        var resultType = rawSqlInfo.ResultTypeName;

        if (rawSqlInfo.HasCancellationToken)
        {
            sb.AppendLine($"    public static IAsyncEnumerable<{resultType}> {methodName}(");
            sb.AppendLine($"        this QuarryContext self,");
            sb.AppendLine($"        string sql,");
            sb.AppendLine($"        CancellationToken cancellationToken,");
            sb.AppendLine($"        params object?[] parameters)");
        }
        else
        {
            sb.AppendLine($"    public static IAsyncEnumerable<{resultType}> {methodName}(");
            sb.AppendLine($"        this QuarryContext self,");
            sb.AppendLine($"        string sql,");
            sb.AppendLine($"        params object?[] parameters)");
        }

        sb.AppendLine($"    {{");

        var ctArg = rawSqlInfo.HasCancellationToken ? "cancellationToken" : "CancellationToken.None";

        if (rawSqlInfo.TypeKind == RawSqlTypeKind.Scalar)
        {
            var readerMethod = rawSqlInfo.ScalarReaderMethod ?? "GetValue";
            var scalarRead = TypeClassification.NeedsSignCast(resultType)
                ? $"({resultType})r.{readerMethod}(0)"
                : $"r.{readerMethod}(0)";
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static r => {scalarRead},");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }
        else if (structName != null && rawSqlInfo.Properties.Count > 0)
        {
            // Struct-based reader path
            sb.AppendLine($"        return self.RawSqlAsyncWithReader<{resultType}, {structName}>(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }
        else if (rawSqlInfo.Properties.Count > 0)
        {
            // Fallback: lambda-based reader (no struct name provided)
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static r =>");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var item = new {resultType}();");
            sb.AppendLine($"                for (var i = 0; i < r.FieldCount; i++)");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    if (r.IsDBNull(i)) continue;");
            sb.AppendLine($"                    switch (r.GetName(i).ToLowerInvariant())");
            sb.AppendLine($"                    {{");

            foreach (var prop in rawSqlInfo.Properties)
            {
                var assignment = GeneratePropertyAssignment(prop, "i");
                sb.AppendLine($"                        case \"{prop.PropertyName.ToLowerInvariant()}\": item.{prop.PropertyName} = {assignment}; break;");
            }

            sb.AppendLine($"                    }}");
            sb.AppendLine($"                }}");
            sb.AppendLine($"                return item;");
            sb.AppendLine($"            }},");
            sb.AppendLine($"            {ctArg},");
            sb.AppendLine($"            parameters);");
        }
        else
        {
            sb.AppendLine($"        return self.RawSqlAsyncWithReader(");
            sb.AppendLine($"            sql,");
            sb.AppendLine($"            static _ => new {resultType}(),");
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

    private static string GeneratePropertyAssignment(RawSqlPropertyInfo prop, string ordinalExpr)
    {
        if (prop.CustomTypeMappingClass != null)
        {
            var dbReaderMethod = prop.DbReaderMethodName ?? "GetValue";
            return $"new {prop.CustomTypeMappingClass}().FromDb(r.{dbReaderMethod}({ordinalExpr}))";
        }

        if (prop.IsForeignKey && prop.ReferencedEntityName != null)
        {
            return $"new EntityRef<{prop.ReferencedEntityName}, {prop.ClrType}>(r.{prop.ReaderMethodName}({ordinalExpr}))";
        }

        if (prop.IsEnum)
        {
            return $"({prop.FullClrType})r.{prop.ReaderMethodName}({ordinalExpr})";
        }

        if (TypeClassification.NeedsSignCast(prop.ClrType))
        {
            return $"({prop.ClrType})r.{prop.ReaderMethodName}({ordinalExpr})";
        }

        if (prop.ReaderMethodName == "GetValue")
        {
            return $"({prop.FullClrType})r.GetValue({ordinalExpr})";
        }

        return $"r.{prop.ReaderMethodName}({ordinalExpr})";
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
            "uint" or "UInt32" => isNullable ? "(uint?)Convert.ToUInt32(v)" : "(uint)Convert.ToInt64(v)",
            "ushort" or "UInt16" => isNullable ? "(ushort?)Convert.ToUInt16(v)" : "(ushort)Convert.ToInt64(v)",
            "ulong" or "UInt64" => isNullable ? "(ulong?)Convert.ToUInt64(v)" : "(ulong)Convert.ToInt64(v)",
            "string" or "String" => "v?.ToString() ?? string.Empty",
            "Guid" => isNullable ? "(Guid?)(Guid)v" : "(Guid)v",
            "DateTime" => isNullable ? "(DateTime?)Convert.ToDateTime(v)" : "Convert.ToDateTime(v)",
            _ => $"({resultType})v"
        };
    }

}
