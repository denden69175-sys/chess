using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Chess.Server;

/// <summary>
/// A player connected over a WebSocket. Outgoing messages go through a queue with one writer,
/// because a WebSocket allows only one send at a time and the game logic sends from any thread.
/// </summary>
public sealed class SocketPlayer : IPlayer
{
    private readonly WebSocket _socket;
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>();

    public SocketPlayer(WebSocket socket)
    {
        _socket = socket;
        _ = Task.Run(WriteLoopAsync);
    }

    public string Name { get; set; } = "Игрок";
    public string? Id { get; set; }

    public void Send(Message message) => _outgoing.Writer.TryWrite(message.ToJson());

    public void Close() => _outgoing.Writer.TryComplete();

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var text in _outgoing.Reader.ReadAllAsync())
            {
                if (_socket.State != WebSocketState.Open)
                    break;
                var bytes = Encoding.UTF8.GetBytes(text);
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
    }

    public async IAsyncEnumerable<string> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellation)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (_socket.State == WebSocketState.Open)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellation);
            if (result.MessageType == WebSocketMessageType.Close)
                yield break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage)
            {
                yield return text.ToString();
                text.Clear();
            }
        }
    }
}
