// ============================================================
// CarryLamp.cs —— 能拿起来、能放下的光源
//
// 挂在哪：灯的同一个物体上（那个物体要有 Trigger 的 Collider2D，否则按不到 E）
//
// 【它为什么是最值钱的一个新动词】
//   此前所有的光都"由关卡提供"：灯在哪、什么时候亮，只能等待和躲避。
//   灯可被拿起之后，光第一次成为"可携带的资源"：
//
//     拿着灯 → 所在之处被照亮 → 保持实体 → 能按开关，但钻不过 1 格的缝
//     放下灯 → 又能变影子 → 能钻缝，但一进光就被强制变实体
//
//   一个动作，同时给出"能力"和"代价"，而且不需要任何新数值。
//
// 【为什么是移动它，而不是 SetParent 到猫身上】
//   猫的 Transform 缩放是 2。一旦把灯挂成猫的子物体，
//   灯的缩放假发变成 2 倍 —— 光扇网格会被画成两倍大，
//   而判定用的是世界半径，于是"看到的 ≠ 判定的"。
//   所以这里只搬它的【位置】，父子关系不动、缩放永远保持原样。
//
// 【为什么拿着的位置要"找空位"】
//   灯被塞进墙里的时候，光扇的射线一出发就被挡住 ——
//   画面一片黑，容易被误认为灯有问题。
//   所以每帧都检查一下"这个位置站得下灯吗"，站不下就往下 / 往前后挪，
//   全都站不下才认命地放在原位（宁可灯卡住，也不要灯消失）。
// ============================================================

using UnityEngine;

public class CarryLamp : Interactable
{
    [Header("拿在手里")]
    [Tooltip("相对猫的位置偏移。默认在身前偏下，不要在正头顶（会捅天花板）")]
    public Vector2 carryOffset = new Vector2(0.45f, 0.25f);

    [Tooltip("判断「这个位置放得下灯吗」用的半径")]
    public float freeRadius = 0.28f;

    [Tooltip("跟着谁走。留空 = 自动找场景里标签为 Player 的物体")]
    public Transform carrier;

    [Tooltip("留空则自动找自己身上的 LightZone")]
    public LightZone lamp;

    [Tooltip("拿起时顺手把灯点亮")]
    public bool turnOnWhenPicked = true;

    public string pickPrompt = "拿起灯";
    public string dropPrompt = "放下灯";

    bool carried;
    static readonly Collider2D[] probeBuf = new Collider2D[8];

    void Start()
    {
        if (lamp == null) lamp = GetComponentInChildren<LightZone>();

        if (carrier == null)
        {
            GameObject cat = GameObject.FindGameObjectWithTag("Player");
            if (cat != null) carrier = cat.transform;
        }

        prompt = pickPrompt;
    }

    public override bool CanInteract()
    {
        if (!base.CanInteract()) return false;
        if (!carried && carrier == null) return false;
        return true;
    }

    public override void Interact()
    {
        if (!carried) PickUp();
        else PutDown();
    }

    void PickUp()
    {
        carried = true;
        prompt = dropPrompt;

        if (turnOnWhenPicked && lamp != null) lamp.SetEnabled(true);

        Debug.Log("[CarryLamp] 拿起了 " + name);
    }

    void PutDown()
    {
        carried = false;
        prompt = pickPrompt;

        // 就放在当前位置。
        // （之前是回到"拿起时的位置"，但玩家的直觉是就地放下 ——
        //   而且回原位会让人以为按了撤销，而不是"我把灯放下了"。）
        Debug.Log("[CarryLamp] 放下了 " + name + "，位置 " + transform.position);
    }

    void LateUpdate()
    {
        if (!carried || carrier == null) return;

        // 只搬位置，不改父子关系、不改缩放（见文件头那段说明）
        Vector2 want = (Vector2)carrier.position + carryOffset;
        transform.position = FindFreeSpot(want);
    }

    /// <summary>
    /// 从理想位置开始，按优先级找一个"不卡在墙里"的位置。
    /// 顺序：原位 → 下方 → 左右 → 再下方。
    /// </summary>
    Vector2 FindFreeSpot(Vector2 want)
    {
        if (IsFree(want)) return want;

        Vector2[] tries = new Vector2[]
        {
            want + Vector2.down * 0.35f,
            want + Vector2.left  * 0.45f,
            want + Vector2.right * 0.45f,
            want + Vector2.down * 0.70f,
            want + Vector2.down * 1.05f,
            want + Vector2.up   * 0.40f,
        };

        for (int i = 0; i < tries.Length; i++)
        {
            if (IsFree(tries[i])) return tries[i];
        }

        // 全部站不下：宁可卡着，也不要灯消失
        return want;
    }

    /// <summary>这个位置放得下灯吗（不碰墙、不碰玻璃、不碰任何非触发器）</summary>
    bool IsFree(Vector2 p)
    {
        int n = Physics2D.OverlapCircleNonAlloc(p, freeRadius, probeBuf);

        for (int i = 0; i < n; i++)
        {
            Collider2D c = probeBuf[i];
            if (c == null) continue;

            // 跳过灯自己身上的碰撞体
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

            // 跳过猫身上的碰撞体
            if (carrier != null &&
                (c.transform == carrier || c.transform.IsChildOf(carrier))) continue;

            // 触发器不算障碍（开关、陷阱、别的互动物）
            if (c.isTrigger) continue;

            return false;
        }

        return true;
    }

    /// <summary>现在是不是被拿着</summary>
    public bool IsCarried { get { return carried; } }

    void OnDrawGizmosSelected()
    {
        // 画出身前那个"理想摆放点"，方便调 carryOffset
        Vector3 anchor = (carrier != null) ? carrier.position : transform.position;
        Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(anchor + (Vector3)carryOffset, freeRadius);
    }
}
