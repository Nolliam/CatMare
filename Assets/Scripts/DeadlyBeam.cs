// ============================================================
// DeadlyBeam.cs —— 致命光束：影子碰到就死
//
// 挂在哪：一束【方向光形式的 LightZone】所在的物体上
// 菜单：Tools → CatMare → 23 自动生成一整台
//
// ------------------------------------------------------------
// 【为什么复用 LightZone 当视觉，而不是自己画一条线】
//   光扇的遮挡逻辑（射线打墙 → 二分细分找到墙角）是整个项目里
//   最难写对、也最值钱的一段代码。光束要"被墙挡住"、
//   "从窗口射进来"、"箱子能挡"，这些它已经全都会了。
//   再写一套只会多一份要维护的错误来源。
//
// ------------------------------------------------------------
// 【为什么这盏灯不能参与玩家的形态判定】
//   这是这个机关能不能成立的唯一关键：
//     如果它是"正常的光"，影子一进去就被照成实体 → 永远死不了。
//   所以生成器会把 LightZone 的 lightUpPlayer 关掉：
//     它照样发光、照样被墙挡，但【不改形态】。
//   于是影子能在里面保持影子，也才可能被它杀死。
//
// 【和红外线的区别】
//   红外线是一条细线，只封住一条线；
//   这是一整片扇形/条形的红光，封住的是一块面积。
//   前者适合"一条走廊的门禁"，后者适合"一束从窗口射进来的致命光"。
// ============================================================

using UnityEngine;

public class DeadlyBeam : MonoBehaviour
{
    [Header("是哪束光")]
    [Tooltip("留空则自动找自己身上的 LightZone")]
    public LightZone zone;

    [Header("规则")]
    [Tooltip("影子被照到会死")]
    public bool killsShadow = true;

    [Tooltip("实体被照到会死。默认不会 —— 这是只针对影子的机关")]
    public bool killsCat = false;

    [Tooltip("取样点数量。影子很薄，取 3~5 个点能避免「擦边没杀到」")]
    [Range(1, 9)] public int samples = 3;

    [Tooltip("取样点在影子身体上下分布的范围（世界单位）")]
    public float sampleSpread = 0.12f;

    [Header("调试")]
    public bool debugLog = false;

    static CatForm cachedCat;
    float cooldown;

    void Awake()
    {
        if (zone == null) zone = GetComponent<LightZone>();
        if (zone == null) zone = GetComponentInChildren<LightZone>();

        if (zone != null && zone.lightUpPlayer)
        {
            Debug.LogWarning("[DeadlyBeam] 「" + name + "」的光还在参与形态判定（lightUpPlayer 是勾上的）。" +
                             "这样影子一进去就会变成实体，永远死不了。建议把它关掉。");
        }
    }

    void Update()
    {
        if (cooldown > 0f) cooldown -= Time.deltaTime;

        if (!killsShadow && !killsCat) return;
        if (zone == null || !zone.IsOn) return;
        if (cooldown > 0f) return;

        CatForm cat = FindCat();
        if (cat == null || cat.IsDead) return;

        // 影子薄、实体高：在身体上下铺几个取样点，
        // 避免"光束只擦到影子下半身"时判不到。
        Vector2 center = cat.FormCenter;
        float half = (sampleSpread > 0.001f) ? sampleSpread * 0.5f : 0f;

        int n = Mathf.Clamp(samples, 1, 9);

        for (int i = 0; i < n; i++)
        {
            float t = (n == 1) ? 0.5f : (i / (float)(n - 1));
            Vector2 p = center + new Vector2(0f, Mathf.Lerp(-half, half, t));

            // 判定用 LightZone 自己的 Illuminates：
            //   它和光扇网格走的是同一套射线，所以
            //   "画面上红光被墙挡住" 和 "判定也被挡住" 永远是同一个结果。
            //   （之前加过一个"纯几何模式"，不看墙 —— 结果是
            //     光被墙挡住了、判定却还在穿墙杀人，比配错还糟。已删除。）
            if (!zone.Illuminates(p)) continue;

            if (cat.IsShadow)
            {
                if (!killsShadow) continue;
                Kill(cat, "影子");
            }
            else
            {
                if (!killsCat) continue;
                Kill(cat, "实体");
            }
        }
    }

    void Kill(CatForm cat, string who)
    {
        cooldown = 0.3f;

        if (debugLog) Debug.Log("[DeadlyBeam] " + name + " 烧到了" + who);

        cat.Die();
    }

    static CatForm FindCat()
    {
        if (cachedCat != null) return cachedCat;
        cachedCat = Object.FindObjectOfType<CatForm>();
        return cachedCat;
    }

    /// <summary>供开关类机关调用（关掉这束光）</summary>
    public void SetEnabled(bool on)
    {
        if (zone != null) zone.SetEnabled(on);
        else gameObject.SetActive(on);
    }
}
