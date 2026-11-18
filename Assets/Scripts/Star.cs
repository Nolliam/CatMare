// ============================================================
// Star.cs —— 收集品：拿到就加分
//
// 挂在哪：一颗星星（需要 Trigger Collider2D）
//
// 【为什么收集品值得加，而且比新机制便宜】
//   它不改任何规则，只改【玩家怎么走路】。
//   同一段路，有了星星，玩家就会绕、会跳、会去那些"不好走但看得见"的地方 ——
//   而那些地方恰恰是精心设计的地形。
//   加机关要设计谜题，加星星只是给已有的地形加一个理由。
//
// 【为什么用静态计数器，而不是找一个"HUD 单例"去加】
//   关卡重载（死亡 / 按 R）会把所有实例销毁重建，
//   实例字段会丢。静态字段 + 场景名判断，才能做到
//   "死了重来，星星也重来；但换关卡，计数归零"。
//
// 【为什么收集不看形态】
//   影子能碰到星星、猫也能碰到。这不是偷懒：
//   如果只有实体能拿，那所有星星都会被"光"绑住，
//   就再也没法把星星放在纯粹的黑暗角落里。
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Star : MonoBehaviour
{
    [Header("分")]
    [Tooltip("这颗星星值几分。普通 1 分，隐藏位置可以给 3 分")]
    public int value = 1;

    [Header("表现")]
    public SpriteRenderer visual;
    public Color color = new Color(1f, 0.88f, 0.35f, 1f);

    [Tooltip("上下浮动幅度（世界单位）。0 = 不动")]
    public float bobAmount = 0.08f;

    [Tooltip("浮动速度")]
    public float bobSpeed = 2.5f;

    [Tooltip("自转速度（度/秒）。0 = 不转")]
    public float spinSpeed = 60f;

    [Tooltip("吃掉的音效")]
    public bool playSound = true;

    // ---------------- 全局计数 ----------------
    /// <summary>本关一共有多少颗</summary>
    public static int Total { get; private set; }

    /// <summary>本关已经吃到多少颗</summary>
    public static int Collected { get; private set; }

    /// <summary>本关目前得了多少分</summary>
    public static int Score { get; private set; }

    static bool hooked;

    Vector3 basePos;
    float phase;
    bool taken;

    /// <summary>
    /// 场景卸载时把计数清零。
    ///
    /// 【为什么挂在 sceneUnloaded 上，而不是比较场景名】
    ///   以前是"场景名变了就清零"。但重载同一个场景时名字不变，
    ///   于是 Total 每次重载都 +1：5 颗星 → 重开变 10 颗 → 永远集不齐。
    ///   改成监听【场景卸载】事件：卸载一定发生在加载新场景之前，
    ///   所以每次进关都从 0 开始计数，和是不是同一关无关。
    /// </summary>
    static void Hook()
    {
        if (hooked) return;
        hooked = true;

        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
    {
        Total = 0;
        Collected = 0;
        Score = 0;
    }

    void Awake()
    {
        Hook();

        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (visual != null) visual.color = color;

        basePos = transform.position;
        phase = Random.Range(0f, 6.28f);

        Total += 1;
    }

    void Update()
    {
        if (taken || visual == null) return;

        // 上下浮动 + 自转：静止的收集品在小地图里很难被注意到
        Vector3 p = basePos;
        if (bobAmount > 0.001f)
            p.y += Mathf.Sin(Time.time * bobSpeed + phase) * bobAmount;

        transform.position = p;

        if (spinSpeed > 0.01f)
            transform.rotation = Quaternion.Euler(0f, 0f, Time.time * spinSpeed);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (taken) return;

        // 只认猫（猫身上任何一个碰撞体都行）
        CatForm form = other.GetComponentInParent<CatForm>();
        if (form == null) return;
        if (form.IsDead) return;

        Collect();
    }

    void Collect()
    {
        taken = true;

        Collected += 1;
        Score += Mathf.Max(1, value);

        if (playSound) SfxPlayer.PlayClick();

        Debug.Log("[Star] 吃到一颗（" + Collected + " / " + Total + "），本关得分 " + Score);

        // 拿掉视觉和碰撞体，物体本身留着（计数的 Total 已经在 Awake 里记过了）
        if (visual != null) visual.enabled = false;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        Destroy(gameObject, 0.05f);
    }

    /// <summary>给 HUD 用：这一关的收集情况</summary>
    public static string GetHudText()
    {
        if (Total <= 0) return null;

        return "★ " + Collected + " / " + Total;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.88f, 0.35f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, 0.2f);
    }
}
