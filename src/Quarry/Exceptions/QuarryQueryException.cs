namespace Quarry;

/// <summary>
/// Exception thrown when a query execution error occurs.
/// </summary>
public class QuarryQueryException : QuarryException
{
    /// <summary>
    /// Gets the SQL that caused the error, if available.
    /// </summary>
    public string? Sql { get; }

    /// <summary>
    /// Creates a new QuarryQueryException.
    /// </summary>
    public QuarryQueryException()
    {
    }

    /// <summary>
    /// Creates a new QuarryQueryException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QuarryQueryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new QuarryQueryException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuarryQueryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new QuarryQueryException with the specified message, SQL, and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="sql">The SQL that caused the error.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuarryQueryException(string message, string? sql, Exception innerException)
        : base(message, innerException)
    {
        Sql = sql;
    }
}
