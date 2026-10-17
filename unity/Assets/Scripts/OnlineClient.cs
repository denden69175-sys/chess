#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// One message to or from the game server. Field names match the server's JSON. JsonUtility
/// writes empty strings for unused fields, which the server ignores.
/// </summary>
[Serializable]
public class NetMessage
{
    public string type = "";
    public string name = "";
    public string mode = "";
    public string code = "";
    public string action = "";
    public string color = "";
    public string army = "";
    public string whiteArmy = "";
    public string blackArmy = "";
    public string result = "";
    public string reason = "";
    public string message = "";
}

/// <summary>
/// WebSocket connection to the game server. Network work runs on background threads; the game
/// reads incoming messages on the main thread with <see cref="TryReceive"/>. When the connection
/// fails or closes, a message of type "closed" is delivered.
/// </summary>
public sealed class OnlineClient : IDisposable
{
    public const string Closed = "closed";

    private readonly ClientWebSocket _socket = new ClientWebSocket();
    private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
    private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    public OnlineClient(string url)
    {
        Url = url;
        Task.Run(Run);
    }

    public string Url { get; }

    public bool IsOpen => _socket.State == WebSocketState.Open;

    public void Send(NetMessage message)
    {
        string json = JsonUtility.ToJson(message);
        Task.Run(async () =>
        {
            await _sendLock.WaitAsync();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancel.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Send failed: " + e.Message);
            }
            finally
            {
                _sendLock.Release();
            }
        });
    }

    public bool TryReceive(out NetMessage message)
    {
        message = new NetMessage();
        if (!_incoming.TryDequeue(out var json))
            return false;
        try
        {
            message = JsonUtility.FromJson<NetMessage>(json) ?? new NetMessage();
        }
        catch (Exception)
        {
            message = new NetMessage { type = "error", message = "Сервер прислал непонятный ответ." };
        }
        return true;
    }

    private async Task Run()
    {
        string reason = "Соединение закрыто.";
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await _socket.ConnectAsync(new Uri(Url), timeout.Token);
            }

            var buffer = new byte[8192];
            while (_socket.State == WebSocketState.Open)
            {
                using (var text = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        text.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    _incoming.Enqueue(Encoding.UTF8.GetString(text.ToArray()));
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = "Не удалось подключиться: сервер не отвечает.";
        }
        catch (Exception e)
        {
            reason = "Нет связи с сервером. " + e.Message;
            Debug.LogWarning("Online connection: " + e);
        }

        if (!_cancel.IsCancellationRequested)
            _incoming.Enqueue(JsonUtility.ToJson(new NetMessage { type = Closed, message = reason }));
    }

    public void Dispose()
    {
        _cancel.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
                _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (Exception)
        {
            // Closing is best effort; the server treats a dropped socket the same way.
        }
    }
}
