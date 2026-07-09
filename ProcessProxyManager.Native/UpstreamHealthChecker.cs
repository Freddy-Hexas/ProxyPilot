using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace ProcessProxyManager.Native;

public sealed class UpstreamHealthChecker
{
    private static readonly UpstreamHealthTarget[] Targets =
    [
        new("www.google.com", 443, "/generate_204"),
        new("www.youtube.com", 443, "/generate_204")
    ];

    public async Task<UpstreamHealthResult> CheckAsync(
        string proxyType,
        string proxyHost,
        int proxyPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(proxyHost) || proxyPort <= 0)
        {
            return new UpstreamHealthResult(false, "Upstream proxy is not configured.", []);
        }

        var details = new List<string>();
        foreach (var target in Targets)
        {
            var result = await CheckTargetAsync(proxyType, proxyHost, proxyPort, target, cancellationToken);
            details.Add(result);
            if (result.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return new UpstreamHealthResult(true, "Upstream proxy is reachable.", details);
            }
        }

        return new UpstreamHealthResult(false, "Upstream proxy is not usable for Google/YouTube.", details);
    }

    private static async Task<string> CheckTargetAsync(
        string proxyType,
        string proxyHost,
        int proxyPort,
        UpstreamHealthTarget target,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(proxyHost, proxyPort, timeout.Token);

            await using var stream = client.GetStream();
            if (IsSocksProxy(proxyType))
            {
                await OpenSocks5TunnelAsync(stream, target, timeout.Token);
            }
            else
            {
                await OpenHttpConnectTunnelAsync(stream, target, timeout.Token);
            }

            await using var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(target.Host, null, System.Security.Authentication.SslProtocols.None, checkCertificateRevocation: false);

            var request = Encoding.ASCII.GetBytes(
                $"GET {target.Path} HTTP/1.1\r\nHost: {target.Host}\r\nConnection: close\r\nUser-Agent: ProxyPilot/1.0\r\n\r\n");
            await ssl.WriteAsync(request, timeout.Token);
            await ssl.FlushAsync(timeout.Token);

            var line = await ReadAsciiLineAsync(ssl, timeout.Token);
            return line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)
                ? $"OK {target.Host}: {line}"
                : $"FAIL {target.Host}: unexpected response";
        }
        catch (Exception exception)
        {
            return $"FAIL {target.Host}: {exception.Message}";
        }
    }

    private static async Task OpenHttpConnectTunnelAsync(
        NetworkStream stream,
        UpstreamHealthTarget target,
        CancellationToken cancellationToken)
    {
        var request = Encoding.ASCII.GetBytes(
            $"CONNECT {target.Host}:{target.Port} HTTP/1.1\r\nHost: {target.Host}:{target.Port}\r\nProxy-Connection: Keep-Alive\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var statusLine = await ReadAsciiLineAsync(stream, cancellationToken);
        while (!string.IsNullOrEmpty(await ReadAsciiLineAsync(stream, cancellationToken)))
        {
            // Drain headers.
        }

        if (!statusLine.Contains(" 200 ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(statusLine);
        }
    }

    private static async Task OpenSocks5TunnelAsync(
        NetworkStream stream,
        UpstreamHealthTarget target,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cancellationToken);
        var greeting = await ReadExactAsync(stream, 2, cancellationToken);
        if (greeting[0] != 0x05 || greeting[1] != 0x00)
        {
            throw new InvalidOperationException("SOCKS5 no-auth handshake failed.");
        }

        var hostBytes = Encoding.ASCII.GetBytes(target.Host);
        if (hostBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Target host is too long.");
        }

        var request = new byte[7 + hostBytes.Length];
        request[0] = 0x05;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = 0x03;
        request[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(request, 5);
        request[^2] = (byte)(target.Port >> 8);
        request[^1] = (byte)(target.Port & 0xff);
        await stream.WriteAsync(request, cancellationToken);

        var header = await ReadExactAsync(stream, 4, cancellationToken);
        if (header[1] != 0x00)
        {
            throw new InvalidOperationException($"SOCKS5 connect failed: {header[1]}");
        }

        var addressLength = header[3] switch
        {
            0x01 => 4,
            0x03 => (await ReadExactAsync(stream, 1, cancellationToken))[0],
            0x04 => 16,
            _ => throw new InvalidOperationException("SOCKS5 returned an unknown address type.")
        };

        await ReadExactAsync(stream, addressLength + 2, cancellationToken);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Connection closed.");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task<string> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer[0] == '\n')
            {
                break;
            }

            if (buffer[0] != '\r')
            {
                bytes.Add(buffer[0]);
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static bool IsSocksProxy(string proxyType)
    {
        return proxyType.Contains("socks", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record UpstreamHealthTarget(string Host, int Port, string Path);
}

public sealed record UpstreamHealthResult(bool Success, string Message, IReadOnlyList<string> Details);
