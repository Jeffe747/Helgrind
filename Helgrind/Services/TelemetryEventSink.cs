using System.Threading.Channels;

namespace Helgrind.Services;

public sealed class TelemetryEventSink
{
    private readonly Channel<SuspiciousRequestEventRecord> _channel = Channel.CreateBounded<SuspiciousRequestEventRecord>(
        new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public ChannelReader<SuspiciousRequestEventRecord> Reader => _channel.Reader;

    public bool Enqueue(SuspiciousRequestEventRecord telemetryEvent)
        => _channel.Writer.TryWrite(telemetryEvent);
}