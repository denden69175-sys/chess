#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chess.Core;
using UnityEngine;
using Random = UnityEngine.Random;

public enum GameMode
{
    VsBot,
    TwoPlayers,
}

/// <summary>
/// The start menu, the board, the move list and the bot's replies.
/// Everything is built from code, so the game starts in any scene, including an empty one.
/// </summary>
public class ChessBoardView : MonoBehaviour
{
    private static readonly Color LightSquare = new Color32(240, 217, 181, 255);
    private static readonly Color DarkSquare = new Color32(181, 136, 99, 255);
    private static readonly Color LastMoveTint = new Color32(205, 210, 106, 150);
    private static readonly Color SelectedTint = new Color32(130, 151, 105, 170);
    private static readonly Color CheckTint = new Color32(220, 60, 60, 170);
    private static readonly Color TargetDot = new Color32(20, 85, 30, 110);
    private static readonly Color Background = new Color32(48, 46, 43, 255);

    // World units above the board kept free for the buttons and the status line.
    private const float TopPanel = 1.6f;

    // World units right of the board for the move list, when the screen is wide enough.
    private const float SidePanel = 4.6f;

    private const float BoardHalf = 4.2f;

    // The bot waits at least this long before moving, so its reply does not look instant.
    private const float BotMinDelay = 0.7f;
    private const float BotMaxDelay = 1.6f;

    private static readonly string[] LevelNames = { "Лёгкий", "Средний", "Сложный" };
    private static readonly string[] ColorNames = { "Белые", "Чёрные", "Случайно" };

    private readonly Dictionary<string, Sprite> _pieceSprites = new Dictionary<string, Sprite>();
    private readonly SpriteRenderer[] _overlays = new SpriteRenderer[64];
    private readonly SpriteRenderer[] _dots = new SpriteRenderer[64];
    private readonly Dictionary<int, SpriteRenderer> _pieces = new Dictionary<int, SpriteRenderer>();

    private Camera _camera = null!;
    private Sprite _squareSprite = null!;
    private Sprite _dotSprite = null!;
    private Transform _root = null!;

    private bool _inMenu = true;
    private GameMode _mode = GameMode.VsBot;
    private int _menuColor;
    private BotLevel _level = BotLevel.Medium;

    private Game _game = new Game();
    private PieceColor _human = PieceColor.White;

    private Square? _selected;
    private SpriteRenderer? _dragged;
    private bool _dragging;

    private Task<Move?>? _botTask;
    private CancellationTokenSource? _botCancel;
    private float _botReadyAt;

    private List<string> _russianMoves = new List<string>();
    private Game? _russianMovesGame;
    private int _russianMovesCount = -1;

    private Vector2 _moveListScroll;
    private int _shownMoveCount;
    private string _notice = "";

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
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Background;

        _root = new GameObject("Board Root").transform;
        _root.SetParent(transform, false);

        _squareSprite = MakeSquareSprite();
        _dotSprite = MakeDotSprite(64);
        LoadPieceSprites();
        BuildBoard();
        ShowMenu();
    }

    private void OnDestroy() => _botCancel?.Cancel();

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
            }
        }
    }

    private void BuildBoard()
    {
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

    private void ShowMenu()
    {
        CancelBot();
        _inMenu = true;
        _root.gameObject.SetActive(false);
    }

    private void StartGame(GameMode mode)
    {
        CancelBot();
        _mode = mode;
        _human = mode == GameMode.TwoPlayers || _menuColor == 0 ? PieceColor.White
            : _menuColor == 1 ? PieceColor.Black
            : (Random.value < 0.5f ? PieceColor.White : PieceColor.Black);
        _game = new Game();
        _selected = null;
        _notice = "";
        _moveListScroll = Vector2.zero;
        _inMenu = false;
        _root.gameObject.SetActive(true);
        PlaceBoardTiles();
        Redraw();
        MaybeStartBot();
    }

    /// <summary>Whose pieces the person at the screen may move right now.</summary>
    private bool PlayerMayMove =>
        !_game.IsOver && !BotThinking && (_mode == GameMode.TwoPlayers || _game.Current.SideToMove == _human);

    private void Undo()
    {
        CancelBot();
        if (_game.Moves.Count > 0)
            _game.Undo();
        // Against the bot, also take back its reply so it is the player's turn again.
        if (_mode == GameMode.VsBot && _game.Current.SideToMove != _human && _game.Moves.Count > 0)
            _game.Undo();
        _selected = null;
        _notice = "";
        Redraw();
        MaybeStartBot();
    }

    private void PlayMove(Move move)
    {
        _game.MakeMove(move);
        _selected = null;
        _notice = "";
        Redraw();
        MaybeStartBot();
    }

    private void MaybeStartBot()
    {
        if (_mode != GameMode.VsBot || _game.IsOver || _game.Current.SideToMove == _human)
            return;
        var position = _game.Current;
        var bot = new Bot(_level);
        _botCancel = new CancellationTokenSource();
        var token = _botCancel.Token;
        _botReadyAt = Time.time + Random.Range(BotMinDelay, BotMaxDelay);
        _botTask = Task.Run(() => bot.ChooseMove(position, token), token);
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
        if (_inMenu)
            return;

        FitCamera();

        // The search usually finishes quickly; the move is shown only after the pause.
        if (_botTask != null && _botTask.IsCompleted && Time.time >= _botReadyAt)
        {
            var task = _botTask;
            _botTask = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result.HasValue)
                PlayMove(task.Result.Value);
            else if (task.IsFaulted)
                Debug.LogException(task.Exception);
        }

        HandlePointer();
    }

    private void SaveGame()
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Games");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".pgn");
            string white = "Белые", black = "Чёрные";
            if (_mode == GameMode.VsBot)
            {
                white = _human == PieceColor.White ? "Игрок" : "Бот";
                black = _human == PieceColor.Black ? "Игрок" : "Бот";
            }
            File.WriteAllText(file, _game.ToPgn(white, black, DateTime.Now.ToString("yyyy.MM.dd")));
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
        if (!PlayerMayMove)
            return;

        var mover = _game.Current.SideToMove;
        Vector3 world = _camera.ScreenToWorldPoint(Input.mousePosition);
        Square? hovered = WorldToSquare(world);

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
            else
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
        // Promotion always picks a queen for now; a choice dialog comes later.
        var move = _game.FindMove(from, to) ?? _game.FindMove(from, to, PieceType.Queen);
        if (!move.HasValue)
            return false;
        _dragging = false;
        _dragged = null;
        PlayMove(move.Value);
        return true;
    }

    // ---------------------------------------------------------------- drawing

    private bool Flipped => _mode == GameMode.VsBot && _human == PieceColor.Black;

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

        var position = _game.Current;
        for (int i = 0; i < 64; i++)
        {
            var piece = position[i];
            if (!piece.HasValue)
                continue;
            string key = (piece.Value.Color == PieceColor.White ? "w" : "b")
                + char.ToUpperInvariant(piece.Value.Symbol);
            if (!_pieceSprites.TryGetValue(key, out var sprite))
                continue;
            var renderer = NewSprite(key, _root, sprite, 5);
            renderer.transform.position = SquareToWorld(new Square(i));
            renderer.transform.localScale = Vector3.one * 0.95f;
            _pieces[i] = renderer;
        }
    }

    private void RedrawHighlights()
    {
        for (int i = 0; i < 64; i++)
        {
            _overlays[i].enabled = false;
            _dots[i].enabled = false;
        }

        if (_game.Moves.Count > 0)
        {
            var last = _game.Moves[_game.Moves.Count - 1];
            Tint(last.From, LastMoveTint);
            Tint(last.To, LastMoveTint);
        }

        var position = _game.Current;
        if (position.IsInCheck(position.SideToMove))
        {
            var king = position.FindKing(position.SideToMove);
            if (king.HasValue)
                Tint(king.Value, CheckTint);
        }

        if (_selected.HasValue)
        {
            Tint(_selected.Value, SelectedTint);
            foreach (var move in _game.LegalMoves)
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

    /// <summary>True when there is room for the move list to the right of the board.</summary>
    private bool WideLayout => _camera.aspect * (BoardHalf + TopPanel * 0.5f) * 2 >= BoardHalf * 2 + SidePanel;

    private void FitCamera()
    {
        float aspect = Mathf.Max(0.1f, _camera.aspect);
        if (WideLayout)
        {
            // Board on the left, move list on the right.
            float halfHeight = BoardHalf + TopPanel * 0.5f;
            _camera.orthographicSize = halfHeight;
            float halfWidth = halfHeight * aspect;
            // Centre the board and the list together; the board's left edge sits at -BoardHalf.
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

    // ---------------------------------------------------------------- UI

    private string ColorName(PieceColor color, bool genitive) =>
        color == PieceColor.White ? (genitive ? "белых" : "белые") : (genitive ? "чёрных" : "чёрные");

    private string StatusText()
    {
        switch (_game.Status)
        {
            case GameStatus.Checkmate:
                if (_mode == GameMode.TwoPlayers)
                    return $"Мат. Победили {ColorName(_game.Winner!.Value, false)}!";
                return _game.Winner == _human ? "Мат. Вы победили!" : "Мат. Победил бот.";
            case GameStatus.Stalemate:
                return "Пат. Ничья.";
            case GameStatus.FiftyMoveRule:
                return "Ничья по правилу 50 ходов.";
            case GameStatus.ThreefoldRepetition:
                return "Ничья: позиция повторилась три раза.";
            case GameStatus.InsufficientMaterial:
                return "Ничья: никто не может поставить мат.";
        }

        if (BotThinking)
            return "Бот думает...";
        bool check = _game.Current.IsInCheck(_game.Current.SideToMove);
        string turn = _mode == GameMode.TwoPlayers
            ? $"Ход {ColorName(_game.Current.SideToMove, true)}."
            : "Ваш ход.";
        return check ? "Шах! " + turn : turn;
    }

    private GUIStyle Style(GUIStyle baseStyle, int size, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var style = new GUIStyle(baseStyle) { fontSize = size, alignment = anchor };
        style.normal.textColor = Color.white;
        return style;
    }

    private void OnGUI()
    {
        // Layout is written for a 720 px tall screen and scaled to the real one.
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;

        if (_inMenu)
            DrawMenu(width);
        else
            DrawGameUi(width, scale);
    }

    private void DrawMenu(float width)
    {
        var title = Style(GUI.skin.label, 48);
        var heading = Style(GUI.skin.label, 20);
        var small = Style(GUI.skin.label, 14);
        var button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
        var toolbar = new GUIStyle(GUI.skin.button) { fontSize = 15 };

        float panelWidth = Mathf.Min(440, width - 40);
        GUILayout.BeginArea(new Rect((width - panelWidth) / 2, 60, panelWidth, 640));

        GUILayout.Label("Шахматы", title, GUILayout.Height(70));
        GUILayout.Space(20);

        GUILayout.Label("Игра против бота", heading);
        GUILayout.Label("Ваш цвет", small);
        _menuColor = GUILayout.Toolbar(_menuColor, ColorNames, toolbar, GUILayout.Height(32));
        GUILayout.Space(6);
        GUILayout.Label("Сложность", small);
        _level = (BotLevel)GUILayout.Toolbar((int)_level, LevelNames, toolbar, GUILayout.Height(32));
        GUILayout.Space(10);
        if (GUILayout.Button("Играть против бота", button, GUILayout.Height(48)))
            StartGame(GameMode.VsBot);

        GUILayout.Space(24);
        if (GUILayout.Button("Вдвоём на одном устройстве", button, GUILayout.Height(48)))
            StartGame(GameMode.TwoPlayers);

        GUILayout.Space(24);
        GUILayout.Label("Скоро", heading);
        GUI.enabled = false;
        GUILayout.Button("Путешествие во времени", button, GUILayout.Height(40));
        GUILayout.Button("Онлайн со своей армией", button, GUILayout.Height(40));
        GUI.enabled = true;

        GUILayout.EndArea();
    }

    private void DrawGameUi(float width, float scale)
    {
        var status = Style(GUI.skin.label, 22);
        var button = new GUIStyle(GUI.skin.button) { fontSize = 16 };

        GUILayout.BeginArea(new Rect(10, 8, width - 20, 110));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Меню", button, GUILayout.Height(34), GUILayout.Width(110)))
            ShowMenu();
        if (GUILayout.Button("Новая партия", button, GUILayout.Height(34), GUILayout.Width(160)))
            StartGame(_mode);
        GUI.enabled = _game.Moves.Count > 0;
        if (GUILayout.Button("Отменить ход", button, GUILayout.Height(34), GUILayout.Width(160)))
            Undo();
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(StatusText(), status, GUILayout.Height(40));
        GUILayout.EndArea();

        if (_inMenu)
            return;

        if (WideLayout)
            DrawMoveList(scale);
        else
            DrawLastMoves(width);

    }

    /// <summary>The moves in Russian notation, recomputed only when a move is made or taken back.</summary>
    private List<string> RussianMoves()
    {
        if (_russianMovesGame != _game || _russianMovesCount != _game.Moves.Count)
        {
            _russianMoves = _game.MovesIn(NotationLanguage.Russian);
            _russianMovesGame = _game;
            _russianMovesCount = _game.Moves.Count;
        }
        return _russianMoves;
    }

    /// <summary>The full move list in a column right of the board.</summary>
    private void DrawMoveList(float scale)
    {
        // Place the column from the board's right edge to the right side of the screen.
        Vector3 boardRight = _camera.WorldToScreenPoint(new Vector3(BoardHalf, BoardHalf, 0));
        Vector3 boardBottom = _camera.WorldToScreenPoint(new Vector3(BoardHalf, -BoardHalf, 0));
        float left = boardRight.x / scale + 16;
        float top = (Screen.height - boardRight.y) / scale;
        float bottom = (Screen.height - boardBottom.y) / scale;
        float columnWidth = Mathf.Min(260, Screen.width / scale - left - 16);
        if (columnWidth < 120)
            return;

        var header = Style(GUI.skin.label, 18, TextAnchor.MiddleLeft);
        var row = Style(GUI.skin.label, 16, TextAnchor.MiddleLeft);
        var button = new GUIStyle(GUI.skin.button) { fontSize = 14 };

        GUI.Box(new Rect(left, top, columnWidth, bottom - top), GUIContent.none);
        GUILayout.BeginArea(new Rect(left + 8, top + 6, columnWidth - 16, bottom - top - 12));
        GUILayout.Label("Ходы", header);

        var moves = RussianMoves();
        if (moves.Count != _shownMoveCount)
        {
            // New move: scroll to the end of the list.
            _shownMoveCount = moves.Count;
            _moveListScroll.y = float.MaxValue;
        }

        _moveListScroll = GUILayout.BeginScrollView(_moveListScroll, GUILayout.ExpandHeight(true));
        for (int i = 0; i < moves.Count; i += 2)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{i / 2 + 1}.", row, GUILayout.Width(40));
            GUILayout.Label(moves[i], row, GUILayout.Width(90));
            GUILayout.Label(i + 1 < moves.Count ? moves[i + 1] : "", row, GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        if (_notice.Length > 0)
        {
            var notice = Style(GUI.skin.label, 13);
            notice.wordWrap = true;
            GUILayout.Label(_notice, notice);
        }
        GUI.enabled = _game.Moves.Count > 0;
        if (GUILayout.Button("Сохранить партию", button, GUILayout.Height(30)))
            SaveGame();
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    /// <summary>On narrow screens: the last few moves in one line under the status.</summary>
    private void DrawLastMoves(float width)
    {
        var line = Style(GUI.skin.label, 15);
        if (_notice.Length > 0)
        {
            GUI.Label(new Rect(10, 104, width - 20, 24), _notice, line);
            return;
        }
        var moves = RussianMoves();
        if (moves.Count == 0)
            return;
        int start = Mathf.Max(0, moves.Count - 6);
        if (start % 2 == 1)
            start--;
        var parts = new List<string>();
        for (int i = start; i < moves.Count; i++)
            parts.Add(i % 2 == 0 ? $"{i / 2 + 1}. {moves[i]}" : moves[i]);
        GUI.Label(new Rect(10, 104, width - 20, 24), string.Join("  ", parts), line);
    }
}
