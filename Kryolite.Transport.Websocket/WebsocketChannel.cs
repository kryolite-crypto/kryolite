using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Kryolite.ByteSerializer;
using Kryolite.Shared;

namespace Kryolite.Transport.Websocket;

public class WebsocketChannel : IDisposable
{
    public bool IsConnected => _connection?.Socket.State == WebSocketState.Open;

    public Channel<ArraySegment<byte>> Broadcasts => _duplex;
    public CancellationToken ConnectionToken => _cts.Token;

    private readonly Uri _uri;
    private int _msgId;
    private Connection? _connection;
    private CancellationTokenSource _cts;
    private byte[] _msgBuf = new byte[4];

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Channel<ArraySegment<byte>> _duplex = Channel.CreateUnbounded<ArraySegment<byte>>();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ArraySegment<byte>>> _requests = new();

    private static readonly byte[] _duplexMessage = [0];
    private static readonly byte[] _unaryRequest = [1];
    private static readonly byte[] _unaryReply = [2];

    private static readonly HttpClient _httpClient = new();

    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }
    public long MessagesSent { get; private set; }
    public long MessagesReceived { get; private set; }

    public WebsocketChannel(Uri uri, WebSocket ws, CancellationToken token)
    {
        Console.WriteLine("Create wtih ws");
        _uri = uri;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _connection = new (this, ws, _cts.Token);
    }

    private WebsocketChannel(Uri uri, CancellationToken token)
    {
        Console.WriteLine("Create");
        _uri = uri;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    public static WebsocketChannel ForAddress(Uri uri, CancellationToken token)
        => new(uri, token);

    /// <summary>
    /// Test connection to node
    /// </summary>
    /// <returns></returns>
    public async ValueTask<(bool, string)> Ping()
    {
        try
        {
            if (IsConnected)
            {
                return (true, string.Empty);
            }

            var result = await _httpClient.GetAsync(new Uri(_uri, "?action=ping"), _cts.Token);

            return (result.IsSuccessStatusCode, result.ReasonPhrase ?? string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Get public key from node
    /// </summary>
    /// <returns></returns>
    public async ValueTask<(AuthResponse?, string)> GetPublicKey()
    {
        try
        {
            var result = await _httpClient.GetAsync(new Uri(_uri, "?action=whois"), _cts.Token);

            if (!result.IsSuccessStatusCode)
            {
                return (null, result.ReasonPhrase ?? string.Empty);
            }

            var bytes = await result.Content.ReadAsByteArrayAsync();

            return (Serializer.Deserialize<AuthResponse>(bytes), string.Empty);
        }
        catch (Exception ex)
        {
            return (null, ex.ToString());
        }
    }

    /// <summary>
    /// Get peers from node
    /// </summary>
    /// <returns></returns>
    public async ValueTask<(NodeListResponse?, string)> GetPeers()
    {
        try
        {
            var result = await _httpClient.GetAsync(new Uri(_uri, "?action=peers"), _cts.Token);

            if (!result.IsSuccessStatusCode)
            {
                return (null, result.ReasonPhrase ?? string.Empty);
            }

            var bytes = await result.Content.ReadAsByteArrayAsync();

            return (Serializer.Deserialize<NodeListResponse>(bytes), string.Empty);
        }
        catch (Exception ex)
        {
            return (null, ex.ToString());
        }
    }

    public bool Connect(AuthRequest authRequest, [NotNullWhen(false)] out string? reason)
    {
        reason = null;

        try
        {
            if (_connection?.Socket.State == WebSocketState.Connecting || _connection?.Socket.State == WebSocketState.Open)
            {
                return true;
            }

            if (_connection is not null)
            {
                _connection.Dispose();
            }

            var uriBuilder = new UriBuilder(_uri)
            {
                Scheme = _uri.Scheme == "https" ? "wss" : "ws"
            };

            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", Convert.ToBase64String(Serializer.Serialize(authRequest)));
            ws.ConnectAsync(uriBuilder.Uri, _cts.Token).Wait(TimeSpan.FromSeconds(30), _cts.Token);

            _connection = new Connection(this, ws, _cts.Token);

            return true;
        }
        catch (TimeoutException tEx)
        {
            reason = tEx.Message;
            return false;
        }
        catch (OperationCanceledException cEx)
        {
            reason = cEx.Message;
            return false;
        }
        catch (WebSocketException wEx)
        {
            reason = wEx.Message;
            return false;
        }
    }

    public async Task SendDuplex(byte[] payload, CancellationToken token)
    {
        if (_connection is null)
        {
            TransportException.Throw();
        }

        if (!IsConnected)
        {
            TransportException.Throw();
        }

        await _lock.WaitAsync(token);

        await _connection.Socket.SendAsync(_duplexMessage, WebSocketMessageType.Binary, false, token);
        await _connection.Socket.SendAsync(payload, WebSocketMessageType.Binary, true, token);

        MessagesSent++;
        BytesSent += _duplexMessage.Length + payload.Length;

        _lock.Release();
    }

    public async Task<ArraySegment<byte>> SendUnary(ArrayBufferWriter<byte> writer, CancellationToken token)
    {
        if (_connection is null)
        {
            TransportException.Throw();
        }

        if (!IsConnected)
        {
            TransportException.Throw();
        }

        var msgId = Interlocked.Increment(ref _msgId);
        var tcs = new TaskCompletionSource<ArraySegment<byte>>();

        if (!_requests.TryAdd(msgId, tcs))
        {
            TransportException.Throw();
        }

        await _lock.WaitAsync(token);

        if (!BitConverter.TryWriteBytes(_msgBuf, msgId))
        {
            TransportException.Throw();
        }

        await _connection.Socket.SendAsync(_unaryRequest, WebSocketMessageType.Binary, false, token);
        await _connection.Socket.SendAsync(_msgBuf, WebSocketMessageType.Binary, false, token);
        await _connection.Socket.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, token);

        MessagesSent++;
        BytesSent += _unaryRequest.Length + _msgBuf.Length + writer.WrittenCount;

        _lock.Release();

        return await tcs.Task
            .WaitAsync(TimeSpan.FromSeconds(30), token)
            .ContinueWith((result) =>
            {
                _requests.TryRemove(msgId, out _);

                if (result.Exception is not null)
                {
                    throw result.Exception.InnerException!; // TimeoutException
                }

                return result.Result;
            });
    }

    public Task Disconnect(CancellationToken token)
    {
        if (!IsConnected)
        {
            return Task.CompletedTask;
        }

        return _connection?.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, token) ?? Task.CompletedTask;
    }

    public T CreateClient<T>() where T : IWebsocketService<T> => ServiceResolver.CreateClient<T>(this);

    public void Dispose()
    {
        _duplex.Writer.Complete();
        _cts.Dispose();
        _connection?.Dispose();
    }

    private class Connection : IDisposable
    {
        public WebSocket Socket => _ws;

        private WebsocketChannel _channel;
        private WebSocket _ws;
        private CancellationTokenSource _cts;

        public Connection(WebsocketChannel channel, WebSocket ws, CancellationToken token)
        {
            _channel = channel;
            _ws = ws;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            
            _ = Receive(_cts.Token);
        }

        private async Task Receive(CancellationToken token)
        {
            var buffer = new byte[1024 * 32];

            Console.WriteLine("Start Recv");

            while (!token.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, token);

                using var stream = new MemoryStream(result.Count);
                stream.Write(buffer.AsSpan(0, result.Count));

                Console.WriteLine("Recv");

                // Read all data
                while (!result.EndOfMessage)
                {
                    result = await _ws.ReceiveAsync(buffer, token);
                    stream.Write(buffer.AsSpan(0, result.Count));
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _channel.Disconnect(token);
                    _cts.Cancel();
                    break;
                }

                var length = (int)stream.Length;
                var data = new ArraySegment<byte>(stream.GetBuffer()).Slice(0, length);

                _ = Task.Run(async () =>
                {
                    try
                    {
                    Console.WriteLine("PAcket: " + data[0]);
                    switch (data[0])
                    {
                        case 0:
                            await _channel._duplex.Writer.WriteAsync(data.Slice(1), token);
                            break;
                        case 1:
                            var idBytes = data.Slice(1, 4);
                            var method = data[5];
                            var payload = data.Slice(6);

                            Console.WriteLine("Method " + method);

                            var service = ServiceResolver.Resolve(_channel);
                            var result = service.CallMethod(method, payload);

                            await _channel._lock.WaitAsync(token);

                            await _ws.SendAsync(_unaryReply, WebSocketMessageType.Binary, false, token);
                            await _ws.SendAsync(idBytes, WebSocketMessageType.Binary, false, token);
                            await _ws.SendAsync(result, WebSocketMessageType.Binary, true, token);

                            _channel._lock.Release();
                            break;
                        case 2:
                            Console.WriteLine("Unary response");
                            var idBytes2 = data.Slice(1, 4);
                            var payload2 = data.Slice(5);

                            var msgId = BitConverter.ToInt32(idBytes2);

                            if (_channel._requests.TryRemove(msgId, out var tcs))
                            {
                                tcs.SetResult(payload2);
                            }

                            break;
                        default:
                            TransportException.Throw();
                            break;
                    }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }, token);

                _channel.MessagesReceived++;
                _channel.BytesReceived += length;
            }
        }

        public void Dispose()
        {
            _ws.Abort();
            _ws.Dispose();
            _cts.Dispose();
        }
    }
}
