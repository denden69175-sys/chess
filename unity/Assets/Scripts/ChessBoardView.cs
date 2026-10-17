#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chess.Core;
using UnityEngine;

/// <summary>
/// Draws the board, lets the player drag pieces and plays the bot's replies.
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

    private readonly Dictionary<string, Sprite> _pieceSprites = new Dictionary<string, Sprite>();
    private readonly SpriteRenderer[] _overlays = new SpriteRenderer[64];
    private readonly SpriteRenderer[] _dots = new SpriteRenderer[64];
    private readonly Dictionary<int, SpriteRenderer> _pieces = new Dictionary<int, SpriteRenderer>();

    private Camera _camera = null!;
    private Sprite _squareSprite = null!;
    private Sprite _dotSprite = null!;

    private Game _game = new Game();
    private PieceColor _human = PieceColor.White;
    private BotLevel _level = BotLevel.Medium;

    private Square? _selected;
    private SpriteRenderer? _dragged;
    private bool _dragging;

    private Task<Move?>? _botTask;
    private CancellationTokenSource? _botCancel;

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

        _squareSprite = MakeSquareSprite();
        _dotSprite = MakeDotSprite(64);
        LoadPieceSprites();
        BuildBoard();
        NewGame(PieceColor.White);
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
        board.SetParent(transform, false);
        for (int i = 0; i < 64; i++)
        {
            var square = new Square(i);
            var tile = NewSprite("Square " + square, board, _squareSprite, 0);
            tile.color = (square.File + square.Rank) % 2 == 0 ? DarkSquare : LightSquare;
            tile.gameObject.name = square.ToString();

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

    private void NewGame(PieceColor human)
    {
        CancelBot();
        _human = human;
        _game = new Game();
        _selected = null;
        PlaceBoardTiles();
        Redraw();
        MaybeStartBot();
    }

    private void Undo()
    {
        CancelBot();
        // Take back the bot's reply and the player's own move, so it is the player's turn again.
        if (_game.Moves.Count > 0)
            _game.Undo();
        if (_game.Current.SideToMove != _human && _game.Moves.Count > 0)
            _game.Undo();
        _selected = null;
        Redraw();
        MaybeStartBot();
    }

    private void PlayMove(Move move)
    {
        _game.MakeMove(move);
        _selected = null;
        Redraw();
        MaybeStartBot();
    }

    private void MaybeStartBot()
    {
        if (_game.IsOver || _game.Current.SideToMove == _human)
            return;
        var position = _game.Current;
        var bot = new Bot(_level);
        _botCancel = new CancellationTokenSource();
        var token = _botCancel.Token;
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
        FitCamera();

        if (_botTask != null && _botTask.IsCompleted)
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

    // ---------------------------------------------------------------- input

    private void HandlePointer()
    {
        if (_game.IsOver || BotThinking || _game.Current.SideToMove != _human)
            return;

        Vector3 world = _camera.ScreenToWorldPoint(Input.mousePosition);
        Square? hovered = WorldToSquare(world);

        if (Input.GetMouseButtonDown(0))
        {
            // Second click of click-click play: a highlighted target square.
            if (_selected.HasValue && hovered.HasValue && TryMove(_selected.Value, hovered.Value))
                return;

            var piece = hovered.HasValue ? _game.Current[hovered.Value] : null;
            if (hovered.HasValue && piece.HasValue && piece.Value.Color == _human)
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

    private Vector3 SquareToWorld(Square square)
    {
        bool flip = _human == PieceColor.Black;
        float x = flip ? 3.5f - square.File : square.File - 3.5f;
        float y = flip ? 3.5f - square.Rank : square.Rank - 3.5f;
        return new Vector3(x, y, 0);
    }

    private Square? WorldToSquare(Vector3 world)
    {
        int file = Mathf.FloorToInt(world.x + 4f);
        int rank = Mathf.FloorToInt(world.y + 4f);
        if (!Square.IsOnBoard(file, rank))
            return null;
        if (_human == PieceColor.Black)
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
            var renderer = NewSprite(key, transform, sprite, 5);
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
                // A capture gets a bigger ring-like dot so the target piece stays visible.
                dot.transform.localScale = Vector3.one * (position[move.To].HasValue ? 0.9f : 0.32f);
            }
        }
    }

    private void Tint(Square square, Color color)
    {
        _overlays[square.Index].enabled = true;
        _overlays[square.Index].color = color;
    }

    private void FitCamera()
    {
        // Show the whole board plus the top panel, in landscape and in portrait.
        const float boardHalf = 4.2f;
        float aspect = Mathf.Max(0.1f, _camera.aspect);
        float halfHeight = Mathf.Max(boardHalf + TopPanel * 0.5f, boardHalf / aspect + TopPanel * 0.5f);
        _camera.orthographicSize = halfHeight;
        _camera.transform.position = new Vector3(0, TopPanel * 0.5f, -10);
    }

    // ---------------------------------------------------------------- UI

    private string StatusText()
    {
        bool humanWon = _game.Winner == _human;
        switch (_game.Status)
        {
            case GameStatus.Checkmate:
                return humanWon ? "Мат. Вы победили!" : "Мат. Победил бот.";
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
        return check ? "Шах! Ваш ход." : "Ваш ход.";
    }

    private static readonly string[] LevelNames = { "Лёгкий", "Средний", "Сложный" };

    private void OnGUI()
    {
        // The panel is laid out for a 720 px tall screen and scaled to the real one.
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;

        var label = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 16 };

        GUILayout.BeginArea(new Rect(10, 8, width - 20, 110));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Новая игра за белых", button, GUILayout.Height(34)))
            NewGame(PieceColor.White);
        if (GUILayout.Button("Новая игра за чёрных", button, GUILayout.Height(34)))
            NewGame(PieceColor.Black);
        GUI.enabled = _game.Moves.Count > 0;
        if (GUILayout.Button("Отменить ход", button, GUILayout.Height(34)))
            Undo();
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        int level = GUILayout.Toolbar((int)_level, LevelNames, button, GUILayout.Height(30), GUILayout.Width(330));
        _level = (BotLevel)level;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Label(StatusText(), label);
        GUILayout.EndArea();
    }
}
