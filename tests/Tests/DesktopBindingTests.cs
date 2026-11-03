using System.Xml.Linq;

namespace FrontierAirportBuilder.Tests;

public sealed class DesktopBindingTests
{
    [Fact]
    public void CompanyCargoSelectorNeverWritesBackToReadOnlyAircraftLabels()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Desktop", "CargoJourneyControl.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var labels = document.Descendants(presentation + "Run").Select(e => e.Attribute("Text")?.Value).Where(text => text?.StartsWith("{Binding") == true).ToArray();
        Assert.Equal(2, labels.Length);
        Assert.All(labels, binding => Assert.Contains("Mode=OneWay", binding));
    }
    [Fact]
    public void ReadOnlyTrackingDiagnosticUsesOneWayBinding()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "Desktop", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var diagnosticBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute("Text")?.Value.Contains("DiagnosticLog", StringComparison.Ordinal) == true);

        Assert.Equal("{Binding DiagnosticLog, Mode=OneWay}", diagnosticBox.Attribute("Text")?.Value);
        Assert.Equal("True", diagnosticBox.Attribute("IsReadOnly")?.Value);
    }
}
