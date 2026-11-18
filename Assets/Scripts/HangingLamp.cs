// ============================================================
// HangingLamp.cs —— 吊灯：能当临时平台，踩两下就掉下来
//
// 挂在哪：灯的物体上（需要 Rigidbody2D + BoxCollider2D，放在 Ground 层）
//        子物体上再放一个 LightZone（灯点），父物体负责物理，子物体负责发光。
//
// 【为什么它值得单独做一个脚本】
//   在这之前，光的位置是死的。关卡是"一道固定形状的光"。
//   吊灯让光第一次变成【可以被玩家改变位置的东西】，
//   而灯的位置就是关卡结构 —— 等于玩家能亲手改地形。
//
// 【为什么灯点要放在灯的下面，而不是灯的中心】
//   灯体自己的碰撞体也在 Ground 层（它要挡光、也要能站）。
//   如果灯点放在碰撞体正中间，灯的第一条射线就会打到自己身上，
//   整盏灯瞬间全黑 —— 这个坑非常隐蔽，排查起来很久。
//   所以灯点往下降 0.6，落在碰撞体外面。
//
// 【为什么用 HingeJoint2D，而不是让它直接掉】
//   因为"会摆的灯"看起来是挂着的，"不动的灯"看起来是钉住的。
//   摆动还带来一个免费的细节：猫站上去它会晃，站不稳 ——
//   这正好告诉玩家"这是个临时平台，不是地板"。
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class HangingLamp : MonoBehaviour
{
    /// <summary>绳子的另一端怎么确定</summary>
    public enum AnchorMode
    {
        Auto,          // 往一个方向打射线，撞到瓦片就把锚点打在那儿
        FixedLength,   // 固定在正上方 ropeLength 处
        Manual         // 用指定的空物体
    }

    [Header("固定点")]
    [Tooltip("Auto = 自动往某个方向打一条射线，撞到瓦片就把锚点打在那儿（配合瓦片地图最好用）\n" +
             "FixedLength = 固定在正上方 Rope Length 处（旧行为）\n" +
             "Manual = 用下面那个 Anchor 字段指定的空物体")]
    public AnchorMode anchorMode = AnchorMode.Auto;

    [Tooltip("Auto 模式往哪个方向找锚点。默认向上找天花板；改成 (0,-1) 就是拴在地面上")]
    public Vector2 anchorSearchDir = Vector2.up;

    [Tooltip("Auto 模式最多找多远。找不到就当 FixedLength")]
    public float anchorSearchDistance = 8f;

    [Tooltip("Auto 模式认哪些层是「可以拴住」的。留空 = 自动用 Ground")]
    public LayerMask anchorLayer;

    [Tooltip("Manual 模式用：把你要拴的空物体拖进来")]
    public Transform anchor;

    [Tooltip("绳长自动 = 灯到锚点的距离。关掉就用 Rope Length 的值")]
    public bool autoRopeLength = true;

    [Header("绳索外观")]
    [Tooltip("绳长（世界单位）。FixedLength 模式用它；Auto/Manual 在 autoRopeLength 关掉时也用它")]
    public float ropeLength = 1.5f;

    [Header("绳子外观")]
    public bool showRope = true;
    [Tooltip("绳子粗细（世界单位）")]
    public float ropeWidth = 0.06f;
    public Color ropeColor = new Color(0.28f, 0.24f, 0.32f, 1f);
    [Tooltip("绳子系在灯体的什么高度（相对灯中心）")]
    public float ropeAttachHeight = 0.5f;

    [Header("踩几下掉")]
    public int requiredStomps = 2;

    [Header("摆动")]
    [Tooltip("灯体保持水平（锁死旋转）。勾上 = 一块稳定的平台；取消 = 会跟着绳子转")]
    public bool keepLevel = true;

    [Tooltip("摆动阻尼。0 = 会一直晃；2~6 = 很快安静下来")]
    [Range(0f, 10f)] public float swingDamping = 1.5f;

    [Header("掉下去之后")]
    [Tooltip("留空则自动找子物体上的 LightZone")]
    public LightZone lamp;

    [Tooltip("落地时熄灭")]
    public bool turnOffOnLanding = true;

    [Tooltip("落地后原地变成一块静态平台（不然它还是会滚）")]
    public bool remainAsPlatform = true;

    Rigidbody2D rb;
    DistanceJoint2D joint;

    int stomps;
    bool fallen;
    bool landed;
    Vector2 lastRefreshPos;
    Vector2 ropeAnchor;
    Vector2 stubDir = Vector2.down;      // 断绳那一小截朝哪垂
    SpriteRenderer rope;
    Sprite ropeSprite;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        joint = GetComponent<DistanceJoint2D>();
        if (joint == null) joint = gameObject.AddComponent<DistanceJoint2D>();

        joint.autoConfigureConnectedAnchor = false;
        joint.autoConfigureDistance = false;
        joint.connectedBody = null;                 // 连到世界坐标上的一个固定点
        joint.anchor = Vector2.zero;                // 从灯的中心量起

        // ---- 绳子的另一端：自动找 / 正上方 / 手动指定 ----
        //
        // 【为什么默认是"自动找"】
        //   关卡是瓦片地图，天花板就是一层瓦片。
        //   让程序往上打一条射线、撞到瓦片就把锚点打在那儿，
        //   比手动在世界里摆一个空物体精确得多，
        //   而且重画瓦片之后也会自动对齐。
        Vector2 anchorWorld = ComputeAnchorWorld();

        // 自动模式什么都没找到：退回"沿搜索方向固定长度"
        if (anchorMode == AnchorMode.Auto && !foundAnchor)
        {
            Vector2 d = (anchorSearchDir.sqrMagnitude > 0.0001f) ? anchorSearchDir.normalized : Vector2.up;
            anchorWorld = (Vector2)transform.position + d * ropeLength;
        }

        ropeLength = Mathf.Max(0.2f, ropeLength);

        joint.connectedAnchor = anchorWorld;

        // 【必须是 DistanceJoint2D，不能是 HingeJoint2D】
        //   HingeJoint2D 是"钉住"：它会把灯的锚点拉到和 connectedAnchor 重合 ——
        //   于是灯会直接被拽到天花板上去，踩都踩不到。
        //   DistanceJoint2D 是"绳长"：它只约束"灯离锚点不超过这么远"，
        //   灯就老老实实挂在下面，可以摆、可以被踩。
        joint.distance = ropeLength;

        // 绳可以松，不能更远（maxDistanceOnly = false 就是一根硬杆）
        joint.maxDistanceOnly = false;

        // 灯体保持水平：不然它会跟着绳子转，站上去像踩在转盘上
        if (keepLevel) rb.freezeRotation = true;

        if (lamp == null) lamp = GetComponentInChildren<LightZone>();

        ropeAnchor = joint.connectedAnchor;
        BuildRope();
    }

    // ------------------------------------------------------------
    // 算出绳子的另一端在哪
    // ------------------------------------------------------------
    bool foundAnchor;

    Vector2 ComputeAnchorWorld()
    {
        foundAnchor = false;

        if (anchorMode == AnchorMode.Manual && anchor != null)
        {
            foundAnchor = true;
            if (autoRopeLength)
                ropeLength = Vector2.Distance(transform.position, anchor.position);
            return anchor.position;
        }

        if (anchorMode == AnchorMode.FixedLength)
        {
            return (Vector2)transform.position + Vector2.up * ropeLength;
        }

        // ---- Auto：沿一个方向打射线，撞到东西就把锚点定在撞点上 ----
        LayerMask mask = anchorLayer;
        if (mask.value == 0)
        {
            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0) mask = 1 << ground;
        }

        Vector2 origin = transform.position;
        Vector2 dir = (anchorSearchDir.sqrMagnitude > 0.0001f) ? anchorSearchDir.normalized : Vector2.up;

        // 用 RaycastAll 而不是 Raycast：灯自己也在 Ground 层，
        // 单条 Raycast 会撞到自己身上，锚点就跑到灯里面去了。
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, anchorSearchDistance, mask);

        float bestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null) continue;

            // 跳过自己身上的所有碰撞体
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

            // 跳过自己的刚体（绳子上挂的附属碰撞体）
            if (rb != null && c.attachedRigidbody == rb) continue;

            if (hits[i].distance < bestDist) bestDist = hits[i].distance;
        }

        if (bestDist < float.MaxValue)
        {
            foundAnchor = true;
            if (autoRopeLength) ropeLength = bestDist;
            return origin + dir * bestDist;
        }

        // 没撞到任何东西
        return origin + dir * ropeLength;
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (landed) return;

        CatForm form = col.collider.GetComponentInParent<CatForm>();

        // ---- 还没掉：数踩踏次数 ----
        if (!fallen)
        {
            if (form == null) return;
            if (form.IsShadow) return;              // 影子没有实体，踩不动

            if (col.contactCount == 0) return;

            // 只算"从上面踩下来"。
            // 阈值放宽到中心点以下 0.3 —— 灯会轻微摆动，接触点常常不在正上方，
            // 卡得太严会导致"明明踩到了却不计数"。
            if (col.GetContact(0).point.y < transform.position.y - 0.3f) return;

            stomps++;

            if (stomps < requiredStomps)
            {
                SfxPlayer.PlayClick();
                return;
            }

            Drop();
            return;
        }

        // ---- 已经掉下来了：撞到地面就碎 ----
        if (form != null) return;                   // 砸到猫不算落地

        int ground = LayerMask.NameToLayer("Ground");
        if (col.gameObject.layer != ground) return;

        Land();
    }

    void Drop()
    {
        // 记下断绳的方向（从锚点指向灯），断口那一小截朝这个方向垂
        stubDir = (Vector2)transform.position - ropeAnchor;
        if (stubDir.sqrMagnitude < 0.0001f) stubDir = Vector2.down;
        stubDir = stubDir.normalized;

        fallen = true;

        if (joint != null) Destroy(joint);

        // 给一点初速，掉得干脆一点（不然会先"松手"再慢慢落）
        rb.velocity = new Vector2(rb.velocity.x, -3f);

        SfxPlayer.PlayClick();
        GameHUD.Banner(CatMareText.LampFell, 1.5f);
    }

    void Land()
    {
        landed = true;

        if (turnOffOnLanding && lamp != null) lamp.SetEnabled(false);

        SfxPlayer.PlayLand(-20f);                   // 借落地声当碎裂声

        if (!remainAsPlatform)
        {
            Destroy(gameObject);
            return;
        }

        // 【落地后不再冻成静态】
        // 之前跑成 Static，结果它变成一块"焊死在地上"的方块：
        // 推不动、看着像关卡地形的一部分，而不是"刚刚砸下来的东西"。
        // 现在保持 Dynamic + 锁住旋转 ——
        //   · 猫还能站在上面（它挡光也挡脚）
        //   · 猫能把它推走（实体推实体，符合规则）
        //   · 不会自己滚来滚去（旋转锁死）
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.freezeRotation = true;

        // 【关键】吊灯落地 = 遮挡关系变了。
        // 别的灯的光扇是"算一次"的，必须叫它们全部重算，
        // 否则它们的光扇里还留着吊灯挂在半空时的旧影子 ——
        // 那就是"看着是影子、走进去却不变成影子"的根源。
        LightZone.RefreshAll();
    }

    // 下落过程中每动一点就叫所有灯重算。
    // 只在下落时执行，落地变 Static 之后就再也不会进这里了。
    void LateUpdate()
    {
        UpdateRope();

        if (!fallen || landed) return;

        Vector2 p = transform.position;
        if ((p - lastRefreshPos).sqrMagnitude < 0.04f) return;

        lastRefreshPos = p;
        LightZone.RefreshAll();
    }

    // ------------------------------------------------------------
    // 绳子
    //
    // 【为什么必须画出来】
    //   没有绳子的吊灯看起来是"浮在半空的一块东西"。
    //   玩家看不出它是挂着的，也就想不到它可以被打下来。
    //   一根绳子是这句规则的说明书。
    //
    // 【为什么绳子不能是灯的子物体】
    //   灯会摆动、会转。绳子如果挂在灯下面，就会跟着一起转 ——
    //   而其实绳子是"从天花板垂下来"的，它只该跟着灯的下端移动。
    //   所以绳子是场景里的独立物体，每帧按两个端点重新摆。
    //
    // 【断了之后为什么留一截】
    //   留 0.35 单位的一段挂在原处。这样"绳子断了"这件事被看得见，
    //   而不是"绳子凭空消失"。
    // ------------------------------------------------------------
    void BuildRope()
    {
        if (!showRope) return;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.filterMode = FilterMode.Point;
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;

        ropeSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        ropeSprite.hideFlags = HideFlags.HideAndDontSave;

        GameObject go = new GameObject("Rope_" + name);
        rope = go.AddComponent<SpriteRenderer>();
        rope.sprite = ropeSprite;
        rope.color = ropeColor;
        rope.sortingOrder = -9;        // 在地形(-10)之上、猫(0)之下

        // 不吃光照：暗处的绳子也得看得见，否则等于没有
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            Material m = new Material(sh);
            m.hideFlags = HideFlags.HideAndDontSave;
            rope.sharedMaterial = m;
        }
    }

    void UpdateRope()
    {
        if (rope == null) return;

        Vector2 a = ropeAnchor;
        Vector2 b;

        if (fallen)
        {
            // 断了：从锚点留一小截，朝灯原来的那一侧垂
            b = a + stubDir * 0.35f;
        }
        else
        {
            // 绳子的灯端：从灯中心朝【锚点的方向】偏 ropeAttachHeight。
            // 锚点在上方就是灯顶，锚点在侧面就是灯的一侧，锚点在地上就是灯底。
            Vector2 dir = a - (Vector2)transform.position;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            b = (Vector2)transform.position + dir.normalized * ropeAttachHeight;
        }

        Vector2 d = b - a;
        float len = d.magnitude;

        if (len < 0.02f)
        {
            rope.enabled = false;
            return;
        }

        rope.enabled = true;
        rope.transform.position = (a + b) * 0.5f;
        rope.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        rope.transform.localScale = new Vector3(ropeWidth, len, 1f);
    }
}
