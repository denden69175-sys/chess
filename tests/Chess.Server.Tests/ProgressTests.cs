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
    }

    private static readonly JsonSerializerOptions Fields = new() { IncludeFields = true };

    private static PlayerProgress Read(Message message) => JsonSerializer.Deserialize<PlayerProgress>(message.data!, Fields)!;

    private static FakePlayer Hello(GameServer server, string id = "player-0001")
    {
        var player = new FakePlayer();
        server.Handle(player, new Message { type = "hello", name = "Аня", id = id });
        return player;
    }

    private static void Step(GameServer server, IPlayer player, ProgressAction action) =>
        server.Handle(player, new Message { type = "progress", action = "step", data = JsonSerializer.Serialize(action, Fields) });

    [Fact]
    public void TheFirstSyncKeepsTheGamesOwnProgress()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var local = new PlayerProgress { coins = 120 };
        local.owned.Add("ruby");
        server.Handle(player, new Message { type = "progress", action = "sync", data = JsonSerializer.Serialize(local, Fields) });
        Assert.Equal(120, Read(player.Last).coins);

        // Later the server's copy wins: a game cannot just announce more coins.
        local.coins = 99999;
        server.Handle(player, new Message { type = "progress", action = "sync", data = JsonSerializer.Serialize(local, Fields) });
        Assert.Equal(120, Read(player.Last).coins);
        Assert.Contains("ruby", Read(player.Last).owned);
    }

    [Fact]
    public void StepsCountOnceEvenWhenSentTwice()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var puzzle = PuzzleBook.All[0];
        var step = new ProgressAction { seq = 1, kind = ProgressAction.Puzzle, number = puzzle.Number };
        Step(server, player, step);
        Step(server, player, step);
        Assert.Equal(puzzle.Reward, Read(player.Last).coins);
        Assert.Equal(1, player.Last.seq);
    }

    [Fact]
    public void TheServerRollsChestsAndChecksTheRules()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        // No coins: buying does nothing, and so does opening a chest the player does not have.
        Step(server, player, new ProgressAction { seq = 1, kind = ProgressAction.Buy, number = (int)ChestType.Common });
        Step(server, player, new ProgressAction { seq = 2, kind = ProgressAction.Open, number = (int)ChestType.Common });
        Assert.Equal(0, Read(player.Last).chestsOpened);
        Assert.Null(player.Last.skin);

        var local = new PlayerProgress();
        local.chests[(int)ChestType.Rare] = 1;
        var other = Hello(server, "player-0002");
        server.Handle(other, new Message { type = "progress", action = "sync", data = JsonSerializer.Serialize(local, Fields) });
        Step(server, other, new ProgressAction { seq = 1, kind = ProgressAction.Open, number = (int)ChestType.Rare });
        var answer = other.Last;
        Assert.NotNull(answer.skin);
        Assert.False(answer.duplicate);
        Assert.Contains(answer.skin!, Read(answer).owned);
        Assert.Equal(0, Read(answer).chests[(int)ChestType.Rare]);
    }

    [Fact]
    public void AChestOpenedOfflineKeepsItsSkin()
    {
        var server = new GameServer(new Random(1));
        var player = Hello(server);
        var local = new PlayerProgress();
        local.chests[(int)ChestType.Epic] = 1;
        server.Handle(player, new Message { type = "progress", action = "sync", data = JsonSerializer.Serialize(local, Fields) });
        Step(server, player, new ProgressAction { seq = 1, kind = ProgressAction.Open, number = (int)ChestType.Epic, skin = "lava" });
        Step(server, player, new ProgressAction { seq = 2, kind = ProgressAction.Equip, item = "lava" });
        var progress = Read(player.Last);
        Assert.Contains("lava", progress.owned);
        Assert.Equal("lava", progress.pieces);
    }

    [Fact]
    public void ProgressNeedsAPlayerId()
    {
        var server = new GameServer();
        var player = new FakePlayer();
        server.Handle(player, new Message { type = "progress", action = "sync", data = "{}" });
        Assert.Equal("error", player.Inbox.Last().type);
    }

    [Fact]
    public void ProgressSurvivesARestart()
    {
        string folder = Path.Combine(Path.GetTempPath(), "progress-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(folder);
            var server = new GameServer(new Random(1), new Ratings(store), new ProgressBook(store));
            var player = Hello(server);
            Step(server, player, new ProgressAction { seq = 1, kind = ProgressAction.Game, mode = "classic", won = 1, bot = 0 });
            store.Flush();

            var again = new GameServer(null, null, new ProgressBook(new FileStore(folder)));
            var back = Hello(again);
            again.Handle(back, new Message { type = "progress", action = "sync", data = "{}" });
            Assert.Equal(1, back.Last.seq);
            Assert.Equal(1, Read(back.Last).Total(Goal.WinBotEasy));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
