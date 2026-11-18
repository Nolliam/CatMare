// ============================================================
// LightSwitchByLight.cs —— 光敏开关：被光照亮时才触发
//
// 挂在哪：开关物体上（需要一个 Trigger 的 Collider2D，不是必须）
//
// 【为什么它比普通开关值钱】
//   普通开关（LightSwitch）是"我把光关掉"——光是一种可以被消除的麻烦。
//   光敏开关是"我要把光引到这里"——光变成了一种需要被送达的东西。
//
//   这两个方向加起来，一盏灯就同时是【障碍】和【钥匙】：
//     同一束光，照到玩家会使其变实体（危险），
//     照到这个开关上会打开门（奖励）。
//   于是"要不要让光照进来"变成一个真实的取舍，而不是"见到光就躲"。
//
// 【它和"推箱挡光"是天生一对】
//   把箱子推到光和开关之间 → 开关失去光照 → 门关上
//   把箱子推走 → 光回来 → 门打开
//   一条完整的因果链，一个新概念都没有。
// ============================================================

using UnityEngine;

public class LightSwitchByLight : MonoBehaviour
{
    [Header("谁可以触发它")]
    [Tooltip("能触发这个开关的灯。留空 = 自动用场景里所有的灯")]
    public LightZone[] sensors;

    [Header("被照亮时发生什么")]
    [Tooltip("被照亮时把这些灯切到和现在相反的状态")]
    public LightZone[] toggleLights;

    [Tooltip("被照亮时激活这些物体（关掉时禁用）")]
    public GameObject[] toggleObjects;

    [Header("条件")]
    [Tooltip("需要几条【独立光路】同时照到才激活。\n" +
             "1 = 只要有一条光到（原来的行为）\n" +
             "2 = 两条光同时到（强光敏开关）—— 可以是两盏灯，\n" +
             "    也可以是同一盏灯折两次打到同一个点")]
    public int requiredPaths = 1;

    [Tooltip("「只发光、不改形态」的灯（致命红光那种）算不算数。\n" +
             "默认不算：那束光照到开关时玩家会被烧，却又给他开了门，讲不通")]
    public bool countDeadlyLights = false;

    [Tooltip("反过来：被照亮时关闭，变暗时打开")]
    public bool invert = false;

    [Tooltip("只触发一次，之后就锁死")]
    public bool onceOnly = false;

    [Tooltip("状态变化时是否播一下声音/闪一下")]
    public bool flashVisual = true;

    [Tooltip("留空则自动找自己身上的 SpriteRenderer")]
    public SpriteRenderer switchVisual;

    public Color litColor = new Color(1f, 0.95f, 0.55f);
    public Color darkColor = new Color(0.35f, 0.33f, 0.28f);

    bool lit;
    bool fired;
    float lastApply = -99f;

    void Start()
    {
        if (sensors == null || sensors.Length == 0)
            sensors = FindObjectsOfType<LightZone>();

        if (switchVisual == null) switchVisual = GetComponent<SpriteRenderer>();

        // 开局先按当前状态同步一次，避免"一开始就是错的"
        lit = ComputeLit();
        Apply(lit);
    }

    void Update()
    {
        if (onceOnly && fired) return;

        bool now = ComputeLit();
        if (now == lit) return;

        // 防抖：0.15 秒内只响应一次，避免光边缘抖动导致开关疯狂开合
        if (Time.time - lastApply < 0.15f) return;

        lit = now;
        lastApply = Time.time;
        if (onceOnly) fired = true;

        Apply(lit);
    }

    bool ComputeLit()
    {
        Vector2 p = transform.position;

        // ---- 按【光路条数】判定 ----
        //
        // 【为什么数光路，而不是数灯】
        //   强光敏开关要的是"两条光同时到"。
        //   如果按灯数，那么"同一盏灯折两次打到同一个点"永远只能算一条 ——
        //   而那恰恰是这套系统里最有意思的谜题：
        //   让一束光在两面镜子之间绕一圈，回到开关身上。
        //   按光路数，这种情况自然成立。
        int need = Mathf.Max(1, requiredPaths);
        int have = 0;

        if (sensors != null)
        {
            for (int i = 0; i < sensors.Length; i++)
            {
                if (sensors[i] == null) continue;

                // 「只发光、不改形态」的灯默认不参与
                if (!sensors[i].lightUpPlayer && !countDeadlyLights) continue;

                have += sensors[i].CountLightPaths(p);
            }
        }

        bool ok = have >= need;
        return invert ? !ok : ok;
    }

    void Apply(bool on)
    {
        if (toggleLights != null)
        {
            for (int i = 0; i < toggleLights.Length; i++)
            {
                if (toggleLights[i] != null) toggleLights[i].SetEnabled(on);
            }
        }

        if (toggleObjects != null)
        {
            for (int i = 0; i < toggleObjects.Length; i++)
            {
                if (toggleObjects[i] != null) toggleObjects[i].SetActive(on);
            }
        }

        if (flashVisual && switchVisual != null)
            switchVisual.color = on ? litColor : darkColor;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = lit ? litColor : darkColor;
        Gizmos.DrawWireSphere(transform.position, 0.35f);
    }
}
