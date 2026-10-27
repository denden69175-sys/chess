using Chess.Core;
using Chess.Server;
using Xunit;

namespace Chess.Server.Tests;

public class GameCheckTests
{
    [Fact]
    public void RatedGamesBetweenTheSamePairAreLimitedPerDay()
    {
        var server = new GameServer(new Random(1));
        var a = Player(server, "a");
        var b = Player(server, "b");
        var rated = new List<bool>();
        for (int i = 0; i < GameServer.RatedGamesPerPair + 2; i++)
        {
            server.Handle(a, new Message { type = "find", mode = Modes.Classic });
            server.Handle(b, new Message { type = "find", mode = Modes.Classic });
            rated.Add(a.Inbox.Last(m => m.type == "start").rated == true);
            server.Handle(a, new Message { type = "resign" });
        }
        Assert.Equal(GameServer.RatedGamesPerPair, rated.Count(r => r));
        Assert.False(rated.Last());
    }

    [Fact]
    public void FiveDTeamAndThreeGamesReplay()
    {
        // A 5D game dropped after four turns: counted, but nobody's win.
        var game = new MultiverseGame();
        var turns = Enumerable.Range(0, 4).Select(_ => PlayTurn(game)).ToList();
        var replay = new Match(Modes.FiveD, new IPlayer[] { new Fake(), new Fake() });
        foreach (var turn in turns)
            Assert.True(replay.TryPlay(turn), turn);
        var checkedFiveD = GameCheck.Verify(new ProgressAction { mode = Modes.FiveD, seat = 0, won = 1, actions = string.Join("\n", turns) });
        Assert.NotNull(checkedFiveD);
        Assert.Equal(0, checkedFiveD!.won);
        Assert.Null(GameCheck.Verify(new ProgressAction { mode = Modes.FiveD, seat = 0, actions = turns[1] }));

        var team = GameCheck.Verify(new ProgressAction { mode = Modes.Team, seat = 0, actions = "d2d4\nd7d5\ne2e4\ne7e5\nc2c4\nc7c6\ng1f3\ng8f6" });
        Assert.NotNull(team);

        Assert.Null(GameCheck.Verify(new ProgressAction { mode = Modes.Three, seat = 0, actions = "a1-a9" }));
        Assert.Null(GameCheck.Verify(new ProgressAction { mode = "nonsense", seat = 0, actions = "e2e4" }));
    }

    [Fact]
    public void AnArmyGameNeedsLegalArmies()
    {
        string code = Army.Classic.ToCode();
        Assert.NotNull(GameCheck.Verify(new ProgressAction { mode = Modes.Army, seat = 1, whiteArmy = code, blackArmy = code, actions = "f2f3\ne7e5\ng2g4\nd8h4" }));
        var queens = new Army(Army.Classic.Pieces.ToDictionary(p => p.Key, p => p.Value == PieceType.King ? PieceType.King : PieceType.Queen));
        Assert.False(queens.IsValid);
        Assert.Null(GameCheck.Verify(new ProgressAction { mode = Modes.Army, seat = 1, whiteArmy = queens.ToCode(), blackArmy = code, actions = "f2f3\ne7e5\ng2g4\nd8h4" }));
    }

    /// <summary>Plays the first single-move turn that can be submitted and returns it as text.</summary>
    private static string PlayTurn(MultiverseGame game)
    {
        foreach (var move in game.MovesFrom(0))
        {
            var copy = game.Clone();
            if (copy.TryPlay(move.ToString()) && copy.TryPlay("submit"))
            {
                game.TryPlay(move.ToString());
                game.TryPlay("submit");
                return move.ToString();
            }
        }
        throw new InvalidOperationException("No turn.");
    }

    private static Fake Player(GameServer server, string name)
    {
        var player = new Fake();
        server.Handle(player, new Message { type = "hello", name = name, id = "pair-" + name + "-0000", secret = "secret-" + name + "-00000000000" });
        return player;
    }

    private sealed class Fake : IPlayer
    {
        public string Name { get; set; } = "";
        public string? Id { get; set; }
        public List<Message> Inbox { get; } = new();
        public void Send(Message message) => Inbox.Add(message);
    }
}
