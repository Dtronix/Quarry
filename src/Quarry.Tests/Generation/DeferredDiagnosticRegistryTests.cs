using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Guards the deferred-diagnostic descriptor registry (#311). Deferred
/// <c>DiagnosticInfo</c> emissions carry only a string ID; the descriptor is resolved
/// at report time from <c>QuarryGenerator.s_deferredDescriptors</c>. Three diagnostics
/// (QRY048, QRY900, QRY063) have shipped unregistered and were silently dropped by the
/// old miss path — these tests pin the registrations that closed those gaps.
/// </summary>
[TestFixture]
public class DeferredDiagnosticRegistryTests
{
    [TestCase("QRY900")] // InternalError — ChainAnalyzer catch handlers, SqlExprBinder nav-aggregate exceptions
    [TestCase("QRY063")] // NavigationTargetNotFound — ChainAnalyzer.ResolveNavigationColumn
    [TestCase("QRY048")] // MySqlBindOrderFallback — first shipped-unregistered occurrence (#304)
    public void PreviouslyDroppedDeferredIds_AreRegistered(string id)
    {
        var descriptor = QuarryGenerator.TryGetDeferredDescriptor(id);
        Assert.That(descriptor, Is.Not.Null, $"{id} must be registered in s_deferredDescriptors");
        Assert.That(descriptor!.Id, Is.EqualTo(id));
    }

    [Test]
    public void UnregisteredId_ResolvesToNull_SoReportPathFallsBackToQRY900()
    {
        // ReportDeferredDiagnostic reports QRY900 naming the ID when this returns null;
        // QRY900 itself is registered (asserted above), so that fallback cannot recurse.
        Assert.That(QuarryGenerator.TryGetDeferredDescriptor("QRY999"), Is.Null);
    }
}
