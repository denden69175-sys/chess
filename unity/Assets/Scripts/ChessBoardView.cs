#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chess.Core;
using UnityEngine;
using Random = UnityEngine.Random;

public enum Opponent
{
    Bot,
    SameDevice,
    Online,
}

/// <summary>
/// The whole app: menu screens, the board, the move list, the bot, time travel and online play.
/// Everything is built from code, so the game starts in any scene, including an empty one.
/// The screens are in ChessBoardView.Menu.cs, the in-game interface in ChessBoardView.Hud.cs and
/// online play in ChessBoardView.Online.cs.
/// </summary>
public partial class ChessBoardView : MonoBehaviour
{
    private static readonly Color LightSquare = new Color32(238, 216, 182, 255);
    private static readonly Color DarkSquare = new Color32(178, 132, 96, 255);
    private static readonly Color LastMoveTint = new Color32(205, 210, 106, 150);
    private static readonly Color SelectedTint = new Color32(130, 151, 105, 170);
    private static readonly Color CheckTint = new Color32(220, 60, 60, 170);
    private static readonly Color JumpTint = new Color32(160, 100, 255, 150);
    private static readonly Color TargetDot = new Color32(20, 85, 30, 110);
    private static readonly Color GameBackground = new Color32(22, 18, 48, 255);

    // World units above the board kept free for the buttons and the status line.
    private const float TopPanel = 1.6f;

    // World units right of the board for the side panel, when the screen is wide enough.
    private const float SidePanel = 4.8f;

    // Half the height the board needs, with its frame and the letters on it.
    private const float BoardHalf = 4.6f;

    // The bot waits at least this long before moving, so its reply does not look instant.
    private const float BotMinDelay = 0.7f;
    private const float BotMaxDelay = 1.6f;

    private const float MoveAnimation = 0.18f;

    private readonly Dictionary<string, Sprite> _pieceSprites = new Dictionary<string, Sprite>();
    private readonly Dictionary<string, Texture2D> _pieceTextures = new Dictionary<string, Texture2D>();
    private readonly List<Texture2D> _floatingPieces = new List<Texture2D>();
    private readonly SpriteRenderer[] _overlays = new SpriteRenderer[64];
    private readonly SpriteRenderer[] _dots = new SpriteRenderer[64];
    private readonly Dictionary<int, SpriteRenderer> _pieces = new Dictionary<int, SpriteRenderer>();

    private Camera _camera = null!;
    private Sprite _squareSprite = null!;
    private Sprite _dotSprite = null!;
    private Transform _root = null!;

    // Settings of the current or next game.
    private Opponent _opponent = Opponent.Bot;
    private bool _timeTravel;
    private Army? _whiteArmy;
    private Army? _blackArmy;
    private string _botArmyName = "";
    private BotLevel _level = BotLevel.Medium;
    private int _menuColor;

    private TimeTravelGame _game = new TimeTravelGame();
    private PieceColor _human = PieceColor.White;

    /// <summary>What to highlight for each action played so far, in order.</summary>
    private readonly List<Mark> _marks = new List<Mark>();

    private Square? _selected;
    private SpriteRenderer? _dragged;
    private bool _dragging;

    // Time travel: choosing a piece to send back, then the board it lands on.
    private bool _jumpMode;
    private Square? _jumpPiece;

    // Promotion: the move waits for the player to pick a piece.
    private Square? _promotionFrom;
    private Square? _promotionTo;

    private Task<string?>? _botTask;
    private CancellationTokenSource? _botCancel;
    private float _botReadyAt;

    private SpriteRenderer? _animated;
    private Vector3 _animateFrom;
    private Vector3 _animateTo;
    private float _animateStart;
    private bool _animatePop;

    private bool _endShown;
    private bool _endHidden;
    private float _endAt;

    private float _jumpFlashAt = -100;

    private readonly struct Mark
    {
        public Mark(Square from, Square to, bool jump)
        {
            From = from;
            To = to;
            Jump = jump;
        }

        public Square From { get; }
        public Square To { get; }
        public bool Jump { get; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateIfMissing()
    {
        if (FindObjectOfType<ChessBoardView>() == null)
            new GameObject("Chess").AddComponent<ChessBoardView>();
    }

    private void Awake()
    {
        _camera = Camera.main;
        if (_camera == null)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            _camera = cameraObject.AddComponent<Camera>();
        }
        if (FindObjectOfType<AudioListener>() == null)
            _camera.gameObject.AddComponent<AudioListener>();
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = GameBackground;

        _root = new GameObject("Board Root").transform;
        _root.SetParent(transform, false);

        _squareSprite = MakeSquareSprite();
        _dotSprite = MakeDotSprite(64);
        LoadPieceSprites();
        BuildBoard();
        LoadMenuSettings();
        ShowPage(Page.Home);
    }

    private void OnDestroy()
    {
        _botCancel?.Cancel();
        _net?.Dispose();
    }

    // ---------------------------------------------------------------- setup

    private static Sprite MakeSquareSprite()
    {
        var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }

    private static Sprite MakeDotSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                float alpha = Mathf.Clamp01(radius - distance);
                texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>A white rounded square that can be stretched without bending its corners.</summary>
    private static Sprite MakeRoundedSprite(int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0);
                float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0);
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private void LoadPieceSprites()
    {
        foreach (var color in new[] { "w", "b" })
        {
            foreach (var type in new[] { "K", "Q", "R", "B", "N", "P" })
            {
                var texture = Resources.Load<Texture2D>("Pieces/" + color + type);
                if (texture == null)
                {
                    Debug.LogError($"Missing piece image Resources/Pieces/{color}{type}.png");
                    continue;
                }
                texture.filterMode = FilterMode.Bilinear;
                var rect = new Rect(0, 0, texture.width, texture.height);
                _pieceSprites[color + type] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), texture.width);
                _pieceTextures[color + type] = texture;
                _floatingPieces.Add(texture);
            }
        }
    }

    private static string PieceKey(Piece piece) =>
        (piece.Color == PieceColor.White ? "w" : "b") + char.ToUpperInvariant(piece.Symbol);

    private Texture2D? PieceTexture(PieceType type, PieceColor color)
    {
        _pieceTextures.TryGetValue(PieceKey(new Piece(type, color)), out var texture);
        return texture;
    }

    private void BuildBoard()
    {
        var rounded = MakeRoundedSprite(64, 16);
        var shadow = NewSprite("Board Shadow", _root, rounded, -3);
        shadow.drawMode = SpriteDrawMode.Sliced;
        shadow.size = new Vector2(8.9f, 8.9f);
        shadow.color = new Color(0, 0, 0, 0.45f);
        shadow.transform.localPosition = new Vector3(0.06f, -0.14f, 0);

        var frame = NewSprite("Board Frame", _root, rounded, -2);
        frame.drawMode = SpriteDrawMode.Sliced;
        frame.size = new Vector2(8.7f, 8.7f);
        frame.color = new Color32(92, 60, 40, 255);

        var board = new GameObject("Board").transform;
        board.SetParent(_root, false);
        for (int i = 0; i < 64; i++)
        {
            var square = new Square(i);
            var tile = NewSprite(square.ToString(), board, _squareSprite, 0);
            tile.color = (square.File + square.Rank) % 2 == 0 ? DarkSquare : LightSquare;

            _overlays[i] = NewSprite("Overlay " + square, tile.transform, _squareSprite, 1);
            _overlays[i].enabled = false;

            _dots[i] = NewSprite("Dot " + square, tile.transform, _dotSprite, 2);
            _dots[i].color = TargetDot;
            _dots[i].transform.localScale = Vector3.one * 0.32f;
            _dots[i].enabled = false;
        }
    }

    private static SpriteRenderer NewSprite(string name, Transform parent, Sprite sprite, int order)
    {
        var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
        renderer.transform.SetParent(parent, false);
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return renderer;
    }

    // ---------------------------------------------------------------- game flow

    /// <summary>Starts a game with the current settings (<see cref="_opponent"/>, armies, time travel).</summary>
    private void StartGame()
    {
        CancelBot();
        var start = _whiteArmy != null || _blackArmy != null
            ? Army.ToPosition(_whiteArmy ?? Army.Classic, _blackArmy ?? Army.Classic)
            : Position.Start;
        _game = new TimeTravelGame(start);
        _marks.Clear();
        _selected = null;
        _jumpMode = false;
        _jumpPiece = null;
        _promotionFrom = null;
        _promotionTo = null;
        _endShown = false;
        _endHidden = false;
        _onlineEnded = false;
        _waitingForServer = false;
        _notice = "";
        _moveListScroll = Vector2.zero;
        _root.gameObject.SetActive(true);
        ShowPage(Page.Game);
        PlaceBoardTiles();
        Redraw();
        Sfx.Play(Sfx.Start);
        MaybeStartBot();
    }

    /// <summary>The colour the player gets against the bot, from the menu choice.</summary>
    private PieceColor ChosenColor() =>
        _menuColor == 0 ? PieceColor.White
        : _menuColor == 1 ? PieceColor.Black
        : (Random.value < 0.5f ? PieceColor.White : PieceColor.Black);

    private void StartBotGame(bool timeTravel, Army? playerArmy)
    {
        _opponent = Opponent.Bot;
        _timeTravel = timeTravel;
        _human = ChosenColor();
        if (playerArmy != null)
        {
            // The bot fights with one of the ready-made armies.
            var presets = Army.Presets;
            var pick = presets[Random.Range(0, presets.Count)];
            _botArmyName = pick.Name;
            _whiteArmy = _human == PieceColor.White ? playerArmy : pick.Army;
            _blackArmy = _human == PieceColor.Black ? playerArmy : pick.Army;
        }
        else
        {
            _botArmyName = "";
            _whiteArmy = null;
            _blackArmy = null;
        }
        StartGame();
    }

    private void StartSameDeviceGame(bool timeTravel)
    {
        _opponent = Opponent.SameDevice;
        _timeTravel = timeTravel;
        _human = PieceColor.White;
        _whiteArmy = null;
        _blackArmy = null;
        StartGame();
    }

    private void Rematch()
    {
        if (_page == Page.FiveD)
            RematchFiveD();
        else if (_opponent == Opponent.Bot)
            StartBotGame(_timeTravel, _botArmyName.Length > 0 ? PlayerArmy() : null);
        else
            StartGame();
    }

    /// <summary>Whose pieces the person at the screen may move right now.</summary>
    private bool PlayerMayMove =>
        _page == Page.Game && !_game.IsOver && !BotThinking && !_waitingForServer && !_onlineEnded
        && _promotionFrom == null && _jumpPiece == null
        && (_opponent == Opponent.SameDevice || _game.Current.SideToMove == _human);

    private bool CanUndo => _opponent != Opponent.Online && _game.ActionCount > 0;

    private void Undo()
    {
        CancelBot();
        UndoOne();
        // Against the bot, also take back its reply so it is the player's turn again.
        if (_opponent == Opponent.Bot && _game.Current.SideToMove != _human && _game.ActionCount > 0)
            UndoOne();
        _selected = null;
        _jumpMode = false;
        _notice = "";
        _endShown = false;
        _endHidden = false;
        Redraw();
        MaybeStartBot();
    }

    private void UndoOne()
    {
        if (_game.ActionCount == 0)
            return;
        _game.Undo();
        _marks.RemoveAt(_marks.Count - 1);
    }

    /// <summary>The player chose a move on the board. Online, the server confirms it first.</summary>
    private void PlayerMove(Move move)
    {
        if (_opponent == Opponent.Online)
        {
            SendAction(move.ToUci());
            _selected = null;
            RedrawHighlights();
            return;
        }
        Apply(move);
    }

    private void PlayerJump(TimeJump jump)
    {
        _jumpPiece = null;
        if (_opponent == Opponent.Online)
        {
            _jumpMode = false;
            SendAction(jump.ToString());
            RedrawHighlights();
            return;
        }
        Apply(jump);
    }

    private void Apply(Move move)
    {
        bool capture = _game.Current[move.To].HasValue || move.Kind == MoveKind.EnPassant;
        _game.MakeMove(move);
        _marks.Add(new Mark(move.From, move.To, false));
        AfterAction(capture ? Sfx.Capture : Sfx.Move);
        AnimateSlide(move.From, move.To);
    }

    private void Apply(TimeJump jump)
    {
        _game.Jump(jump);
        _marks.Add(new Mark(jump.From, jump.From, true));
        _jumpFlashAt = Time.time;
        AfterAction(Sfx.Jump);
        AnimatePop(jump.From);
    }

    /// <summary>Plays a move ("e2e4") or a jump ("jump:e4:2") from the bot or the server.</summary>
    private bool ApplyText(string action)
    {
        if (TimeJump.TryParse(action, out var jump))
        {
            if (!_game.IsLegalJump(jump))
                return false;
            Apply(jump);
            return true;
        }
        var move = _game.Active.FindMove(action);
        if (!move.HasValue)
            return false;
        Apply(move.Value);
        return true;
    }

    private void AfterAction(AudioClip sound)
    {
        _selected = null;
        _jumpMode = false;
        _jumpPiece = null;
        _notice = "";
        Redraw();
        if (_game.IsOver)
        {
            Sfx.Play(sound);
            _endAt = Time.time + 0.9f;
        }
        else
        {
            Sfx.Play(_game.Current.IsInCheck(_game.Current.SideToMove) ? Sfx.Check : sound);
        }
        MaybeStartBot();
    }

    private void MaybeStartBot()
    {
        if (_opponent != Opponent.Bot || _game.IsOver || _game.Current.SideToMove == _human)
            return;
        var bot = new Bot(_level);
        _botCancel = new CancellationTokenSource();
        var token = _botCancel.Token;
        _botReadyAt = Time.time + Random.Range(BotMinDelay, BotMaxDelay);
        if (_timeTravel)
        {
            var choice = _game.SnapshotForBot();
            _botTask = Task.Run(() => bot.ChooseTimeTravelAction(choice, token), token);
        }
        else
        {
            var position = _game.Current;
            _botTask = Task.Run(() => bot.ChooseMove(position, token)?.ToUci(), token);
        }
    }

    private void CancelBot()
    {
        _botCancel?.Cancel();
        _botCancel = null;
        _botTask = null;
    }

    private bool BotThinking => _botTask != null;

    private void Update()
    {
        PollNetwork();
        if (_page == Page.FiveD)
            UpdateFiveD();
        else if (_page == Page.Team)
            UpdateTeam();
        else if (_page == Page.Three)
            UpdateThree();
        if (_page != Page.Game)
            return;

        FitCamera();
        Animate();

        // The search usually finishes quickly; the move is shown only after the pause.
        if (_botTask != null && _botTask.IsCompleted && Time.time >= _botReadyAt)
        {
            var task = _botTask;
            _botTask = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                ApplyText(task.Result);
            else if (task.IsFaulted)
                Debug.LogException(task.Exception);
        }

        if (GameFinished && !_endShown && Time.time >= _endAt)
        {
            _endShown = true;
            Sfx.Play(PlayerWon == false ? Sfx.Lose : Sfx.Win);
        }

        HandlePointer();
    }

    private void SaveGame()
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Games");
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string file;
            if (_timeTravel)
            {
                // A game with jumps is not a normal chess game, so it is saved as a numbered list.
                file = Path.Combine(folder, stamp + "_время.txt");
                var text = new StringBuilder("Путешествие во времени, " + DateTime.Now.ToString("dd.MM.yyyy") + "\n");
                var log = _game.RussianLog;
                for (int i = 0; i < log.Count; i += 2)
                    text.Append($"{i / 2 + 1}. {log[i]} {(i + 1 < log.Count ? log[i + 1] : "")}\n");
                File.WriteAllText(file, text.ToString());
            }
            else
            {
                file = Path.Combine(folder, stamp + ".pgn");
                File.WriteAllText(file, _game.Active.ToPgn(PlayerName(PieceColor.White), PlayerName(PieceColor.Black),
                    DateTime.Now.ToString("yyyy.MM.dd")));
            }
            _notice = "Сохранено: " + Path.GetFileName(file);
            Debug.Log("Game saved to " + file);
        }
        catch (Exception e)
        {
            _notice = "Не удалось сохранить: " + e.Message;
        }
    }

    // ---------------------------------------------------------------- input

    private void HandlePointer()
    {
        if (!PlayerMayMove || Time.unscaledTime - Theme.LastClickTime < 0.25f)
            return;

        var mover = _game.Current.SideToMove;
        Vector3 world = _camera.ScreenToWorldPoint(Input.mousePosition);
        Square? hovered = WorldToSquare(world);

        if (_jumpMode)
        {
            // Pick the piece to send back in time; the boards to choose from open in a window.
            if (Input.GetMouseButtonDown(0) && hovered.HasValue && JumpTargets(hovered.Value).Count > 0)
            {
                _jumpPiece = hovered;
                Sfx.Play(Sfx.Click);
                RedrawHighlights();
            }
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            // Second click of click-click play: a highlighted target square.
            if (_selected.HasValue && hovered.HasValue && TryMove(_selected.Value, hovered.Value))
                return;

            var piece = hovered.HasValue ? _game.Current[hovered.Value] : null;
            if (hovered.HasValue && piece.HasValue && piece.Value.Color == mover)
            {
                _selected = hovered;
                _dragging = true;
                _pieces.TryGetValue(hovered.Value.Index, out var renderer);
                _dragged = renderer;
                if (_dragged != null)
                    _dragged.sortingOrder = 10;
            }
            else if (hovered.HasValue)
            {
                _selected = null;
            }
            RedrawHighlights();
        }

        if (_dragging && _dragged != null && Input.GetMouseButton(0))
            _dragged.transform.position = new Vector3(world.x, world.y, 0);

        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            var from = _selected;
            if (from.HasValue && hovered.HasValue && hovered.Value != from.Value && TryMove(from.Value, hovered.Value))
                return;
            // Dropped back on its square or somewhere illegal: put the piece back, keep it selected.
            RedrawPieces();
        }
    }

    private bool TryMove(Square from, Square to)
    {
        var move = _game.Active.FindMove(from, to);
        if (!move.HasValue && _game.Active.FindMove(from, to, PieceType.Queen).HasValue)
        {
            // A pawn reaches the last rank: ask which piece it becomes.
            _dragging = false;
            _dragged = null;
            _promotionFrom = from;
            _promotionTo = to;
            RedrawPieces();
            return true;
        }
        if (!move.HasValue)
            return false;
        _dragging = false;
        _dragged = null;
        PlayerMove(move.Value);
        return true;
    }

    private void Promote(PieceType type)
    {
        var move = _game.Active.FindMove(_promotionFrom!.Value, _promotionTo!.Value, type);
        _promotionFrom = null;
        _promotionTo = null;
        if (move.HasValue)
            PlayerMove(move.Value);
    }

    /// <summary>The jumps the piece on <paramref name="from"/> can make now.</summary>
    private List<TimeJump> JumpTargets(Square from)
    {
        var result = new List<TimeJump>();
        foreach (var jump in _game.LegalJumps())
            if (jump.From == from)
                result.Add(jump);
        return result;
    }

    // ---------------------------------------------------------------- drawing

    private bool Flipped => _opponent != Opponent.SameDevice && _human == PieceColor.Black;

    private Vector3 SquareToWorld(Square square)
    {
        float x = Flipped ? 3.5f - square.File : square.File - 3.5f;
        float y = Flipped ? 3.5f - square.Rank : square.Rank - 3.5f;
        return new Vector3(x, y, 0);
    }

    private Square? WorldToSquare(Vector3 world)
    {
        int file = Mathf.FloorToInt(world.x + 4f);
        int rank = Mathf.FloorToInt(world.y + 4f);
        if (!Square.IsOnBoard(file, rank))
            return null;
        if (Flipped)
        {
            file = 7 - file;
            rank = 7 - rank;
        }
        return new Square(file, rank);
    }

    private void PlaceBoardTiles()
    {
        for (int i = 0; i < 64; i++)
            _overlays[i].transform.parent.position = SquareToWorld(new Square(i));
    }

    private void Redraw()
    {
        RedrawPieces();
        RedrawHighlights();
    }

    private void RedrawPieces()
    {
        foreach (var renderer in _pieces.Values)
            Destroy(renderer.gameObject);
        _pieces.Clear();
        _dragged = null;
        _animated = null;

        var position = _game.Current;
        for (int i = 0; i < 64; i++)
        {
            var piece = position[i];
            if (!piece.HasValue)
                continue;
            string key = PieceKey(piece.Value);
            if (!_pieceSprites.TryGetValue(key, out var sprite))
                continue;
            var renderer = NewSprite(key, _root, sprite, 5);
            renderer.transform.position = SquareToWorld(new Square(i));
            renderer.transform.localScale = Vector3.one * 0.95f;
            _pieces[i] = renderer;
        }
    }

    private void AnimateSlide(Square from, Square to)
    {
        if (!_pieces.TryGetValue(to.Index, out var renderer))
            return;
        _animated = renderer;
        _animateFrom = SquareToWorld(from);
        _animateTo = SquareToWorld(to);
        _animateStart = Time.time;
        _animatePop = false;
        renderer.sortingOrder = 8;
        renderer.transform.position = _animateFrom;
    }

    private void AnimatePop(Square square)
    {
        if (!_pieces.TryGetValue(square.Index, out var renderer))
            return;
        _animated = renderer;
        _animateTo = SquareToWorld(square);
        _animateStart = Time.time;
        _animatePop = true;
        renderer.transform.localScale = Vector3.zero;
    }

    private void Animate()
    {
        if (_animated == null)
            return;
        float duration = _animatePop ? 0.45f : MoveAnimation;
        float t = Mathf.Clamp01((Time.time - _animateStart) / duration);
        float eased = 1 - (1 - t) * (1 - t);
        if (_animatePop)
        {
            // Overshoot a little, like the piece arriving through a portal.
            float scale = t < 1 ? Mathf.Sin(t * Mathf.PI * 0.75f) / Mathf.Sin(Mathf.PI * 0.75f) : 1;
            _animated.transform.localScale = Vector3.one * 0.95f * scale;
            _animated.transform.rotation = Quaternion.Euler(0, 0, (1 - eased) * 360);
        }
        else
        {
            _animated.transform.position = Vector3.Lerp(_animateFrom, _animateTo, eased);
        }
        if (t >= 1)
        {
            _animated.sortingOrder = 5;
            _animated.transform.rotation = Quaternion.identity;
            _animated = null;
        }
    }

    private void RedrawHighlights()
    {
        for (int i = 0; i < 64; i++)
        {
            _overlays[i].enabled = false;
            _dots[i].enabled = false;
        }

        if (_marks.Count > 0)
        {
            var last = _marks[_marks.Count - 1];
            if (last.Jump)
            {
                Tint(last.From, JumpTint);
            }
            else
            {
                Tint(last.From, LastMoveTint);
                Tint(last.To, LastMoveTint);
            }
        }

        var position = _game.Current;
        if (position.IsInCheck(position.SideToMove))
        {
            var king = position.FindKing(position.SideToMove);
            if (king.HasValue)
                Tint(king.Value, CheckTint);
        }

        if (_jumpMode)
        {
            // Every piece that can travel glows purple.
            foreach (var jump in _game.LegalJumps())
                Tint(jump.From, _jumpPiece == jump.From ? SelectedTint : JumpTint);
            return;
        }

        if (_selected.HasValue)
        {
            Tint(_selected.Value, SelectedTint);
            foreach (var move in _game.Active.LegalMoves)
            {
                if (move.From != _selected.Value)
                    continue;
                var dot = _dots[move.To.Index];
                dot.enabled = true;
                // A capture gets a bigger dot so the target piece stays visible.
                dot.transform.localScale = Vector3.one * (position[move.To].HasValue ? 0.9f : 0.32f);
            }
        }
    }

    private void Tint(Square square, Color color)
    {
        _overlays[square.Index].enabled = true;
        _overlays[square.Index].color = color;
    }

    /// <summary>True when there is room for the side panel to the right of the board.</summary>
    private bool WideLayout => _camera.aspect * (BoardHalf + TopPanel * 0.5f) * 2 >= BoardHalf * 2 + SidePanel;

    private void FitCamera()
    {
        float aspect = Mathf.Max(0.1f, _camera.aspect);
        if (WideLayout)
        {
            // Board on the left, side panel on the right.
            float halfHeight = BoardHalf + TopPanel * 0.5f;
            _camera.orthographicSize = halfHeight;
            float halfWidth = halfHeight * aspect;
            // Centre the board and the panel together; the board's left edge sits at -BoardHalf.
            float used = BoardHalf * 2 + SidePanel;
            float margin = Mathf.Max(0.3f, (halfWidth * 2 - used) / 2);
            _camera.transform.position = new Vector3(halfWidth - margin - BoardHalf, TopPanel * 0.5f, -10);
        }
        else
        {
            // Narrow or portrait screen: board in the middle, a short move line under the status.
            float halfHeight = Mathf.Max(BoardHalf + TopPanel * 0.5f, BoardHalf / aspect + TopPanel * 0.5f);
            _camera.orthographicSize = halfHeight;
            _camera.transform.position = new Vector3(0, TopPanel * 0.5f, -10);
        }
    }
}
