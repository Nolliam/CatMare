// ============================================================
// CameraFollow.cs —— 相机跟随
//
// 【为什么用 SmoothDamp 而不是直接赋值】
//   直接赋值（每帧把相机放到猫身上）会让画面随猫的每一次微小抖动而抖，
//   看起来廉价。SmoothDamp 有一点延迟，画面更稳，而且跟随感更"重"。
//
// 【为什么要"死区"】
//   如果相机永远精确跟着猫，跳跃时整个画面会上下晃，玩久了晕。
//   死区是画面中间的一块矩形：猫在这块区域里移动，相机不动。
//   超出死区才追。这是几乎所有 2D 平台游戏的标准做法。
//
// 【为什么用 LateUpdate】
//   猫的位置在 FixedUpdate 里被物理更新，Update 里可能还有别的脚本在动它。
//   相机要在"这一帧所有东西都动完之后"再跟，否则会拍到上一帧的位置。
// ============================================================

using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("跟随")]
    [Tooltip("跟随平滑时间。越大越黏，越小越跟手")]
    public float smoothTime = 0.18f;

    [Tooltip("死区：目标在这块矩形内移动时，相机不动（宽高是世界单位）")]
    public Vector2 deadZone = new Vector2(1.6f, 1.2f);

    [Header("取景偏移")]
    [Tooltip("相机相对目标的偏移。y 给正数可以让猫偏下，看到更多前方")]
    public Vector3 offset = new Vector3(0f, 0.8f, -10f);

    [Header("限制（可选）")]
    public bool clampToBounds = false;
    public Vector2 minBounds = new Vector2(-50f, -50f);
    public Vector2 maxBounds = new Vector2(50f, 50f);

    [Header("像素完美")]
    [Tooltip("把相机位置吸附到像素网格上。这是让像素块大小一致的唯一办法")]
    public bool pixelPerfect = true;

    [Tooltip("把相机位置吸附到像素格。整画面会一跳一跳，默认关")]
    public bool snapPosition = false;

    [Tooltip("必须等于你的美术素材 Pixels Per Unit（一格 16 像素 = 16）")]
    public int pixelsPerUnit = 16;

    [Tooltip("希望画面高度是多少世界单位。实际会取最接近的整数倍缩放")]
    public float targetWorldHeight = 12f;

    [Tooltip("手动指定缩放倍率。0 = 按 targetWorldHeight 自动算。" +
             "越大 = 每个美术像素占更多屏幕像素 = 画面更近（看得更少）。" +
             "想看更多世界就填 3 或 4；想看细节就填 6。\n" +
             "为什么需要它：像素完美要求倍率是整数，自动算出来的只能是" +
             "11.46 / 14.33 / 19.1 这几个档，填 12 和 14 可能得到同一个结果")]
    public int forcedZoom = 0;

    Vector3 velocity;
    Vector3 currentTargetPos;
    Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();

        if (target == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }

        if (target != null)
        {
            currentTargetPos = target.position;
            transform.position = currentTargetPos + offset;
        }

        ApplyPixelPerfectSize();
    }

    // ------------------------------------------------------------
    // 像素完美：为什么"每个像素独立"的感觉来自相机，不来自素材
    //
    // 【问题的本质】
    //   角色美术为 16×16 像素。若屏幕上一个美术像素被缩放到 7.5 个物理像素，
    //   那么有的像素占 7 格、有的占 8 格 —— 移动时像素块大小不停跳，
    //   看起来就是"糊的、不独立的"。素材本身一点问题都没有。
    //
    // 【解法：让一个美术像素 = 整数个屏幕像素】
    //   倍率 = 屏幕高度 / (每单位像素数 × 想要的世界高度)，取整
    //   orthographicSize = 屏幕高度 / (2 × 每单位像素数 × 倍率)
    //
    //   1080 高、PPU 16、想要 9 单位：
    //     倍率 = 1080 / (16×9) = 7.5 → 取 8
    //     ortho = 1080 / (2×16×8) = 4.21875
    //   → 每个美术像素正好 8 个物理像素，永远方、永远一样大。
    //
    // 【为什么还要吸附相机位置】
    //   倍率对了，但相机停在"半个像素"的位置，像素边界仍会落在屏幕像素中间。
    //   所以位置也要量化到 1/PPU 的整数倍上。
    //   这会让相机移动"一格一格"地跳 —— 这是像素完美的必然代价，
    //   比"整个画面在抖"好得多，因为整个场景是一起跳的。
    //
    // 【为什么不用 Unity 的 Pixel Perfect Camera 组件】
    //   能用就用（URP 里有）。但这个脚本自行计算，公式可读，
    //   也不依赖额外的包。两者选一个，别同时开。
    // ------------------------------------------------------------
    void ApplyPixelPerfectSize()
    {
        if (!pixelPerfect || cam == null) return;

        int ppu = Mathf.Max(1, pixelsPerUnit);
        float target = Mathf.Max(0.5f, targetWorldHeight);

        // 【为什么 Orthographic Size 会被"锁死"在一个数值上】
        //   像素完美要求：一个美术像素 = 整数个屏幕像素（否则贴图会糊）。
        //   所以倍率 zoom 必须是整数，而它只能取
        //     zoom = round(屏幕高 / (每单位像素 × 目标世界高))
        //   于是 Size = 屏幕高 / (2 × 每单位像素 × zoom) 就只剩几个可能的值。
        //   手动修改 Camera 的 Size 无效，是因为它在 LateUpdate 里每帧被重新计算。
        //
        //   想看更多世界：把 forcedZoom 填小（3 或 4），或者把 targetWorldHeight 调大。
        int zoom;
        if (forcedZoom > 0)
        {
            zoom = forcedZoom;
        }
        else
        {
            zoom = Mathf.Max(1, Mathf.RoundToInt(Screen.height / (ppu * target)));
        }

        cam.orthographicSize = Screen.height / (2f * ppu * zoom);
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (pixelPerfect) ApplyPixelPerfectSize();

        Vector3 tp = target.position;
        Vector3 want = currentTargetPos;

        // 死区：只有超出才更新目标点
        float dx = tp.x - want.x;
        if (Mathf.Abs(dx) > deadZone.x * 0.5f)
        {
            want.x = tp.x - Mathf.Sign(dx) * deadZone.x * 0.5f;
        }

        float dy = tp.y - want.y;
        if (Mathf.Abs(dy) > deadZone.y * 0.5f)
        {
            want.y = tp.y - Mathf.Sign(dy) * deadZone.y * 0.5f;
        }

        currentTargetPos = want;

        Vector3 desired = want + offset;

        if (clampToBounds)
        {
            desired.x = Mathf.Clamp(desired.x, minBounds.x, maxBounds.x);
            desired.y = Mathf.Clamp(desired.y, minBounds.y, maxBounds.y);
        }

        Vector3 pos = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);

        // 吸附到像素网格
        // 注意：默认【不吸附位置】。位置吸附会让整个画面一跳一跳，
        // 而尺寸吸附（上面的 ortho 计算）已经保证了每个像素大小一致、
        // 不会忽大忽小。只留尺寸、不吸附位置 = 像素干净 + 运动顺滑。
        if (pixelPerfect && snapPosition)
        {
            float ppu = Mathf.Max(1, pixelsPerUnit);
            pos.x = Mathf.Round(pos.x * ppu) / ppu;
            pos.y = Mathf.Round(pos.y * ppu) / ppu;
        }

        pos.z = desired.z;
        transform.position = pos;
    }
}
