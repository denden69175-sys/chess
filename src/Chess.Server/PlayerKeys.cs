using System.Security.Cryptography;
using System.Text;

namespace Chess.Server;

/// <summary>
/// Each player id belongs to whoever used it first: the game makes a secret key next to its id,
/// and the server keeps a hash of that key. Someone who learns another player's id (it is not
/// secret) cannot play under it, take their rating or spend their coins. Not thread-safe:
/// <see cref="GameServer"/> calls it under its lock.
/// </summary>
public sealed class PlayerKeys
{
    public const int MinLength = 16;
    public const int MaxLength = 128;

    private const string Table = "keys";
    private readonly Dictionary<string, string> _hashes = new();
    private readonly IStore? _store;

    public PlayerKeys(IStore? store = null)
    {
        _store = store;
        if (store == null)
            return;
        foreach (var (id, json) in store.Load(Table))
        {
            // Stored as a JSON string.
            string hash = json.Trim().Trim('"');
            if (hash.Length > 0)
                _hashes[id] = hash;
        }
    }

    public static bool IsWellFormed(string? secret) =>
        secret != null && secret.Length >= MinLength && secret.Length <= MaxLength && secret.All(c => c > ' ' && c < 127);

    /// <summary>
    /// True when the secret is the one this id was first used with; an id seen for the first time
    /// is taken by this secret.
    /// </summary>
    public bool Check(string id, string secret)
    {
        string hash = Hash(id, secret);
        if (_hashes.TryGetValue(id, out var known))
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(known), Encoding.ASCII.GetBytes(hash));
        _hashes[id] = hash;
        _store?.Save(Table, id, "\"" + hash + "\"");
        return true;
    }

    private static string Hash(string id, string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + ":" + secret)));
}
