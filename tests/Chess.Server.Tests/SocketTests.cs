using System.Net.WebSockets;
using System.Text;
using Chess.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chess.Server.Tests;

public class SocketTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SocketTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<WebSocket> Connect()
    {
        var client = _factory.Server.CreateWebSocketClient();
        return await client.ConnectAsync(new Uri(_factory.Server.BaseAddress, "ws"), CancellationToken.None);
    }

    private static Task Send(WebSocket socket, Message message) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJson()), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<Message> Receive(WebSocket socket, string type)
    {
        var buffer = new byte[8192];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            var message = Message.FromJson(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
            if (message.type == type)
                return message;
        }
    }

    [Fact]
    public async Task TwoClientsPlayOverWebSockets()
    {
        var a = await Connect();
        var b = await Connect();
        await Receive(a, "welcome");
        await Receive(b, "welcome");
        await Send(a, new Message { type = "hello", name = "Аня" });
        await Send(b, new Message { type = "hello", name = "Боря" });
        await Send(a, new Message { type = "create", mode = Modes.Classic });
        var room = await Receive(a, "room");
        await Send(b, new Message { type = "join", code = room.code });

        var startA = await Receive(a, "start");
        var startB = await Receive(b, "start");
        Assert.Equal("Боря", startA.name);
        var white = startA.color == "white" ? a : b;
        var black = white == a ? b : a;

        await Send(white, new Message { type = "move", action = "e2e4" });
        Assert.Equal("e2e4", (await Receive(black, "move")).action);

        await white.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        Assert.Equal("opponent_left", (await Receive(black, "end")).reason);
    }

    [Fact]
    public async Task RootPageAnswers()
    {
        var text = await _factory.CreateClient().GetStringAsync("/");
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
