using System.Linq;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class MultiverseTests
    {
        private static void Play(MultiverseGame game, params string[] turns)
        {
            // Each turn is one move on timeline 0, written like "e2e4".
            foreach (var uci in turns)
            {
                var line = game.Timeline(0);
                var move = game.MovesFrom(0).Single(m => m.IsSpatial && m.OnBoard.ToUci() == uci);
                game.MakeMove(move);
                game.Submit();
            }
        }

        private static Move5D Travel(MultiverseGame game, string from, int toTimeline, int toPly, string to) =>
            game.MovesFrom(0).Single(m => m.From == Square.Parse(from) && m.ToTimeline == toTimeline
                && m.ToPly == toPly && m.To == Square.Parse(to));

        [Fact]
        public void StartHasOnlyTheTwentyNormalMoves()
        {
            var game = new MultiverseGame();
            Assert.Equal(20, game.MovesFrom(0).Count);
            Assert.All(game.MovesFrom(0), m => Assert.True(m.IsSpatial));
        }

        [Fact]
        public void KnightJumpingIntoThePastStartsANewTimeline()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");

            // Knight f3 at ply 2: two files right and one turn back lands on h3 of the first board.
            var jump = Travel(game, "f3", 0, 0, "h3");
            game.MakeMove(jump);

            Assert.True(game.HasTimeline(1));
            var branch = game.Timeline(1);
            Assert.Equal(1, branch.StartPly);
            Assert.Equal(PieceType.Knight, branch.End[Square.Parse("h3")]!.Value.Type);
            Assert.Equal(PieceType.Knight, branch.End[Square.Parse("g1")]!.Value.Type);
            Assert.Null(game.Timeline(0).End[Square.Parse("f3")]);
            Assert.Equal(1, game.Present);
            Assert.True(game.CanSubmit);
        }

        [Fact]
        public void UndoRemovesTheNewTimeline()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");
            game.MakeMove(Travel(game, "f3", 0, 0, "h3"));
            game.UndoMove();
            Assert.False(game.HasTimeline(1));
            Assert.Equal(2, game.Timeline(0).EndPly);
            Assert.False(game.CanSubmit);
        }

        [Fact]
        public void BlackMustAnswerOnBothTimelines()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");
            game.MakeMove(Travel(game, "f3", 0, 0, "h3"));
            game.Submit();

            // Present is the new timeline's board at ply 1; black moves there first.
            Assert.Equal(PieceColor.Black, game.Mover);
            Assert.Equal(new[] { 1 }, game.RequiredTimelines().Select(t => t.Index).ToArray());
            var reply = game.MovesFrom(1).First(m => m.IsSpatial && m.OnBoard.ToUci() == "e7e5");
            game.MakeMove(reply);
            // Now timeline 1 and timeline 0 both end on a board where white is to move.
            Assert.True(game.CanSubmit);
        }

        [Fact]
        public void PieceCanHopToTheSameMomentOfAnotherTimeline()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");
            game.MakeMove(Travel(game, "f3", 0, 0, "h3"));
            game.Submit();
            game.MakeMove(game.MovesFrom(1).First(m => m.IsSpatial && m.OnBoard.ToUci() == "d7d6"));
            game.Submit();

            // White: timeline 1 ends at ply 2 (white to move), timeline 0 at ply 3 (black to move).
            Assert.Equal(PieceColor.White, game.Mover);
            var hop = game.MovesFrom(1).FirstOrDefault(m => m.ToTimeline == 0 && !m.IsSpatial);
            // Timeline 0 has white-to-move boards only in the past (plies 0 and 2), so any such move branches.
            Assert.NotEqual(default, hop);
        }

        [Fact]
        public void TextFormRoundTrips()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");
            var jump = Travel(game, "f3", 0, 0, "h3");
            Assert.Equal("0,2,f3>0,0,h3", jump.ToString());
            Assert.Equal(jump, game.FindMove(jump.ToString()));
            Assert.True(game.TryPlay(jump.ToString()));
            Assert.True(game.TryPlay("submit"));
            Assert.False(game.TryPlay("0,0,e2>0,0,e4"));
        }

        [Fact]
        public void LeavingTheKingToBeTakenCannotBeSubmitted()
        {
            var game = new MultiverseGame(Position.FromFen("4k3/8/8/8/8/8/4r3/4K3 w - - 0 1"));
            var stay = game.MovesFrom(0).Single(m => m.IsSpatial && m.OnBoard.ToUci() == "e1d2");
            game.MakeMove(stay);
            Assert.True(game.PresentMoved);
            Assert.False(game.CanSubmit);
        }

        [Fact]
        public void KingEscapesFoolsMateThroughTime()
        {
            // On one board this is mate, but here the king can step into the past.
            var game = new MultiverseGame();
            Play(game, "f2f3", "e7e5", "g2g4", "d8h4");
            Assert.False(game.IsOver);
            Assert.True(game.AttackedIfPassing());
            var escape = game.FindTurn(MultiverseGame.SearchBudget, null);
            Assert.NotNull(escape);
            Assert.False(escape![0].IsSpatial);
        }

        [Fact]
        public void MateWithNoPastToEscapeTo()
        {
            var game = new MultiverseGame(Position.FromFen("R5k1/5ppp/8/8/8/8/8/6K1 b - - 0 1"));
            Assert.Equal(MultiverseResult.Checkmate, game.Result);
            Assert.Equal(PieceColor.White, game.Winner);
        }

        [Fact]
        public void StalemateIsADraw()
        {
            var game = new MultiverseGame(Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"));
            Assert.Equal(MultiverseResult.Stalemate, game.Result);
            Assert.Null(game.Winner);
        }

        [Fact]
        public void SearchFindsATurnAtTheStart()
        {
            var game = new MultiverseGame();
            var turn = game.FindTurn(MultiverseGame.SearchBudget, null);
            Assert.NotNull(turn);
            Assert.Single(turn!);
        }

        [Fact]
        public void TimelinesBeyondOneExtraAreInactive()
        {
            var game = new MultiverseGame();
            Assert.True(game.IsActive(0));
            Assert.True(game.IsActive(1));
            Assert.False(game.IsActive(2));
        }

        [Fact]
        public void BotTakesAFreeQueen()
        {
            var game = new MultiverseGame(Position.FromFen("4k3/8/8/3q4/8/8/3R4/4K3 w - - 0 1"));
            var turn = new MultiverseBot(1).ChooseTurn(game);
            Assert.NotNull(turn);
            Assert.Equal("d2d5", turn![0].OnBoard.ToUci());
        }

        [Fact]
        public void TwoBotsPlayALongGameWithoutBreakingRules()
        {
            var game = new MultiverseGame();
            var white = new MultiverseBot(3);
            var black = new MultiverseBot(4);
            for (int turn = 0; turn < 40 && !game.IsOver; turn++)
            {
                var moves = (game.Mover == PieceColor.White ? white : black).ChooseTurn(game);
                Assert.NotNull(moves);
                foreach (var move in moves!)
                    game.MakeMove(move);
                Assert.True(game.CanSubmit);
                game.Submit();
            }
            Assert.True(game.History.Count > 10);
        }
    
        [Fact]
        public void MovedPieceCanJumpStraightBackToItsSquare()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");
            // f3 was empty one turn ago, so the knight can appear there.
            var jump = Travel(game, "f3", 0, 0, "f3");
            game.MakeMove(jump);
            Assert.True(game.HasTimeline(1));
            Assert.Equal(PieceType.Knight, game.Timeline(1).End[Square.Parse("f3")]!.Value.Type);
            Assert.True(game.CanSubmit);
        }

        [Fact]
        public void PieceCannotJumpOntoItsOwnPastSelf()
        {
            var game = new MultiverseGame();
            Play(game, "g1f3", "e7e6");
            Assert.DoesNotContain(game.MovesFrom(0), m => m.From == Square.Parse("b1") && !m.IsSpatial && m.To == Square.Parse("b1"));
        }
    }
}
