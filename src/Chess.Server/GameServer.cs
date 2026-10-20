using Chess.Core;

namespace Chess.Server;

/// <summary>A connected player as the game logic sees it; the WebSocket code implements it.</summary>
public interface IPlayer
{
    string Name { get; set; }

    /// <summary>The id the client keeps between launches, for the rating; null until it says hello.</summary>
    string? Id { get; set; }
    void Send(Message message);
}

/// <summary>
/// Matchmaking, private rooms and running matches. Transport-free so it can be tested without
/// sockets. All state changes happen under one lock: the server is small and games are slow.
/// </summary>
public sealed class GameServer
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lobby> _queue = new();
    private readonly Dictionary<string, Lobby> _rooms = new();
    private readonly Dictionary<IPlayer, Match> _matches = new();
    private readonly Random _random;
    private readonly Ratings _ratings;

    public GameServer(Random? random = null, Ratings? ratings = null)
    {
        _random = random ?? new Random();
        _ratings = ratings ?? new Ratings();
    }

    private sealed record Waiting(IPlayer Player, Army? Army);

    /// <summary>Players gathering for one game: the quick-match queue of a mode, or a private room.</summary>
    private sealed class Lobby
    {
        public Lobby(string mode, string? code)
        {
            Mode = mode;
            Code = code;
        }

        public string Mode { get; }
        public string? Code { get; }
        public List<Waiting> Members { get; } = new();
        public int Needed => Modes.PlayersIn(Mode);
        public bool Has(IPlayer player) => Members.Any(m => m.Player == player);
    }

    public void Connected(IPlayer player) =>
        player.Send(new Message { type = "welcome", message = "Сервер шахмат на связи." });

    public void Handle(IPlayer player, Message message)
    {
        lock (_gate)
        {
            switch (message.type)
            {
                case "hello":
                    Hello(player, message);
                    break;
                case "top":
                    SendTop(player);
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
                    Leave(player, "resign");
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
            Leave(player, "opponent_left");
        }
    }

    // ---------------------------------------------------------------- rating

    private void Hello(IPlayer player, Message message)
    {
        player.Name = Clean(message.name);
        string id = (message.id ?? "").Trim();
        if (id.Length < 8 || id.Length > 64 || !id.All(c => char.IsLetterOrDigit(c) || c == '-'))
            return;
        player.Id = id;
        SendRating(player, _ratings.Get(id, player.Name));
    }

    private static void SendRating(IPlayer player, RatingRecord record) =>
        player.Send(new Message { type = "rating", rating = record.Rating, games = record.Games, wins = record.Wins });

    /// <summary>The ten best players, one per line: name, rating, games, wins, separated by tabs.</summary>
    private void SendTop(IPlayer player)
    {
        var lines = _ratings.Top(10).Select(r => $"{r.Name}\t{r.Rating}\t{r.Games}\t{r.Wins}");
        player.Send(new Message { type = "top", list = string.Join("\n", lines) });
    }

    /// <summary>
    /// Quick-match games count for the rating; games in a private room do not, so friends cannot
    /// trade points. Everyone needs an id, and one person may not sit in two seats.
    /// </summary>
    private static bool IsRated(Lobby lobby) =>
        lobby.Code == null
        && lobby.Members.All(m => m.Player.Id != null)
        && lobby.Members.Select(m => m.Player.Id).Distinct().Count() == lobby.Members.Count;

    /// <summary>Each seat's rating change for the finished match, or null for an unrated one.</summary>
    private int[]? Rate(Match match)
    {
        if (!match.Rated)
            return null;
        int n = match.Players.Count;
        var ids = match.Players.Select(p => p.Id!).ToList();
        var ratings = ids.Select(id => (double)_ratings.Get(id).Rating).ToList();
        string result = match.Result;
        bool three = match.Mode == Modes.Three;
        var scores = new double?[n, n];
        var strength = new double[n];
        for (int i = 0; i < n; i++)
        {
            // In team games a player is judged by the team's average rating.
            strength[i] = three ? ratings[i] : Enumerable.Range(0, n).Where(j => j % 2 == i % 2).Average(j => ratings[j]);
            for (int j = 0; j < n; j++)
            {
                if (i == j || (!three && i % 2 == j % 2))
                    continue;
                if (result == "draw")
                    scores[i, j] = 0.5;
                else if (three)
                    scores[i, j] = result == "seat" + i ? 1 : result == "seat" + j ? 0 : 0.5;
                else
                    scores[i, j] = result == (i % 2 == 0 ? "white" : "black") ? 1 : 0;
            }
        }
        return _ratings.Apply(ids, scores, strength);
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
        if (!_queue.TryGetValue(mode, out var lobby))
            _queue[mode] = lobby = new Lobby(mode, null);
        lobby.Members.Add(new Waiting(player, army));
        if (lobby.Members.Count >= lobby.Needed)
        {
            _queue.Remove(mode);
            StartMatch(lobby);
            return;
        }
        Announce(lobby);
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
        var lobby = new Lobby(message.mode!, code);
        lobby.Members.Add(new Waiting(player, army));
        _rooms[code] = lobby;
        Announce(lobby);
    }

    private void Join(IPlayer player, Message message)
    {
        string code = (message.code ?? "").Trim();
        if (!_rooms.TryGetValue(code, out var lobby))
        {
            Error(player, "Комнаты с таким кодом нет.");
            return;
        }
        if (lobby.Has(player))
        {
            Error(player, "Вы уже в этой комнате: дождитесь остальных.");
            return;
        }
        // The guest plays the host's mode, whatever the guest's menu said.
        if (!ReadyToWait(player, new Message { mode = lobby.Mode, army = message.army }, out var army))
            return;
        lobby.Members.Add(new Waiting(player, army));
        if (lobby.Members.Count >= lobby.Needed)
        {
            _rooms.Remove(code);
            StartMatch(lobby);
            return;
        }
        Announce(lobby);
    }

    /// <summary>Tells everyone in a lobby how many players are there and how many the game needs.</summary>
    private static void Announce(Lobby lobby)
    {
        foreach (var member in lobby.Members)
        {
            member.Player.Send(new Message
            {
                type = lobby.Code == null ? "waiting" : "room",
                mode = lobby.Mode,
                code = lobby.Code,
                count = lobby.Members.Count,
                needed = lobby.Needed,
                names = string.Join("|", lobby.Members.Select(m => m.Player.Name)),
            });
        }
    }

    private void RemoveWaiting(IPlayer player)
    {
        foreach (var (key, lobby) in _queue.Where(p => p.Value.Has(player)).ToList())
            Drop(_queue, key, lobby, player);
        foreach (var (key, lobby) in _rooms.Where(p => p.Value.Has(player)).ToList())
            Drop(_rooms, key, lobby, player);
    }

    private static void Drop(Dictionary<string, Lobby> lobbies, string key, Lobby lobby, IPlayer player)
    {
        lobby.Members.RemoveAll(m => m.Player == player);
        if (lobby.Members.Count == 0)
            lobbies.Remove(key);
        else
            Announce(lobby);
    }

    private void StartMatch(Lobby lobby)
    {
        var seats = lobby.Members.ToList();
        if (seats.Count == 2)
        {
            if (_random.Next(2) != 0)
                seats.Reverse();
        }
        else
        {
            for (int i = seats.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (seats[i], seats[j]) = (seats[j], seats[i]);
            }
        }

        var white = seats[0];
        var black = seats[1];
        var match = new Match(lobby.Mode, seats.Select(s => s.Player).ToList(), white.Army, black.Army) { Rated = IsRated(lobby) };
        foreach (var seat in seats)
            _matches[seat.Player] = match;

        string names = string.Join("|", seats.Select(s => s.Player.Name));
        for (int i = 0; i < seats.Count; i++)
        {
            seats[i].Player.Send(new Message
            {
                type = "start",
                mode = lobby.Mode,
                seat = i,
                color = lobby.Mode == Modes.Three ? null : i % 2 == 0 ? "white" : "black",
                // In two-player modes "name" is the opponent; "names" lists every seat in order.
                name = seats.Count == 2 ? seats[1 - i].Player.Name : null,
                names = names,
                rated = match.Rated,
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
        int seat = match.SeatOf(player);
        if (match.CurrentSeat != seat)
        {
            Error(player, "Сейчас ход соперника.");
            return;
        }
        if (string.IsNullOrEmpty(action) || !match.TryPlay(action))
        {
            Error(player, "Такой ход невозможен.");
            return;
        }

        Broadcast(match, new Message { type = "move", action = action, seat = seat });
        if (match.IsOver)
            Finish(match, match.Reason);
    }

    /// <summary>
    /// A player resigned or lost the connection. In "Тройной трон" the other two play on and
    /// get an "out" message; in every other mode the player's side loses.
    /// </summary>
    private void Leave(IPlayer player, string reason)
    {
        if (!_matches.TryGetValue(player, out var match) || match.Finished)
            return;
        int seat = match.SeatOf(player);
        bool over = match.Leave(seat);
        if (over)
        {
            Finish(match, reason);
            return;
        }
        var notice = new Message { type = "out", seat = seat, reason = reason };
        Broadcast(match, notice);
        _matches.Remove(player);
    }

    /// <summary>Sends a message to every player still in the match.</summary>
    private void Broadcast(Match match, Message message)
    {
        foreach (var player in match.Players)
            if (_matches.TryGetValue(player, out var current) && current == match)
                player.Send(message);
    }

    private void Finish(Match match, string reason)
    {
        if (match.Finished)
            return;
        match.Finished = true;
        var deltas = Rate(match);
        for (int seat = 0; seat < match.Players.Count; seat++)
        {
            var player = match.Players[seat];
            if (!_matches.TryGetValue(player, out var current) || current != match)
                continue;
            var end = new Message { type = "end", result = match.Result, reason = reason };
            if (deltas != null)
            {
                var record = _ratings.Get(player.Id!);
                end.rating = record.Rating;
                end.delta = deltas[seat];
                end.games = record.Games;
                end.wins = record.Wins;
            }
            player.Send(end);
        }
        foreach (var player in match.Players)
            if (_matches.TryGetValue(player, out var current) && current == match)
                _matches.Remove(player);
    }

    private static void Error(IPlayer player, string text) =>
        player.Send(new Message { type = "error", message = text });

    private static string Clean(string? name)
    {
        string trimmed = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (trimmed.Length == 0)
            return "Игрок";
        return trimmed.Length > 20 ? trimmed.Substring(0, 20) : trimmed;
    }
}
