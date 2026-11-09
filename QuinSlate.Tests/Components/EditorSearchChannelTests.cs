using QuinSlate.Ui.Components;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace QuinSlate.Tests.Components;

public sealed class EditorSearchChannelTests
{
    private readonly List<string> posted = new List<string>();
    private readonly EditorSearchChannel channel;

    public EditorSearchChannelTests()
    {
        channel = new EditorSearchChannel(writeBody => posted.Add(EditorBridgeJson.Build(writeBody)));
    }

    [Fact]
    public void TryReceive_Results_ReadsCountsTierAndCurrent()
    {
        EditorSearchResultsEventArgs received = null;
        channel.ResultsReceived += (s, e) => received = e;

        bool handled = Receive("""{"type":"searchResults","counts":[2,0,1,0,3],"capped":[false,false,false,false,true],"tier":3,"current":{"index":3,"ordinal":3}}""");

        Assert.True(handled);
        Assert.NotNull(received);
        Assert.Equal(6, received.Total);
        Assert.True(received.AnyCapped);
        Assert.Equal(3, received.Tier);
        Assert.Equal(1, received.CountFor(3));
        Assert.Equal(3, received.CountFor(5));
        Assert.Equal(0, received.CountFor(6));
        Assert.Equal(3, received.CurrentIndex);
        Assert.Equal(3, received.CurrentOrdinal);
    }

    [Fact]
    public void TryReceive_ResultsWithoutCurrent_ReportsNoCurrentMatch()
    {
        EditorSearchResultsEventArgs received = null;
        channel.ResultsReceived += (s, e) => received = e;

        Receive("""{"type":"searchResults","counts":[0,4,0,0,0],"capped":[false,false,false,false,false],"tier":1,"current":null}""");

        Assert.Equal(0, received.CurrentIndex);
        Assert.Equal(0, received.CurrentOrdinal);
    }

    [Fact]
    public void TryReceive_Navigate_RaisesTheBufferIndex()
    {
        int navigatedTo = 0;
        channel.NavigateRequested += (s, index) => navigatedTo = index;

        Assert.True(Receive("""{"type":"searchNavigate","index":4}"""));
        Assert.Equal(4, navigatedTo);
    }

    [Fact]
    public void TryReceive_OtherMessage_IsNotHandled()
    {
        Assert.False(Receive("""{"type":"contentSync","index":1,"text":""}"""));
    }

    [Fact]
    public void SendQuery_WritesQueryTabOrderAndMargin()
    {
        channel.SendQuery("milk", new[] { 3, 1, 2 }, 30.5);

        using JsonDocument message = JsonDocument.Parse(posted[0]);
        JsonElement root = message.RootElement;
        Assert.Equal("searchQuery", root.GetProperty("type").GetString());
        Assert.Equal("milk", root.GetProperty("query").GetString());
        Assert.Equal(new[] { 3, 1, 2 }, Array.ConvertAll(ToArray(root.GetProperty("tabOrder")), e => e.GetInt32()));
        Assert.Equal(30.5, root.GetProperty("scrollMarginTop").GetDouble());
    }

    [Fact]
    public void SendStepAndClose_WriteDirectionAndSelection()
    {
        channel.SendStep(forward: false, fromCaret: true);
        channel.SendClose(selectCurrent: true);

        Assert.Equal("""{"type":"searchStep","direction":"prev","fromCaret":true}""", posted[0]);
        Assert.Equal("""{"type":"searchClose","selectCurrent":true}""", posted[1]);
    }

    private bool Receive(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return channel.TryReceive(root.GetProperty("type").GetString(), root);
    }

    private static JsonElement[] ToArray(JsonElement array)
    {
        var items = new List<JsonElement>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            items.Add(item);
        }

        return items.ToArray();
    }
}
