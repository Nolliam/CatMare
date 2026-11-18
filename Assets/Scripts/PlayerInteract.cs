// ============================================================
// PlayerInteract.cs —— 玩家靠近互动物、按 E 触发
//
// 挂在哪：Cat
//
// 【为什么检测放在这里，而不是每个互动物自己判断"玩家在不在这附近"】
//   如果每个开关、每个窗帘、每个箱子都写一遍"检测玩家"，那是几十份重复代码，
//   而且每份都要维护自己的距离、提示、按键逻辑。
//
//   反过来：由玩家主动扫描附近，找到最近的互动物，用 E 触发它。
//   每个互动物只需要实现"我做了什么"（Interact）。
//   这一条叫"控制反转"——把"谁来找谁"的方向倒过来，代码就少一大半。
//
// 【为什么选最近的那一个，而不是全部触发】
//   两个开关挨在一起时，如果同时触发，玩家会觉得失控。
//   只作用于最近的，行为可预测。
//
// 【为什么用 OverlapCircleAll 而不是触发器事件】
//   和 LightSensor 一样：幂等、不怕重叠、不需要维护 enter/exit 计数。
//   场景里互动物不多，每帧一次扫描开销可以忽略。
// ============================================================

using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [Header("检测")]
    [Tooltip("能互动的距离")]
    public float range = 1.8f;

    [Tooltip("只在这一层里找互动物。留空 = 全部层")]
    public LayerMask interactLayer = ~0;

    [Header("按键")]
    public KeyCode interactKey = KeyCode.E;

    /// <summary>当前瞄准的互动物（离得最近且在范围内）</summary>
    public Interactable Current { get; private set; }

    CatMove move;
    CatForm form;

    void Awake()
    {
        move = GetComponent<CatMove>();
        form = GetComponent<CatForm>();
    }

    void Update()
    {
        // 死亡时不检测
        if (move != null && move.IsDead)
        {
            Current = null;
            return;
        }

        Current = FindNearest();

        if (Current != null && Input.GetKeyDown(interactKey))
        {
            Current.Interact();
            SfxPlayer.PlayClick();
        }
    }

    Interactable FindNearest()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, range, interactLayer);

        Interactable best = null;
        float bestDist = float.MaxValue;

        bool isShadow = form != null && form.IsShadow;

        for (int i = 0; i < hits.Length; i++)
        {
            // 别把自己身上的碰撞体当成互动物
            if (hits[i].transform == transform || hits[i].transform.IsChildOf(transform)) continue;

            Interactable it = hits[i].GetComponentInParent<Interactable>();
            if (it == null) continue;
            if (!it.CanInteract()) continue;

            // 影形态推不动任何实体机关 —— 这是"能力互斥"的一半，
            // 它让"要不要变回猫"成为一个真实的取舍，而不需要加任何数值惩罚。
            if (isShadow && it.requiresCatForm) continue;

            float d = Vector2.SqrMagnitude((Vector2)it.transform.position - (Vector2)transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = it;
            }
        }

        return best;
    }

    // 提示文字的绘制已经搬到 GameHUD：
    // 画字的地方只留一个，这里只负责"当前瞄着谁"（Current）。

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
