using System.Net.WebSockets;
using System.Text;
using Chess.Server;

var builder = WebApplication.CreateBuilder(args);

// Hosting services such as Render pass the port in PORT; locally the server listens on 5080,
// which is what the Unity client tries by default.
string port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") == null)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Ratings and progress go to PostgreSQL when DATABASE_URL is set (needed on Render, whose disk is
// wiped on restart), otherwise to JSON files next to the server or in DATA_DIR.
string? database = Environment.GetEnvironmentVariable("DATABASE_URL");
IStore store = !string.IsNullOrWhiteSpace(database)
    ? new PostgresStore(database)
    : new FileStore(Environment.GetEnvironmentVariable("DATA_DIR") ?? AppContext.BaseDirectory);
Console.WriteLine(store is PostgresStore ? "Saving to the database." : "Saving to files.");
builder.Services.AddSingleton(new GameServer(null, new Ratings(store), new ProgressBook(store)));
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/", () => "Chess server is running. Connect to /ws.");

app.Map("/ws", async (HttpContext context, GameServer server) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var player = new SocketPlayer(socket);
    server.Connected(player);
    try
    {
        await foreach (var text in player.ReadMessagesAsync(context.RequestAborted))
        {
            var message = Message.FromJson(text);
            if (message != null)
                server.Handle(player, message);
        }
    }
    catch (WebSocketException)
    {
        // The client dropped the connection; treated like a normal disconnect below.
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        server.Disconnected(player);
        player.Close();
    }
});

app.Run();

public partial class Program
{
}
