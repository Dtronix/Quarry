namespace Quarry;

/// <summary>
/// Base exception for all Quarry-related errors.
/// </summary>
public class QuarryException : Exception
{
    /// <summary>
    /// Creates a new QuarryException.
    /// </summary>
    public QuarryException()
    {
    }

    /// <summary>
    /// Creates a new QuarryException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QuarryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new QuarryException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuarryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
