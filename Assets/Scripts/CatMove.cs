// ============================================================
// CatMove.cs —— 黑猫的平台跳跃移动
//
// 挂在哪：Cat 根节点（需要 Rigidbody2D）
//
// 【为什么重力自己算，不用 Rigidbody2D 自带的】
//   默认重力上升和下落一样快，跳起来像在月球。
//   手感好的跳跃都是"上升轻、下落沉"——这个不对称是跳跃爽感的主要来源。
//   所以 gravityScale 设为 0，重力全在代码里算。
//
// 【土狼时间 / 跳跃缓冲】
//   土狼时间：离开平台后的 0.1 秒内仍然可以起跳。
//            没有它，玩家会觉得"我明明还在边上，怎么就掉下去了"。
//   跳跃缓冲：落地前 0.15 秒内按的跳，会在落地瞬间补上。
//            没有它，玩家会觉得"我按了它不理我"。
//
// 【和暗影形态的关系】
//   这个脚本只管"猫形态"的物理。暗影形态的漂浮由 CatForm 决定：
//   如果 CatForm.IsShadow 为真，这里就不施加重力，改为缓慢上浮（按住跳跃）。
//
// 【为什么输入在 Update、物理在 FixedUpdate】
//   Rigidbody 的模拟跑在固定时间步里。在 Update 里改速度会随帧率抖动。
//   输入每帧都读（不会漏按键），但改速度的动作放在 FixedUpdate。
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CatMove : MonoBehaviour
{
    [Header("水平移动")]
    public float moveSpeed = 6f;
    [Tooltip("加速有多快（按着方向键时）。越大越跟手")]
    public float groundAccel = 45f;
    [Tooltip("减速有多快（松开方向键时）。这个数决定滑不滑。" +
             "比加速大 1.5~2 倍才有「一松手就停」的手感；" +
             "等于加速就会滑出去半格，跑酷会觉得踩在冰上")]
    public float groundDecel = 80f;
    public float airControl = 0.65f;

    [Header("跳跃")]
    public float jumpVelocity = 12.5f;
    public float gravityUp = -32f;
    public float gravityDown = -55f;
    public float maxFallSpeed = -26f;
    [Tooltip("松开跳跃键时上升速度保留的比例")]
    public float jumpCutMultiplier = 0.42f;

    [Header("宽容度")]
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.15f;

    [Header("地面检测")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.12f;
    public LayerMask groundLayer;

    [Tooltip("贴地容差：脚下再往下探这么多。\n" +
             "站在【移动平台】上时，物理接触是一帧有一帧没有的，" +
             "圆形检测正好卡在边界上，于是「落地」反复跳变 ——" +
             "表现就是偶尔能跳一下、然后长时间跳不了。这条射线兜住那段空隙。")]
    public float groundSnap = 0.2f;

    [Tooltip("左右两条射线离中心的距离。0 = 三条重合")]
    public float groundRaySpread = 0.35f;

    [Tooltip("射线起点【往上抬】多少再往下打。\n" +
             "为什么必须抬：射线起点在脚底，如果它正好落在薄平台的下表面以下，\n" +
             "一条【向下】的射线就永远碰不到平台（平台在你上方）→ 判成没落地。\n" +
             "往上抬一点，让射线从平台内部/上方穿过去，薄的也能打中。")]
    public float groundRayLift = 0.2f;

    [Tooltip("三条射线的起点偏移（世界单位）。\n" +
             "X 正值往右挪，Y 正值往上挪。\n" +
             "和 Ground Ray Lift 叠加：起点 = Ground Check 位置 + 这个偏移 + (0, Lift)")]
    public Vector2 groundRayOffset = Vector2.zero;

    [Tooltip("在 Scene 视图里画出脚下的三条射线：绿 = 碰到地面，红 = 没碰到。\n" +
             "编辑模式和 Play 模式都能看")]
    public bool debugGizmos = true;

    [Header("暗影形态的漂浮")]
    [Tooltip("在黑暗中按住跳跃时的上浮速度上限")]
    public float shadowRiseSpeed = 2.6f;
    [Tooltip("上浮加速度。小一点，飘起来才有飘浮的感觉")]
    public float shadowRiseAccel = 9f;

    public bool IsGrounded { get; private set; }
    public float VelocityY { get { return rb != null ? rb.velocity.y : 0f; } }

    Rigidbody2D rb;
    CatForm form;
    float coyoteTimer;
    float bufferTimer;
    bool wasGrounded;

    // 输入缓存
    float inputX;
    bool jumpHeld;
    bool downHeld;

    public bool IsDead { get; set; }

    [Header("调试")]
    [Tooltip("左上角显示形态 / 落地 / 跳跃缓冲的实时状态。\n" +
             "查「为什么跳不起来」时勾上：一眼就能看出是没落地、还是形态不对")]
    public bool debugHud = false;

    Collider2D groundHit;
    bool jumpDeniedLogged;
    GUIStyle dbgStyle;

    // 三条地面射线的状态（给 Gizmos 画图用）
    readonly Vector2[] rayOrigin = new Vector2[3];
    readonly bool[] rayHitOk = new bool[3];
    readonly float[] rayHitDist = new float[3];
    readonly string[] rayHitName = new string[3];

    /// <summary>
    /// 脚下三条向下的射线：左 / 中 / 右。
    ///
    /// 【为什么用三条射线，而不是一个圆】
    ///   圆检测回答的是"脚下这个圈里有没有东西"，但它对"站在边缘"很苛刻：
    ///   脚只要偏出去一点就判不到；而且移动平台上接触每帧都在抖，
    ///   圆正好卡在边界，落地状态就跟着抖。
    ///
    ///   三条射线回答的是更具体的问题："我脚下这一条竖线上有没有地面"。
    ///   左右两条还能覆盖"站在平台边缘"的情况 ——
    ///   只要有一条碰到 Ground 或 Glass，就允许跳。
    ///
    ///   而且它在 Scene 视图里是可见的（绿=碰到，红=没碰到），
    ///   "为什么跳不了"这个问题可以直接用眼睛回答。
    /// </summary>
    void CastGroundRays()
    {
        groundHit = null;

        float len = Mathf.Max(0.01f, groundSnap) + Mathf.Max(0f, groundRayLift);
        float bestDist = float.MaxValue;

        Vector2 basePos = (groundCheck != null)
            ? (Vector2)groundCheck.position
            : (Vector2)transform.position + Vector2.down * 1.5f;

        // 起点 = Ground Check 位置 + 偏移 + 抬高
        //   抬起来以后，射线必然从上往下穿过平台本体，
        //   所以薄平台（0.25 格）也能打中。
        basePos += groundRayOffset;
        basePos.y += groundRayLift;

        for (int i = 0; i < 3; i++)
        {
            float off = (i - 1) * groundRaySpread;
            Vector2 o = new Vector2(basePos.x + off, basePos.y);

            rayOrigin[i] = o;
            rayHitOk[i] = false;
            rayHitDist[i] = len;
            rayHitName[i] = null;

            // RaycastAll：射线起点可能就在自己的碰撞体里，
            // 单条 Raycast 会先返回自己，反而把地面漏掉。
            RaycastHit2D[] hits = Physics2D.RaycastAll(o, Vector2.down, len, groundLayer);

            float best = len;
            bool found = false;

            for (int h = 0; h < hits.Length; h++)
            {
                Collider2D c = hits[h].collider;
                if (c == null) continue;
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

                // 只认 Ground / Glass 两层（groundLayer 掩码已经限定了，这里再兜一次保险）
                if (!IsGroundLayer(c.gameObject.layer)) continue;

                if (hits[h].distance < best)
                {
                    best = hits[h].distance;
                    found = true;
                    groundHit = c;
                    rayHitName[i] = c.gameObject.name;
                }
            }

            if (found)
            {
                rayHitOk[i] = true;
                rayHitDist[i] = best;
                if (best < bestDist) bestDist = best;
            }
        }
    }

    bool IsGroundLayer(int layer)
    {
        return (groundLayer.value & (1 << layer)) != 0;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        form = GetComponent<CatForm>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Start()
    {
        // 【为什么要把地面层打印出来】
        //   这一整套东西里最容易踩的坑是"图层忘了勾"：
        //   在新图层上放了一块地板，逻辑上它明明是地面，
        //   但跳跃只认 Ground Layer 遮码 —— 于是站在上面跳不起来，
        //   而且完全没有任何报错。找这个坑能找一小时。
        //   启动时打一行，一眼就能看出遮码里有什么。
        Debug.Log("[CatMove] 地面检测层 = " + LayerMaskToString(groundLayer) +
                  "（站在不在这个列表里的碰撞体上，跳不起来）");
    }

    static string LayerMaskToString(LayerMask mask)
    {
        string s = "";
        for (int i = 0; i < 32; i++)
        {
            if ((mask.value & (1 << i)) == 0) continue;
            string n = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(n)) n = "Layer" + i;
            s += (s.Length > 0 ? ", " : "") + n;
        }
        return s.Length > 0 ? s : "（空！什么都踩不到）";
    }

    void Update()
    {
        if (IsDead) { inputX = 0f; jumpHeld = false; downHeld = false; return; }

        inputX = Input.GetAxisRaw("Horizontal");
        jumpHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        downHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            bufferTimer = jumpBufferTime;
            jumpDeniedLogged = false;      // 每按一次只报一次
        }
        else
        {
            bufferTimer -= Time.deltaTime;
        }

        // 可变跳跃高度
        if ((Input.GetKeyUp(KeyCode.Space) || Input.GetKeyUp(KeyCode.W) || Input.GetKeyUp(KeyCode.UpArrow))
            && rb.velocity.y > 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * jumpCutMultiplier);
        }
    }

    void FixedUpdate()
    {
        if (IsDead) return;

        float dt = Time.fixedDeltaTime;

        // ---- 地面检测：脚下三条向下射线 ----
        CastGroundRays();

        IsGrounded = (groundHit != null);

        // ---- 落地音效 ----
        // 此刻 rb.velocity.y 还是"撞地前"的速度（物理求解在 FixedUpdate 之后），
        // 所以正好能按落地的狠劲决定音量：轻轻落地几乎没声，高处砸下来很闷一声。
        if (IsGrounded && !wasGrounded) SfxPlayer.PlayLand(rb.velocity.y);
        wasGrounded = IsGrounded;

        if (IsGrounded) coyoteTimer = coyoteTime;
        else coyoteTimer -= dt;

        // ---- 水平 ----
        // 【加速和减速分开算，这是"手感"的关键】
        //   加速慢 = 起步有重量感（好）
        //   减速慢 = 松手还在滑（不好，跑酷会觉得踩在冰上）
        //   所以松开方向键时用 groundDecel，通常比 groundAccel 大 1.5~2 倍。
        float targetVx = inputX * moveSpeed;

        bool noInput = Mathf.Abs(inputX) < 0.01f;

        float accel;
        if (IsGrounded)
            accel = noInput ? groundDecel : groundAccel;
        else
            accel = groundAccel * airControl;

        float vx = Mathf.MoveTowards(rb.velocity.x, targetVx, accel * dt);

        bool shadow = form != null && form.IsShadow;

        float vy = rb.velocity.y;

        // ---- 跳跃（两种形态都能跳，但暗影形态主要是漂浮）----
        if (bufferTimer > 0f && coyoteTimer > 0f && !shadow)
        {
            vy = jumpVelocity;
            bufferTimer = 0f;
            coyoteTimer = 0f;
            jumpDeniedLogged = false;
            SfxPlayer.PlayJump();
        }
        else if (bufferTimer > 0f && !jumpDeniedLogged)
        {
            // 玩家按了跳跃，但这一帧跳不了 —— 把【那一刻】的状态打出来。
            // 屏幕上的面板需要自己抬眼去看，而"跳不起来"往往发生在
            // 正盯着角色的那一瞬间，所以这里改成自动记一条。
            jumpDeniedLogged = true;

            Debug.LogWarning("[CatMove] 跳跃被拒绝：" +
                             "\n  形态 = " + (shadow ? "影子（影子本来就不能跳，空格是上浮）" : "猫") +
                             "\n  落地 = " + IsGrounded +
                             "\n  脚下 = " + ((groundHit != null)
                                 ? groundHit.gameObject.name + " / 层 " +
                                   LayerMask.LayerToName(groundHit.gameObject.layer)
                                 : "没检测到任何东西") +
                             "\n  coyote = " + coyoteTimer.ToString("0.000") +
                             "   buffer = " + bufferTimer.ToString("0.000") +
                             "\n  地面检测点 = " + (groundCheck != null ? groundCheck.position.ToString() : "没设") +
                             "   半径 = " + groundCheckRadius +
                             "\n  地面检测层掩码 = " + groundLayer.value);
        }

        if (shadow)
        {
            // 暗影形态：默认【悬停】，按跳跃上浮，按下键下沉
            // 为什么默认悬停：暗影是一团没有重量的东西，它不该往下掉。
            // 悬停还带来一个好处——玩家松开按键时画面是静止的，
            // 可以停下来观察、思考下一步，这对解谜很重要。
            float targetVy = 0f;

            if (downHeld) targetVy = -shadowRiseSpeed * 0.7f;
            else if (jumpHeld) targetVy = shadowRiseSpeed;

            vy = Mathf.MoveTowards(vy, targetVy, shadowRiseAccel * dt);
        }
        else
        {
            // 猫形态：正常重力
            float g = vy > 0f ? gravityUp : gravityDown;
            vy += g * dt;
            if (vy < maxFallSpeed) vy = maxFallSpeed;
        }

        rb.velocity = new Vector2(vx, vy);
    }

    void OnGUI()
    {
        if (!debugHud) return;

        if (dbgStyle == null)
        {
            dbgStyle = new GUIStyle(GUI.skin.label);
            dbgStyle.fontSize = 15;
            dbgStyle.alignment = TextAnchor.UpperLeft;
            dbgStyle.normal.textColor = new Color(1f, 1f, 0.6f);
            CatFont.Apply(dbgStyle, null);
        }

        bool shadow = form != null && form.IsShadow;

        string groundName = (groundHit != null)
            ? groundHit.gameObject.name + " [" + LayerMask.LayerToName(groundHit.gameObject.layer) + "]"
            : "（没检测到）";

        string text =
            "形态      " + (shadow ? "影子（不能跳，空格=上浮）" : "猫") + "\n" +
            "感知      " + ((form != null && form.IsLit) ? "有光 → 应该变实体" : "无光 → 保持影子") + "\n" +
            "落地      " + (IsGrounded ? "是" : "否") + "   脚下: " + groundName + "\n" +
            "vy        " + rb.velocity.y.ToString("0.00") + "\n" +
            "coyote    " + coyoteTimer.ToString("0.00") + "   buffer " + bufferTimer.ToString("0.00") + "\n" +
            "跳跃条件  " + ((bufferTimer > 0f && coyoteTimer > 0f && !shadow) ? "满足" : "不满足");

        // 光照诊断：把离猫最近的一盏灯的判定过程打出来
        if (form != null && explainLight)
        {
            LightZone z = FindClosestZone(form.FormCenter);
            if (z != null) text += "\n\n" + z.ExplainPoint(form.FormCenter);

            // 传感器那边失败的那个采样点（比中心点更接近真相：
            // 判定要求身上 9 个点全在光里，中心点照到不代表整身被照到）
            LightSensor sensor = GetComponent<LightSensor>();
            if (sensor != null && !string.IsNullOrEmpty(sensor.DebugInfo))
                text += "\n—— LightSensor 失败的那个点 ——\n" + sensor.DebugInfo;
        }

        GUI.Label(new Rect(12f, 12f, 860f, 420f), text, dbgStyle);
    }

    [Header("光照诊断")]
    [Tooltip("在调试面板里显示「最近那盏灯为什么没照到我」的完整判定过程")]
    public bool explainLight = true;

    LightZone FindClosestZone(Vector2 p)
    {
        LightZone best = null;
        float bestD = float.MaxValue;

        for (int i = 0; i < LightZone.AllZones.Count; i++)
        {
            LightZone z = LightZone.AllZones[i];
            if (z == null) continue;

            float d = Vector2.Distance(p, z.transform.position);
            if (d < bestD) { bestD = d; best = z; }
        }

        return best;
    }

    // ------------------------------------------------------------
    // 在 Scene 视图里画出脚下的三条射线
    //
    //   绿 = 这条射线碰到了 Ground / Glass
    //   红 = 没碰到
    //   黄点 = 命中的位置
    //
    // 编辑模式下也画（这时会临时打一次射线），所以不 Play 也能看。
    // ------------------------------------------------------------
    void OnDrawGizmos()
    {
        if (!debugGizmos) return;

        // 没在 Play 的时候，每帧手工打一次，这样颜色是实时的
        if (!Application.isPlaying) CastGroundRays();

        float len = Mathf.Max(0.01f, groundSnap) + Mathf.Max(0f, groundRayLift);
        Vector2 basePos = (groundCheck != null)
            ? (Vector2)groundCheck.position
            : (Vector2)transform.position + Vector2.down * 1.5f;

        basePos += groundRayOffset;
        basePos.y += groundRayLift;

        for (int i = 0; i < 3; i++)
        {
            float off = (i - 1) * groundRaySpread;
            Vector2 o = new Vector2(basePos.x + off, basePos.y);

            Gizmos.color = rayHitOk[i]
                ? new Color(0.2f, 1f, 0.2f, 1f)
                : new Color(1f, 0.25f, 0.25f, 1f);

            Vector2 end = o + Vector2.down * len;
            Gizmos.DrawLine(o, end);
            Gizmos.DrawWireSphere(end, 0.03f);

            if (rayHitOk[i])
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
                Gizmos.DrawWireSphere(o + Vector2.down * rayHitDist[i], 0.06f);
            }
        }
    }
}
