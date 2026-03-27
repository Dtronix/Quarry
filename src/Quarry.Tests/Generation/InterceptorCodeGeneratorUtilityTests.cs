using NUnit.Framework;
using Quarry.Generators.Generation;

namespace Quarry.Tests.Generation;

[TestFixture]
public class InterceptorCodeGeneratorUtilityTests
{
    #region BuildReceiverType

    [Test]
    public void BuildReceiverType_IEntityAccessor_AlwaysUsesOneTypeArg()
    {
        var result = InterceptorCodeGenerator.BuildReceiverType("IEntityAccessor", "User", "(int, string)");
        Assert.That(result, Is.EqualTo("IEntityAccessor<User>"));
    }

    [Test]
    public void BuildReceiverType_EntityAccessor_AlwaysUsesOneTypeArg()
    {
        var result = InterceptorCodeGenerator.BuildReceiverType("EntityAccessor", "User", "(int, string)");
        Assert.That(result, Is.EqualTo("EntityAccessor<User>"));
    }

    [Test]
    public void BuildReceiverType_IQueryBuilder_WithResultType_UsesTwoTypeArgs()
    {
        var result = InterceptorCodeGenerator.BuildReceiverType("IQueryBuilder", "User", "(int, string)");
        Assert.That(result, Is.EqualTo("IQueryBuilder<User, (int, string)>"));
    }

    [Test]
    public void BuildReceiverType_IQueryBuilder_WithoutResultType_UsesOneTypeArg()
    {
        var result = InterceptorCodeGenerator.BuildReceiverType("IQueryBuilder", "User", null);
        Assert.That(result, Is.EqualTo("IQueryBuilder<User>"));
    }

    [Test]
    public void BuildReceiverType_IEntityAccessor_WithNullResultType_UsesOneTypeArg()
    {
        var result = InterceptorCodeGenerator.BuildReceiverType("IEntityAccessor", "Order", null);
        Assert.That(result, Is.EqualTo("IEntityAccessor<Order>"));
    }

    [Test]
    public void BuildReceiverType_IEntityAccessor_IgnoresResultType()
    {
        // Even with a named tuple result type, IEntityAccessor only gets entity type
        var result = InterceptorCodeGenerator.BuildReceiverType(
            "IEntityAccessor", "Order", "(int OrderId, decimal Total, OrderPriority Priority)");
        Assert.That(result, Is.EqualTo("IEntityAccessor<Order>"));
    }

    #endregion

    #region IsEntityAccessorType

    [TestCase("IEntityAccessor", ExpectedResult = true)]
    [TestCase("EntityAccessor", ExpectedResult = true)]
    [TestCase("IQueryBuilder", ExpectedResult = false)]
    [TestCase("IDeleteBuilder", ExpectedResult = false)]
    [TestCase("IJoinedQueryBuilder", ExpectedResult = false)]
    public bool IsEntityAccessorType_IdentifiesCorrectly(string typeName)
    {
        return InterceptorCodeGenerator.IsEntityAccessorType(typeName);
    }

    #endregion

    #region ToReturnTypeName

    [Test]
    public void ToReturnTypeName_IEntityAccessor_ReturnsIQueryBuilder()
    {
        var result = InterceptorCodeGenerator.ToReturnTypeName("IEntityAccessor");
        Assert.That(result, Is.EqualTo("IQueryBuilder"));
    }

    [Test]
    public void ToReturnTypeName_IQueryBuilder_ReturnsSelf()
    {
        var result = InterceptorCodeGenerator.ToReturnTypeName("IQueryBuilder");
        Assert.That(result, Is.EqualTo("IQueryBuilder"));
    }

    #endregion
}
