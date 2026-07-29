using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PM.AgentRuns;

public interface IAgentRunnerEventStream : IAsyncDisposable
{
    IAsyncEnumerable<AgentRunStreamMessage> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentRunnerEventStream : IAgentRunnerEventStream
{
    private const int MaximumEventBytes = 1_048_576;
    private readonly HttpClient _client;
    private readonly HttpResponseMessage _response;
    private readonly Stream _stream;
    private readonly string _runId;
    private long _sequence;
    private bool _ended;

    internal AgentRunnerEventStream(
        HttpClient client,
        HttpResponseMessage response,
        Stream stream,
        string runId,
        long afterSequence)
    {
        _client = client;
        _response = response;
        _stream = stream;
        _runId = runId;
        _sequence = afterSequence;
    }

    public async IAsyncEnumerable<AgentRunStreamMessage> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_ended) yield break;
        using var reader = new StreamReader(_stream, Encoding.UTF8, false, 4096, true);
        var eventName = string.Empty;
        var id = string.Empty;
        var data = new StringBuilder();
        while (true)
        {
            var line = await ReadLine(reader, cancellationToken);
            if (line == null)
            {
                if (_ended) yield break;
                throw new AgentRunnerStreamException("runner_stream_disconnected",
                    "The runner event stream disconnected before its terminal signal.");
            }

            if (line.Length == 0)
            {
                if (eventName.Length == 0)
                {
                    id = string.Empty;
                    data.Clear();
                    continue;
                }

                var parsed = ParseMessage(eventName, id, data.ToString());
                eventName = string.Empty;
                id = string.Empty;
                data.Clear();
                if (parsed == null) continue;
                if (parsed.End != null) _ended = true;
                yield return parsed;
                if (_ended) yield break;
                continue;
            }

            if (line[0] == ':') continue;
            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? string.Empty : line[(separator + 1)..].TrimStart(' ');
            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                    if (Encoding.UTF8.GetByteCount(data.ToString()) > MaximumEventBytes)
                        throw new AgentRunnerStreamException("runner_stream_event_too_large",
                            "A runner event exceeded the client size limit.");
                    break;
                case "retry":
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _response.Dispose();
        _client.Dispose();
    }

    private AgentRunStreamMessage? ParseMessage(string eventName, string id, string data)
    {
        try
        {
            if (eventName == "run-event")
            {
                var runEvent = JsonSerializer.Deserialize<AgentRunEvent>(data, AgentRunJson.Options)
                               ?? throw new AgentRunnerStreamException("invalid_runner_stream",
                                   "The runner returned an empty durable event.");
                var validation = AgentRunContractValidator.ValidateEvent(runEvent);
                if (!validation.Success || runEvent.RunId != _runId ||
                    !long.TryParse(id, out var eventId) || eventId != runEvent.Sequence ||
                    !AgentRunReplay.ValidateNextSequence(_sequence, runEvent).Success)
                    throw new AgentRunnerStreamException("invalid_event_sequence",
                        "The runner returned an invalid durable event sequence.");
                _sequence = runEvent.Sequence;
                return AgentRunStreamMessage.Durable(runEvent);
            }

            if (eventName == "stream-end")
            {
                var end = JsonSerializer.Deserialize<AgentRunStreamEnd>(data, AgentRunJson.Options)
                          ?? throw new AgentRunnerStreamException("invalid_runner_stream",
                              "The runner returned an invalid terminal stream signal.");
                if (!Enum.IsDefined(end.State) || !AgentRunLifecycle.IsTerminal(end.State) ||
                    end.LastSequence != _sequence)
                    throw new AgentRunnerStreamException("invalid_event_sequence",
                        "The runner returned an invalid terminal stream sequence.");
                return AgentRunStreamMessage.Terminal(end);
            }

            return null;
        }
        catch (JsonException exception)
        {
            throw new AgentRunnerStreamException("invalid_runner_stream",
                "The runner event stream contained invalid JSON.", exception);
        }
    }

    private static async Task<string?> ReadLine(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            throw new AgentRunnerStreamException("runner_stream_disconnected",
                "The runner event stream disconnected.", exception);
        }
    }
}
