#nullable enable
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's look: colours, fonts and drawing helpers for the IMGUI screens. Everything is drawn
/// with rounded, gradient shapes made in code, so there are no image assets to keep in sync.
/// Call <see cref="Init"/> at the start of OnGUI.
/// </summary>
public static class Theme
{
    public static readonly Color Text = new Color32(246, 243, 255, 255);
    public static readonly Color Muted = new Color32(172, 166, 210, 255);
    public static readonly Color Gold = new Color32(255, 204, 77, 255);
    public static readonly Color BackgroundTop = new Color32(52, 34, 110, 255);
    public static readonly Color BackgroundBottom = new Color32(14, 12, 36, 255);
    public static readonly Color Panel = new Color32(28, 24, 62, 235);
    public static readonly Color PanelLight = new Color32(48, 42, 96, 255);
    public static readonly Color Green = new Color32(64, 190, 104, 255);
    public static readonly Color Blue = new Color32(64, 132, 238, 255);
    public static readonly Color Purple = new Color32(150, 88, 245, 255);
    public static readonly Color Orange = new Color32(246, 138, 48, 255);
    public static readonly Color Red = new Color32(232, 72, 92, 255);
    public static readonly Color Teal = new Color32(32, 186, 186, 255);
    public static readonly Color Grey = new Color32(88, 86, 116, 255);

    private static readonly Dictionary<string, GUIStyle> Styles = new Dictionary<string, GUIStyle>();
    private static readonly Dictionary<long, Texture2D> Gradients = new Dictionary<long, Texture2D>();
    private static Font? _body;
    private static Font? _display;
    private static bool _ready;

    /// <summary>
    /// Set while a window covers the screen: controls behind it keep their look but ignore the
    /// pointer. IMGUI hands clicks to controls in the order they are drawn, so the window's own
    /// buttons, drawn last, could not get them otherwise.
    /// </summary>
    public static bool InputBlocked { get; set; }

    /// <summary>
    /// When the interface last took a click. The board ignores the pointer for a moment after
    /// that, so a click on a button lying over the board does not also select a square.
    /// </summary>
    public static float LastClickTime { get; private set; } = -100;

    public static void ClickTaken() => LastClickTime = Time.unscaledTime;

    private static Rect? _dragView;
    private static float _dragDistance;

    /// <summary>
    /// Lets a finger (or the mouse) drag the content of a scroll view, as on a phone, where the
    /// thin scroll bars are hard to hit. Call it just before GUI.BeginScrollView. A drag longer
    /// than a few pixels is not also a tap on the button it started on.
    /// </summary>
    public static Vector2 DragScroll(Rect view, Vector2 scroll)
    {
        var e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && view.Contains(e.mousePosition):
                _dragView = view;
                _dragDistance = 0;
                break;
            case EventType.MouseDrag when _dragView == view:
                scroll -= e.delta;
                _dragDistance += e.delta.magnitude;
                e.Use();
                break;
            case EventType.MouseUp when _dragView == view:
                _dragView = null;
                if (_dragDistance > 10)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                break;
        }
        return scroll;
    }

    public static void Init()
    {
        if (_ready)
            return;
        _ready = true;
        _body = Resources.Load<Font>("Fonts/Rubik");
        _display = Resources.Load<Font>("Fonts/RussoOne");
        GUI.skin.settings.cursorColor = Text;
        GUI.skin.settings.selectionColor = new Color(0.6f, 0.4f, 1f, 0.5f);
    }

    public static Color Lighten(Color color, float amount) => Color.Lerp(color, Color.white, amount);

    public static Color Darken(Color color, float amount) => Color.Lerp(color, Color.black, amount);

    public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    // ---------------------------------------------------------------- text

    /// <summary>A label style, cached. Display text uses the heavy title font.</summary>
    public static GUIStyle Label(int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft,
        bool bold = false, bool display = false, bool wrap = false)
    {
        string key = $"L{size}|{(Color32)color}|{anchor}|{bold}|{display}|{wrap}";
        if (!Styles.TryGetValue(key, out var style))
        {
            style = new GUIStyle
            {
                font = display ? _display : _body,
                fontSize = size,
                fontStyle = bold && !display ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor,
                wordWrap = wrap,
                richText = true,
                clipping = TextClipping.Overflow,
            };
            style.normal.textColor = color;
            Styles[key] = style;
        }
        return style;
    }

    /// <summary>Text with a soft drop shadow, for titles and button captions.</summary>
    public static void ShadowText(Rect rect, string text, GUIStyle style, float offset = 2)
    {
        var color = style.normal.textColor;
        style.normal.textColor = new Color(0, 0, 0, 0.45f * GUI.color.a);
        GUI.Label(new Rect(rect.x + offset * 0.5f, rect.y + offset, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    /// <summary>A text field drawn on a dark rounded box.</summary>
    public static string TextField(Rect rect, string value, int maxLength, int size = 20)
    {
        Fill(rect, new Color32(16, 14, 38, 255), 12);
        Outline(rect, PanelLight, 2, 12);
        string key = "Field" + size;
        if (!Styles.TryGetValue(key, out var style))
        {
            style = new GUIStyle(Label(size, Text)) { clipping = TextClipping.Clip };
            style.padding = new RectOffset(14, 14, 0, 0);
            style.focused.textColor = Text;
            style.hover.textColor = Text;
            Styles[key] = style;
        }
        return GUI.TextField(rect, value, maxLength, style);
    }

    // ---------------------------------------------------------------- shapes

    public static void Fill(Rect rect, Color color, float radius = 0)
    {
        if (Event.current.type != EventType.Repaint)
            return;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color * GUI.color, 0, radius);
    }

    public static void Outline(Rect rect, Color color, float width, float radius)
    {
        if (Event.current.type != EventType.Repaint)
            return;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color * GUI.color, width, radius);
    }

    /// <summary>A rounded box with a vertical gradient from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    public static void Gradient(Rect rect, Color top, Color bottom, float radius = 0)
    {
        if (Event.current.type != EventType.Repaint)
            return;
        GUI.DrawTexture(rect, GradientTexture(top, bottom), ScaleMode.StretchToFill, true, 0, GUI.color, 0, radius);
    }

    private static Texture2D GradientTexture(Color top, Color bottom)
    {
        Color32 a = top, b = bottom;
        long key = ((long)(a.r | a.g << 8 | a.b << 16 | a.a << 24) << 32) | (uint)(b.r | b.g << 8 | b.b << 16 | b.a << 24);
        if (Gradients.TryGetValue(key, out var texture) && texture != null)
            return texture;
        texture = new Texture2D(1, 64, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        for (int y = 0; y < 64; y++)
            texture.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
        texture.Apply();
        Gradients[key] = texture;
        return texture;
    }

    /// <summary>A raised card: shadow, gradient face and a light rim.</summary>
    public static void Card(Rect rect, Color color, float radius = 18, bool raised = true)
    {
        if (raised)
            Fill(new Rect(rect.x, rect.y + 6, rect.width, rect.height), new Color(0, 0, 0, 0.35f), radius);
        Gradient(rect, Lighten(color, 0.12f), Darken(color, 0.18f), radius);
        Outline(rect, WithAlpha(Lighten(color, 0.45f), 0.55f), 2, radius);
    }

    public static void PanelBox(Rect rect, float radius = 18)
    {
        Fill(new Rect(rect.x, rect.y + 5, rect.width, rect.height), new Color(0, 0, 0, 0.3f), radius);
        Fill(rect, Panel, radius);
        Outline(rect, WithAlpha(PanelLight, 0.9f), 2, radius);
    }

    // ---------------------------------------------------------------- controls

    /// <summary>A chunky game button that lifts on hover and clicks with a sound.</summary>
    public static bool Button(Rect rect, string text, Color color, int size = 20, bool enabled = true)
    {
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && enabled;
        bool active = GUI.enabled;
        bool hover = active && !InputBlocked && rect.Contains(Event.current.mousePosition);
        bool pressed = hover && Input.GetMouseButton(0);

        var face = rect;
        if (hover && !pressed)
            face.y -= 2;
        if (pressed)
            face.y += 2;
        var baseColor = active ? color : Grey;
        float radius = Mathf.Min(16, rect.height * 0.5f);
        Fill(new Rect(rect.x, rect.y + 5, rect.width, rect.height), Darken(baseColor, 0.6f), radius);
        Gradient(face, Lighten(baseColor, hover ? 0.3f : 0.16f), Darken(baseColor, hover ? 0.02f : 0.14f), radius);
        Outline(face, WithAlpha(Lighten(baseColor, 0.5f), 0.7f), 2, radius);
        var label = Label(size, active ? Text : Muted, TextAnchor.MiddleCenter, bold: true);
        ShadowText(face, text, label, 1.5f);

        bool clicked = !InputBlocked && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        GUI.enabled = wasEnabled;
        if (clicked)
        {
            ClickTaken();
            Sfx.Play(Sfx.Click);
        }
        return clicked;
    }

    /// <summary>A row of options where one is chosen, like a segmented switch.</summary>
    public static int Pills(Rect rect, int selected, string[] options, Color color, int size = 17)
    {
        Fill(rect, new Color32(16, 14, 38, 255), rect.height * 0.5f);
        float width = rect.width / options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            var cell = new Rect(rect.x + i * width + 3, rect.y + 3, width - 6, rect.height - 6);
            bool hover = GUI.enabled && !InputBlocked && cell.Contains(Event.current.mousePosition);
            if (i == selected)
            {
                Gradient(cell, Lighten(color, 0.2f), Darken(color, 0.1f), cell.height * 0.5f);
                Outline(cell, WithAlpha(Lighten(color, 0.5f), 0.7f), 2, cell.height * 0.5f);
            }
            else if (hover)
            {
                Fill(cell, WithAlpha(PanelLight, 0.8f), cell.height * 0.5f);
            }
            GUI.Label(cell, options[i], Label(size, i == selected ? Text : Muted, TextAnchor.MiddleCenter, bold: i == selected));
            if (!InputBlocked && GUI.Button(cell, GUIContent.none, GUIStyle.none) && i != selected)
            {
                selected = i;
                Sfx.Play(Sfx.Click);
            }
        }
        return selected;
    }

    /// <summary>A filled bar, e.g. the army budget.</summary>
    public static void Bar(Rect rect, float fraction, Color color)
    {
        Fill(rect, new Color32(16, 14, 38, 255), rect.height * 0.5f);
        if (fraction <= 0)
            return;
        var filled = new Rect(rect.x + 2, rect.y + 2, Mathf.Max(rect.height - 4, (rect.width - 4) * Mathf.Clamp01(fraction)), rect.height - 4);
        Gradient(filled, Lighten(color, 0.25f), Darken(color, 0.1f), filled.height * 0.5f);
    }

    // ---------------------------------------------------------------- background

    private struct Floater
    {
        public float X;
        public float Speed;
        public float Size;
        public float Phase;
        public float Spin;
        public int Piece;
    }

    private static Floater[]? _floaters;

    /// <summary>Full-screen gradient with chess pieces drifting slowly upwards.</summary>
    public static void Background(float width, float height, IReadOnlyList<Texture2D> pieces)
    {
        var color = GUI.color;
        GUI.color = Color.white;
        Gradient(new Rect(0, 0, width, height), BackgroundTop, BackgroundBottom);

        if (_floaters == null)
        {
            var random = new System.Random(7);
            _floaters = new Floater[16];
            for (int i = 0; i < _floaters.Length; i++)
            {
                _floaters[i] = new Floater
                {
                    X = (float)random.NextDouble(),
                    Speed = 8 + (float)random.NextDouble() * 16,
                    Size = 50 + (float)random.NextDouble() * 90,
                    Phase = (float)random.NextDouble() * 2000,
                    Spin = -20 + (float)random.NextDouble() * 40,
                    Piece = random.Next(12),
                };
            }
        }

        if (pieces.Count > 0 && Event.current.type == EventType.Repaint)
        {
            var matrix = GUI.matrix;
            foreach (var f in _floaters)
            {
                float travel = height + f.Size * 2;
                float y = height + f.Size - ((Time.time * f.Speed + f.Phase) % travel);
                float x = f.X * width + Mathf.Sin((Time.time + f.Phase) * 0.3f) * 30;
                var rect = new Rect(x - f.Size / 2, y - f.Size / 2, f.Size, f.Size);
                GUIUtility.RotateAroundPivot(Mathf.Sin(Time.time * 0.2f + f.Phase) * f.Spin, rect.center);
                GUI.color = new Color(1, 1, 1, 0.07f + f.Size / 1400f);
                GUI.DrawTexture(rect, pieces[f.Piece % pieces.Count]);
                GUI.matrix = matrix;
            }
        }
        GUI.color = color;
    }
}
