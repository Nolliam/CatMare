// ============================================================
// WorldLayer.cs —— 随形态显隐的"另一个世界"
//
// 挂在哪：一个父物体上。它会把【自己所有的子物体】的渲染和碰撞一起开关。
//        也可以直接挂在瓦片地图上（瓦片地图本身不用是子物体）。
//
// 用途：SHIFT 这个主题的核心实现。
//   同一个房间放两套地形 —— 一套只在实体时存在，一套只在影子时存在。
//   按 Shift 切换形态，脚下和面前的地形整个换掉。
//
// ------------------------------------------------------------
// 【为什么不做成 SetActive(false)】
//   关 GameObject 会把上面的 LightZone / Mover / 其他脚本一起停掉，
//   下次打开时它们的内部状态是断的（LightZone.SetEnabled 里有同样的问题）。
//   所以这里只关【渲染器】和【碰撞体】，物体本身保持活着：
//     · 看不见 ✓
//     · 穿得过 ✓
//     · 但脚本还在跑，状态不会断 ✓
// ------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class WorldLayer : MonoBehaviour
{
    public enum Mode
    {
        Both,        // 两个世界都存在（默认，等于没挂这个组件）
        SolidOnly,   // 只在【实体】形态存在
        ShadowOnly   // 只在【影子】形态存在
    }

    [Tooltip("这一套地形在哪种形态下存在")]
    public Mode mode = Mode.ShadowOnly;

    [Tooltip("切换形态时是否要有一点延迟。0 = 立刻切")]
    public float switchDelay = 0f;

    [Tooltip("勾上 = 只在进入本关时按当时形态设置一次，之后不再跟随\n" +
             "（用来做「一开始就断开」的东西）")]
    public bool setOnceOnStart = false;

    [Header("调试")]
    public bool debugLog = false;

    readonly List<Renderer> renderers = new List<Renderer>();
    readonly List<Collider2D> colliders = new List<Collider2D>();

    CatForm cat;
    bool lastVisible = true;
    bool applied;
    float delayTimer;

    void Awake()
    {
        Cache();

        cat = FindCat();
    }

    void Cache()
    {
        renderers.Clear();
        colliders.Clear();

        // 自己身上的
        AddFrom(gameObject);

        // 所有子物体上的
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i] == transform) continue;
            AddFrom(all[i].gameObject);
        }
    }

    void AddFrom(GameObject go)
    {
        Renderer[] rs = go.GetComponents<Renderer>();
        for (int i = 0; i < rs.Length; i++)
            if (rs[i] != null && !renderers.Contains(rs[i])) renderers.Add(rs[i]);

        Collider2D[] cs = go.GetComponents<Collider2D>();
        for (int i = 0; i < cs.Length; i++)
            if (cs[i] != null && !colliders.Contains(cs[i])) colliders.Add(cs[i]);
    }

    static CatForm FindCat()
    {
        return Object.FindObjectOfType<CatForm>();
    }

    void Start()
    {
        if (cat == null) cat = FindCat();

        ApplyNow();

        if (setOnceOnStart) enabled = false;
    }

    void Update()
    {
        if (setOnceOnStart) return;
        if (cat == null) { cat = FindCat(); if (cat == null) return; }

        bool want = ShouldShow(cat.IsShadow);

        if (want == lastVisible && applied) return;

        // 延迟切换：让玩家看清"世界变了"，而不是瞬间闪一下
        if (switchDelay > 0.001f && applied)
        {
            delayTimer -= Time.deltaTime;
            if (delayTimer > 0f) return;
        }

        lastVisible = want;
        ApplyNow();
        delayTimer = switchDelay;
    }

    bool ShouldShow(bool isShadow)
    {
        switch (mode)
        {
            case Mode.SolidOnly: return !isShadow;
            case Mode.ShadowOnly: return isShadow;
            default: return true;
        }
    }

    void ApplyNow()
    {
        bool show = ShouldShow(cat != null && cat.IsShadow);

        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] != null) renderers[i].enabled = show;

        for (int i = 0; i < colliders.Count; i++)
            if (colliders[i] != null) colliders[i].enabled = show;

        lastVisible = show;
        applied = true;

        if (debugLog)
            Debug.Log("[WorldLayer] " + name + " → " + (show ? "出现" : "消失"));
    }

    /// <summary>让外部（比如开关）也能控制这一层</summary>
    public void ForceShow(bool show)
    {
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] != null) renderers[i].enabled = show;

        for (int i = 0; i < colliders.Count; i++)
            if (colliders[i] != null) colliders[i].enabled = show;
    }

    void OnDrawGizmos()
    {
        if (mode == Mode.Both) return;

        // 在 Scene 里标出这一层属于哪个世界
        Bounds b = new Bounds(transform.position, Vector3.one);

        Renderer r = GetComponent<Renderer>();
        if (r != null) b = r.bounds;
        else
        {
            TilemapRenderer tr = GetComponent<TilemapRenderer>();
            if (tr != null && tr.GetComponent<Tilemap>() != null)
                b = tr.GetComponent<Tilemap>().localBounds;
        }

        Gizmos.color = (mode == Mode.ShadowOnly)
            ? new Color(0.45f, 0.55f, 1f, 0.5f)     // 蓝 = 影子世界
            : new Color(1f, 0.85f, 0.35f, 0.5f);    // 黄 = 实体世界

        Gizmos.DrawWireCube(transform.position, new Vector3(math_max(b.size.x, 1f), math_max(b.size.y, 1f), 0f));
    }

    static float math_max(float a, float b) { return a > b ? a : b; }
}
