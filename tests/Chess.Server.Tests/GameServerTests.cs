using Chess.Core;
using Chess.Server;
using Xunit;

namespace Chess.Server.Tests;

public class GameServerTests
{
    private sealed class FakePlayer : IPlayer
    {
        public FakePlayer(string name) => Name = name;
        public string Name { get; set; }
        public List<Message> Inbox { get; } = new();
        public void Send(Message message) => Inbox.Add(message);
        public Message Last(string type) => Inbox.Last(m => m.type == type);
        public bool Got(string type) => Inbox.Any(m => m.type == type);
    }

    private static (GameServer server, FakePlayer white, FakePlayer black) StartClassic(string mode = Modes.Classic)
    {
        var server = new GameServer(new Random(1));
        var a = new FakePlayer("Аня");
        var b = new FakePlayer("Боря");
        server.Handle(a, new Message { type = "find", mode = mode });
        server.Handle(b, new Message { type = "find", mode = mode });
        bool aWhite = a.Last("start").color == "white";
        return (server, aWhite ? a : b, aWhite ? b : a);
    }

    [Fact]
    public void QuickMatchPairsTwoPlayersWithOppositeColors()
    {
        var server = new GameServer(new Random(1));
        var a = new FakePlayer("Аня");
        var b = new FakePlayer("Боря");
        server.Handle(a, new Message { type = "find", mode = Modes.Classic });
        Assert.Equal("waiting", a.Inbox.Last().type);

        server.Handle(b, new Message { type = "find", mode = Modes.Classic });
        var startA = a.Last("start");
        var startB = b.Last("start");
        Assert.NotEqual(startA.color, startB.color);
        Assert.Equal("Боря", startA.name);
        Assert.Equal("Аня", startB.name);
    }

    [Fact]
    public void DifferentModesDoNotMatch()
    {
        var server = new GameServer();
        var a = new FakePlayer("a");
        var b = new FakePlayer("b");
        server.Handle(a, new Message { type = "find", mode = Modes.Classic });
        server.Handle(b, new Message { type = "find", mode = Modes.TimeTravel });
        Assert.False(a.Got("start"));
        Assert.False(b.Got("start"));
    }

    [Fact]
    public void LegalMoveIsEchoedToBoth()
    {
        var (server, white, black) = StartClassic();
        server.Handle(white, new Message { type = "move", action = "e2e4" });
        Assert.Equal("e2e4", white.Last("move").action);
        Assert.Equal("e2e4", black.Last("move").action);
    }

    [Fact]
    public void IllegalOrOutOfTurnMovesAreRejected()
    {
        var (server, white, black) = StartClassic();
        server.Handle(black, new Message { type = "move", action = "e7e5" });
        Assert.Equal("error", black.Inbox.Last().type);
        server.Handle(white, new Message { type = "move", action = "e2e5" });
        Assert.Equal("error", white.Inbox.Last().type);
        Assert.False(black.Got("move"));
    }

    [Fact]
    public void CheckmateEndsTheGame()
    {
        var (server, white, black) = StartClassic();
        foreach (var (p, m) in new[] { (white, "f2f3"), (black, "e7e5"), (white, "g2g4"), (black, "d8h4") })
            server.Handle(p, new Message { type = "move", action = m });
        var end = white.Last("end");
        Assert.Equal("black", end.result);
        Assert.Equal("checkmate", end.reason);
        Assert.True(black.Got("end"));
    }

    [Fact]
    public void LeavingGivesTheWinToTheOpponent()
    {
        var (server, white, black) = StartClassic();
        server.Disconnected(white);
        var end = black.Last("end");
        Assert.Equal("black", end.result);
        Assert.Equal("opponent_left", end.reason);
    }

    [Fact]
    public void ResignEndsTheGame()
    {
        var (server, white, black) = StartClassic();
        server.Handle(black, new Message { type = "resign" });
        Assert.Equal("white", white.Last("end").result);
    }

    [Fact]
    public void RoomCodeLetsAFriendJoin()
    {
        var server = new GameServer(new Random(3));
        var host = new FakePlayer("host");
        var guest = new FakePlayer("guest");
        server.Handle(host, new Message { type = "create", mode = Modes.TimeTravel });
        string code = host.Last("room").code!;
        Assert.Equal(4, code.Length);

        server.Handle(guest, new Message { type = "join", code = "0000" });
        Assert.Equal("error", guest.Inbox.Last().type);

        server.Handle(guest, new Message { type = "join", code = code });
        Assert.Equal(Modes.TimeTravel, guest.Last("start").mode);
        Assert.True(host.Got("start"));
    }

    [Fact]
    public void ArmyModeChecksTheBudgetAndSendsBothArmies()
    {
        var server = new GameServer(new Random(1));
        var a = new FakePlayer("a");
        var b = new FakePlayer("b");
        var queens = Army.Presets.First(p => p.Name == "Четыре ферзя").Army.ToCode();

        server.Handle(a, new Message { type = "find", mode = Modes.Army, army = "мусор" });
        Assert.Equal("error", a.Inbox.Last().type);

        server.Handle(a, new Message { type = "find", mode = Modes.Army, army = queens });
        server.Handle(b, new Message { type = "find", mode = Modes.Army, army = Army.Classic.ToCode() });
        var start = a.Last("start");
        Assert.NotNull(start.whiteArmy);
        Assert.NotNull(start.blackArmy);
        Assert.Contains(queens, new[] { start.whiteArmy, start.blackArmy });
    }

    [Fact]
    public void TimeTravelJumpIsAcceptedOnline()
    {
        var (server, white, black) = StartClassic(Modes.TimeTravel);
        foreach (var (p, m) in new[] { (white, "e2e4"), (black, "e7e5"), (white, "g1f3"), (black, "b8c6") })
            server.Handle(p, new Message { type = "move", action = m });
        server.Handle(white, new Message { type = "move", action = "jump:f3:1" });
        Assert.Equal("jump:f3:1", black.Last("move").action);
    }
}
