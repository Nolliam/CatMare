// ============================================================
// CatTail.cs —— 尾巴（用一个"跟随链"做出来，不需要骨骼）
//
// 【这是最初那一版，原样恢复，逻辑一行没改】
//   保留它的原因：这一版是最容易读懂的一版。
//   它不追求物理正确，只做一件事：每一节朝前一节挪过去。
//   要理解"跟随链"这个思路，读这一版比读 Verlet 那版快十倍。
//
//   但它有两个必然的毛病（不是调参没调好，是算法本身）：
//     1) 站着不动时，每一节都往前一节收，而"拉回距离"只在
//        【太远】的时候生效 —— 所以整条尾巴会一路收，最后缩成一个点。
//     2) 跑太快时，第 0 节追不上身体，整条链留在半空 —— 和猫分家。
//   根因是同一句话：距离被当成了"事后修正"，而不是位置本身。
//
//   想修好它，做法是把距离变成硬约束（两个方向都要处理），
//   并且用惯性代替 Lerp —— 那就是后面那一版做的事。
//
// 【为什么只做尾巴，不做全身的体节】
//   收益/成本比最高的是尾巴：
//     + 它立刻让整只猫"活"起来（尾巴一摆，动物感就来了）
//     + 它不参与碰撞、不影响物理、不改变状态机
//   而全身分节要付出很多：
//     - 碰撞体不能是方块了（要么用胶囊，要么每节一个碰撞体）
//     - "哪一节在黑暗里"这种判定会变成噩梦
//     - 变形、形态切换、死亡都要在各节上重新实现
//
// 【原理：跟随链（follow the chain）】
//   每一节朝前一节的位置移动，但保持固定距离。
//     第 0 节 → 追身体后方的一个锚点
//     第 i 节 → 追第 i-1 节
//   因为每节都有延迟，所以整体就会自然地拖出弧线。
//
// 【暗态时尾巴必须跟着消失】
//   不然会出现"只有一条尾巴飘在黑暗里"的恐怖画面（不是刻意设计的那种恐怖）。
//   所以每帧读 CatVisual.BodyAlpha，同步自己的透明度。
// ============================================================

using UnityEngine;

public class CatTail : MonoBehaviour
{
    [Header("形状")]
    public int segments = 6;
    [Tooltip("每节之间的世界距离")]
    public float segmentLength = 0.13f;
    [Tooltip("第 0 节离身体中心多远（往身后）")]
    public float anchorDistance = 0.34f;

    [Tooltip("整条尾巴的位移补偿。尾巴从 Cat 的原点长出来，你的原点在头附近，" +
             "所以把这里往下调（比如 y = -0.6），尾巴才会长在屁股上")]
    public Vector2 anchorOffset = Vector2.zero;

    [Header("跟随手感")]
    [Tooltip("跟随速度。越小越软，越大越硬")]
    public float followSpeed = 7f;
    [Tooltip("身体转向后，尾巴多久跟上（秒）")]
    public float anchorSmooth = 0.12f;

    [Header("外观")]
    public Color tailColor = new Color(0.07f, 0.07f, 0.10f, 1f);
    [Tooltip("最后一节的缩放（相对第 0 节），做出逐渐变细")]
    public float taperEnd = 0.38f;

    CatVisual visual;
    Rigidbody2D rb;
    SpriteRenderer[] segs;
    Vector2[] segPos;
    Sprite whiteSprite;

    Vector2 anchorDir = Vector2.left;    // 尾巴往身后
    float facing = 1f;

    void Awake()
    {
        visual = GetComponent<CatVisual>();
        rb = GetComponent<Rigidbody2D>();

        whiteSprite = MakeWhiteSprite();
        BuildSegments();

        // 初始位置：沿身后排开
        Vector2 start = (Vector2)transform.position + anchorOffset + anchorDir * anchorDistance;
        for (int i = 0; i < segPos.Length; i++)
        {
            segPos[i] = start + anchorDir * (segmentLength * i);
            if (segs[i] != null) segs[i].transform.position = segPos[i];
        }
    }

    Sprite MakeWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.filterMode = FilterMode.Point;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    void BuildSegments()
    {
        int n = Mathf.Max(2, segments);
        segs = new SpriteRenderer[n];
        segPos = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            GameObject go = new GameObject("Tail" + i);
            go.transform.SetParent(transform.parent);      // 和猫同级，但不跟着猫的形变

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = whiteSprite;
            sr.sortingOrder = -1;                          // 画在身体后面
            sr.color = new Color(tailColor.r, tailColor.g, tailColor.b, 1f);

            float t = (n <= 1) ? 0f : i / (float)(n - 1);
            float size = Mathf.Lerp(0.15f, 0.15f * taperEnd, t);
            sr.transform.localScale = new Vector3(size, size, 1f);

            segs[i] = sr;
        }
    }

    void LateUpdate()
    {
        if (segs == null || segs.Length == 0) return;

        float dt = Time.deltaTime;

        // ---- 尾巴朝向 = 运动的反方向 ----
        Vector2 targetDir = -anchorDir;

        if (rb != null && rb.velocity.sqrMagnitude > 0.09f)
        {
            targetDir = -((Vector2)rb.velocity).normalized;
        }
        else
        {
            targetDir = new Vector2(-facing, -0.35f).normalized;   // 静止时微微下垂
        }

        anchorDir = Vector2.Lerp(anchorDir, targetDir, Mathf.Clamp01(dt / Mathf.Max(0.01f, anchorSmooth)));

        // 锚点 = 身体位置 + 配置偏移 + 身后方向
        Vector2 anchor = (Vector2)transform.position + anchorOffset + anchorDir * anchorDistance;

        // ---- 第 0 节直接钉死在锚点上 ----
        // 【这就是"第一块必须紧贴身体"的全部答案】
        //   原来这里是 segPos[0] = Lerp(segPos[0], anchor, followSpeed*dt)，
        //   意思是"第 0 节去追锚点"。它追不上 —— 每帧只走 11% 的距离，
        //   而身体一直在往前动，所以身体和第一块之间永远有一条缝。
        //
        //   但"拖"这件事应该发生在第 1 节之后，不该发生在身体和尾巴之间。
        //   身体和尾巴之间是【关节】，关节不该有延迟。所以这里直接赋值。
        //   anchorOffset 用于把关节对齐到尾部。
        segPos[0] = anchor;

        // ---- 其余各节追前一节，并强制保持距离 ----
        for (int i = 1; i < segPos.Length; i++)
        {
            segPos[i] = Vector2.Lerp(segPos[i], segPos[i - 1], Mathf.Clamp01(followSpeed * dt));

            Vector2 d = segPos[i] - segPos[i - 1];
            float dist = d.magnitude;
            if (dist > segmentLength && dist > 0.0001f)
            {
                segPos[i] = segPos[i - 1] + d / dist * segmentLength;
            }
        }

        // ---- 写回位置和透明度 ----
        float alpha = (visual != null) ? visual.BodyAlpha : 1f;

        for (int i = 0; i < segPos.Length; i++)
        {
            if (segs[i] == null) continue;

            segs[i].transform.position = segPos[i];
            segs[i].color = new Color(tailColor.r, tailColor.g, tailColor.b, alpha);
            segs[i].enabled = alpha > 0.02f;
        }

        // 朝向更新（用于静止时的下垂方向）
        if (rb != null && Mathf.Abs(rb.velocity.x) > 0.1f) facing = Mathf.Sign(rb.velocity.x);
    }
}
