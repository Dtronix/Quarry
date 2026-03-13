namespace Quarry.Tests;

/// <summary>
/// Unit tests for exception hierarchy.
/// </summary>
[TestFixture]
public class ExceptionTests
{
    #region QuarryException Tests

    [Test]
    public void QuarryException_IsException()
    {
        var ex = new QuarryException();

        Assert.That(ex, Is.InstanceOf<Exception>());
    }

    [Test]
    public void QuarryException_WithMessage()
    {
        var ex = new QuarryException("test message");

        Assert.That(ex.Message, Is.EqualTo("test message"));
    }

    [Test]
    public void QuarryException_WithMessageAndInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new QuarryException("outer", inner);

        Assert.That(ex.Message, Is.EqualTo("outer"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    #endregion

    #region QuarryConnectionException Tests

    [Test]
    public void QuarryConnectionException_InheritsFromQuarryException()
    {
        var ex = new QuarryConnectionException();

        Assert.That(ex, Is.InstanceOf<QuarryException>());
    }

    [Test]
    public void QuarryConnectionException_WithMessage()
    {
        var ex = new QuarryConnectionException("connection failed");

        Assert.That(ex.Message, Is.EqualTo("connection failed"));
    }

    [Test]
    public void QuarryConnectionException_WithMessageAndInner()
    {
        var inner = new InvalidOperationException("socket error");
        var ex = new QuarryConnectionException("connection failed", inner);

        Assert.That(ex.Message, Is.EqualTo("connection failed"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    #endregion

    #region QuarryQueryException Tests

    [Test]
    public void QuarryQueryException_InheritsFromQuarryException()
    {
        var ex = new QuarryQueryException();

        Assert.That(ex, Is.InstanceOf<QuarryException>());
    }

    [Test]
    public void QuarryQueryException_WithMessage()
    {
        var ex = new QuarryQueryException("query failed");

        Assert.That(ex.Message, Is.EqualTo("query failed"));
    }

    [Test]
    public void QuarryQueryException_WithMessageAndInner()
    {
        var inner = new InvalidOperationException("timeout");
        var ex = new QuarryQueryException("query failed", inner);

        Assert.That(ex.Message, Is.EqualTo("query failed"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [Test]
    public void QuarryQueryException_WithSql()
    {
        var inner = new InvalidOperationException("timeout");
        var ex = new QuarryQueryException("query failed", "SELECT * FROM users", inner);

        Assert.That(ex.Message, Is.EqualTo("query failed"));
        Assert.That(ex.Sql, Is.EqualTo("SELECT * FROM users"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [Test]
    public void QuarryQueryException_SqlCanBeNull()
    {
        var inner = new InvalidOperationException("timeout");
        var ex = new QuarryQueryException("query failed", null, inner);

        Assert.That(ex.Sql, Is.Null);
    }

    #endregion

    #region QuarryMappingException Tests

    [Test]
    public void QuarryMappingException_InheritsFromQuarryException()
    {
        var ex = new QuarryMappingException();

        Assert.That(ex, Is.InstanceOf<QuarryException>());
    }

    [Test]
    public void QuarryMappingException_WithMessage()
    {
        var ex = new QuarryMappingException("mapping failed");

        Assert.That(ex.Message, Is.EqualTo("mapping failed"));
    }

    [Test]
    public void QuarryMappingException_WithMessageAndInner()
    {
        var inner = new InvalidCastException("cast error");
        var ex = new QuarryMappingException("mapping failed", inner);

        Assert.That(ex.Message, Is.EqualTo("mapping failed"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [Test]
    public void QuarryMappingException_WithTypes()
    {
        var ex = new QuarryMappingException("mapping failed", typeof(string), typeof(int));

        Assert.That(ex.Message, Is.EqualTo("mapping failed"));
        Assert.That(ex.SourceType, Is.EqualTo(typeof(string)));
        Assert.That(ex.TargetType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void QuarryMappingException_WithTypesAndInner()
    {
        var inner = new InvalidCastException("cast error");
        var ex = new QuarryMappingException("mapping failed", typeof(string), typeof(int), inner);

        Assert.That(ex.Message, Is.EqualTo("mapping failed"));
        Assert.That(ex.SourceType, Is.EqualTo(typeof(string)));
        Assert.That(ex.TargetType, Is.EqualTo(typeof(int)));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }

    [Test]
    public void QuarryMappingException_TypesCanBeNull()
    {
        var ex = new QuarryMappingException("mapping failed", null, null);

        Assert.That(ex.SourceType, Is.Null);
        Assert.That(ex.TargetType, Is.Null);
    }

    #endregion

    #region Exception Hierarchy Tests

    [Test]
    public void CatchQuarryException_CatchesConnectionException()
    {
        Exception? caught = null;

        try
        {
            throw new QuarryConnectionException("connection failed");
        }
        catch (QuarryException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(caught, Is.InstanceOf<QuarryConnectionException>());
    }

    [Test]
    public void CatchQuarryException_CatchesQueryException()
    {
        Exception? caught = null;

        try
        {
            throw new QuarryQueryException("query failed");
        }
        catch (QuarryException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(caught, Is.InstanceOf<QuarryQueryException>());
    }

    [Test]
    public void CatchQuarryException_CatchesMappingException()
    {
        Exception? caught = null;

        try
        {
            throw new QuarryMappingException("mapping failed");
        }
        catch (QuarryException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(caught, Is.InstanceOf<QuarryMappingException>());
    }

    #endregion
}
