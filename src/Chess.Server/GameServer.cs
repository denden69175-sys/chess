using Chess.Core;

namespace Chess.Server;

/// <summary>A connected player as the game logic sees it; the WebSocket code implements it.</summary>
public interface IPlayer
{
    string Name { get; set; }
    void Send(Message message);
}

/// <summary>
/// Matchmaking, private rooms and running matches. Transport-free so it can be tested without
/// sockets. All state changes happen under one lock: the server is small and games are slow.
/// </summary>
public sealed class GameServer
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Waiting> _queue = new();
    private readonly Dictionary<string, Waiting> _rooms = new();
    private readonly Dictionary<IPlayer, Match> _matches = new();
    private readonly Random _random;

    public GameServer(Random? random = null)
    {
        _random = random ?? new Random();
    }

    private sealed record Waiting(IPlayer Player, string Mode, Army? Army, string? Code);

    public void Connected(IPlayer player) =>
        player.Send(new Message { type = "welcome", message = "Сервер шахмат на связи." });

    public void Handle(IPlayer player, Message message)
    {
        lock (_gate)
        {
            switch (message.type)
            {
                case "hello":
                    player.Name = Clean(message.name);
                    break;
                case "find":
                    Find(player, message);
                    break;
                case "create":
                    Create(player, message);
                    break;
                case "join":
                    Join(player, message);
                    break;
                case "cancel":
                    RemoveWaiting(player);
                    break;
                case "move":
                    Move(player, message.action);
                    break;
                case "resign":
                    Resign(player);
                    break;
                case "ping":
                    player.Send(new Message { type = "pong" });
                    break;
                default:
                    Error(player, "Неизвестная команда.");
                    break;
            }
        }
    }

    public void Disconnected(IPlayer player)
    {
        lock (_gate)
        {
            RemoveWaiting(player);
            if (_matches.TryGetValue(player, out var match))
            {
                var opponent = match.Opponent(player);
                Finish(match, match.ColorOf(opponent), "opponent_left");
            }
        }
    }

    // ---------------------------------------------------------------- matchmaking

    private bool ReadyToWait(IPlayer player, Message message, out Army? army)
    {
        army = null;
        if (_matches.TryGetValue(player, out var current) && !current.Finished)
        {
            Error(player, "Вы уже играете партию.");
            return false;
        }
        if (!Modes.IsKnown(message.mode))
        {
            Error(player, "Неизвестный режим.");
            return false;
        }
        if (message.mode == Modes.Army)
        {
            army = Army.FromCode(message.army);
            if (army == null || !army.IsValid)
            {
                Error(player, "Армия не подходит под правила: проверьте бюджет и короля.");
                return false;
            }
        }
        RemoveWaiting(player);
        return true;
    }

    private void Find(IPlayer player, Message message)
    {
        if (!ReadyToWait(player, message, out var army))
            return;
        string mode = message.mode!;
        if (_queue.TryGetValue(mode, out var other) && other.Player != player)
        {
            _queue.Remove(mode);
            StartMatch(mode, other, new Waiting(player, mode, army, null));
            return;
        }
        _queue[mode] = new Waiting(player, mode, army, null);
        player.Send(new Message { type = "waiting", mode = mode });
    }

    private void Create(IPlayer player, Message message)
    {
        if (!ReadyToWait(player, message, out var army))
            return;
        string code;
        do
        {
            code = _random.Next(1000, 10000).ToString();
        } while (_rooms.ContainsKey(code));
        _rooms[code] = new Waiting(player, message.mode!, army, code);
        player.Send(new Message { type = "room", code = code, mode = message.mode });
    }

    private void Join(IPlayer player, Message message)
    {
        string code = (message.code ?? "").Trim();
        if (!_rooms.TryGetValue(code, out var host))
        {
            Error(player, "Комнаты с таким кодом нет.");
            return;
        }
        if (host.Player == player)
        {
            Error(player, "Это ваша комната: дождитесь соперника.");
            return;
        }
        // The guest plays the host's mode, whatever the guest's menu said.
        if (!ReadyToWait(player, new Message { mode = host.Mode, army = message.army }, out var army))
            return;
        _rooms.Remove(code);
        StartMatch(host.Mode, host, new Waiting(player, host.Mode, army, null));
    }

    private void RemoveWaiting(IPlayer player)
    {
        foreach (var key in _queue.Where(p => p.Value.Player == player).Select(p => p.Key).ToList())
            _queue.Remove(key);
        foreach (var key in _rooms.Where(p => p.Value.Player == player).Select(p => p.Key).ToList())
            _rooms.Remove(key);
    }

    private void StartMatch(string mode, Waiting first, Waiting second)
    {
        bool firstIsWhite = _random.Next(2) == 0;
        var white = firstIsWhite ? first : second;
        var black = firstIsWhite ? second : first;

        var match = new Match(mode, white.Player, black.Player, white.Army, black.Army);
        _matches[white.Player] = match;
        _matches[black.Player] = match;

        foreach (var (me, them, color) in new[] { (white, black, "white"), (black, white, "black") })
        {
            me.Player.Send(new Message
            {
                type = "start",
                mode = mode,
                color = color,
                name = them.Player.Name,
                whiteArmy = white.Army?.ToCode(),
                blackArmy = black.Army?.ToCode(),
            });
        }
    }

    // ---------------------------------------------------------------- play

    private void Move(IPlayer player, string? action)
    {
        if (!_matches.TryGetValue(player, out var match) || match.Finished)
        {
            Error(player, "Партия не идёт.");
            return;
        }
        if (match.SideToMove != match.ColorOf(player))
        {
            Error(player, "Сейчас ход соперника.");
            return;
        }
        if (string.IsNullOrEmpty(action) || !match.TryPlay(action))
        {
            Error(player, "Такой ход невозможен.");
            return;
        }

        var echo = new Message { type = "move", action = action };
        match.White.Send(echo);
        match.Black.Send(echo);

        if (match.Status != GameStatus.Ongoing)
        {
            var winner = match.Winner;
            Finish(match, winner, match.Status switch
            {
                GameStatus.Checkmate => "checkmate",
                GameStatus.Stalemate => "stalemate",
                GameStatus.FiftyMoveRule => "fifty_moves",
                GameStatus.ThreefoldRepetition => "repetition",
                _ => "insufficient_material",
            });
        }
    }

    private void Resign(IPlayer player)
    {
        if (_matches.TryGetValue(player, out var match) && !match.Finished)
            Finish(match, match.ColorOf(match.Opponent(player)), "resign");
    }

    private void Finish(Match match, PieceColor? winner, string reason)
    {
        if (match.Finished)
            return;
        match.Finished = true;
        var end = new Message
        {
            type = "end",
            result = winner == null ? "draw" : winner == PieceColor.White ? "white" : "black",
            reason = reason,
        };
        match.White.Send(end);
        match.Black.Send(end);
        _matches.Remove(match.White);
        _matches.Remove(match.Black);
    }

    private static void Error(IPlayer player, string text) =>
        player.Send(new Message { type = "error", message = text });

    private static string Clean(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return "Игрок";
        return trimmed.Length > 20 ? trimmed.Substring(0, 20) : trimmed;
    }
}
