using QuinSlate.Ui.Logging;

namespace QuinSlate.Tests.Logging;

public sealed class LogBootstrapperTests
{
    [Fact]
    public void ResolveLogDirectory_ComposesLogsSubfolder()
    {
        var appData = Path.Combine(Path.GetTempPath(), "QuinSlateLogTest");

        var result = LogBootstrapper.ResolveLogDirectory(appData);

        Assert.Equal(Path.Combine(appData, "Logs"), result);
    }

    [Fact]
    public void Initialize_CreatesLogsDirectoryAndWritesFile()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(temp);
        try
        {
            LogBootstrapper.Initialize(temp);
            Serilog.Log.Information("test entry");
            LogBootstrapper.Shutdown();

            var logsDirectory = Path.Combine(temp, "Logs");
            Assert.True(Directory.Exists(logsDirectory));
            Assert.NotEmpty(Directory.GetFiles(logsDirectory, "quinslate-*.log"));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
