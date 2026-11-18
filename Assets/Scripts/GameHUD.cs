// ============================================================
// GameHUD.cs —— 屏幕上所有文字：互动提示、横幅、操作提示
//
// 挂在哪：场景里一个空物体，叫 HUD
//
// 【为什么全部集中到这一个脚本】
//   之前"按 E 做什么"的提示写在 PlayerInteract 里。
//   但接下来还要显示：检查点提示、通关横幅、操作提示。
//   如果每样都自己画 GUI，它们会互相不知道对方在哪，
//   迟早叠字、位置打架、透明度各写一套。
//   所以规则是：【画字的地方只有一个，别处只申请要显示什么】。
//
// 【为什么用 OnGUI，暂时不用 Canvas/TextMeshPro】
//   OnGUI 不需要预制体、不需要字体资源、不需要 UI 事件系统，
//   不会因为缺包而编译失败。它丑，但它一定在。
//   等美术风格定了再换 Canvas —— 那时是"美化"，不是"补功能"。
//   现在换只会在界面还没定型时先花两天搭 UI。
//
// 【横幅为什么要能淡入淡出】
//   文字"啪"地出现再"啪"地消失，会打断注意力。
//   淡入淡出让它像"从黑暗里浮出来的字"，和这个游戏的调子一致。
// ============================================================

using UnityEngine;

public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance;

    [Header("引用（留空会自动在场景里找）")]
    public PlayerInteract player;
    public CatForm form;

    [Header("显示")]
    [Tooltip("左下角那行操作提示")]
    public bool showHints = true;

    [Header("字体")]
    [Tooltip("留空 = 用 Unity 内置字体。内置字体不含中文，" +
             "打包成 WebGL 后所有中文会变成方块或者干脆不显示 —— " +
             "因为网页里没有系统字体可以回退。要显示中文，必须在这里塞一个含中文的 TTF")]
    public Font uiFont;

    [Header("颜色")]
    public Color textColor = new Color(0.95f, 0.95f, 1f);

    // ---- 横幅（静态，任何脚本都能调用） ----
    static string bannerText = "";
    static float bannerTimer;
    static float bannerDuration;

    /// <summary>
    /// 屏幕顶部那行常驻状态（比如"它快撑不住了"）。
    /// 由别的脚本每帧赋值为 null 或空串就不显示。
    /// 做成静态是因为写它的人（ShadowTimer）和被画的地方（这里）不该互相引用。
    /// </summary>
    public static string StatusText { get; set; }

    GUIStyle _prompt;
    GUIStyle _hint;
    GUIStyle _banner;

    void Awake()
    {
        Instance = this;
        if (player == null) player = FindObjectOfType<PlayerInteract>();
        if (form == null) form = FindObjectOfType<CatForm>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>屏幕中间显示一句话。duration 秒后淡出。</summary>
    public static void Banner(string text, float duration = 3f)
    {
        bannerText = text;
        bannerDuration = Mathf.Max(0.5f, duration);
        bannerTimer = bannerDuration;
    }

    void Update()
    {
        if (bannerTimer > 0f)
        {
            bannerTimer -= Time.deltaTime;
            if (bannerTimer <= 0f)
            {
                bannerTimer = 0f;
                bannerText = "";
            }
        }
    }

    // ------------------------------------------------------------
    /// <summary>
    /// 所有 HUD 文字样式都从这里造。
    /// 【为什么只写一次】字体、字号这些必须统一在一处设，
    /// 否则以后换字体要改三个地方，一定漏一个。
    /// </summary>
    GUIStyle MakeStyle(int size, TextAnchor anchor, FontStyle style)
    {
        GUIStyle s = new GUIStyle(GUI.skin.label);
        s.fontSize = size;
        s.alignment = anchor;
        s.fontStyle = style;

        // 字体：填了 uiFont 就用它，没填就自动从 Resources/Fonts 找中文字体。
        // 不兜底的话汉字会【完全不画】——不是方块，是空白，而且没有任何报错。
        CatFont.Apply(s, uiFont);

        return s;
    }

    GUIStyle PromptStyle
    {
        get
        {
            if (_prompt == null) _prompt = MakeStyle(18, TextAnchor.MiddleCenter, FontStyle.Normal);
            return _prompt;
        }
    }

    GUIStyle HintStyle
    {
        get
        {
            if (_hint == null) _hint = MakeStyle(13, TextAnchor.UpperLeft, FontStyle.Normal);
            return _hint;
        }
    }

    GUIStyle BannerStyle
    {
        get
        {
            if (_banner == null) _banner = MakeStyle(30, TextAnchor.MiddleCenter, FontStyle.Bold);
            return _banner;
        }
    }

    void OnGUI()
    {
        // ---- 1. 互动提示（底部） ----
        DrawPrompt();

        // ---- 2. 操作提示（左下） ----
        if (showHints) DrawHints();

        // ---- 3. 横幅（中间） ----
        DrawBanner();

        // ---- 4. 顶部状态 ----
        DrawStatus();

        // ---- 5. 收集品计数（右上） ----
        DrawStars();
    }

    void DrawStars()
    {
        string text = Star.GetHudText();
        if (string.IsNullOrEmpty(text)) return;

        GUIStyle style = BannerStyle;
        style.fontSize = 20;
        style.alignment = TextAnchor.UpperRight;
        style.normal.textColor = new Color(1f, 0.88f, 0.35f, 0.95f);

        GUI.Label(new Rect(0f, Screen.height * 0.045f, Screen.width - 18f, 30f), text, style);

        // 恢复，别影响横幅
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = 30;
    }

    void DrawStatus()
    {
        if (string.IsNullOrEmpty(StatusText)) return;

        GUIStyle style = BannerStyle;
        style.fontSize = 20;
        style.normal.textColor = new Color(1f, 0.82f, 0.55f, 0.9f);

        GUI.Label(new Rect(0f, Screen.height * 0.08f, Screen.width, 30f), StatusText, style);

        style.fontSize = 30;   // 恢复，别影响横幅
    }

    void DrawPrompt()
    {
        if (player == null) return;

        Interactable cur = player.Current;
        if (cur == null) return;

        // 提示的格式在 CatMareText 里，改台词不用来这里
        string text = string.Format(CatMareText.PromptFormat, player.interactKey, cur.prompt);

        GUIStyle style = PromptStyle;
        style.normal.textColor = textColor;

        float w = 420f;
        float h = 34f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.74f;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(x, y - 4f, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(x, y, w, h - 6f), text, style);
    }

    void DrawHints()
    {
        // 影形态下多一句，因为"按不了开关"这件事玩家不会自己想到
        string restartName = (form != null) ? form.restartKey.ToString() : "R";

        string hints = string.Format(CatMareText.HintBar, restartName);

        if (form != null && form.IsShadow)
            hints += "\n" + CatMareText.ShadowHint;

        GUIStyle style = HintStyle;
        style.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.45f);

        GUI.Label(new Rect(14f, Screen.height - 52f, Screen.width - 28f, 44f), hints, style);
    }

    void DrawBanner()
    {
        if (bannerTimer <= 0f || string.IsNullOrEmpty(bannerText)) return;

        // 淡入 0.15 秒，最后 0.7 秒淡出
        float elapsed = bannerDuration - bannerTimer;
        float a = 1f;
        if (elapsed < 0.15f) a = elapsed / 0.15f;
        float outTime = Mathf.Min(0.7f, bannerDuration * 0.5f);
        if (bannerTimer < outTime) a = Mathf.Min(a, bannerTimer / outTime);

        GUIStyle style = BannerStyle;
        style.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, a);

        float h = 90f;
        float y = Screen.height * 0.40f;

        GUI.color = new Color(0f, 0f, 0f, 0.5f * a);
        GUI.DrawTexture(new Rect(0f, y - 12f, Screen.width, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(0f, y, Screen.width, h - 20f), bannerText, style);
    }
}
