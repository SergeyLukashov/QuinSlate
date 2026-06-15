using Serilog.Core;
using Serilog.Events;
using System;

namespace QuinSlate.Ui.Logging;

/// <summary>
/// Serilog enricher that attaches the current managed thread id to every log
/// event as a <c>ThreadId</c> property. Implemented in-house to avoid adding the
/// <c>Serilog.Enrichers.Thread</c> package.
/// </summary>
public sealed class ThreadIdEnricher : ILogEventEnricher
{
    private const string ThreadIdPropertyName = "ThreadId";

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent == null)
        {
            throw new ArgumentNullException(nameof(logEvent));
        }

        if (propertyFactory == null)
        {
            throw new ArgumentNullException(nameof(propertyFactory));
        }

        var property = propertyFactory.CreateProperty(ThreadIdPropertyName, Environment.CurrentManagedThreadId);
        logEvent.AddPropertyIfAbsent(property);
    }
}
