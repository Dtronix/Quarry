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

    [Test]
    public void ResolveDeferredReport_RegisteredId_ReturnsRealDescriptorWithOriginalArgs()
    {
        var diag = new Generators.Models.DiagnosticInfo(
            "QRY063", default, "NavProp", "SomeEntity", "MissingTarget");

        var (descriptor, args) = QuarryGenerator.ResolveDeferredReport(diag);

        Assert.That(descriptor.Id, Is.EqualTo("QRY063"));
        Assert.That(args, Is.EqualTo(new object[] { "NavProp", "SomeEntity", "MissingTarget" }));
    }

    [Test]
    public void ResolveDeferredReport_UnregisteredId_FallsBackToQRY900NamingTheId()
    {
        var diag = new Generators.Models.DiagnosticInfo("QRY999", default, "arg1", "arg2");

        var (descriptor, args) = QuarryGenerator.ResolveDeferredReport(diag);

        Assert.That(descriptor.Id, Is.EqualTo("QRY900"),
            "an unregistered deferred ID must be reported as an internal error, never dropped");
        Assert.That(args, Has.Length.EqualTo(1));
        var message = (string)args[0];
        Assert.That(message, Does.Contain("QRY999"), "the fallback must name the unregistered ID");
        Assert.That(message, Does.Contain("arg1, arg2"), "the fallback must preserve the original message args");
        // The composed diagnostic must format without throwing (QRY900's format has one placeholder).
        var formatted = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            descriptor.MessageFormat.ToString(), args);
        Assert.That(formatted, Does.Contain("QRY999"));
    }
}
