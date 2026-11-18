// ============================================================
// PressurePlate.cs —— 压力板：只有猫踩得动，影子踩不动
//
// 挂在哪：一块薄板（需要 Collider2D，勾 Is Trigger）
//
// 【为什么需要它，而不是再加一个"按 E 的开关"】
//   E 开关和压力板的区别不是按键方式，是【一只手够不够用】。
//     E 开关：按一下，然后走开，开关保持。可以先按开关，再去做别的。
//     压力板：人得站在上面。于是"踩住"和"去做另一件事"冲突了。
//   这个冲突就是谜题。没有它，玩家永远可以"先按开关再过去"，
//   所有机关都变成同一道题。
//
// 【为什么影子踩不动】
//   因为影子没有实体。这不是数值，这是规则。
//   于是出现一个非常好的两难：
//     要开门 → 得是猫 → 但猫过不去那条 1 格的缝。
//   这两个需求必须由【两个不同的东西】同时满足，
//   玩家就得想办法（比如先让某个箱子压住板子）。
//
// 【为什么默认"踩住才生效"，而不是"踩一次切换"】
//   踩住才生效 = 物理直觉，玩家一踩上去就看到结果，不用猜。
//   切换式留成 toggleMode，用来做"踩一下，永远打开"的那种关卡。
// ============================================================

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PressurePlate : MonoBehaviour
{
    [Header("谁能踩动")]
    [Tooltip("勾上 = 只有猫形态踩得动（影形态没有实体）。这正是这个机关存在的理由")]
    public bool catFormOnly = true;

    [Tooltip("推箱、掉下来的吊灯之类的【动态刚体】也能压住它。" +
             "配合下面的最小质量，可以做出'把箱子推上去压住板子'的谜题")]
    public bool acceptDynamicBodies = true;

    [Tooltip("动态刚体至少要有这么重才算压得住。太轻的碎片不该触发机关")]
    public float minBodyMass = 0.5f;

    [Header("踩下去发生什么")]
    [Tooltip("踩下时点亮这些灯")]
    public LightZone[] lightTargets;

    [Tooltip("踩下时是否把它们点亮。取消勾选 = 踩下反而熄灭（用来做'压住遮光'的谜题）")]
    public bool turnLightsOn = true;

    [Tooltip("踩下时禁用这些碰撞体（比如一扇门）")]
    public Collider2D[] disableWhilePressed;

    [Tooltip("勾上 = 踩一次切换一次（不用一直站着）")]
    public bool toggleMode = false;

    [Header("表现")]
    public SpriteRenderer visual;
    public Color idleColor = new Color(0.45f, 0.42f, 0.35f, 1f);
    public Color pressedColor = new Color(0.95f, 0.80f, 0.40f, 1f);
    [Tooltip("踩下去时板子下沉多少")]
    public float pressDepth = 0.08f;

    List<Collider2D> inside = new List<Collider2D>();
    bool isPressed;
    bool applied;
    Vector3 restPos;

    void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (visual == null) visual = GetComponent<SpriteRenderer>();
        restPos = transform.position;

        Apply(false);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!inside.Contains(other)) inside.Add(other);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        inside.Remove(other);
    }

    void Update()
    {
        // 谁在上面：猫（有实体时）或者够重的动态刚体（推箱、掉下来的吊灯）
        bool pressedNow = false;

        for (int i = inside.Count - 1; i >= 0; i--)
        {
            Collider2D c = inside[i];

            if (c == null) { inside.RemoveAt(i); continue; }

            // 看不见的（比如玻璃被关掉的碰撞体）不算
            if (!c.enabled || !c.gameObject.activeInHierarchy) continue;

            // ① 猫
            CatForm f = c.GetComponentInParent<CatForm>();
            if (f != null)
            {
                if (f.IsDead) continue;

                // 影形态没有实体 —— 压在板上也压不下去
                if (catFormOnly && f.IsShadow) continue;

                pressedNow = true;
                continue;
            }

            // ② 别的动态刚体（箱子、掉下来的吊灯）
            if (!acceptDynamicBodies) continue;

            Rigidbody2D rb = c.attachedRigidbody;
            if (rb == null) continue;
            if (rb.bodyType != RigidbodyType2D.Dynamic) continue;
            if (rb.mass < minBodyMass) continue;

            pressedNow = true;
        }

        if (toggleMode)
        {
            // 切换式：从"没压"变成"压住"的那一帧翻一次
            if (pressedNow && !isPressed) Apply(!applied);
            isPressed = pressedNow;
            return;
        }

        if (pressedNow != isPressed)
        {
            isPressed = pressedNow;
            Apply(isPressed);
        }
    }

    void Apply(bool pressed)
    {
        applied = pressed;

        if (lightTargets != null)
        {
            for (int i = 0; i < lightTargets.Length; i++)
            {
                if (lightTargets[i] == null) continue;
                lightTargets[i].SetEnabled(pressed ? turnLightsOn : !turnLightsOn);
            }
        }

        if (disableWhilePressed != null)
        {
            for (int i = 0; i < disableWhilePressed.Length; i++)
            {
                if (disableWhilePressed[i] != null)
                    disableWhilePressed[i].enabled = !pressed;
            }
        }

        if (visual != null) visual.color = pressed ? pressedColor : idleColor;

        Vector3 p = restPos;
        if (pressed) p.y -= pressDepth;
        transform.position = p;
    }

    void OnDrawGizmos()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(0.95f, 0.80f, 0.40f, 0.5f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
