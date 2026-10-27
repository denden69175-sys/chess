using System.Text.Json;
using Chess.Core;
using Chess.Server;
using Xunit;

namespace Chess.Server.Tests;

public class ProgressTests
{
    private sealed class FakePlayer : IPlayer
    {
        public string Name { get; set; } = "Игрок";
        public string? Id { get; set; }
        public List<Message> Inbox { get; } = new();
        public void Send(Message message) => Inbox.Add(message);
        public Message Last => Inbox.Last(m => m.type == "progress");
        public Message LastOf(string type) => Inbox.Last(m => m.type == type);
    }

    private static readonly JsonSerializerOptions Fields = new() { IncludeFields = true };

    /// <summary>Fool's mate: Black mates on move two.</summary>
    private const string FoolsMate = "f2f3\ne7e5\ng2g4\nd8h4";

    private static PlayerProgress Read(Message message) => JsonSerializer.Deserialize<PlayerProgress>(message.data!, Fields)!;

    private static FakePlayer Hello(GameServer server, string id = "player-0001", string secret = "my-secret-key-0001")
    {
        var player = new FakePlayer();
        server.Handle(player, new Message { type = "hello", name = "Аня", id = id, secret = secret });
        return player;
    }

    private static void Step(GameServer server, IPlayer player, ProgressAction action) =>
        server.Handle(player, new Message { type = "progress", action = "step", data = JsonSerializer.Serialize(action, Fields) });

    private static void Sync(GameServer server, IPlayer player, PlayerProgress? local = null) =>
        server.Handle(player, new Message { type = "progress", action = "sync", data = JsonSerializer.Serialize(local ?? new PlayerProgress(), Fields) });

    private static ProgressAction BotWin(int seq) => new()
    {
        seq = seq, kind = ProgressAction.Game, mode = Modes.Classic, won = 1, bot = 0, seat = 1, actions = FoolsMate,
    };

    [Fact]
    public void TheGamesOwnProgressIsNeverTaken()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var local = new PlayerProgress { coins = 99999 };
        local.owned.Add("ruby");
        Sync(server, player, local);
        Assert.Equal(0, Read(player.Last).coins);
        Assert.DoesNotContain("ruby", Read(player.Last).owned);
    }

    [Fact]
    public void StepsCountOnceEvenWhenSentTwice()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var puzzle = PuzzleBook.All.First(p => p.MateIn == 1);
        var step = new ProgressAction { seq = 1, kind = ProgressAction.Puzzle, number = puzzle.Number, actions = puzzle.Solution };
        Step(server, player, step);
        Step(server, player, step);
        Assert.Equal(puzzle.Reward, Read(player.Last).coins);
        Assert.Equal(1, player.Last.seq);
    }

    [Fact]
    public void APuzzleCountsOnlyWithAMatingLine()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var puzzle = PuzzleBook.All.First(p => p.MateIn == 1);
        // No moves, and a legal move that is not mate: neither pays.
        Step(server, player, new ProgressAction { seq = 1, kind = ProgressAction.Puzzle, number = puzzle.Number });
        var notMate = new Game(puzzle.Start).LegalMoves.First(m => !MateSolver.IsCheckmate(puzzle.Start.Apply(m)));
        Step(server, player, new ProgressAction { seq = 2, kind = ProgressAction.Puzzle, number = puzzle.Number, actions = notMate.ToUci() });
        Assert.Equal(0, Read(player.Last).coins);
        // The queue still moves on past the rejected steps.
        Assert.Equal(2, player.Last.seq);
    }

    [Fact]
    public void AGameCountsOnlyWhatItsMovesShow()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);

        // Claims a win but the moves show White (the player's seat) being mated: a loss.
        Step(server, player, new ProgressAction
        {
            seq = 1, kind = ProgressAction.Game, mode = Modes.Classic, won = 1, bot = 2, seat = 0, captures = 64, actions = FoolsMate,
        });
        var progress = Read(player.Last);
        Assert.Equal(1, progress.Total(Goal.PlayGames));
        Assert.Equal(0, progress.Total(Goal.WinGames));
        Assert.Equal(0, progress.Total(Goal.Captures));

        // An illegal move: nothing counts.
        Step(server, player, new ProgressAction { seq = 2, kind = ProgressAction.Game, mode = Modes.Classic, won = 1, bot = 2, seat = 1, actions = "e2e5" });
        Assert.Equal(1, Read(player.Last).Total(Goal.PlayGames));

        // A real win counts.
        Step(server, player, BotWin(3));
        Assert.Equal(1, Read(player.Last).Total(Goal.WinBotEasy));
    }

    [Fact]
    public void AGameCannotClaimToBeOnline()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var step = BotWin(1);
        step.online = true;
        Step(server, player, step);
        Assert.Equal(0, Read(player.Last).Total(Goal.WinOnline));
        Assert.Equal(0, Read(player.Last).Total(Goal.PlayGames));
    }

    [Fact]
    public void OnlyTheFirstGamesOfADayCount()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        for (int i = 1; i <= ProgressBook.GamesPerDay + 5; i++)
            Step(server, player, BotWin(i));
        Assert.Equal(ProgressBook.GamesPerDay, Read(player.Last).Total(Goal.PlayGames));
    }

    [Fact]
    public void OnlineGamesAreCountedByTheServer()
    {
        var server = new GameServer(new Random(1));
        var a = Hello(server, "player-000a", "secret-a-0000000000");
        var b = Hello(server, "player-000b", "secret-b-0000000000");
        server.Handle(a, new Message { type = "find", mode = Modes.Classic });
        server.Handle(b, new Message { type = "find", mode = Modes.Classic });
        var white = a.LastOf("start").color == "white" ? a : b;
        var black = white == a ? b : a;
        foreach (var (who, move) in new[] { (white, "f2f3"), (black, "e7e5"), (white, "g2g4"), (black, "d8h4") })
            server.Handle(who, new Message { type = "move", action = move });

        Assert.Equal(1, Read(black.Last).Total(Goal.WinOnline));
        Assert.Equal(1, Read(white.Last).Total(Goal.PlayGames));
        Assert.Equal(0, Read(white.Last).Total(Goal.WinGames));
    }

    [Fact]
    public void ChestsAreRolledByTheServerUnderItsRules()
    {
        var book = new ProgressBook(null, new Random(1));
        var server = new GameServer(new Random(1), null, book);
        var player = Hello(server);
        // No coins: buying does nothing, and so does opening a chest the player does not have.
        Step(server, player, new ProgressAction { seq = 1, kind = ProgressAction.Buy, number = (int)ChestType.Common });
        Step(server, player, new ProgressAction { seq = 2, kind = ProgressAction.Open, number = (int)ChestType.Common });
        Assert.Equal(0, Read(player.Last).chestsOpened);
        Assert.Null(player.Last.skin);

        book.Find("player-0001")!.Progress.chests[(int)ChestType.Rare] = 1;
        Step(server, player, new ProgressAction { seq = 3, kind = ProgressAction.Open, number = (int)ChestType.Rare });
        var answer = player.Last;
        Assert.NotNull(answer.skin);
        Assert.Contains(answer.skin!, Read(answer).owned);
        Assert.Equal(0, Read(answer).chests[(int)ChestType.Rare]);
    }

    [Fact]
    public void ProgressNeedsAPlayerId()
    {
        var server = new GameServer();
        var player = new FakePlayer();
        Sync(server, player);
        Assert.Equal("error", player.Inbox.Last().type);
    }

    [Fact]
    public void AnIdBelongsToTheFirstSecret()
    {
        var server = new GameServer();
        var owner = Hello(server);
        Assert.Equal("player-0001", owner.Id);

        var thief = Hello(server, "player-0001", "someone-elses-key-1");
        Assert.Null(thief.Id);
        Assert.Equal("error", thief.Inbox.Last().type);

        var oldGame = new FakePlayer();
        server.Handle(oldGame, new Message { type = "hello", name = "x", id = "player-0002" });
        Assert.Null(oldGame.Id);

        Assert.Equal("player-0001", Hello(server).Id);
    }

    [Fact]
    public void FloodingIsCutOff()
    {
        var server = new GameServer();
        var player = new FakePlayer();
        for (int i = 0; i < 300; i++)
            server.Handle(player, new Message { type = "ping" });
        int pongs = player.Inbox.Count(m => m.type == "pong");
        Assert.InRange(pongs, (int)GameServer.MessageBurst, (int)GameServer.MessageBurst + 5);
        Assert.Single(player.Inbox, m => m.type == "error");
    }

    [Fact]
    public void ProgressAndKeysSurviveARestart()
    {
        string folder = Path.Combine(Path.GetTempPath(), "progress-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(folder);
            var server = new GameServer(new Random(1), new Ratings(store), new ProgressBook(store), new PlayerKeys(store));
            var player = Hello(server);
            Step(server, player, BotWin(1));
            store.Flush();

            var again = new GameServer(null, null, new ProgressBook(new FileStore(folder)), new PlayerKeys(new FileStore(folder)));
            Assert.Null(Hello(again, "player-0001", "a-different-key-0001").Id);
            var back = Hello(again);
            Sync(again, back);
            Assert.Equal(1, back.Last.seq);
            Assert.Equal(1, Read(back.Last).Total(Goal.WinBotEasy));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
