namespace Quarry;

/// <summary>
/// Exception thrown when a database connection error occurs.
/// </summary>
public class QuarryConnectionException : QuarryException
{
    /// <summary>
    /// Creates a new QuarryConnectionException.
    /// </summary>
    public QuarryConnectionException()
    {
    }

    /// <summary>
    /// Creates a new QuarryConnectionException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QuarryConnectionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new QuarryConnectionException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuarryConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
