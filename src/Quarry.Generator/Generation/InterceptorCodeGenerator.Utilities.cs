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
    /// Extracts the namespace from a syntax node's containing file.
    /// </summary>
    private static string? GetNamespaceFromFilePath(Microsoft.CodeAnalysis.SyntaxNode? syntaxNode)
    {
        if (syntaxNode?.SyntaxTree == null)
            return null;

        var root = syntaxNode.SyntaxTree.GetRoot();

        // Look for file-scoped namespace
        var fileScopedNamespace = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScopedNamespace != null)
        {
            return fileScopedNamespace.Name.ToString();
        }

        // Look for block-scoped namespace containing the invocation
        var namespaceDecl = syntaxNode.Ancestors()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (namespaceDecl != null)
        {
            return namespaceDecl.Name.ToString();
        }

        // Fallback: look for any namespace in the file
        var anyNamespace = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (anyNamespace != null)
        {
            return anyNamespace.Name.ToString();
        }

        return null;
    }

    /// <summary>
    /// Converts an absolute file path to a shorter relative-style path for display in comments.
    /// Returns a path starting from a recognized project marker, or the last few path segments.
    /// </summary>
    private static string GetRelativePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return filePath;

        // Common project root markers to look for
        string[] markers = ["src", "test", "tests", "lib", "samples", "examples", "benchmark", "benchmarks"];

        foreach (var marker in markers)
        {
            // Try with backslash (Windows)
            var index = filePath.IndexOf(marker + "\\", StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                return filePath.Substring(index);

            // Try with forward slash (Unix/normalized)
            index = filePath.IndexOf(marker + "/", StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                return filePath.Substring(index);
        }

        // No marker found - return last 3 path segments for context
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length <= 3)
            return filePath;

        var lastSegments = new string[3];
        Array.Copy(segments, segments.Length - 3, lastSegments, 0, 3);
        return string.Join(Path.DirectorySeparatorChar.ToString(), lastSegments);
    }

    /// <summary>
    /// Extracts the namespace from a fully qualified type name.
    /// </summary>
    private static string? GetNamespaceFromTypeName(string typeName)
    {
        // Handle global:: prefix
        if (typeName.StartsWith("global::"))
        {
            typeName = typeName.Substring(8);
        }

        // Find the last dot that's not within angle brackets (generic types)
        int depth = 0;
        int lastDotIndex = -1;

        for (int i = typeName.Length - 1; i >= 0; i--)
        {
            char c = typeName[i];
            if (c == '>')
                depth++;
            else if (c == '<')
                depth--;
            else if (c == '.' && depth == 0)
            {
                lastDotIndex = i;
                break;
            }
        }

        if (lastDotIndex <= 0)
            return null;

        return typeName.Substring(0, lastDotIndex);
    }

    /// <summary>
    /// Checks if a clause interceptor should be skipped because the clause could not be translated.
    /// When skipped, the original runtime method runs instead of a silent no-op fallback.
    /// </summary>
    private static bool ShouldSkipNonTranslatableClause(UsageSiteInfo site)
    {
        switch (site.Kind)
        {
            case InterceptorKind.Where:
            case InterceptorKind.DeleteWhere:
            case InterceptorKind.UpdateWhere:
                return site.ClauseInfo == null || !site.ClauseInfo.IsSuccess;

            case InterceptorKind.OrderBy:
            case InterceptorKind.ThenBy:
                if (site.ClauseInfo is OrderByClauseInfo orderByInfo && orderByInfo.IsSuccess)
                    return false;
                return site.ClauseInfo == null || !site.ClauseInfo.IsSuccess;

            case InterceptorKind.GroupBy:
            case InterceptorKind.Having:
                return site.ClauseInfo == null || !site.ClauseInfo.IsSuccess;

            case InterceptorKind.Set:
            case InterceptorKind.UpdateSet:
                if (site.ClauseInfo is SetClauseInfo setInfo && setInfo.IsSuccess)
                    return false;
                return site.ClauseInfo == null || !site.ClauseInfo.IsSuccess;

            case InterceptorKind.UpdateSetPoco:
                return site.UpdateInfo == null || site.UpdateInfo.Columns.Count == 0;

            case InterceptorKind.InsertExecuteNonQuery:
            case InterceptorKind.InsertExecuteScalar:
                return site.InsertInfo == null || site.InsertInfo.Columns.Count == 0;

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if a Select interceptor should be skipped entirely (not emitted).
    /// </summary>
    private static bool ShouldSkipSelectInterceptor(UsageSiteInfo site)
    {
        var projection = site.ProjectionInfo;
        if (projection == null)
            return false; // Let the fallback path handle it

        // Skip anonymous types - they should have been rejected by QRY014
        if (projection.Kind == ProjectionKind.Anonymous)
            return true;

        // Skip unknown or failed projections
        if (projection.Kind == ProjectionKind.Unknown)
            return true;

        // Skip non-optimal paths
        if (!projection.IsOptimalPath)
            return true;

        // Skip tuple projections with invalid result types
        // This can happen when the entity types are generated by the same source generator
        // and the semantic model can't fully resolve the tuple element types
        if (projection.Kind == ProjectionKind.Tuple)
        {
            var resultType = projection.ResultTypeName;
            if (string.IsNullOrWhiteSpace(resultType) ||
                !resultType.StartsWith("(") ||
                resultType.Contains("( ") ||
                resultType.Contains(", )") ||
                ContainsUnresolvedTupleElement(resultType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a tuple type string contains unresolved elements (missing types).
    /// </summary>
    private static bool ContainsUnresolvedTupleElement(string tupleType)
    {
        // Remove parentheses
        var inner = tupleType.Substring(1, tupleType.Length - 2);
        var parts = inner.Split(',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return true;

            // Check if this looks like just a name without a type
            // Valid: "int UserId", "string? Name"
            // Invalid: "UserId", "Name"
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0)
            {
                // Single word - must be a type like "int" or "DateTime"
                // If it starts with uppercase and isn't a known type, it's likely just a name
                if (trimmed.Length > 0 && char.IsUpper(trimmed[0]) && !IsKnownUppercaseTypeName(trimmed))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a name is a known .NET type that starts with uppercase.
    /// </summary>
    private static bool IsKnownUppercaseTypeName(string name)
    {
        var baseName = name.TrimEnd('?');
        return baseName switch
        {
            "Boolean" or "Byte" or "SByte" or "Int16" or "UInt16" or
            "Int32" or "UInt32" or "Int64" or "UInt64" or
            "Single" or "Double" or "Decimal" or "String" or "Char" or
            "DateTime" or "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" or
            "Guid" or "Object" => true,
            _ => false
        };
    }

    /// <summary>
    /// Sanitizes a tuple type name by stripping element names and keeping only types.
    /// For example, "(string Status, int)" becomes "(string, int)".
    /// This prevents element names from being emitted as types in interceptor signatures
    /// when the semantic model produces unresolved element types.
    /// </summary>
    private static string SanitizeTupleResultType(string tupleType)
    {
        // Delegate to the full implementation
        return SanitizeTupleResultTypeCore(tupleType);
    }

    /// <summary>
    /// Resolves the best available result type for execution interceptors.
    /// Prefers the semantic model's type (most qualified) but falls back to
    /// enriched ProjectionInfo type for tuples, single-column, and empty results.
    /// </summary>
    /// <summary>
    /// Public accessor for carrier eligibility checks in QuarryGenerator.
    /// </summary>
    internal static string? ResolveExecutionResultTypePublic(
        string? siteResultType, string? chainResultType, ProjectionInfo? projectionInfo)
        => ResolveExecutionResultType(siteResultType, chainResultType, projectionInfo);

    private static string? ResolveExecutionResultType(
        string? siteResultType,
        string? chainResultType,
        ProjectionInfo? projectionInfo)
    {
        // For non-tuple, non-empty results, prefer the semantic model's type (most qualified)
        if (!string.IsNullOrEmpty(siteResultType)
            && !siteResultType!.Contains("(") && !siteResultType.Contains("ValueTuple"))
            return siteResultType;
        if (!string.IsNullOrEmpty(chainResultType)
            && !chainResultType!.Contains("(") && !chainResultType.Contains("ValueTuple"))
            return chainResultType;

        // For tuples, empty results, or broken types: use enriched projection type
        var projResult = projectionInfo?.ResultTypeName;
        if (!string.IsNullOrEmpty(projResult))
            return projResult;

        // Last resort: return whatever we have (may be empty/broken)
        return string.IsNullOrEmpty(siteResultType) ? chainResultType : siteResultType;
    }

    /// <summary>
    /// Strips tuple element names from a C# tuple type, keeping only the type parts.
    /// For example, "(string Status, int)" becomes "(string, int)".
    /// </summary>
    private static string SanitizeTupleResultTypeCore(string tupleType)
    {
        if (!tupleType.StartsWith("(") || !tupleType.EndsWith(")"))
            return tupleType;

        var inner = tupleType.Substring(1, tupleType.Length - 2);
        var parts = inner.Split(',');
        var sanitized = new string[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            var trimmed = parts[i].Trim();
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var typePart = trimmed.Substring(0, spaceIdx);
                var namePart = trimmed.Substring(spaceIdx + 1);

                // If the "type" part is empty/whitespace or looks like a name without a type,
                // then the whole token is just a name — use "object" as fallback
                if (string.IsNullOrWhiteSpace(typePart))
                {
                    sanitized[i] = "object";
                }
                else
                {
                    // Strip the element name, keep only the type
                    sanitized[i] = typePart;
                }
            }
            else
            {
                // Single word — could be a type ("int") or a misplaced name ("Status")
                if (trimmed.Length > 0 && char.IsUpper(trimmed[0]) &&
                    !trimmed.Contains('.') && !IsKnownUppercaseTypeName(trimmed))
                {
                    // Likely a name, not a type — use "object"
                    sanitized[i] = "object";
                }
                else
                {
                    sanitized[i] = trimmed;
                }
            }
        }

        return $"({string.Join(", ", sanitized)})";
    }

    /// <summary>
    /// Generates inline expression tree navigation code for a path segment.
    /// Each segment maps to a specific Unsafe.As cast and property access.
    /// </summary>
    private static string GenerateSegmentNavigation(string segment, string prevExpr)
    {
        // Handle Arguments[N] format
        if (segment.StartsWith("Arguments["))
        {
            return $"Unsafe.As<MethodCallExpression>({prevExpr}).Arguments[{segment.Substring("Arguments[".Length, 1)}]";
        }

        return segment switch
        {
            "Body" => $"{prevExpr}.Body",
            "Left" => $"Unsafe.As<BinaryExpression>({prevExpr}).Left",
            "Right" => $"Unsafe.As<BinaryExpression>({prevExpr}).Right",
            "Operand" => $"Unsafe.As<UnaryExpression>({prevExpr}).Operand",
            "Object" => $"Unsafe.As<MethodCallExpression>({prevExpr}).Object!",
            "Expression" => $"Unsafe.As<MemberExpression>({prevExpr}).Expression!",
            "Test" => $"Unsafe.As<ConditionalExpression>({prevExpr}).Test",
            "IfTrue" => $"Unsafe.As<ConditionalExpression>({prevExpr}).IfTrue",
            "IfFalse" => $"Unsafe.As<ConditionalExpression>({prevExpr}).IfFalse",
            _ => throw new InvalidOperationException($"Unknown path segment: {segment}")
        };
    }

    /// <summary>
    /// Generates inline navigation code that arrives at the MemberExpression for a given path.
    /// Returns the variable name holding the final MemberExpression.
    /// </summary>
    /// <remarks>
    /// For enum captured variables, the C# compiler inserts a UnaryExpression(Convert) node
    /// wrapping the MemberExpression. The generated code uses a pattern match to unwrap it.
    /// </remarks>
    private static string GenerateInlineNavigation(StringBuilder sb, string path, int paramIndex)
    {
        var segments = path.Split('.');
        // "Body" is the first segment — navigate from expr
        // The remaining segments (except the last, which is the MemberExpression itself)
        // are intermediate casts. We build a single chained expression.

        // Build the chain from the inside out
        var currentExpr = "expr";
        foreach (var segment in segments)
        {
            currentExpr = GenerateSegmentNavigation(segment, currentExpr);
        }

        // The final node is a MemberExpression, but for enum captured variables
        // the compiler inserts a UnaryExpression(Convert) wrapper that must be stripped.
        var nodeVar = $"_n{paramIndex}";
        var memberVar = $"_m{paramIndex}";
        sb.AppendLine($"        var {nodeVar} = {currentExpr};");
        sb.AppendLine($"        var {memberVar} = {nodeVar} is UnaryExpression _u{paramIndex} ? Unsafe.As<MemberExpression>(_u{paramIndex}.Operand) : Unsafe.As<MemberExpression>({nodeVar});");
        return memberVar;
    }

    /// <summary>
    /// Generates cached extractor code using static fields for all captured parameters.
    /// Emits inline Unsafe.As navigation with cached FieldInfo.
    /// </summary>
    private static void GenerateCachedExtraction(StringBuilder sb, List<CachedExtractorField> fields)
    {
        foreach (var field in fields)
        {
            sb.AppendLine($"        // Inline extraction: {field.ExpressionPath}");
            var memberVar = GenerateInlineNavigation(sb, field.ExpressionPath, field.ParameterIndex);
            sb.AppendLine($"        {field.FieldName} ??= Unsafe.As<FieldInfo>({memberVar}.Member);");
            sb.AppendLine($"        var p{field.ParameterIndex} = {field.FieldName}.GetValue(Unsafe.As<ConstantExpression>({memberVar}.Expression!).Value);");
        }
    }

    private static void GeneratePlaceholderInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);

        sb.AppendLine($"    // TODO: Implement interceptor for {site.MethodName}");
        sb.AppendLine($"    // file static ... {methodName}(...) {{ }}");
    }

    /// <summary>
    /// Gets the DbDataReader method call for a specific column type and ordinal.
    /// </summary>
    private static string GetReaderCall(string clrType, int ordinal, bool isNullable)
    {
        // Handle nullable types - need null check
        if (isNullable)
        {
            var baseType = GetNonNullableType(clrType);
            var baseReaderMethod = GetReaderMethod(baseType);
            return $"r.IsDBNull({ordinal}) ? default({clrType}) : r.{baseReaderMethod}({ordinal})";
        }

        var readerMethod = GetReaderMethod(clrType);
        return $"r.{readerMethod}({ordinal})";
    }

    /// <summary>
    /// Gets the appropriate DbDataReader method for a CLR type.
    /// </summary>
    private static string GetReaderMethod(string clrType)
    {
        return clrType switch
        {
            "bool" or "Boolean" or "System.Boolean" => "GetBoolean",
            "byte" or "Byte" or "System.Byte" => "GetByte",
            "sbyte" or "SByte" or "System.SByte" => "GetByte",
            "short" or "Int16" or "System.Int16" => "GetInt16",
            "ushort" or "UInt16" or "System.UInt16" => "GetInt16",
            "int" or "Int32" or "System.Int32" => "GetInt32",
            "uint" or "UInt32" or "System.UInt32" => "GetInt32",
            "long" or "Int64" or "System.Int64" => "GetInt64",
            "ulong" or "UInt64" or "System.UInt64" => "GetInt64",
            "float" or "Single" or "System.Single" => "GetFloat",
            "double" or "Double" or "System.Double" => "GetDouble",
            "decimal" or "Decimal" or "System.Decimal" => "GetDecimal",
            "string" or "String" or "System.String" => "GetString",
            "char" or "Char" or "System.Char" => "GetChar",
            "Guid" or "System.Guid" => "GetGuid",
            "DateTime" or "System.DateTime" => "GetDateTime",
            "DateTimeOffset" or "System.DateTimeOffset" => "GetValue",
            "TimeSpan" or "System.TimeSpan" => "GetValue",
            "DateOnly" or "System.DateOnly" => "GetValue",
            "TimeOnly" or "System.TimeOnly" => "GetValue",
            _ => "GetValue"
        };
    }

    /// <summary>
    /// Gets the non-nullable version of a nullable type.
    /// </summary>
    private static string GetNonNullableType(string clrType)
    {
        if (clrType.EndsWith("?"))
        {
            return clrType.Substring(0, clrType.Length - 1);
        }

        if (clrType.StartsWith("Nullable<") && clrType.EndsWith(">"))
        {
            return clrType.Substring(9, clrType.Length - 10);
        }

        if (clrType.StartsWith("System.Nullable<") && clrType.EndsWith(">"))
        {
            return clrType.Substring(16, clrType.Length - 17);
        }

        return clrType;
    }

    /// <summary>
    /// Escapes a string for use in a verbatim string literal.
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Returns a <c>.WithClauseBit(N)</c> suffix for conditional clause interceptors,
    /// or an empty string for unconditional clauses.
    /// </summary>
    private static string ClauseBitSuffix(int? bitIndex)
        => bitIndex.HasValue ? $".WithClauseBit({bitIndex.Value})" : "";

    /// <summary>
    /// Returns the concrete type name corresponding to a concrete or interface builder type name.
    /// </summary>
    private static string ToConcreteTypeName(string typeName)
    {
        if (typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1]))
            return typeName.Substring(1);
        return typeName;
    }

    /// <summary>
    /// Maps IEntityAccessor to the appropriate return type for interceptors.
    /// IEntityAccessor methods return IQueryBuilder types, not IEntityAccessor types.
    /// </summary>
    private static string ToReturnTypeName(string thisType)
        => thisType is "IEntityAccessor" or "EntityAccessor" ? "IQueryBuilder" : thisType;

    /// <summary>
    /// Returns true if the builder type name is an entity accessor type.
    /// When true, the builder is a boxed EntityAccessor struct and must be
    /// converted to a QueryBuilder via CreateQueryBuilder() before Unsafe.As casts.
    /// </summary>
    private static bool IsEntityAccessorType(string builderTypeName)
        => builderTypeName is "IEntityAccessor" or "EntityAccessor";

    /// <summary>
    /// Returns the expression to convert a builder to a QueryBuilder when the receiver is IEntityAccessor.
    /// Unboxes the EntityAccessor struct and calls CreateQueryBuilder() to get a real QueryBuilder.
    /// </summary>
    private static string EntityAccessorToQueryBuilder(string entityType)
        => $"((EntityAccessor<{entityType}>)(object)builder).CreateQueryBuilder()";

    /// <summary>
    /// Gets a short type name from a fully qualified type name.
    /// </summary>
    internal static string GetShortTypeName(string fullTypeName)
    {
        // Remove global:: prefix
        if (fullTypeName.StartsWith("global::"))
        {
            fullTypeName = fullTypeName.Substring(8);
        }

        // For now, return as-is. In later phases, we may want to shorten common types.
        return fullTypeName;
    }
}
