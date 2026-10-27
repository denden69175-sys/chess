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
        public string? Id { get; set; }
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

    [Fact]
    public void FiveDTurnsArriveWhole()
    {
        var (server, white, black) = StartClassic(Modes.FiveD);
        server.Handle(white, new Message { type = "move", action = "0,0,g1>0,0,f3" });
        server.Handle(black, new Message { type = "move", action = "0,1,e7>0,1,e6" });
        // The knight jumps one turn back and starts a new timeline.
        server.Handle(white, new Message { type = "move", action = "0,2,f3>0,0,h3" });
        Assert.Equal("0,2,f3>0,0,h3", black.Last("move").action);

        // Black has to move on the new timeline; a turn that skips it is refused.
        server.Handle(black, new Message { type = "move", action = "0,3,e6>0,3,e5" });
        Assert.Equal("error", black.Inbox.Last().type);
        server.Handle(black, new Message { type = "move", action = "1,1,e7>1,1,e5;0,3,d7>0,3,d6" });
        Assert.Equal("1,1,e7>1,1,e5;0,3,d7>0,3,d6", white.Last("move").action);
    }

    private static (GameServer server, FakePlayer[] seats) StartParty(string mode, int count)
    {
        var server = new GameServer(new Random(5));
        var players = Enumerable.Range(0, count).Select(i => new FakePlayer("p" + i)).ToArray();
        foreach (var p in players)
            server.Handle(p, new Message { type = "find", mode = mode });
        var seats = new FakePlayer[count];
        foreach (var p in players)
            seats[p.Last("start").seat!.Value] = p;
        return (server, seats);
    }

    [Fact]
    public void TeamGameWaitsForFourPlayers()
    {
        var server = new GameServer(new Random(5));
        var players = Enumerable.Range(0, 4).Select(i => new FakePlayer("p" + i)).ToArray();
        for (int i = 0; i < 3; i++)
            server.Handle(players[i], new Message { type = "find", mode = Modes.Team });
        Assert.Equal(3, players[0].Last("waiting").count);
        Assert.Equal(4, players[0].Last("waiting").needed);
        Assert.False(players[0].Got("start"));

        server.Handle(players[3], new Message { type = "find", mode = Modes.Team });
        Assert.Equal(new[] { 0, 1, 2, 3 }, players.Select(p => p.Last("start").seat!.Value).OrderBy(x => x));
        Assert.Equal(4, players[0].Last("start").names!.Split('|').Length);
    }

    [Fact]
    public void TeamPlayersMoveOnlyOnTheirTurnAndFlank()
    {
        var (server, seats) = StartParty(Modes.Team, 4);
        // White 1 commands files a–d, so the e-pawn is not theirs.
        server.Handle(seats[0], new Message { type = "move", action = "e2e4" });
        Assert.Equal("error", seats[0].Inbox.Last().type);
        server.Handle(seats[2], new Message { type = "move", action = "e2e4" });
        Assert.Equal("error", seats[2].Inbox.Last().type);

        server.Handle(seats[0], new Message { type = "move", action = "d2d4" });
        Assert.All(seats, p => Assert.Equal("d2d4", p.Last("move").action));
        server.Handle(seats[1], new Message { type = "move", action = "d7d5" });
        server.Handle(seats[2], new Message { type = "move", action = "e2e4" });
        Assert.Equal(2, seats[3].Last("move").seat);
    }

    [Fact]
    public void LeavingATeamGameLosesItForTheTeam()
    {
        var (server, seats) = StartParty(Modes.Team, 4);
        server.Disconnected(seats[3]);
        Assert.Equal("white", seats[0].Last("end").result);
        Assert.Equal("white", seats[1].Last("end").result);
    }

    [Fact]
    public void RoomForThreeStartsWhenTheThirdJoins()
    {
        var server = new GameServer(new Random(3));
        var host = new FakePlayer("host");
        var a = new FakePlayer("a");
        var b = new FakePlayer("b");
        server.Handle(host, new Message { type = "create", mode = Modes.Three });
        string code = host.Last("room").code!;
        server.Handle(a, new Message { type = "join", code = code });
        Assert.Equal(2, host.Last("room").count);
        Assert.Equal(3, a.Last("room").needed);
        Assert.False(host.Got("start"));
        server.Handle(b, new Message { type = "join", code = code });
        Assert.Equal(Modes.Three, b.Last("start").mode);
        Assert.True(host.Got("start"));
    }

    [Fact]
    public void ThreeGameGoesOnWhenOnePlayerGivesUp()
    {
        var (server, seats) = StartParty(Modes.Three, 3);
        server.Handle(seats[0], new Message { type = "move", action = "e2-e4" });
        Assert.Equal("e2-e4", seats[2].Last("move").action);

        // Red gives up on their turn: their army leaves and Black moves next.
        server.Handle(seats[1], new Message { type = "resign" });
        Assert.Equal(1, seats[0].Last("out").seat);
        Assert.False(seats[0].Got("end"));
        server.Handle(seats[2], new Message { type = "move", action = "l5-l6" });
        Assert.Equal("error", seats[2].Inbox.Last().type);
        server.Handle(seats[2], new Message { type = "move", action = "k5-j5" });
        Assert.Equal("k5-j5", seats[0].Last("move").action);
        Assert.NotEqual("k5-j5", seats[1].Last("move").action);

        server.Handle(seats[2], new Message { type = "resign" });
        Assert.Equal("seat0", seats[0].Last("end").result);
    }

    private static FakePlayer Known(GameServer server, string name)
    {
        var player = new FakePlayer(name);
        server.Handle(player, new Message { type = "hello", name = name, id = "id-" + name + "-0000", secret = "test-secret-0000000000" });
        return player;
    }

    [Fact]
    public void HelloSendsTheStartingRating()
    {
        var server = new GameServer();
        var a = Known(server, "Аня");
        Assert.Equal(Ratings.Start, a.Last("rating").rating);
        Assert.Equal(0, a.Last("rating").games);
    }

    [Fact]
    public void QuickMatchWinMovesTheRatingUpAndTheLossDown()
    {
        var server = new GameServer(new Random(1));
        var a = Known(server, "Аня");
        var b = Known(server, "Боря");
        server.Handle(a, new Message { type = "find", mode = Modes.Classic });
        server.Handle(b, new Message { type = "find", mode = Modes.Classic });
        Assert.True(a.Last("start").rated);
        var black = a.Last("start").color == "black" ? a : b;
        var white = black == a ? b : a;
        server.Handle(black, new Message { type = "resign" });

        Assert.Equal(16, white.Last("end").delta);
        Assert.Equal(1016, white.Last("end").rating);
        Assert.Equal(-16, black.Last("end").delta);
        Assert.Equal(984, black.Last("end").rating);

        server.Handle(white, new Message { type = "top" });
        var lines = white.Last("top").list!.Split('\n');
        Assert.StartsWith(white.Name + "\t1016", lines[0]);
        Assert.StartsWith(black.Name + "\t984", lines[1]);
    }

    [Fact]
    public void RoomGamesAndPlayersWithoutIdAreNotRated()
    {
        var server = new GameServer(new Random(3));
        var host = Known(server, "host");
        var guest = Known(server, "guest");
        server.Handle(host, new Message { type = "create", mode = Modes.Classic });
        server.Handle(guest, new Message { type = "join", code = host.Last("room").code });
        Assert.False(host.Last("start").rated);
        server.Handle(host, new Message { type = "resign" });
        Assert.Null(guest.Last("end").delta);

        var anonymous = new FakePlayer("x");
        server.Handle(anonymous, new Message { type = "find", mode = Modes.Classic });
        server.Handle(host, new Message { type = "find", mode = Modes.Classic });
        Assert.False(host.Last("start").rated);
    }

    [Fact]
    public void TeamAndThreeGamesAreRatedForEveryone()
    {
        var server = new GameServer(new Random(5));
        var team = Enumerable.Range(0, 4).Select(i => Known(server, "t" + i)).ToArray();
        foreach (var p in team)
            server.Handle(p, new Message { type = "find", mode = Modes.Team });
        var bySeat = team.OrderBy(p => p.Last("start").seat).ToArray();
        server.Handle(bySeat[1], new Message { type = "resign" });
        Assert.Equal(16, bySeat[0].Last("end").delta);
        Assert.Equal(16, bySeat[2].Last("end").delta);
        Assert.Equal(-16, bySeat[1].Last("end").delta);
        Assert.Equal(-16, bySeat[3].Last("end").delta);

        var three = Enumerable.Range(0, 3).Select(i => Known(server, "r" + i)).ToArray();
        foreach (var p in three)
            server.Handle(p, new Message { type = "find", mode = Modes.Three });
        var seats = three.OrderBy(p => p.Last("start").seat).ToArray();
        server.Handle(seats[1], new Message { type = "resign" });
        server.Handle(seats[2], new Message { type = "resign" });
        Assert.Equal(16, seats[0].Last("end").delta);
        // The first to leave gets no "end", but the rating still counts the loss.
        server.Handle(seats[1], new Message { type = "hello", name = "r1", id = "id-r1-0000", secret = "test-secret-0000000000" });
        Assert.Equal(992, seats[1].Last("rating").rating);
    }

    [Fact]
    public void RatingsSurviveARestart()
    {
        string path = Path.Combine(Path.GetTempPath(), "ratings-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(path);
            var server = new GameServer(new Random(1), new Ratings(store));
            var a = Known(server, "Аня");
            var b = Known(server, "Боря");
            server.Handle(a, new Message { type = "find", mode = Modes.Classic });
            server.Handle(b, new Message { type = "find", mode = Modes.Classic });
            server.Handle(a, new Message { type = "resign" });

            store.Flush();
            var again = new GameServer(null, new Ratings(new FileStore(path)));
            var a2 = Known(again, "Аня");
            Assert.Equal(984, a2.Last("rating").rating);
            Assert.Equal(1, a2.Last("rating").games);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
