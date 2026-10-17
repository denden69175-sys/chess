using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chess.Server;

/// <summary>
/// One message in either direction. Every message has a <see cref="type"/>; the other fields are
/// filled only where that type needs them. Lower-case names match the Unity client, which reads
/// and writes these with JsonUtility.
/// </summary>
public sealed class Message
{
    // Client to server: hello, find, create, join, cancel, move, resign, ping.
    // Server to client: welcome, waiting, room, start, move, end, error, pong.
    public string type { get; set; } = "";

    /// <summary>Player name (hello) or the opponent's name (start).</summary>
    public string? name { get; set; }

    /// <summary>"classic", "army" or "timetravel".</summary>
    public string? mode { get; set; }

    /// <summary>Private room code (room, join).</summary>
    public string? code { get; set; }

    /// <summary>A move ("e2e4", "e7e8q") or a jump ("jump:e4:2").</summary>
    public string? action { get; set; }

    /// <summary>"white" or "black" (start).</summary>
    public string? color { get; set; }

    /// <summary>The sender's army (find, create, join in army mode).</summary>
    public string? army { get; set; }

    public string? whiteArmy { get; set; }
    public string? blackArmy { get; set; }

    /// <summary>"white", "black" or "draw" (end).</summary>
    public string? result { get; set; }

    /// <summary>Why the game ended: checkmate, stalemate, resign, opponent_left, draw rules.</summary>
    public string? reason { get; set; }

    /// <summary>Human-readable text for errors, in Russian.</summary>
    public string? message { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, MessageJson.Default.Message);

    public static Message? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, MessageJson.Default.Message);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Compile-time JSON code for <see cref="Message"/>, so the server also works as a trimmed
/// single-file program, where reflection-based JSON is switched off.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Message))]
internal partial class MessageJson : JsonSerializerContext
{
}
