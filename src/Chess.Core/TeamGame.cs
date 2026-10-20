using System;
using System.Collections.Generic;
using System.Linq;

namespace Chess.Core
{
    /// <summary>
    /// "Союзники", our 2 vs 2 mode on one ordinary board. Each colour is a team of two players who
    /// move in turn: White 1, Black 1, White 2, Black 2, and again. Player 1 of a team commands the
    /// queen's flank (pieces that start on files a–d, the queen among them), player 2 the king's flank
    /// (files e–h, the king among them). A player moves only their own pieces, with two exceptions:
    /// when the team's king is in check ("Выручка") or when none of the player's own pieces can move,
    /// any piece of the team may move. A promoted pawn stays with its player. Check, mate and draws
    /// are the usual ones, decided for the whole team.
    /// </summary>
    public sealed class TeamGame
    {
        public const int Players = 4;

        private readonly Game _game;
        private readonly List<int?[]> _owners = new List<int?[]>();

        public TeamGame() : this(Position.Start)
        {
        }

        public TeamGame(Position start)
        {
            _game = new Game(start);
            var owners = new int?[64];
            for (int i = 0; i < 64; i++)
            {
                var piece = start[i];
                if (piece.HasValue)
                    owners[i] = PlayerOf(piece.Value.Color, new Square(i).File >= 4);
            }
            _owners.Add(owners);
        }

        public Game Game => _game;
        public Position Current => _game.Current;
        public bool IsOver => _game.IsOver;
        public GameStatus Status => _game.Status;
        public PieceColor? Winner => _game.Winner;

        /// <summary>Whose turn it is: 0 = White 1, 1 = Black 1, 2 = White 2, 3 = Black 2.</summary>
        public int Player => _game.Moves.Count % Players;

        public static PieceColor ColorOf(int player) => player % 2 == 0 ? PieceColor.White : PieceColor.Black;

        /// <summary>True for the player who commands the king's flank (files e–h).</summary>
        public static bool IsKingSide(int player) => player >= 2;

        public static int PlayerOf(PieceColor color, bool kingSide) => (color == PieceColor.White ? 0 : 1) + (kingSide ? 2 : 0);

        public static int Partner(int player) => (player + 2) % Players;

        /// <summary>The player who owns the piece on this square, or null for an empty square.</summary>
        public int? OwnerAt(Square square) => _owners[_owners.Count - 1][square.Index];

        /// <summary>True when the player to move may use the whole team's pieces this turn.</summary>
        public bool TeamMoveAllowed
        {
            get
            {
                if (IsOver)
                    return false;
                if (Current.IsInCheck(Current.SideToMove))
                    return true;
                return !_game.LegalMoves.Any(m => OwnerAt(m.From) == Player);
            }
        }

        /// <summary>The moves the player whose turn it is may make.</summary>
        public List<Move> LegalMoves()
        {
            if (IsOver)
                return new List<Move>();
            if (TeamMoveAllowed)
                return _game.LegalMoves.ToList();
            int player = Player;
            return _game.LegalMoves.Where(m => OwnerAt(m.From) == player).ToList();
        }

        public bool IsLegal(Move move) => LegalMoves().Contains(move);

        public Move? FindMove(string uci)
        {
            var move = _game.FindMove(uci);
            return move.HasValue && IsLegal(move.Value) ? move : null;
        }

        public void MakeMove(Move move)
        {
            if (!IsLegal(move))
                throw new InvalidOperationException($"Move {move} is not allowed for player {Player}.");
            var owners = (int?[])_owners[_owners.Count - 1].Clone();
            var color = Current.SideToMove;
            owners[move.To.Index] = owners[move.From.Index];
            owners[move.From.Index] = null;
            int rank = move.From.Rank;
            switch (move.Kind)
            {
                case MoveKind.EnPassant:
                    owners[move.To.Index + (color == PieceColor.White ? -8 : 8)] = null;
                    break;
                case MoveKind.CastleKingside:
                    owners[rank * 8 + 5] = owners[rank * 8 + 7];
                    owners[rank * 8 + 7] = null;
                    break;
                case MoveKind.CastleQueenside:
                    owners[rank * 8 + 3] = owners[rank * 8];
                    owners[rank * 8] = null;
                    break;
            }
            _game.MakeMove(move);
            _owners.Add(owners);
        }

        public bool TryPlay(string uci)
        {
            var move = FindMove(uci);
            if (!move.HasValue)
                return false;
            MakeMove(move.Value);
            return true;
        }

        public void Undo()
        {
            _game.Undo();
            _owners.RemoveAt(_owners.Count - 1);
        }
    }
}
