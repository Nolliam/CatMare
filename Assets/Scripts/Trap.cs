// ============================================================
// Trap.cs —— 陷阱：碰到就死
//
// 挂在哪：陷阱物体（需要 Collider2D，勾 Is Trigger）
//
// 【为什么用 Trigger 而不是碰撞】
//   陷阱不应该把猫挡住 —— 猫应该走进去然后死。
//   用 Trigger 就不参与物理阻挡，只负责"检测重叠"。
//
// 【为什么死亡交给 CatForm，而不是直接 Destroy】
//   直接销毁会让重生、形态、输入状态全部乱掉。
//   统一由 CatForm.Die() 处理：进入死亡状态 → 停输入 → 等一小会儿 → 回到出生点。
//   这样任何死法（陷阱、坠落、Boss）都走同一条路。
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Trap : MonoBehaviour
{
    [Tooltip("只有带这个标签的物体碰到才算（留空则不检查标签）")]
    public string targetTag = "Player";

    [Header("表现")]
    [Tooltip("死亡时是否让陷阱闪一下")]
    public bool flashOnKill = true;

    [Tooltip("【移动的陷阱必须勾上】OnTriggerEnter2D 只在开始重叠的那一帧触发。" +
             "陷阱自己会动的时候，猫和它可能在两帧之间交错而过，事件不触发 ——" +
             "表现就是有时候能穿过去、有时候死。勾上之后改成每帧重叠查询。")]
    public bool detectByOverlap = false;

    SpriteRenderer sr;
    Color baseColor;
    static readonly Collider2D[] overlapBuf = new Collider2D[8];

    void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }

    void Update()
    {
        if (!detectByOverlap) return;

        Collider2D col = GetComponent<Collider2D>();
        if (col == null || !col.enabled) return;

        Vector2 center = col.bounds.center;
        Vector2 size = col.bounds.size;

        int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, overlapBuf);
        for (int i = 0; i < n; i++)
        {
            Collider2D other = overlapBuf[i];
            if (other == null || other == col) continue;
            if (other.transform.IsChildOf(transform)) continue;
            if (other.GetComponentInParent<CatForm>() == null) continue;

            TryKill(other);
            return;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (detectByOverlap) return;   // 已经在 Update 里每帧查过了
        TryKill(other);
    }

    void TryKill(Collider2D other)
    {
        CatForm form = other.GetComponentInParent<CatForm>();
        if (form == null) return;

        // ---- 影形态对陷阱免疫 ----
        // 设计上：影形态是一团没有实体的东西，物理陷阱碰不到它。
        // 代价是"待在黑暗里"变得绝对安全 —— 所以黑暗本身必须有别的代价
        // （比如饱和度下降、怪物出现），否则玩家会永远躲在影形态里。
        if (form.IsShadow) return;

        if (!string.IsNullOrEmpty(targetTag) && !other.CompareTag(targetTag) &&
            !form.CompareTag(targetTag))
        {
            return;
        }

        form.Die();

        if (flashOnKill && sr != null)
        {
            sr.color = Color.white;
            Invoke(nameof(RestoreColor), 0.08f);
        }
    }

    void RestoreColor()
    {
        if (sr != null) sr.color = baseColor;
    }

    void OnDrawGizmos()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.5f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
