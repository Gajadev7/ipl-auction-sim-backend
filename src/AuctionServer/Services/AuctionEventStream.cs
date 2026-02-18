using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AuctionEngine;

namespace AuctionServer.Services;

public sealed class AuctionEventStream
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = [];
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public AuctionEventStream(AuctionManager auctionManager)
    {
        auctionManager.OnAuctionEvent += HandleAuctionEvent;
    }

    public Subscription Subscribe()
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers[subscriberId] = channel;
        return new Subscription(subscriberId, channel.Reader, this);
    }

    private void HandleAuctionEvent(AuctionEvent auctionEvent)
    {
        var message = BuildSseMessage(auctionEvent);

        foreach (var subscriber in _subscribers)
        {
            if (subscriber.Value.Writer.TryWrite(message))
            {
                continue;
            }

            if (_subscribers.TryRemove(subscriber.Key, out var staleChannel))
            {
                staleChannel.Writer.TryComplete();
            }
        }
    }

    private string BuildSseMessage(AuctionEvent auctionEvent)
    {
        var payload = JsonSerializer.SerializeToElement(
            auctionEvent,
            auctionEvent.GetType(),
            _jsonOptions);

        var envelope = new AuctionEventEnvelope(auctionEvent.GetType().Name, payload);
        var json = JsonSerializer.Serialize(envelope, _jsonOptions);

        return $"event: {envelope.Type}\ndata: {json}\n\n";
    }

    private void Unsubscribe(Guid subscriberId)
    {
        if (_subscribers.TryRemove(subscriberId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public sealed class Subscription : IAsyncDisposable
    {
        private readonly Guid _subscriberId;
        private readonly AuctionEventStream _owner;
        private bool _disposed;

        internal Subscription(Guid subscriberId, ChannelReader<string> reader, AuctionEventStream owner)
        {
            _subscriberId = subscriberId;
            Reader = reader;
            _owner = owner;
        }

        public ChannelReader<string> Reader { get; }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _owner.Unsubscribe(_subscriberId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record AuctionEventEnvelope(string Type, JsonElement Data);
}
