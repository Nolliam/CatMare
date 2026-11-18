// ============================================================
// MainMenu.cs —— 主界面
//
// 挂在哪：主菜单场景里的一个空物体（菜单 21 会自动生成整个场景）
//
// 【为什么用 OnGUI（IMGUI），而不是拖一套 Canvas + Button】
//   本项目的 HUD 已经是 IMGUI 了（GameHUD 用 GUIStyle 画字）。
//   主界面用同一套，好处是：
//     · 不用在场景里摆一堆 UI 物体，不会因为分辨率变了就错位
//     · 以后改按钮位置、改文案，只改这个文件
//   代价是按钮长得朴素。等需要"有美术感的菜单"时再换成 Canvas 不迟。
//
// 【这一版的原则：画面是主角，UI 是配角】
//   · 不画标题、不画副标题 —— 背景图上已经有字了，再叠一层是重复
//   · 背景不加暗色遮罩（dimAmount 默认 0），图保持原样
//   · 按钮放在左下角，扁、半透明、小，尽量不压住画
// ============================================================

using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("画面")]
    [Tooltip("标题背景图。留空就只有纯色背景")]
    public Texture2D background;

    [Tooltip("背景没盖满的地方用什么颜色")]
    public Color backgroundColor = Color.black;

    [Tooltip("背景之上压一层黑。0 = 不压（默认），想让按钮更清楚可以调到 0.2")]
    [Range(0f, 0.9f)] public float dimAmount = 0f;

    [Header("按钮位置（都按屏幕比例，0~1）")]
    [Tooltip("左边距。0.07 = 离左边缘 7% 屏宽")]
    [Range(0f, 0.8f)] public float buttonX = 0.07f;

    [Tooltip("上边距。0.62 = 从顶部往下 62% 屏高（也就是左下区域）")]
    [Range(0f, 0.95f)] public float buttonY = 0.62f;

    [Range(0.08f, 0.5f)] public float buttonW = 0.20f;
    [Range(0.03f, 0.15f)] public float buttonH = 0.065f;

    [Tooltip("两个按钮之间的空隙（占屏幕高度比例）")]
    [Range(0f, 0.1f)] public float buttonGap = 0.018f;

    [Header("文字")]

    [Tooltip("按钮底板颜色（带透明度，默认半透明黑）")]
    public Color buttonColor = new Color(0f, 0f, 0f, 0.45f);
    public Color buttonHoverColor = new Color(0.35f, 0.30f, 0.12f, 0.75f);
    public Color buttonTextColor = new Color(1f, 0.95f, 0.75f);

    [Tooltip("中文字体。不填就是内置字体（不含中文，网页版会变方块）")]
    public Font uiFont;

    [Tooltip("底部提示文字。留空则不显示")]
    public string hint = "ENTER / SPACE  start      ESC  quit";

    [Header("流程")]
    [Tooltip("关卡列表。START 进去的是第 0 个；LEVELS 会把它们全列出来")]
    public string[] levels = new string[] { "Room1", "Room2" };

    [Tooltip("点开始要加载的场景名。留空 = 用 levels[0]")]
    public string nextScene = "";

    [Tooltip("按 Esc 也能退出")]
    public bool quitOnEscape = true;

    [Tooltip("显示「关卡选择」按钮")]
    public bool showLevelSelect = true;

    public string startLabelText = "START";
    public string levelsLabelText = "LEVELS";
    public string quitLabelText = "QUIT";

    GUIStyle stButton, stHint;
    Texture2D texNormal, texHover;
    bool busy;
    bool levelsOpen;

    void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;

        if (Application.isEditor && Application.isPlaying) Application.runInBackground = true;

        // 开机打一行，出问题时有据可查（尺寸不对会导致画面被放大成一片糊）
        if (background == null)
            Debug.LogWarning("[MainMenu] background 是空的，只会画纯色背景");
        else
            Debug.Log("[MainMenu] 背景图尺寸 = " + background.width + " x " + background.height);
    }

    static Texture2D MakeFlat(Color c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, c);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    void BuildStyles()
    {
        if (stButton != null) return;

        texNormal = MakeFlat(buttonColor);
        texHover = MakeFlat(buttonHoverColor);

        stButton = new GUIStyle(GUI.skin.button);
        stButton.alignment = TextAnchor.MiddleCenter;
        stButton.fontStyle = FontStyle.Bold;

        // 用纯色贴图当底板：默认的灰盒子太抢眼，会压住背景画
        stButton.normal.background = texNormal;
        stButton.hover.background = texHover;
        stButton.active.background = texHover;
        stButton.focused.background = texNormal;

        stButton.normal.textColor = buttonTextColor;
        stButton.hover.textColor = Color.white;
        stButton.active.textColor = Color.white;

        CatFont.Apply(stButton, uiFont);

        stHint = new GUIStyle(GUI.skin.label);
        stHint.alignment = TextAnchor.UpperLeft;
        stHint.fontSize = 12;
        stHint.normal.textColor = new Color(1f, 1f, 1f, 0.30f);
        CatFont.Apply(stHint, uiFont);
    }

    void Update()
    {
        if (busy) return;

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            StartGame();

        if (quitOnEscape && Input.GetKeyDown(KeyCode.Escape))
        {
            // 关卡列表开着的时候，Esc 先关列表，不退出游戏
            if (levelsOpen) levelsOpen = false;
            else QuitGame();
        }
    }

    void OnGUI()
    {
        BuildStyles();

        float W = Screen.width;
        float H = Screen.height;

        // ---- 背景：铺满整个屏幕，多出来的部分裁掉，不变形 ----
        //
        // 【为什么不再自己算比例】
        //   我上一版手写了"按宽/按高"两分支来保持比例，
        //   但只要背景图报出来的尺寸有一点不对（比如 0 或 NaN），
        //   算出来的矩形就会变成天文数字 —— 画面被放大成一片糊，
        //   即画面上那两个巨大黑点。
        //   ScaleMode.ScaleAndCrop 是 Unity 自己做这件事：
        //   等比放大到刚好盖住整个 Rect，然后裁掉溢出。
        //   一行搞定，而且不会因为除零炸掉。
        if (background != null)
        {
            GUI.DrawTexture(new Rect(0f, 0f, W, H), background, ScaleMode.ScaleAndCrop);
        }
        else
        {
            Color old = GUI.color;
            GUI.color = backgroundColor;
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ---- 可选暗色遮罩（默认关掉，保持画面原样）----
        if (dimAmount > 0.001f)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, dimAmount);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ---- 按钮：左下 ----
        float bw = W * buttonW;
        float bh = H * buttonH;
        float bx = W * buttonX;
        float by = H * buttonY;
        float step = bh + H * buttonGap;

        stButton.fontSize = Mathf.RoundToInt(H * 0.026f);

        Color oldBtn = GUI.color;
        GUI.color = busy ? new Color(1f, 1f, 1f, 0.4f) : Color.white;

        if (GUI.Button(new Rect(bx, by, bw, bh), startLabelText, stButton))
            StartGame();

        int row = 1;

        if (showLevelSelect)
        {
            if (GUI.Button(new Rect(bx, by + step * row, bw, bh), levelsLabelText, stButton))
                levelsOpen = !levelsOpen;

            row++;
        }

        if (GUI.Button(new Rect(bx, by + step * row, bw, bh), quitLabelText, stButton))
            QuitGame();

        GUI.color = oldBtn;

        // ---- 关卡列表（在按钮右边展开）----
        if (showLevelSelect && levelsOpen && levels != null && levels.Length > 0)
        {
            float lx = bx + bw + W * 0.02f;
            float lw = bw * 1.15f;

            for (int i = 0; i < levels.Length; i++)
            {
                if (string.IsNullOrEmpty(levels[i])) continue;

                float ly = by + step * i;

                if (GUI.Button(new Rect(lx, ly, lw, bh), levels[i], stButton))
                    LoadLevel(levels[i]);
            }
        }

        // ---- 提示：跟在按钮下面，左对齐，不压画面中间 ----
        if (!string.IsNullOrEmpty(hint) && stHint != null)
        {
            float hy = by + step * (row + 1) + H * 0.01f;
            GUI.Label(new Rect(bx + 2f, hy, W * 0.6f, H * 0.05f), hint, stHint);
        }
    }

    public void StartGame()
    {
        // 没填 nextScene 就用关卡列表第一个
        string target = nextScene;

        if (string.IsNullOrEmpty(target) && levels != null && levels.Length > 0)
            target = levels[0];

        LoadLevel(target);
    }

    /// <summary>加载指定场景</summary>
    public void LoadLevel(string scene)
    {
        if (busy) return;

        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogError("[MainMenu] 场景名是空的。检查 nextScene 或 levels 数组。");
            return;
        }

        busy = true;
        Debug.Log("[MainMenu] 加载场景 " + scene);
        SceneManager.LoadScene(scene);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
