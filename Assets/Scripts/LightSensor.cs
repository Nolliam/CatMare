// ============================================================
// LightSensor.cs —— 判断猫现在有没有被光"照到"
//
// 【为什么判定交给每盏灯自己做】
//   遮挡判定需要"从光源到猫"的这条线，只有光源自己知道方向。
//   所以由 LightZone.Illuminates(point) 回答，LightSensor 只负责汇总：
//     场景里任何一盏灯能照到它 → 在光里
//   一盏都照不到 → 在黑暗里
//
// 【为什么每帧查一次，不用触发器事件】
//   遮挡是"视线"问题，不是"重叠"问题，天然没有 enter/exit 事件可依赖。
//   每帧查 N 盏灯（每盏一条 raycast），N 通常小于 5，开销可以忽略。
//
// 【为什么光源列表可以自动找】
//   后续会加很多灯。如果每加一盏都要手动拖进列表，一定会漏。
//   默认自动在场景里找所有 LightZone，想手动指定就把 autoFind 关掉。
// ============================================================

using UnityEngine;

public class LightSensor : MonoBehaviour
{
    [Header("光源")]
    [Tooltip("勾上则自动在场景里找所有 LightZone（推荐）")]
    public bool autoFindLights = true;
    public LightZone[] lights;

    [Tooltip("猫身上的检测点偏移（放在身体中间略偏上）")]
    public Vector2 offset = new Vector2(0f, 0.05f);

    [Header("判定范围")]
    [Tooltip("在身体上取一片点判定。开着 = 身上只要有一点在阴影里就变影形态")]
    public bool sampleWholeBody = true;

    [Tooltip("猫形态下的采样框。大一点：身上碰到一点阴影就变影子")]
    public Vector2 sampleSizeCat = new Vector2(0.90f, 1.15f);

    [Tooltip("影形态下的采样框。必须小 —— 影子只有 0.1 高，" +
             "如果还用猫的大框，下半截会埋进地板里，那些点永远'没被照亮'，" +
             "结果就是贴地进入光里也永远变不回猫")]
    public Vector2 sampleSizeShadow = new Vector2(0.55f, 0.14f);

    [Tooltip("地形层。落在地形内部的采样点会被忽略（不算它没被照亮）")]
    public LayerMask solidLayer;

    [Tooltip("每个方向取几个点。3 = 3×3 = 9 个点")]
    [Range(1, 7)] public int sampleCount = 3;

    /// <summary>当前是否被任意一盏灯照到</summary>
    public bool IsLit { get; private set; }

    [Header("调试")]
    public bool debugLog = false;

    CatForm form;
    bool lastLit;
    float refreshTimer;
    Vector2[] catPoints;
    Vector2[] shadowPoints;

    void Awake()
    {
        form = GetComponent<CatForm>();
    }

    void Start()
    {
        if (autoFindLights)
        {
            lights = FindObjectsOfType<LightZone>();
            Debug.Log("[LightSensor] 自动找到 " + (lights != null ? lights.Length : 0) + " 盏灯");
        }

        BuildSamplePoints();
    }

    // ------------------------------------------------------------
    // 为什么要在身体上取一片点，而不是只取中心一个点
    //
    //   取一个点 = "我的心脏在光里，我就在光里"。
    //   玩家会觉得莫名其妙：明明半个身子已经埋进阴影了，还是猫形态。
    //
    //   取一片点、而且【只要有一个点没被照到就算黑暗】之后，
    //   规则变成："身上哪都能被照到，才是猫"。
    //   于是"碰到一点影子就变影形态"成立 —— 光变成一种需要躲的东西。
    //
    //   代价：站在灯的边缘会更早变成影子，因为边缘总有一角先暗。
    //   这个宽度由 sampleSize 控制：调小会宽容一点，调大会严格。
    // ------------------------------------------------------------
    void BuildSamplePoints()
    {
        catPoints = BuildGrid(sampleSizeCat);
        shadowPoints = BuildGrid(sampleSizeShadow);
    }

    Vector2[] BuildGrid(Vector2 size)
    {
        int n = Mathf.Clamp(sampleCount, 1, 7);
        Vector2[] pts = new Vector2[n * n];

        int k = 0;
        for (int y = 0; y < n; y++)
        {
            float ty = (n == 1) ? 0f : (y / (float)(n - 1)) - 0.5f;
            for (int x = 0; x < n; x++)
            {
                float tx = (n == 1) ? 0f : (x / (float)(n - 1)) - 0.5f;
                pts[k] = new Vector2(tx * size.x, ty * size.y);
                k++;
            }
        }

        return pts;
    }

    /// <summary>最近一次"这个点为什么没被照亮"的诊断文本（给调试面板用）</summary>
    public string DebugInfo { get; private set; }

    Vector2 failPoint;
    int failIndex;

    LightZone FindClosestZone(Vector2 p)
    {
        if (lights == null) return null;

        LightZone best = null;
        float bestD = float.MaxValue;

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;

            float d = Vector2.Distance(p, lights[i].transform.position);
            if (d < bestD) { bestD = d; best = lights[i]; }
        }

        return best;
    }

    bool AnyLightOn(Vector2 p)
    {
        if (lights == null) return false;

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;

            // 「只发光、不改形态」的灯（比如致命红光）不参与形态判定。
            // 它照样会被墙挡住、照样有光扇，但影子在它里面仍然是影子 ——
            // 否则"光一照就变实体"，那束光就永远杀不到影子。
            if (!lights[i].lightUpPlayer) continue;

            if (lights[i].Illuminates(p)) return true;
        }
        return false;
    }

    void FixedUpdate()
    {
        bool lit;

        bool shadow = (form != null && form.IsShadow);
        Vector2[] pts = shadow ? shadowPoints : catPoints;

        if (sampleWholeBody && pts != null && pts.Length > 0)
        {
            lit = true;
            Vector2 basePos = (Vector2)transform.position + offset;

            for (int s = 0; s < pts.Length; s++)
            {
                Vector2 p = basePos + pts[s];

                // 【关键】埋在地形内部的采样点不算数。
                // 不加这一条，影子贴地时下半截的采样点永远在地板里，
                // 那些点永远"没被照亮" —— 于是走进光里也变不回猫。
                if (solidLayer.value != 0 && Physics2D.OverlapPoint(p, solidLayer) != null)
                    continue;

                if (AnyLightOn(p)) continue;

                // 有一个点没被照到 → 整体判定为黑暗
                lit = false;

                // 记下是哪个点、以及那盏灯为什么没照到它（给调试面板用）
                failPoint = p;
                failIndex = s;
                DebugInfo = "第 " + s + " 个采样点 " + p.ToString("0.00") + " 没被照亮\n" +
                            (FindClosestZone(p) != null ? FindClosestZone(p).ExplainPoint(p) : "（没有灯）");
                break;
            }
        }
        else
        {
            lit = AnyLightOn((Vector2)transform.position + offset);
        }

        IsLit = lit;

        if (lit != lastLit)
        {
            lastLit = lit;
            if (debugLog) Debug.Log("[LightSensor] " + (lit ? "进入光照" : "进入黑暗"));
        }

        if (form != null) form.OnLightChanged(lit);
    }

    void OnDrawGizmos()
    {
        Vector2 p = (Vector2)transform.position + offset;

        Gizmos.color = IsLit ? Color.yellow : new Color(0.4f, 0.4f, 1f);
        Gizmos.DrawWireSphere(p, 0.12f);

        // 画出角色身周采样框，便于在 Scene 视图确认覆盖范围
        if (sampleWholeBody)
        {
            Gizmos.DrawWireCube(p, new Vector3(sampleSizeCat.x, sampleSizeCat.y, 0f));
            Gizmos.color = new Color(0.4f, 0.4f, 1f, 0.8f);
            Gizmos.DrawWireCube(p, new Vector3(sampleSizeShadow.x, sampleSizeShadow.y, 0f));
        }
    }
}
