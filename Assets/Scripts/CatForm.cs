// ============================================================
// CatForm.cs —— 猫 / 暗影 两种形态的切换
//
// 规则：
//   在黑暗里 → 只剩黄眼睛 → 可以钻窄缝、可以向上飘
//   一旦有光 → 变回小猫 → 沉重的身体、正常重力
//
// 【为什么用"两个碰撞体互相开关"来实现钻缝】
//   "能从很小的空隙钻过去"本质上是【碰撞体变小】。
//   与其去算像素，不如准备两个 Collider2D：
//     猫碰撞体：正常大小（约 0.8 × 1.0）
//     影碰撞体：压扁（约 0.8 × 0.28）
//   切换形态时开关这两个，不要同时开着——两个都开等于用了大的那个。
//
// 【为什么变回来之前要检查"有没有空间"】
//   场景：猫在黑暗里以影形态钻到一条很矮的通道里，通道尽头是光照区。
//   如果立刻变回猫，猫的碰撞体会和天花板重叠，物理引擎会把它猛推出来，
//   看起来像"被弹飞"，甚至卡进墙里。
//   所以：先查一次"猫的碰撞盒在这里放得下吗"，放不下就保持影形态（视觉上也是眼睛），
//   等它滑出通道再变回来。玩家不会察觉，但不会卡住。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CatForm : MonoBehaviour
{
    [Header("碰撞体（两个，切换开关）")]
    public Collider2D catCollider;
    public Collider2D shadowCollider;

    [Header("引用")]
    public CatMove move;
    public CatVisual visual;

    [Header("环境")]
    [Tooltip("地形所在的层，用于检查猫的碰撞体放得下吗")]
    public LayerMask solidLayer;

    [Header("透光玻璃")]
    [Tooltip("留空则自动找场景里所有 GlassPane。只有勾了 passableInShadow 的才会被影子穿过")]
    public GlassPane[] passThrough;

    [Header("死亡与重生")]
    public Transform spawnPoint;
    public float respawnDelay = 0.7f;

    [Header("卡住时的重来键")]
    [Tooltip("掉进碗里出不来时按这个键回出生点")]
    public KeyCode restartKey = KeyCode.R;

    public bool IsShadow { get; private set; }
    public bool IsDead { get; private set; }

    /// <summary>
    /// 现在是不是被光照到（由 LightSensor 每帧写入）。
    /// 【为什么单独暴露这个】
    ///   "在黑暗里"和"是影子形态"是两件事：
    ///   在 1 格通道里被光照到，形态会变回实体，但碰撞体仍然是薄的（挤着）。
    ///   走丢的计时器必须按"真的在黑暗里"算，不能按"形态"算 ——
    ///   否则会出现"站在光里却被计时器杀死"。
    /// </summary>
    public bool IsLit { get { return lit; } }

    /// <summary>
    /// 当前形态碰撞体在世界空间里的中心点。
    ///
    /// 【为什么需要它】
    ///   红外线、激光、瞄准线这类东西，逻辑是"这条线有没有扫到目标"。
    ///   用物理碰撞体去判，就要依赖层、触发器、碰撞体开关状态 ——
    ///   任何一项配错，表现都是"明明扫到了却不死"，而且极难查。
    ///   直接拿一个【点】去做几何计算（点到线段距离），就绕开了所有这些东西。
    ///
    /// 【为什么不用 collider.bounds.center】
    ///   禁用状态的碰撞体，bounds 不一定可靠（HasRoomForCat 里有同样的问题）。
    ///   所以用 offset × 缩放自己算，永远准。
    /// </summary>
    public Vector2 FormCenter
    {
        get
        {
            Collider2D c = IsShadow ? shadowCollider : catCollider;
            if (c == null) return transform.position;

            return (Vector2)transform.position + Vector2.Scale(GetOffset(c), WorldScale());
        }
    }

    /// <summary>当前形态碰撞体在世界空间里的高度（影子约 0.2，猫约 3.0）</summary>
    public float FormHeight
    {
        get
        {
            Collider2D c = IsShadow ? shadowCollider : catCollider;
            if (c == null) return 0.2f;

            return GetSize(c).y * Mathf.Abs(transform.lossyScale.y);
        }
    }

    /// <summary>死过几次（终点横幅要用）</summary>
    public int DeathCount { get; private set; }

    [Header("物理")]
    [Tooltip("强制给两个碰撞体挂零摩擦材质。" +
             "移动是每帧直接设速度的，只要有摩擦就会互相打架：" +
             "站着动不了、走起来发黏、被卡住。")]
    public bool forceNoFriction = true;

    [Header("形态切换")]
    [Tooltip("目标形态必须稳定持续这么久才真的切换。用来压制光边缘抖动导致的反复闪")]
    public float formSwitchDelay = 0.12f;

    [Header("重来")]
    [Tooltip("按 R 重新加载整个关卡（箱子、提灯、开关、吊灯全部复原）。" +
             "取消勾选 = 按 R 只回到检查点，场景状态不变")]
    public bool restartReloadsLevel = true;

    [Tooltip("死亡后也重载整个关卡（保留最近的检查点位置）。" +
             "关掉 = 死亡只是把猫挪回检查点，世界里被你改动的东西保留")]
    public bool reloadLevelOnDeath = true;

    // 死亡次数跨重载保留：重载场景会重建 Cat，实例字段归零，
    // 所以用静态变量记着。换关卡（场景名变了）时清空。
    static int sessionDeaths;
    static string lastScene;

    // 死亡重载时，把"该回到哪个检查点"带过场景边界
    static Vector3? pendingSpawn;

    bool lit = true;
    float deadTimer;
    Vector3 spawnPos;
    bool suppressFormSfx;   // 重生时不要连播"变回猫"的声音
    Collider2D[] passThroughCols;

    bool lastWanted;
    float pendingTime;

    void Start()
    {
        CachePassThroughColliders();
        ApplyPassThrough(IsShadow);
    }

    // ------------------------------------------------------------
    // 强制零摩擦
    //
    // 【为什么必须在代码里强制，不能只靠 Inspector】
    //   猫的移动是"每帧直接把速度设成目标值"。物理摩擦会在这一步之后
    //   再把速度磨掉一点，两者互相打架 —— 表现就是：
    //     站着按方向键不动、走起来很黏、贴在墙上推不出去。
    //
    //   这个材质必须在【两个碰撞体】上都有。而且每次在 Inspector 里
    //   换碰撞体类型（比如方块换胶囊），新碰撞体的材质栏都是空的 ——
    //   换一次就复发一次。所以干脆在运行时统一挂上，一劳永逸。
    // ------------------------------------------------------------
    void ApplyNoFriction()
    {
        if (!forceNoFriction) return;

        PhysicsMaterial2D mat = new PhysicsMaterial2D("Cat_NoFriction_Runtime");
        mat.friction = 0f;
        mat.bounciness = 0f;
        mat.hideFlags = HideFlags.HideAndDontSave;

        if (catCollider != null) catCollider.sharedMaterial = mat;
        if (shadowCollider != null) shadowCollider.sharedMaterial = mat;
    }


    // ------------------------------------------------------------
    // 透光玻璃
    //
    // 【为什么是"影子能穿"而不是"猫能穿"】
    //   因为玻璃是实体，影子不是。这是同一套语言：
    //     实体碰得到实体，影子碰得到光。
    //   于是玻璃在关卡里同时说两句话：
    //     "猫过不去" + "光过得去" —— 一句话就造出一个特殊的暗区。
    //
    // 【为什么用 IgnoreCollision 而不用物理层矩阵】
    //   层矩阵是全局设置，改动一次会影响所有同类物体，
    //   而且写在 Project Settings 里，代码里看不出来。
    //   IgnoreCollision 只影响这一对碰撞体，明确、可逆、不动全局设置。
    // ------------------------------------------------------------
    void CachePassThroughColliders()
    {
        GlassPane[] panes = (passThrough != null && passThrough.Length > 0)
            ? passThrough
            : FindObjectsOfType<GlassPane>();

        List<Collider2D> list = new List<Collider2D>();

        for (int i = 0; i < panes.Length; i++)
        {
            if (panes[i] == null) continue;
            if (!panes[i].passableInShadow) continue;

            Collider2D[] cols = panes[i].GetComponentsInChildren<Collider2D>();
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] != null) list.Add(cols[c]);
            }
        }

        passThroughCols = list.ToArray();
    }

    void ApplyPassThrough(bool shadow)
    {
        if (passThroughCols == null || shadowCollider == null) return;

        for (int i = 0; i < passThroughCols.Length; i++)
        {
            if (passThroughCols[i] == null) continue;
            Physics2D.IgnoreCollision(shadowCollider, passThroughCols[i], shadow);
        }
    }

    void Awake()
    {
        if (move == null) move = GetComponent<CatMove>();
        if (visual == null) visual = GetComponent<CatVisual>();

        spawnPos = (spawnPoint != null) ? spawnPoint.position : transform.position;

        // ---- 处理"跨场景"的两件事 ----
        // 玩家可能有多个 Cat 实例，但这里只有一个；用场景名判断要不要清空统计
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != lastScene)
        {
            lastScene = sceneName;
            sessionDeaths = 0;
            pendingSpawn = null;     // 换了关卡，旧的检查点不能带过来
        }

        // 死于"重载关卡"时，静态变量里带着检查点位置 —— 现在就站过去
        if (pendingSpawn.HasValue)
        {
            spawnPos = pendingSpawn.Value;
            transform.position = spawnPos;
            pendingSpawn = null;
        }

        DeathCount = sessionDeaths;

        ApplyNoFriction();

        // 初始一定是猫形态
        ApplyForm(false);

        // 去抖的初值要和当前形态一致，否则开局会白等一个 formSwitchDelay
        lastWanted = IsShadow;
        pendingTime = 0f;
    }

    void Update()
    {
        if (IsDead)
        {
            deadTimer -= Time.deltaTime;
            if (deadTimer <= 0f) Respawn();
            return;
        }

        // ---- 卡住了就重来 ----
        // 【为什么需要这个键】
        //   有个地形会把人关死：一个碗/盒子，里面有阴影、碗口是亮的。
        //   影子往上飘，一露头就被光照到 → 想变回猫 → 猫太重掉回去 → 无限循环。
        //   没有血条、不会摔死，玩家就只能强退游戏。这不是难度，这是坏掉。
        //   所以给一个永远可用的出口：按 R 从头来。
        //
        // 【为什么默认是"重载整个关卡"，而不是"回检查点"】
        //   关卡里的状态远不止猫的位置：推箱推到哪了、提灯被扔在哪、
        //   开关是开是关、吊灯掉没掉、踩了几下……
        //   这些状态散在十几个脚本里，逐个写"复原"必然漏。
        //   重载场景是唯一"绝对完整"的复原方式，而且不需要维护。
        //   代价是检查点进度一起没了 —— 对"卡住了想重来"这个场景，这是对的。
        if (Input.GetKeyDown(restartKey))
        {
            if (restartReloadsLevel) RestartLevel();
            else Die();
            return;
        }

        // 每帧决定应该是什么形态
        bool wantShadow = !lit;

        // 【形态切换去抖】
        //   光扇的边缘不是一条光滑的线：它会随着遮挡物移动、灯的开关、
        //   细分射线的精度而在几个像素之间抖。
        //   9 点采样一旦在边界上反复跨越，形态就会一帧猫一帧影子地闪。
        //   所以：目标状态必须【稳定持续 formSwitchDelay 秒】才真的切换。
        if (wantShadow == lastWanted) pendingTime += Time.deltaTime;
        else { lastWanted = wantShadow; pendingTime = 0f; }

        if (wantShadow != IsShadow && pendingTime >= formSwitchDelay)
        {
            // 【光永远赢】被光照到就一定变回实体，不检查空间。
            //
            // 为什么不能加"放得下才变"这个条件：
            //   影子只有 0.1 高（本地），猫有 1.5 —— 影子贴地时，
            //   猫的碰撞体必然有一截在地板里。于是"放得下吗"永远回答不是，
            //   猫就永远变不回来 = 站在光里也能一直保持影子形态 = 无敌。
            //
            // 代价：如果光真的照进 1 格高的通道，变实体后会被卡住。
            // 这是关卡设计要避免的事 —— 别把光照进通道口。
            //
            // 前提（必做）：影子碰撞体的偏移 Y 要设成 -0.70（本地值），
            // 让它的底面和猫的底面对齐。
            ApplyForm(wantShadow);
        }
    }

    /// <summary>由 LightSensor 每帧调用</summary>
    public void OnLightChanged(bool isLit)
    {
        lit = isLit;
    }

    /// <summary>按 R 从头来：重载整个关卡，所有机关和物体回到初始状态</summary>
    public void RestartLevel()
    {
        int idx = SceneManager.GetActiveScene().buildIndex;

        if (idx < 0)
        {
            // 场景不在 Build Settings 里（编辑器里直接按 Play 的测试场景）
            // 这种情况没法重载，退回"回检查点"
            Debug.LogWarning("[CatForm] 当前场景不在 Build Settings 里，R 只能回检查点");
            Die();
            return;
        }

        // R 是"从头开始这一关"：清掉检查点，从出生点重来
        pendingSpawn = null;
        sessionDeaths = 0;

        Time.timeScale = 1f;
        SceneManager.LoadScene(idx);
    }

    /// <summary>由 Checkpoint 调用：从这里重生</summary>
    public void SetSpawnPoint(Vector3 worldPos)
    {
        spawnPos = worldPos;
        Debug.Log("[CatForm] 重生点更新到 " + worldPos);
    }

    void ApplyForm(bool shadow)
    {
        if (IsShadow == shadow) return;

        IsShadow = shadow;

        // 形态和碰撞体绑在一起
        if (catCollider != null) catCollider.enabled = !shadow;
        if (shadowCollider != null) shadowCollider.enabled = shadow;

        if (visual != null) visual.SetShadow(shadow);

        ApplyPassThrough(shadow);

        if (!suppressFormSfx)
        {
            if (shadow) SfxPlayer.PlayToShadow();
            else SfxPlayer.PlayToCat();
        }
    }

    // ------------------------------------------------------------
    // 碰撞体尺寸/偏移的读取
    //
    // 【为什么不直接用 collider.bounds】
    //   bounds 是物理系统算出来的包围盒。这个函数被调用时猫的碰撞体
    //   往往正被禁用（影子形态），禁用状态下的 bounds 不保证是真实尺寸。
    //   所以绕开它，自己读 size / offset —— 这两个值永远可读。
    //   （记得乘上 transform 的缩放：本地 size × 缩放 = 世界 size）
    // ------------------------------------------------------------
    Vector2 WorldScale()
    {
        Vector3 s = transform.lossyScale;
        return new Vector2(Mathf.Abs(s.x), Mathf.Abs(s.y));
    }

    static Vector2 GetSize(Collider2D c)
    {
        BoxCollider2D box = c as BoxCollider2D;
        if (box != null) return box.size;

        CapsuleCollider2D cap = c as CapsuleCollider2D;
        if (cap != null) return cap.size;

        return c.bounds.size;
    }

    static Vector2 GetOffset(Collider2D c)
    {
        BoxCollider2D box = c as BoxCollider2D;
        if (box != null) return box.offset;

        CapsuleCollider2D cap = c as CapsuleCollider2D;
        if (cap != null) return cap.offset;

        return Vector2.zero;
    }

    /// <summary>
    /// 猫的碰撞盒在这个位置放得下吗。
    /// 用 0.9 倍尺寸，留一点余量，避免"刚好贴边"被判成放不下。
    /// </summary>
    bool HasRoomForCat()
    {
        if (catCollider == null) return true;

        // 【关键：不用 catCollider.bounds】
        //   bounds 是"物理系统算出来的包围盒"。而这个函数被调用的时候，
        //   猫的碰撞体往往正处于【被禁用】状态（影子形态 / 被挤着）——
        //   禁用碰撞体的 bounds 不保证是真实尺寸，一旦它返回一个空盒子，
        //   Overlap 就查不到任何东西 → 判定"放得下" → 直接启用 1.5 高的碰撞体
        //   → 半个身子插进地板 → 厚度方向被物理推出去 → 卡到瓦片下面。
        //
        //   所以这里绕开 bounds，用【offset + size】自己算世界空间的位置。
        //
        // 【必须乘上 transform 的缩放】
        //   offset 和 size 都是【本地空间】的数。如果 Cat 的 Transform 有缩放
        //   （比如 X2 Y2），真实的碰撞体在世界里是 size × 2 ——
        //   而判定如果按本地尺寸算，就会把"2.2 × 3.0 的猫"当成"1.1 × 1.5"，
        //   于是在根本放不下的地方判定"放得下" → 撑开碰撞体 →
        //   整只猫埋进地板和墙里，头顶顶住天花板，跳不出来。
        Vector2 ws = WorldScale();
        Vector2 center = (Vector2)transform.position
                         + Vector2.Scale(GetOffset(catCollider), ws);
        Vector2 checkSize = Vector2.Scale(GetSize(catCollider), ws) * 0.9f;

        CapsuleCollider2D cap = catCollider as CapsuleCollider2D;

        Collider2D[] hits;
        if (cap != null)
        {
            hits = Physics2D.OverlapCapsuleAll(center, checkSize, cap.direction, 0f, solidLayer);
        }
        else
        {
            hits = Physics2D.OverlapBoxAll(center, checkSize, 0f, solidLayer);
        }

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;

            // 【站在可推的箱子上不算"放不下"】
            // 箱子是动态刚体，它会自己让开；墙不会。
            // 如果不排除它，站在箱子上走进光里就永远变不回猫 ——
            // 因为箱子一直"占着"身体的位置。
            Rigidbody2D other = hits[i].attachedRigidbody;
            if (other != null && other.bodyType == RigidbodyType2D.Dynamic) continue;

            return false;
        }

        return true;
    }

    // ------------------------------------------------------------
    // 死亡
    // ------------------------------------------------------------
    public void Die()
    {
        if (IsDead) return;

        IsDead = true;
        deadTimer = respawnDelay;
        sessionDeaths++;
        DeathCount = sessionDeaths;

        if (move != null) move.IsDead = true;
        if (visual != null) visual.SetDead(true);

        SfxPlayer.PlayDie();

        Debug.Log("[CatForm] 死亡（本关第 " + DeathCount + " 次）");
    }

    void Respawn()
    {
        // 【死亡 = 重载关卡，但保留检查点位置】
        //
        //   为什么不能只是"把猫挪回去"：
        //     箱子可能被推进了坑里、提灯被扔在够不到的地方、
        //     开关被按成了错的状态 —— 这些东西在世界里留着，
        //     回到检查点后面对的可能是【已经无解】的局面。
        //     重载是唯一保证"每次都从头"的办法。
        //
        //   为什么还要 pendingSpawn：
        //     重载会把猫放回场景里它出生的地方，检查点就白设了。
        //     所以临死前把"最近的检查点位置"存到一个【静态变量】里，
        //     新场景的 Cat 在 Awake 里读出来，直接站过去。
        int idx = SceneManager.GetActiveScene().buildIndex;

        if (reloadLevelOnDeath && idx >= 0)
        {
            pendingSpawn = spawnPos;
            Time.timeScale = 1f;
            Debug.Log("[CatForm] 死亡 → 重载关卡，回到检查点 " + spawnPos);
            SceneManager.LoadScene(idx);
            return;
        }

        // 场景不在 Build Settings 里（编辑器里直接 Play 的测试场景）：只能就地重生
        IsDead = false;

        transform.position = spawnPos;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.velocity = Vector2.zero;

        // 重生的瞬间是猫形态，等它自己走进黑暗才会变小
        lit = true;
        suppressFormSfx = true;          // 别在重生时额外播一声"变回猫"
        ApplyForm(false);
        suppressFormSfx = false;

        if (move != null) move.IsDead = false;
        if (visual != null) visual.SetDead(false);

        SfxPlayer.PlayRespawn();

        Debug.Log("[CatForm] 重生（就地）");
    }
}
