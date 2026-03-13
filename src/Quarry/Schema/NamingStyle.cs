namespace Quarry;

/// <summary>
/// Specifies the naming convention for mapping C# property names to database column names.
/// </summary>
public enum NamingStyle
{
    /// <summary>
    /// Exact match - property names map directly to column names.
    /// Example: UserId → UserId
    /// </summary>
    Exact,

    /// <summary>
    /// Snake case - property names are converted to lowercase with underscores.
    /// Example: UserId → user_id
    /// </summary>
    SnakeCase,

    /// <summary>
    /// Camel case - property names are converted to camelCase.
    /// Example: UserId → userId
    /// </summary>
    CamelCase,

    /// <summary>
    /// Lower case - property names are converted to all lowercase.
    /// Example: UserId → userid
    /// </summary>
    LowerCase
}
