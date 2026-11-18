// ============================================================
// MirrorSurface.cs —— 镜面：光打上去会反射
//
// 挂在哪：镜面板本体（必须是 Ground 层，它会挡光）
//
// 【为什么用一个空组件当标记】
//   LightZone 在打光的时候需要回答一个问题：
//   "这条射线撞到的这个东西，是墙还是镜子？"
//   用组件类型回答最直接、最不容易配错：
//   有 MirrorSurface 就是镜子，没有就是墙。
//   （不用 Layer：Layer 区分不了"两个都是 Ground 层但一个是镜子"）
//
// 【法线就是物体的 up】
//   把镜面板旋转 45° → 法线 45° → 水平光折成垂直光。
//   旋转 -45° → 折向另一边。
//   这和真实镜子一致：反射角由镜面朝向决定。
// ============================================================

using System.Collections.Generic;
using UnityEngine;

public class MirrorSurface : MonoBehaviour
{
    /// <summary>场景里所有镜面，光在计算反射时要遍历它们</summary>
    public static readonly List<MirrorSurface> All = new List<MirrorSurface>();

    [Tooltip("反射后的亮度衰减（由 LightZone 的 Bounce Falloff 统一控制，这里只是显示用）")]
    public bool drawGizmo = true;

    /// <summary>镜面法线（世界空间）</summary>
    public Vector2 Normal { get { return transform.up; } }

    /// <summary>镜面切线（沿板子长度方向）</summary>
    public Vector2 Tangent { get { return transform.right; } }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// 这个点是不是落在镜面上。
    ///
    /// 【刻意不留容差】
    ///   试过给 0.4 的容差，结果是"黑暗里也会被算成被照亮"——
    ///   因为容差会让光从板子两头漏出去，判定范围比画面更大。
    ///   宁可严格：判定范围 ≤ 画面范围，最坏是"看着亮但不亮"，
    ///   也比"看着黑却算亮"好 —— 后者会让人在安全的地方变实体然后被打死。
    /// </summary>
    public bool ContainsPoint(Vector2 world, float tolerance = 0f)
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return false;

        if (col.OverlapPoint(world)) return true;

        if (tolerance <= 0.0001f) return false;

        return (col.ClosestPoint(world) - world).sqrMagnitude <= tolerance * tolerance;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmo) return;

        Vector2 n = Normal;
        Vector2 t = Tangent;

        Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + (Vector3)(n * 0.7f));

        Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.45f);
        Gizmos.DrawLine(transform.position - (Vector3)(t * 0.6f),
                        transform.position + (Vector3)(t * 0.6f));
    }
}
