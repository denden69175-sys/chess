using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Chess.Core
{
    public enum BotLevel
    {
        Easy,
        Medium,
        Hard,
    }

    /// <summary>
    /// A small alpha-beta search bot. It only uses <see cref="MoveGenerator"/>, so it plays any
    /// starting position the core accepts, including custom armies.
    /// </summary>
    public sealed class Bot
    {
        private const int MateScore = 100000;

        // Piece-square tables from White's point of view, a1 first (index = rank * 8 + file).
        private static readonly int[] PawnTable =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10,-20,-20, 10, 10,  5,
             5, -5,-10,  0,  0,-10, -5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5,  5, 10, 25, 25, 10,  5,  5,
            10, 10, 20, 30, 30, 20, 10, 10,
            50, 50, 50, 50, 50, 50, 50, 50,
             0,  0,  0,  0,  0,  0,  0,  0,
        };

        private static readonly int[] KnightTable =
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
        };

        private static readonly int[] BishopTable =
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
        };

        private static readonly int[] KingTable =
        {
             20, 30, 10,  0,  0, 10, 30, 20,
             20, 20,  0,  0,  0,  0, 20, 20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
        };

        private readonly Random _random;

        public Bot(BotLevel level, int? seed = null)
        {
            Level = level;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public BotLevel Level { get; }

        private int Depth => Level switch
        {
            BotLevel.Easy => 1,
            BotLevel.Medium => 2,
            _ => 3,
        };

        /// <summary>
        /// Picks a move for the side to move. Safe to call from a background thread;
        /// returns null when there are no legal moves.
        /// </summary>
        public Move? ChooseMove(Position position, CancellationToken cancellation = default) =>
            ChooseMoveWithScore(position, cancellation).Move;

        /// <summary>
        /// The chosen move and its score in centipawns for the side to move (higher is better).
        /// The time-travel bot compares this score with the scores of its jumps.
        /// </summary>
        public (Move? Move, int Score) ChooseMoveWithScore(Position position, CancellationToken cancellation = default)
        {
            var moves = Ordered(position, MoveGenerator.GenerateLegal(position));
            if (moves.Count == 0)
                return (null, position.IsInCheck(position.SideToMove) ? -MateScore : 0);

            // The easy bot sometimes just plays a random move so a beginner can win.
            if (Level == BotLevel.Easy && _random.NextDouble() < 0.3)
            {
                var randomMove = moves[_random.Next(moves.Count)];
                return (randomMove, ScoreAfter(position.Apply(randomMove), cancellation));
            }

            int best = -MateScore - 1;
            var bestMoves = new List<Move>();
            foreach (var move in moves)
            {
                cancellation.ThrowIfCancellationRequested();
                // The window starts one below the best score so that moves scoring exactly as
                // well get an exact score (and join the tie) while worse ones are cut off early.
                int score = -Search(position.Apply(move), Depth - 1, -MateScore - 1, -(best - 1), 1, cancellation);
                if (score > best)
                {
                    best = score;
                    bestMoves.Clear();
                    bestMoves.Add(move);
                }
                else if (score == best)
                {
                    bestMoves.Add(move);
                }
            }

            // Several equally good moves: pick one at random so games do not repeat.
            return (bestMoves[_random.Next(bestMoves.Count)], best);
        }

        /// <summary>
        /// How good <paramref name="after"/> is for the player who just moved into it, searched to
        /// the bot's depth. Lets callers score actions that are not ordinary moves.
        /// </summary>
        public int ScoreAfter(Position after, CancellationToken cancellation = default) =>
            -Search(after, Depth - 1, -MateScore - 1, MateScore + 1, 1, cancellation);

        /// <summary>
        /// Picks a move or a jump in the time-travel mode and returns it as text
        /// ("e2e4" or "jump:e4:2"), or null when there is nothing to play.
        /// A jump is chosen only when it is clearly better than the best ordinary move.
        /// </summary>
        public string? ChooseTimeTravelAction(TimeTravelChoice choice, CancellationToken cancellation = default)
        {
            var (move, moveScore) = ChooseMoveWithScore(choice.Current, cancellation);
            string? best = move?.ToUci();
            int bestScore = move.HasValue ? moveScore + JumpMargin : int.MinValue;

            // The easy bot never time-travels: the mode is hard enough to follow already.
            if (Level == BotLevel.Easy)
                return best;

            foreach (var (jump, result) in choice.Jumps)
            {
                cancellation.ThrowIfCancellationRequested();
                int score = ScoreAfter(result, cancellation);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = jump.ToString();
                }
            }
            return best;
        }

        // Jumps are rare (two per game), so the bot keeps one for a real gain.
        private const int JumpMargin = 150;

        private int Search(Position position, int depth, int alpha, int beta, int ply, CancellationToken cancellation)
        {
            var moves = MoveGenerator.GenerateLegal(position);
            if (moves.Count == 0)
                return position.IsInCheck(position.SideToMove) ? -MateScore + ply : 0;
            if (depth <= 0)
                return Quiescence(position, alpha, beta, 4);

            foreach (var move in Ordered(position, moves))
            {
                cancellation.ThrowIfCancellationRequested();
                int score = -Search(position.Apply(move), depth - 1, -beta, -alpha, ply + 1, cancellation);
                if (score >= beta)
                    return beta;
                if (score > alpha)
                    alpha = score;
            }
            return alpha;
        }

        /// <summary>Keeps looking at captures so the bot does not stop mid-exchange.</summary>
        private int Quiescence(Position position, int alpha, int beta, int depthLeft)
        {
            int standPat = Evaluate(position);
            if (standPat >= beta)
                return beta;
            if (standPat > alpha)
                alpha = standPat;
            if (depthLeft == 0)
                return alpha;

            var captures = MoveGenerator.GenerateLegal(position).Where(m => IsCapture(position, m));
            foreach (var move in Ordered(position, captures))
            {
                int score = -Quiescence(position.Apply(move), -beta, -alpha, depthLeft - 1);
                if (score >= beta)
                    return beta;
                if (score > alpha)
                    alpha = score;
            }
            return alpha;
        }

        private static bool IsCapture(Position position, Move move) =>
            position[move.To].HasValue || move.Kind == MoveKind.EnPassant;

        /// <summary>Captures of valuable pieces by cheap ones first: alpha-beta cuts more that way.</summary>
        private static List<Move> Ordered(Position position, IEnumerable<Move> moves) =>
            moves.OrderByDescending(m =>
            {
                int score = 0;
                var victim = position[m.To];
                if (victim.HasValue)
                    score += 10 * PieceValues.Of(victim.Value.Type) - PieceValues.Of(position[m.From]!.Value.Type);
                if (m.Promotion.HasValue)
                    score += PieceValues.Of(m.Promotion.Value);
                return score;
            }).ToList();

        /// <summary>Score in centipawns from the side to move's point of view.</summary>
        public static int Evaluate(Position position)
        {
            int score = 0;
            for (int i = 0; i < 64; i++)
            {
                var piece = position[i];
                if (!piece.HasValue)
                    continue;
                // Tables are written for White; mirror the rank for Black.
                int tableIndex = piece.Value.Color == PieceColor.White ? i : (7 - i / 8) * 8 + i % 8;
                int value = 100 * PieceValues.Of(piece.Value.Type) + Positional(piece.Value.Type, tableIndex);
                score += piece.Value.Color == position.SideToMove ? value : -value;
            }
            return score;
        }

        private static int Positional(PieceType type, int index) => type switch
        {
            PieceType.Pawn => PawnTable[index],
            PieceType.Knight => KnightTable[index],
            PieceType.Bishop => BishopTable[index],
            PieceType.Queen => BishopTable[index] / 2,
            PieceType.King => KingTable[index],
            _ => 0,
        };
    }
}
