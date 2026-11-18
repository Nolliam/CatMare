// ============================================================
// TutorialSign.cs —— 会随光线自动反色的教学文字
//
// 挂在哪：关卡里放一个空物体，摆在需要显示文字的位置
//
// 【为什么需要它】
//   这个游戏整屏只有黑和白。任何固定颜色的字，
//   总有一半场合是看不见的：
//     白色的字掉进光里 → 背景是亮的 → 糊成一片
//     黑色的字掉进暗里 → 背景是黑的 → 完全消失
//
// 【为什么是"逐字符"上色，而不是整段一个颜色】
//   一段长文字很可能一半在光里、一半在影里。
//   整段取一个颜色，必然有一半是错的：
//     取黑 → 影子那半看不见
//     取白 → 光里那半看不见
//   所以这里把每个字符单独画一遍，各自采样自己位置的光照：
//     光里的字符 → 黑
//     影里的字符 → 白
//     正好在边界 → 由插值得到灰色，看起来像"半黑半白"
//   这不是炫技，是这套黑白画面下唯一能保证整段都读得出来做法。
//
// 【它怎么知道某个位置亮不亮】
//   用 LightZone.Illuminates() —— 和玩家的形态判定是同一个函数。
//   所以"字看起来在暗处"和"猫站在这儿确实是影子"永远一致。
//   用别的方式猜（比如查背景像素），迟早会和判定对不上。
// ============================================================

using System.Collections.Generic;
using UnityEngine;

public class TutorialSign : MonoBehaviour
{
    [TextArea(2, 5)]
    [Tooltip("要显示的文字。可以多行")]
    public string message = "按 A / D 移动\n按空格跳跃";

    [Header("样式")]
    [Tooltip("中文字体。不填就是内置字体（不含中文，网页版会变方块）")]
    public Font uiFont;
    public int fontSize = 16;

    [Tooltip("文字相对这个物体的偏移")]
    public Vector3 textOffset = new Vector3(0f, 0.6f, 0f);

    [Tooltip("画一圈描边（强烈建议保留）")]
    public bool drawShadow = true;

    [Header("随光反色")]
    [Tooltip("有光的时候用什么颜色")]
    public Color litColor = Color.black;

    [Tooltip("无光的时候用什么颜色")]
    public Color darkColor = Color.white;

    [Tooltip("颜色过渡速度。太快会闪，太慢会跟不上")]
    public float colorLerpSpeed = 8f;

    [Tooltip("逐字符采样：一段字一半在光里一半在影里时，只有影里那半反色")]
    public bool perCharLighting = true;

    [Tooltip("每隔几个字符采样一次光照。越大越省性能，边界越粗糙")]
    [Range(1, 8)] public int lightSampleStride = 2;

    [Tooltip("边界柔化（单位：字符宽度）。0 = 硬切换，像逐字变色")]
    [Range(0f, 3f)] public float softEdge = 1f;

    [Header("显示条件")]
    [Tooltip("猫离得比这个远就不显示。0 = 永远显示")]
    public float visibleDistance = 14f;

    Camera cam;
    float litAmount;                 // 整段文字的平滑值（不逐字时用）

    GUIStyle st, stShadow;
    int cachedFontSize = -1;
    string cachedMsg = null;

    // 每个字符的累计宽度（缓存：只和文字内容、字号有关）
    List<float[]> lineCum = new List<float[]>();
    List<string> lines = new List<string>();
    float lineHeight;

    void Start()
    {
        cam = Camera.main;

        if (cam == null)
            Debug.LogError("[TutorialSign] 找不到 Camera.main —— 相机的 Tag 必须设成 MainCamera。");

        if (transform.position.z < -5f)
            Debug.LogWarning("[TutorialSign] " + name + " 的 z = " + transform.position.z +
                             "，被摆在了相机平面附近。文字按 z = 0 投影，看不见就把它移到 z = 0。");
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        bool lit = IsLitAt(transform.position + textOffset);
        litAmount = Mathf.MoveTowards(litAmount, lit ? 1f : 0f, Time.deltaTime * colorLerpSpeed);
    }

    bool IsLitAt(Vector3 world)
    {
        Vector2 p = world;
        var zones = LightZone.AllZones;

        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i] == null) continue;
            if (zones[i].Illuminates(p)) return true;
        }

        return false;
    }

    // ------------------------------------------------------------
    // 样式 + 文字排版测量
    // ------------------------------------------------------------
    void BuildStyles()
    {
        if (st != null && cachedFontSize == fontSize) return;
        cachedFontSize = fontSize;

        st = new GUIStyle(GUI.skin.label);
        st.alignment = TextAnchor.UpperLeft;
        st.fontSize = fontSize;
        st.wordWrap = false;
        CatFont.Apply(st, uiFont);

        stShadow = new GUIStyle(st);

        // 字号变了要重新量字宽
        cachedMsg = null;
    }

    void RebuildMetrics()
    {
        if (cachedMsg == message && lines.Count > 0) return;

        // 【为什么要在建样式之前就返回】
        //   下面用 st.CalcSize 量文字宽度。如果 st 还没创建（Start 里调得早），
        //   会在循环中途抛 NullReferenceException —— 而那时 lines 已经 Add 了，
        //   lineCum 却没 Add，两个列表长度就永久对不上，
        //   之后每一帧 OnGUI 都在 lineCum[l] 越界崩一次。
        //   所以：样式没好就干脆不做，也不记 cachedMsg，下一帧再来。
        if (st == null) return;

        cachedMsg = message;

        lines.Clear();
        lineCum.Clear();

        string[] raw = (message ?? "").Split('\n');
        for (int l = 0; l < raw.Length; l++)
        {
            string line = raw[l];
            lines.Add(line);

            float[] cum = new float[line.Length + 1];
            cum[0] = 0f;

            for (int i = 0; i < line.Length; i++)
            {
                // 用前缀宽度：这样连字距（kerning）也算进去了
                cum[i + 1] = st.CalcSize(new GUIContent(line.Substring(0, i + 1))).x;
            }

            lineCum.Add(cum);
        }

        lineHeight = st.CalcSize(new GUIContent("Ag")).y;
    }

    // ------------------------------------------------------------
    void OnGUI()
    {
        if (string.IsNullOrEmpty(message)) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (visibleDistance > 0.01f)
        {
            Transform cat = FindCat();
            if (cat != null)
            {
                float d = Vector2.Distance((Vector2)cat.position,
                                           (Vector2)(transform.position + textOffset));
                if (d > visibleDistance) return;
            }
        }

        Vector3 world = transform.position + textOffset;

        // 一律按 z = 0 投影：物体被摆在相机平面附近时也能正常显示
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
        if (sp.z < 0f) return;

        BuildStyles();
        RebuildMetrics();

        float W = Screen.width;
        float H = Screen.height;

        // 屏幕坐标（GUI 的 y 从上往下）
        float baseY = H - sp.y;

        // 整段宽度 = 最长那一行
        float maxW = 0f;
        for (int l = 0; l < lineCum.Count; l++)
        {
            float[] c = lineCum[l];
            if (c != null && c.Length > 0) maxW = Mathf.Max(maxW, c[c.Length - 1]);
        }

        float baseX = sp.x - maxW * 0.5f;

        // 相机所在平面到 z=0 的距离，用来把屏幕点换算回世界点
        float planeDist = Mathf.Abs(cam.transform.position.z);

        Color shadowC = (litAmount > 0.5f) ? darkColor : litColor;
        shadowC.a = 0.55f;
        stShadow.normal.textColor = shadowC;

        for (int l = 0; l < lines.Count; l++)
        {
            string line = lines[l];

            // 空行直接跳过。
            // 【必须跳过】不然下面会去读 cum[1]，而空行的 cum 只有一个元素 ——
            // 抛 IndexOutOfRangeException，每帧一次，把整个 OnGUI 打断。
            // 文字里带一个多余的换行就会踩到。
            if (line.Length == 0) continue;

            // 兜底：万一两个列表的长度对不上（历史上出现过一次），
            // 宁可少画一行，也不要每帧抛异常把整个 OnGUI 打断。
            if (l >= lineCum.Count) break;

            float[] cum = lineCum[l];
            float y = baseY + l * lineHeight;

            if (!perCharLighting)
            {
                // ---- 整段一个颜色（省性能）----
                Color c = Color.Lerp(darkColor, litColor, litAmount);
                c.a = 1f;
                st.normal.textColor = c;

                Rect r = new Rect(baseX, y, maxW + 4f, lineHeight + 4f);

                if (drawShadow)
                {
                    GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), line, stShadow);
                    GUI.Label(new Rect(r.x - 1f, r.y - 1f, r.width, r.height), line, stShadow);
                }

                GUI.Label(r, line, st);
                continue;
            }

            // ---- 逐字符：每个字自己采样光照 ----
            int n = line.Length;
            float[] litArr = new float[n];

            // 1) 按 stride 采样，然后在采样点之间插值
            int step = Mathf.Max(1, lightSampleStride);
            float lastT = SampleAt(baseX, y, 0, line, cum, planeDist);
            float nextT = lastT;
            int nextIdx = 0;

            for (int i = 0; i < n; i++)
            {
                if (i >= nextIdx)
                {
                    int sampleIdx = Mathf.Min(((i / step) + 1) * step, n - 1);
                    float prevT = SampleAt(baseX, y, i, line, cum, planeDist);
                    nextT = SampleAt(baseX, y, sampleIdx, line, cum, planeDist);

                    // 从 i 到 sampleIdx 之间线性过渡
                    int span = Mathf.Max(1, sampleIdx - i);
                    for (int k = i; k <= sampleIdx && k < n; k++)
                        litArr[k] = Mathf.Lerp(prevT, nextT, (k - i) / (float)span);

                    nextIdx = sampleIdx + 1;
                    lastT = nextT;
                }
            }

            // 2) 横向柔化，避免逐字硬切
            if (softEdge > 0.01f && n > 2)
            {
                int radius = Mathf.Clamp(Mathf.RoundToInt(softEdge), 1, 3);
                float[] smoothed = new float[n];

                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    int cnt = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int idx = i + k;
                        if (idx < 0 || idx >= n) continue;
                        sum += litArr[idx];
                        cnt++;
                    }
                    smoothed[i] = sum / Mathf.Max(1, cnt);
                }

                litArr = smoothed;
            }

            // 3) 逐字画
            for (int i = 0; i < n; i++)
            {
                char ch = line[i];
                if (ch == ' ') continue;

                float x0 = baseX + cum[i];
                float w = cum[i + 1] - cum[i] + 2f;

                Color c = Color.Lerp(darkColor, litColor, litArr[i]);
                c.a = 1f;

                string s = ch.ToString();
                Rect r = new Rect(x0, y, w, lineHeight + 2f);

                if (drawShadow)
                {
                    GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), s, stShadow);
                }

                st.normal.textColor = c;
                GUI.Label(r, s, st);
            }
        }
    }

    /// <summary>采样第 i 个字符中心处的光照（0 = 暗，1 = 亮）</summary>
    float SampleAt(float baseX, float guiY, int i, string line, float[] cum, float planeDist)
    {
        // 越界保护：字符索引超出量好的宽度表时，当作"没被照亮"处理，
        // 而不是抛异常。这个函数每帧会被调用很多次，抛一次就毁一整帧。
        if (cum == null || i < 0 || i + 1 >= cum.Length) return 0f;

        float cx = baseX + (cum[i] + cum[i + 1]) * 0.5f;
        float cy = guiY + lineHeight * 0.5f;

        // GUI 坐标（y 向下）→ 屏幕坐标（y 向上）
        Vector3 screenPt = new Vector3(cx, Screen.height - cy, planeDist);
        Vector3 worldPt = cam.ScreenToWorldPoint(screenPt);

        return IsLitAt(worldPt) ? 1f : 0f;
    }

    static Transform FindCat()
    {
        GameObject cat = GameObject.FindGameObjectWithTag("Player");
        return (cat != null) ? cat.transform : null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + textOffset, 0.15f);

        if (visibleDistance > 0.01f)
        {
            Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, visibleDistance);
        }
    }
}
