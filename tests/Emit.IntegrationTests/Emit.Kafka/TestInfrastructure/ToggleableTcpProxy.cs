namespace Emit.Kafka.Tests.TestInfrastructure;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Forwards TCP connections from a local port to a target, and can be cut off: while down, every connection,
/// open or new, is reset, as when the target cannot be reached.
/// </summary>
public sealed class ToggleableTcpProxy : IAsyncDisposable
{
    /// <summary>The URL to connect to the target through the proxy.</summary>
    public string Url => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentDictionary<TcpClient, byte> connections = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly string targetHost;
    private readonly int targetPort;
    private readonly Task accepting;
    private volatile bool up;

    /// <summary>
    /// Starts the proxy, down.
    /// </summary>
    /// <param name="target">The URL of the target, whose host and port connections are forwarded to.</param>
    public ToggleableTcpProxy(Uri target)
    {
        targetHost = target.Host;
        targetPort = target.Port;
        listener.Start();
        accepting = AcceptAsync();
    }

    /// <summary>Forwards connections to the target from now on.</summary>
    public void Up() => up = true;

    /// <summary>Resets every open connection, and every new one until <see cref="Up"/>.</summary>
    public void Down()
    {
        up = false;
        foreach (var connection in connections.Keys)
        {
            Reset(connection);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        Down();
        await accepting.ContinueWith(_ => { }, TaskScheduler.Default);
        stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            // Registered before the state is read, so a Down in between resets it too
            var client = await listener.AcceptTcpClientAsync(stopping.Token);
            connections[client] = 0;
            if (!up)
            {
                connections.TryRemove(client, out _);
                Reset(client);
                continue;
            }

            _ = ForwardAsync(client);
        }
    }

    private async Task ForwardAsync(TcpClient client)
    {
        using var target = new TcpClient();
        connections[target] = 0;
        try
        {
            await target.ConnectAsync(targetHost, targetPort, stopping.Token);
            var clientStream = client.GetStream();
            var targetStream = target.GetStream();
            await Task.WhenAny(
                clientStream.CopyToAsync(targetStream, stopping.Token),
                targetStream.CopyToAsync(clientStream, stopping.Token));
        }
        catch (Exception) when (!up || stopping.IsCancellationRequested)
        {
            // The connection was reset by Down, or the proxy is stopping
        }
        finally
        {
            connections.TryRemove(client, out _);
            connections.TryRemove(target, out _);
            Reset(client);
        }
    }

    private static void Reset(TcpClient client)
    {
        try
        {
            client.Client.LingerState = new LingerOption(true, 0);
        }
        catch (ObjectDisposedException)
        {
        }

        client.Dispose();
    }
}
