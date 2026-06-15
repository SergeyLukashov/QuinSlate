using QuinSlate.Ui.Logging;

namespace QuinSlate.Tests.Logging;

public sealed class EnvironmentReportTests
{
    [Fact]
    public void Collect_IncludesCoreKeys()
    {
        var report = EnvironmentReport.Collect();
        var keys = report.Select(pair => pair.Key).ToList();

        Assert.Contains("Application", keys);
        Assert.Contains("Version", keys);
        Assert.Contains("OS", keys);
        Assert.Contains(".NET runtime", keys);
        Assert.Contains("Process architecture", keys);
    }

    [Fact]
    public void Collect_ValuesAreNonEmpty()
    {
        var report = EnvironmentReport.Collect();

        Assert.NotEmpty(report);
        Assert.All(report, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value)));
    }
}
