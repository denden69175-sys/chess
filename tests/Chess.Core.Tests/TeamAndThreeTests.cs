using System.Linq;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class TeamGameTests
    {
        [Fact]
        public void PlayersTakeTurnsAndMoveOnlyTheirOwnFlank()
        {
            var game = new TeamGame();
            Assert.Equal(0, game.Player);
            // White 1 has files a–d: the e-pawn and the g-knight belong to White 2.
            Assert.Null(game.FindMove("e2e4"));
            Assert.Null(game.FindMove("g1f3"));
            Assert.True(game.TryPlay("d2d4"));
            Assert.Equal(1, game.Player);
            Assert.True(game.TryPlay("d7d5"));
            Assert.Equal(2, game.Player);
            Assert.Null(game.FindMove("c2c4"));
            Assert.True(game.TryPlay("g1f3"));
            Assert.Equal(3, game.Player);
        }

        [Fact]
        public void TheKingBelongsToTheKingSidePlayer()
        {
            var game = new TeamGame();
            Assert.Equal(2, game.OwnerAt(Square.Parse("e1")));
            Assert.Equal(0, game.OwnerAt(Square.Parse("d1")));
            Assert.Equal(3, game.OwnerAt(Square.Parse("e8")));
        }

        [Fact]
        public void InCheckTheWholeTeamMayHelp()
        {
            var game = new TeamGame();
            foreach (var move in new[] { "d2d4", "a7a6", "h2h3", "e7e6", "b2b3", "a6a5", "h3h4", "f8b4" })
                Assert.True(game.TryPlay(move), move);
            // White 1 is to move with the king in check: every legal move of the team is allowed.
            Assert.Equal(0, game.Player);
            Assert.True(game.TeamMoveAllowed);
            Assert.Equal(game.Game.LegalMoves.Count, game.LegalMoves().Count);
        }

        [Fact]
        public void PieceKeepsItsOwnerWhenItMoves()
        {
            var game = new TeamGame();
            game.TryPlay("b1c3");
            Assert.Equal(0, game.OwnerAt(Square.Parse("c3")));
            Assert.Null(game.OwnerAt(Square.Parse("b1")));
            game.Undo();
            Assert.Equal(0, game.OwnerAt(Square.Parse("b1")));
        }

        [Fact]
        public void BotOnlyPicksAllowedMoves()
        {
            var game = new TeamGame();
            var bot = new Bot(BotLevel.Easy, 1);
            for (int i = 0; i < 40 && !game.IsOver; i++)
            {
                var allowed = game.LegalMoves();
                var move = bot.ChooseMoveWithScore(game.Current, allowed).Move;
                Assert.NotNull(move);
                Assert.Contains(move!.Value, allowed);
                game.MakeMove(move.Value);
            }
        }
    }

    public class ThreeGameTests
    {
        [Fact]
        public void ArmiesStartOnThreeSides()
        {
            var game = new ThreeGame();
            Assert.Equal(PieceType.King, game[ThreeGame.Index(6, 0)]!.Value.Type);
            Assert.Equal(1, game[ThreeGame.Index(0, 8)]!.Value.Owner);
            Assert.Equal(PieceType.King, game[ThreeGame.Index(0, 8)]!.Value.Type);
            Assert.Equal(2, game[ThreeGame.Index(11, 8)]!.Value.Owner);
            Assert.Equal(0, game.Player);
            // 8 pawns with one or two steps, two knights with three jumps, two rooks with two steps into the open corners.
            Assert.Equal(26, game.LegalMoves().Count);
        }

        [Fact]
        public void TurnsGoRoundAndPawnsWalkTheirOwnWay()
        {
            var game = new ThreeGame();
            Assert.True(game.TryPlay("e2-e4"));
            Assert.Equal(1, game.Player);
            Assert.True(game.TryPlay("b7-d7"));
            Assert.Equal(2, game.Player);
            Assert.True(game.TryPlay("k7-i7"));
            Assert.Equal(0, game.Player);
            Assert.False(game.TryPlay("b6-c6"));
        }

        [Fact]
        public void TakingAKingRecruitsTheArmy()
        {
            var board = new TriPiece?[ThreeGame.Squares];
            board[ThreeGame.Index(6, 0)] = new TriPiece(PieceType.King, 0, 0);
            board[ThreeGame.Index(0, 7)] = new TriPiece(PieceType.Queen, 0, 0);
            board[ThreeGame.Index(0, 8)] = new TriPiece(PieceType.King, 1, 1);
            board[ThreeGame.Index(1, 5)] = new TriPiece(PieceType.Pawn, 1, 1);
            board[ThreeGame.Index(11, 8)] = new TriPiece(PieceType.King, 2, 2);
            var game = new ThreeGame(board, 0);

            Assert.True(game.TryPlay("a8-a9"));
            Assert.False(game.IsAlive(1));
            Assert.Equal(1, game.LastEliminated);
            // The red pawn now serves White but still walks to the right.
            var pawn = game[ThreeGame.Index(1, 5)]!.Value;
            Assert.Equal(0, pawn.Owner);
            Assert.Equal(1, pawn.Army);
            // Red is out, so Black moves next.
            Assert.Equal(2, game.Player);
            Assert.False(game.IsOver);
        }

        [Fact]
        public void LastKingWins()
        {
            var board = new TriPiece?[ThreeGame.Squares];
            board[ThreeGame.Index(6, 0)] = new TriPiece(PieceType.King, 0, 0);
            board[ThreeGame.Index(6, 1)] = new TriPiece(PieceType.Rook, 0, 0);
            board[ThreeGame.Index(6, 5)] = new TriPiece(PieceType.King, 2, 2);
            var game = new ThreeGame(board, 0);
            Assert.False(game.IsAlive(1));
            Assert.True(game.TryPlay("g2-g6"));
            Assert.Equal(0, game.Winner);
            Assert.True(game.IsOver);
        }

        [Fact]
        public void PawnBecomesAQueenAtTheFarEdge()
        {
            var game = new ThreeGame();
            Assert.False(game.IsOver);
            Assert.Equal("c2-c4", new TriMove(ThreeGame.Index(2, 1), ThreeGame.Index(2, 3)).ToString());
        }

        [Fact]
        public void ThreeBotsFinishOrKeepPlayingLegally()
        {
            var game = new ThreeGame();
            var bot = new ThreeBot(5);
            for (int i = 0; i < 600 && !game.IsOver; i++)
            {
                var move = bot.Choose(game.BoardCopy(), game.Player);
                Assert.NotNull(move);
                Assert.True(game.IsLegal(move!.Value));
                game.MakeMove(move.Value);
            }
            Assert.True(game.MoveCount > 30);
        }
    }
}
