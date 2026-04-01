using NUnit.Framework;
using Quarry.Generators.CodeGen;

namespace Quarry.Tests.IR;

[TestFixture]
public class CarrierParameterTests
{
    [Test]
    public void CarrierParameter_Equality()
    {
        var a = new CarrierParameter(0, "P0", "string", "args[0]", "bind0");
        var b = new CarrierParameter(0, "P0", "string", "args[0]", "bind0");
        var c = new CarrierParameter(1, "P1", "int", "args[1]", "bind1");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }
}
