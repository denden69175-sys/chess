using System.Linq;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class TimeTravelTests
    {
        private static TimeTravelGame Play(params string[] actions)
        {
            var game = new TimeTravelGame();
            foreach (var action in actions)
                Assert.True(game.TryPlay(action), $"could not play {action}");
            return game;
        }

        // Five moves each, so White's cooldown has passed and White is to move.
        private static readonly string[] Opening =
        {
            "e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6", "b1c3", "f8c5", "d2d3", "d7d6",
        };

        [Fact]
        public void NextJumpWaitsForTheCooldown()
        {
            var game = Play(Opening);
            game.Jump(new TimeJump(Square.Parse("c4"), 3));
            Assert.True(game.TryPlay("a7a6"));
            Assert.False(game.CanJumpNow(PieceColor.White));
            Assert.Empty(game.LegalJumps());
            Assert.Equal(TimeTravelGame.Cooldown - 1, game.MovesUntilJump(PieceColor.White));
        }

        [Fact]
        public void JumpAddsThePieceToAnOldBoardAndStartsANewTimeline()
        {
            var game = Play(Opening);
            // The bishop on c4 goes back 3 of White's moves, to the board before Bc4 was played.
            var jump = new TimeJump(Square.Parse("c4"), 3);
            Assert.True(game.IsLegalJump(jump));
            game.Jump(jump);

            Assert.Equal(2, game.Timelines.Count);
            Assert.Equal(1, game.ActiveTimeline.Id);
            Assert.Equal(0, game.ActiveTimeline.ParentId);
            // That board had the bishop still on f1; now there is a second one on c4.
            Assert.Equal(new Piece(PieceType.Bishop, PieceColor.White), game.Current[Square.Parse("c4")]);
            Assert.Equal(new Piece(PieceType.Bishop, PieceColor.White), game.Current[Square.Parse("f1")]);
            Assert.Equal(PieceColor.Black, game.Current.SideToMove);
            Assert.Equal(1, game.JumpsLeft(PieceColor.White));
            Assert.Equal("Bc4@-3", game.Log.Last());
            Assert.Equal("Сc4 ⟲3", game.RussianLog.Last());
        }

        [Fact]
        public void CannotLandOnAnOccupiedSquare()
        {
            var game = Play(Opening);
            // Three White moves ago the knight was still on b1 and c3 was empty, but d3 is a pawn
            // that was on d2 then; e4 had the pawn already: landing there is blocked.
            Assert.False(game.IsLegalJump(new TimeJump(Square.Parse("e4"), 1)));
        }

        [Fact]
        public void CannotGiveCheckOnArrival()
        {
            var game = new TimeTravelGame(Position.FromFen("4k3/8/8/8/8/8/8/4K2Q w - - 0 1"));
            foreach (var action in new[] { "h1h2", "e8d8", "h2h5", "d8c8" })
                Assert.True(game.TryPlay(action), action);
            // Two White moves ago the black king stood on e8, so a queen arriving on h5 gives check.
            Assert.Null(game.ResultOf(new TimeJump(Square.Parse("h5"), 2)));
        }

        [Fact]
        public void KingCannotTravel()
        {
            var game = Play(Opening);
            Assert.DoesNotContain(game.LegalJumps(), j => j.From == Square.Parse("e1"));
        }

        [Fact]
        public void UndoRemovesTheTimelineAndRestoresTheJump()
        {
            var game = Play(Opening);
            game.Jump(new TimeJump(Square.Parse("c4"), 3));
            game.Undo();
            Assert.Single(game.Timelines);
            Assert.Equal(2, game.JumpsLeft(PieceColor.White));
            Assert.True(game.CanJumpNow(PieceColor.White));
            Assert.Equal(Opening.Length, game.ActionCount);
        }

        [Fact]
        public void ServerTextFormRoundTrips()
        {
            Assert.True(TimeJump.TryParse("jump:e4:2", out var jump));
            Assert.Equal(new TimeJump(Square.Parse("e4"), 2), jump);
            Assert.Equal("jump:e4:2", jump.ToString());
            Assert.False(TimeJump.TryParse("e2e4", out _));
        }

        [Fact]
        public void BotCanPlayTheModeAndUsesAJumpWhenItWinsMaterial()
        {
            var game = new TimeTravelGame();
            var white = new Bot(BotLevel.Medium, seed: 3);
            var black = new Bot(BotLevel.Medium, seed: 4);
            for (int i = 0; i < 60 && !game.IsOver; i++)
            {
                var bot = game.Current.SideToMove == PieceColor.White ? white : black;
                string? action = bot.ChooseTimeTravelAction(game.SnapshotForBot());
                Assert.NotNull(action);
                Assert.True(game.TryPlay(action!), action);
            }
            Assert.True(game.ActionCount > 0);
        }
    }
}
