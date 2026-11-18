// ============================================================
// TimerLight.cs —— 会自己开关的灯
//
// 挂在哪：灯的同一个物体上（和 LightZone 放一起）
//
// 【为什么这个小小的机关很值钱】
//   在此之前，光是"角色在哪"的问题（走进光里 / 走出光里）。
//   有了它，光开始是"什么时候"的问题。
//   于是同一块地，有时是路，有时是死路 —— 关卡不必变大，深度就翻倍了。
//
//   而且它和 ShadowTimer 咬得很紧：
//     灯灭的那几秒必须变影子冲过去，但影形态是有时限的。
//     "暗的窗口"与"影子能撑多久"两个时钟一对上，关卡就有了呼吸。
//
// 【为什么要有 startOff / phase 两个参数】
//   两个灯如果完全同步，玩家会以为它们是一盏灯。
//   稍微错开相位（phase 0.3），才开始有"节奏"可言，
//   玩家也才会意识到自己需要观察规律。
// ============================================================

using UnityEngine;

public class TimerLight : MonoBehaviour
{
    [Header("要控制的灯")]
    [Tooltip("留空则自动找同一物体上的 LightZone")]
    public LightZone target;

    [Header("节奏")]
    [Tooltip("亮多久（秒）")]
    public float onTime = 2.5f;
    [Tooltip("灭多久（秒）")]
    public float offTime = 1.5f;
    [Tooltip("初始相位偏移（0~1）。多个灯错开一点才有节奏感")]
    [Range(0f, 1f)] public float phase = 0f;
    [Tooltip("开始时不亮")]
    public bool startOff = false;

    [Header("短暂预警（可选，先留空）")]
    [Tooltip("灭之前是否提前把画面提示一下。0 = 不提示")]
    public float preWarn = 0f;

    float timer;
    bool on;

    void Start()
    {
        if (target == null) target = GetComponent<LightZone>();

        on = !startOff;
        timer = (on ? onTime : offTime) * (1f - phase);
        Apply();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        on = !on;
        timer = on ? onTime : offTime;
        Apply();
    }

    void Apply()
    {
        if (target != null) target.SetEnabled(on);
    }
}
