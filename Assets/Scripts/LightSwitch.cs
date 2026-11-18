// ============================================================
// LightSwitch.cs —— 开关：按 E 打开/关闭一组灯
//
// 挂在哪：一个带 Trigger Collider2D 的物体上（开关本体）
//
// 【为什么它要能控制"一组"灯，而不是一盏】
//   一个有多个光源的房间，关掉主灯之后往往还有余晖/侧光。
//   允许一次控制多盏，就能做出"关掉主灯，房间变暗但不是全黑"这种层次。
//   这比"一个开关一盏灯"灵活，而且代码量一样。
//
// 【这就是"制造暗影"的第一种实现：开关型】
//   成本最低，效果最直接：
//     玩家按 E → 灯灭 → 那片区域变暗 → 猫变成影形态 → 钻过原本过不去的缝隙
//
//   分段遮挡（窗帘）可以后续再做成"关掉这盏灯的一部分"，但底层都是
//   "让某个 LightZone 的判定失效"。
// ============================================================

using UnityEngine;

public class LightSwitch : Interactable
{
    [Header("控制的灯")]
    public LightZone[] lights;

    [Header("初始状态")]
    public bool startOn = true;

    [Header("表现（可选）")]
    [Tooltip("开关本体的 SpriteRenderer，关灯时变暗")]
    public SpriteRenderer switchVisual;
    public Color onColor = new Color(0.9f, 0.85f, 0.55f, 1f);
    public Color offColor = new Color(0.3f, 0.3f, 0.35f, 1f);

    bool isOn;

    void Start()
    {
        isOn = startOn;
        Apply();
    }

    public override void Interact()
    {
        isOn = !isOn;
        Apply();

        SfxPlayer.PlayLight(isOn);

        Debug.Log("[LightSwitch] " + (isOn ? "灯亮了" : "灯灭了"));
    }

    void Apply()
    {
        if (lights != null)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null) lights[i].SetEnabled(isOn);
            }
        }

        if (switchVisual != null)
        {
            switchVisual.color = isOn ? onColor : offColor;
        }

        // 只写动词。前面的 [E] 由 HUD 统一加，
        // 之前这里写了 "E 关灯"，结果屏幕上出现 "[E]  E 关灯"。
        prompt = isOn ? CatMareText.LightOff : CatMareText.LightOn;
    }
}
