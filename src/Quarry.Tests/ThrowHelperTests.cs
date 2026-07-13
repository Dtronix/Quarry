using Quarry.Internal;

namespace Quarry.Tests;

/// <summary>
/// Behavioral coverage for the generated-code throw helpers (#307 review F9) —
/// generation tests assert the guard APPEARS in emitted dispatch code; these pin
/// what it actually does when reached.
/// </summary>
[TestFixture]
public class ThrowHelperTests
{
    [Test]
    public void UnenumeratedMask_ThrowsActionableInvalidOperation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ThrowHelper.UnenumeratedMask(5));
        Assert.That(ex!.Message, Does.Contain("mask 5"), "message must name the offending mask value");
        Assert.That(ex.Message, Does.Contain("Quarry"));
        Assert.That(ex.Message, Does.Contain("issues"), "message must direct the user to file an issue");
    }
}
