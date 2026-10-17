using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// A simple 5D chess opponent. It tries moves in order of a quick score (win material, do not
    /// leave the moved piece hanging, a little randomness) and plays the first full turn that is
    /// legal. Time travel without a capture is tried only after ordinary moves, so the bot does not
    /// fill the screen with timelines, but it happily takes pieces across time.
    /// </summary>
    public sealed class MultiverseBot
    {
        private readonly Random _random;

        public MultiverseBot(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>The moves for the current turn, or null when there is no legal turn.</summary>
        public List<Move5D>? ChooseTurn(MultiverseGame game)
        {
            var copy = game.Clone();
            return copy.FindTurn(MultiverseGame.SearchBudget, Score)
                ?? copy.FindTurn(MultiverseGame.SearchBudget * 10, null);
        }

        private static int Value(PieceType type) => type == PieceType.King ? 100 : PieceValues.Of(type);

        private double Score(MultiverseGame game, Move5D move)
        {
            var source = game.Timeline(move.FromTimeline).End;
            var mover = source[move.From]!.Value;
            var captured = game.Timeline(move.ToTimeline)[move.ToPly][move.To];
            bool arrivesInPresent = !move.IsSpatial && move.ToPly == game.Timeline(move.ToTimeline).EndPly;

            game.TryApply(move);
            Position landed;
            if (move.IsSpatial || arrivesInPresent)
                landed = game.Timeline(move.ToTimeline).End;
            else
                landed = game.Timeline(game.Mover == PieceColor.White ? game.MaxTimeline : game.MinTimeline).End;
            bool hanging = MoveGenerator.IsSquareAttacked(landed, move.To, mover.Color.Opponent());
            game.UndoTry();

            double score = _random.NextDouble() * 1.5;
            if (captured.HasValue)
                score += Value(captured.Value.Type) * 10;
            if (hanging)
                score -= Value(mover.Type) * 9;
            if (move.Promotion == PieceType.Queen)
                score += 80;
            // Every new timeline makes the game harder to follow, so it has to pay for itself.
            if (!move.IsSpatial && !arrivesInPresent)
                score -= captured.HasValue ? 15 : 8;
            else if (!move.IsSpatial && !captured.HasValue)
                score -= 3;
            return score;
        }
    }
}
