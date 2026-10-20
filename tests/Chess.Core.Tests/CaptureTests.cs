using System.Linq;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class CaptureTests
    {
        [Fact]
        public void GameListsTakenPiecesIncludingEnPassant()
        {
            var game = new Game();
            foreach (var move in new[] { "e2e4", "d7d5", "e4d5", "c7c5", "d5c6" })
                game.MakeMove(move);
            var taken = game.CapturedPieces();
            Assert.Equal(2, taken.Count);
            Assert.All(taken, p => Assert.Equal(new Piece(PieceType.Pawn, PieceColor.Black), p));
        }

        [Fact]
        public void TimeTravelKeepsCapturesBeforeTheBranchOnly()
        {
            var game = new TimeTravelGame();
            foreach (var move in new[] { "e2e4", "d7d5", "e4d5", "g8f6", "b1c3", "f6d5", "c3d5", "a7a6", "g1f3", "a6a5", "f1c4", "h7h6" })
                Assert.True(game.TryPlay(move), move);
            Assert.Equal(3, game.CapturedPieces().Count);
            // The bishop jumps one own move back; all three captures happened before that board.
            Assert.True(game.TryPlay("jump:c4:1"));
            Assert.Equal(3, game.CapturedPieces().Count);
        }

        [Fact]
        public void TeamGameCountsCapturesPerPlayer()
        {
            var game = new TeamGame();
            foreach (var move in new[] { "d2d4", "c7c5", "e2e4", "h7h6", "d4c5" })
                Assert.True(game.TryPlay(move), move);
            Assert.Single(game.CapturedBy(0));
            Assert.Empty(game.CapturedBy(2));
        }

        [Fact]
        public void MultiverseRemembersCapturesAndForgetsUndoneOnes()
        {
            var game = new MultiverseGame(Position.FromFen("4k3/8/8/3q4/8/8/3R4/4K3 w - - 0 1"));
            var take = game.MovesFrom(0).Single(m => m.IsSpatial && m.OnBoard.ToUci() == "d2d5");
            game.MakeMove(take);
            Assert.Single(game.CapturedPieces);
            game.UndoMove();
            Assert.Empty(game.CapturedPieces);
            game.MakeMove(take);
            game.Submit();
            Assert.Equal(PieceType.Queen, game.CapturedPieces.Single().Type);
        }

        [Fact]
        public void ThreeGameRecordsWhoTookWhat()
        {
            var board = new TriPiece?[ThreeGame.Squares];
            board[ThreeGame.Index(6, 0)] = new TriPiece(PieceType.King, 0, 0);
            board[ThreeGame.Index(0, 7)] = new TriPiece(PieceType.Queen, 0, 0);
            board[ThreeGame.Index(0, 8)] = new TriPiece(PieceType.King, 1, 1);
            board[ThreeGame.Index(11, 8)] = new TriPiece(PieceType.King, 2, 2);
            var game = new ThreeGame(board, 0);
            Assert.True(game.TryPlay("a8-a9"));
            var taken = game.CapturedBy(0).Single();
            Assert.Equal(PieceType.King, taken.Type);
            Assert.Equal(1, taken.Owner);
        }
    }
}
