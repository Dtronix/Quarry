namespace Quarry;

/// <summary>
/// Exception thrown when a type mapping error occurs.
/// </summary>
public class QuarryMappingException : QuarryException
{
    /// <summary>
    /// Gets the source type that failed to map, if available.
    /// </summary>
    public Type? SourceType { get; }

    /// <summary>
    /// Gets the target type that failed to map, if available.
    /// </summary>
    public Type? TargetType { get; }

    /// <summary>
    /// Creates a new QuarryMappingException.
    /// </summary>
    public QuarryMappingException()
    {
    }

    /// <summary>
    /// Creates a new QuarryMappingException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QuarryMappingException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new QuarryMappingException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuarryMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new QuarryMappingException with type information.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="sourceType">The source type that failed to map.</param>
    /// <param name="targetType">The target type that failed to map.</param>
    public QuarryMappingException(string message, Type? sourceType, Type? targetType)
        : base(message)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    /// <summary>
    /// Creates a new QuarryMappingException with type information and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="sourceType">The source type that failed to map.</param>
    /// <param name="targetType">The target type that failed to map.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuarryMappingException(string message, Type? sourceType, Type? targetType, Exception innerException)
        : base(message, innerException)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }
}
