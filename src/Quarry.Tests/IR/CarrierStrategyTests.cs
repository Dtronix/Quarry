using NUnit.Framework;
using Quarry.Generators.CodeGen;

namespace Quarry.Tests.IR;

[TestFixture]
public class CarrierStrategyTests
{
    [Test]
    public void Ineligible_HasReason()
    {
        var strategy = CarrierStrategy.Ineligible("Chain is not tier 1");

        Assert.That(strategy.IsEligible, Is.False);
        Assert.That(strategy.IneligibleReason, Is.EqualTo("Chain is not tier 1"));
        Assert.That(strategy.Fields, Is.Empty);
        Assert.That(strategy.Parameters, Is.Empty);
    }

    [Test]
    public void Eligible_HasFieldsAndParameters()
    {
        var fields = new[]
        {
            new CarrierField("P0", "string"),
            new CarrierField("P1", "int")
        };
        var parameters = new[]
        {
            new CarrierParameter(0, "P0", "string", "args[0]", "cmd.Parameters[0].Value = carrier.P0;"),
            new CarrierParameter(1, "P1", "int", "args[1]", "cmd.Parameters[1].Value = carrier.P1;")
        };
        var strategy = new CarrierStrategy(
            isEligible: true,
            ineligibleReason: null,
            baseClassName: "QueryCarrier",
            fields: fields,
            staticFields: System.Array.Empty<CarrierStaticField>(),
            parameters: parameters);

        Assert.That(strategy.IsEligible, Is.True);
        Assert.That(strategy.BaseClassName, Is.EqualTo("QueryCarrier"));
        Assert.That(strategy.Fields, Has.Count.EqualTo(2));
        Assert.That(strategy.Parameters, Has.Count.EqualTo(2));
    }

    [Test]
    public void CarrierField_Equality()
    {
        var a = new CarrierField("P0", "string");
        var b = new CarrierField("P0", "string");
        var c = new CarrierField("P1", "int");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    [Test]
    public void CarrierParameter_Equality()
    {
        var a = new CarrierParameter(0, "P0", "string", "args[0]", "bind0");
        var b = new CarrierParameter(0, "P0", "string", "args[0]", "bind0");
        var c = new CarrierParameter(1, "P1", "int", "args[1]", "bind1");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    [Test]
    public void CarrierStrategy_Equality()
    {
        var strategy1 = CarrierStrategy.Ineligible("reason1");
        var strategy2 = CarrierStrategy.Ineligible("reason1");
        var strategy3 = CarrierStrategy.Ineligible("reason2");

        Assert.That(strategy1.Equals(strategy2), Is.True);
        Assert.That(strategy1.Equals(strategy3), Is.False);
    }

    [Test]
    public void CarrierStaticField_Equality()
    {
        var a = new CarrierStaticField("SqlField", "string", "\"SELECT * FROM users\"");
        var b = new CarrierStaticField("SqlField", "string", "\"SELECT * FROM users\"");
        var c = new CarrierStaticField("SqlField2", "string", null);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }
}
