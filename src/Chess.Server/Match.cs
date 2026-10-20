using Chess.Core;

namespace Chess.Server;

/// <summary>
/// One online game between the connected players. The server checks every action here.
/// Players sit in seats: in two-player modes seat 0 is White and seat 1 Black; in "Союзники"
/// the seat is the <see cref="TeamGame"/> player (0 = White 1, 1 = Black 1, 2 = White 2,
/// 3 = Black 2); in "Тройной трон" it is the <see cref="ThreeGame"/> player (0 = White, 1 = Red, 2 = Black).
/// </summary>
public sealed class Match
{
    private readonly List<IPlayer> _seats;
    private readonly Game? _game;
    private readonly TimeTravelGame? _timeTravel;
    private readonly TeamGame? _team;
    private readonly ThreeGame? _three;
    private MultiverseGame? _multiverse;
    private string? _forcedResult;

    public Match(string mode, IPlayer white, IPlayer black, Army? whiteArmy, Army? blackArmy)
        : this(mode, new[] { white, black }, whiteArmy, blackArmy)
    {
    }

    public Match(string mode, IReadOnlyList<IPlayer> seats, Army? whiteArmy = null, Army? blackArmy = null)
    {
        if (seats.Count != Modes.PlayersIn(mode))
            throw new ArgumentException($"Mode {mode} needs {Modes.PlayersIn(mode)} players.", nameof(seats));
        Mode = mode;
        _seats = seats.ToList();
        WhiteArmy = whiteArmy;
        BlackArmy = blackArmy;
        if (mode == Modes.TimeTravel)
            _timeTravel = new TimeTravelGame();
        else if (mode == Modes.FiveD)
            _multiverse = new MultiverseGame();
        else if (mode == Modes.Army)
            _game = new Game(Army.ToPosition(whiteArmy!, blackArmy!));
        else if (mode == Modes.Team)
            _team = new TeamGame();
        else if (mode == Modes.Three)
            _three = new ThreeGame();
        else
            _game = new Game();
    }

    public string Mode { get; }
    public IReadOnlyList<IPlayer> Players => _seats;
    public IPlayer White => _seats[0];
    public IPlayer Black => _seats[1];
    public Army? WhiteArmy { get; }
    public Army? BlackArmy { get; }
    public bool Finished { get; set; }

    /// <summary>The game counts for the rating (quick match, every player known by id).</summary>
    public bool Rated { get; init; }

    public int SeatOf(IPlayer player) => _seats.IndexOf(player);

    /// <summary>The seat whose turn it is.</summary>
    public int CurrentSeat
    {
        get
        {
            if (_three != null)
                return _three.Player;
            if (_team != null)
                return _team.Player;
            var side = _multiverse?.Mover ?? _timeTravel?.Current.SideToMove ?? _game!.Current.SideToMove;
            return side == PieceColor.White ? 0 : 1;
        }
    }

    public bool IsOver => _forcedResult != null || (_three?.IsOver ?? Status != GameStatus.Ongoing);

    /// <summary>The chess status for the modes that have one; "Тройной трон" reports Ongoing.</summary>
    public GameStatus Status
    {
        get
        {
            if (_three != null)
                return GameStatus.Ongoing;
            if (_team != null)
                return _team.Status;
            if (_multiverse != null)
            {
                return _multiverse.Result switch
                {
                    MultiverseResult.Checkmate => GameStatus.Checkmate,
                    MultiverseResult.Stalemate => GameStatus.Stalemate,
                    _ => GameStatus.Ongoing,
                };
            }
            return _timeTravel?.Status ?? _game!.Status;
        }
    }

    /// <summary>
    /// Who won, as sent in the "end" message: "white", "black" or "draw", and in "Тройной трон"
    /// the winning seat as "seat0", "seat1" or "seat2".
    /// </summary>
    public string Result
    {
        get
        {
            if (_forcedResult != null)
                return _forcedResult;
            if (_three != null)
                return _three.Winner.HasValue ? "seat" + _three.Winner.Value : "draw";
            var winner = _multiverse != null ? _multiverse.Winner : _team?.Winner ?? _timeTravel?.Winner ?? _game!.Winner;
            return winner == null ? "draw" : winner == PieceColor.White ? "white" : "black";
        }
    }

    public string Reason
    {
        get
        {
            if (_three != null)
                return _three.IsDraw ? "quiet_moves" : "last_king";
            return Status switch
            {
                GameStatus.Checkmate => "checkmate",
                GameStatus.Stalemate => "stalemate",
                GameStatus.FiftyMoveRule => "fifty_moves",
                GameStatus.ThreefoldRepetition => "repetition",
                _ => "insufficient_material",
            };
        }
    }

    /// <summary>The colour a seat plays for in two-player and team modes.</summary>
    public PieceColor ColorOf(int seat) => seat % 2 == 0 ? PieceColor.White : PieceColor.Black;

    /// <summary>
    /// A player gave up or left. In "Тройной трон" their army leaves the board and the other two
    /// play on (returns false while the game goes on); elsewhere their side loses at once.
    /// </summary>
    public bool Leave(int seat)
    {
        if (_three != null)
        {
            _three.Resign(seat);
            return _three.IsOver;
        }
        _forcedResult = ColorOf(seat) == PieceColor.White ? "black" : "white";
        return true;
    }

    /// <summary>Plays the action if it is legal; returns false otherwise and changes nothing.</summary>
    public bool TryPlay(string action)
    {
        if (IsOver)
            return false;
        if (_multiverse != null)
            return PlayTurn(action);
        if (_timeTravel != null)
            return _timeTravel.TryPlay(action);
        if (_team != null)
            return _team.TryPlay(action);
        if (_three != null)
            return _three.TryPlay(action);
        var move = _game!.FindMove(action);
        if (!move.HasValue)
            return false;
        _game.MakeMove(move.Value);
        return true;
    }

    /// <summary>
    /// In 5D chess one action is a whole turn: its moves separated by ';', for example
    /// "0,2,f3>0,0,h3;1,1,e7>1,1,e5". The turn ends at once, so a half-made turn never reaches the other player.
    /// </summary>
    private bool PlayTurn(string action)
    {
        var copy = _multiverse!.Clone();
        foreach (var part in action.Split(';'))
            if (part == "submit" || !copy.TryPlay(part))
                return false;
        if (!copy.TryPlay("submit"))
            return false;
        _multiverse = copy;
        return true;
    }
}

public static class Modes
{
    public const string Classic = "classic";
    public const string Army = "army";
    public const string TimeTravel = "timetravel";
    public const string FiveD = "5d";
    public const string Team = "team";
    public const string Three = "three";

    public static bool IsKnown(string? mode) =>
        mode == Classic || mode == Army || mode == TimeTravel || mode == FiveD || mode == Team || mode == Three;

    /// <summary>How many players a game of this mode needs.</summary>
    public static int PlayersIn(string mode) => mode == Team ? 4 : mode == Three ? 3 : 2;
}
