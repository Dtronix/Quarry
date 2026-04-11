using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class InterfaceTests
{
    [Test]
    public void DapperConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new DapperConversionDiagnostic("Warning", "test message");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Warning"));
        Assert.That(iface.Message, Is.EqualTo("test message"));
    }

    [Test]
    public void EfCoreConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new EfCoreConversionDiagnostic("Error", "ef core error");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Error"));
        Assert.That(iface.Message, Is.EqualTo("ef core error"));
    }

    [Test]
    public void AdoNetConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new AdoNetConversionDiagnostic("Info", "ado info");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Info"));
        Assert.That(iface.Message, Is.EqualTo("ado info"));
    }

    [Test]
    public void SqlKataConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new SqlKataConversionDiagnostic("Warning", "kata warning");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Warning"));
        Assert.That(iface.Message, Is.EqualTo("kata warning"));
    }
}
