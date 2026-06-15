using QuinSlate.Ui.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace QuinSlate.Tests.Logging;

public sealed class ThreadIdEnricherTests
{
    private sealed class CaptureSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new List<LogEvent>();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public void Enrich_AddsThreadIdProperty()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new ThreadIdEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("hello");

        Assert.Single(sink.Events);
        Assert.True(sink.Events[0].Properties.ContainsKey("ThreadId"));
    }
}
