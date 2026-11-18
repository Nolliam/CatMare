// ============================================================
// ShadowTimer.cs —— 在黑暗里停留太久会融入黑暗，无法脱身
//
// 挂在哪：Cat
//
// 【为什么这条规则必须存在】
//   在它之前，影形态几乎是白送的：
//     免疫陷阱、能穿任何 1 格缝、能飘、还能往上飞。
//   它唯一的代价是"按不了开关"，那只是一个很轻的限制。
//   结果就是玩家会想尽办法一直待在影形态里 —— 那样这个游戏
//   就变成了"一个能穿墙的幽灵在找路"，而不是"一只猫在黑暗边缘挣扎"。
//
// 【为什么这个惩罚是对的，而"影形态变慢/掉血"是错的】
//   惩罚必须从故事里长出来，不能是凭空加的一条数值。
//   这只猫的名字叫 CatMare（猫魇）。它融进黑暗的代价是
//   【被黑暗认领】—— 它不是受伤，它是消失。
//   玩家看到的不是血条，是那双黄眼睛一点点变暗。
//   这是同一件事，但一个是账本，一个是恐惧。
//
// 【为什么计时器只在影形态里走】
//   光里是安全的，黑暗里是有时限的。这样"要不要进黑暗"
//   才是一个真实的取舍，而不是"能进就进"。
//
// 【它顺手解决了三个旧问题】
//   1) "碗"形地形：以前是"卡住出不来 → 只能按 R"，
//      现在是"被困在暗处 → 眼睛熄灭 → 走丢"，软锁变成叙事。
//   2) 玩家会自觉控制黑暗里的停留时间，不再无脑钻缝。
//   3) 关卡设计有了时间维度：黑走廊的**长度**从此是有意义的。
// ============================================================

using UnityEngine;

public class ShadowTimer : MonoBehaviour
{
    [Header("时限")]
    [Tooltip("在完全黑暗里能撑多久（秒）。这是全项目最需要你亲手调的一个数")]
    public float maxShadowTime = 9f;

    [Tooltip("剩下这么多秒时开始出提示")]
    public float warnAt = 3f;

    [Header("在光里怎么恢复")]
    [Tooltip("在光里每秒恢复多少（1 = 和消耗一样快）")]
    public float recoverSpeed = 2.5f;

    [Header("超时")]
    public bool killOnTimeout = true;
    [Tooltip("留空 = 用 CatMareText.LostInDark")]
    public string customLostBanner = "";

    CatForm form;
    CatVisual visual;

    /// <summary>还剩多少秒</summary>
    public float TimeLeft { get; private set; }

    /// <summary>剩余比例，1 = 满，0 = 没了</summary>
    public float Ratio { get { return Mathf.Clamp01(TimeLeft / Mathf.Max(0.01f, maxShadowTime)); } }

    void Awake()
    {
        form = GetComponent<CatForm>();
        visual = GetComponent<CatVisual>();
        TimeLeft = maxShadowTime;
    }

    void Update()
    {
        if (form == null) return;

        float dt = Time.deltaTime;

        // 【按"真的在黑暗里"算，不按"是不是影子形态"算】
        //   这两件事不一样：在 1 格通道里被光照到，形态会变回实体，
        //   但碰撞体仍然是薄的（挤着）。
        //   以前用 form.IsShadow 计时，于是"站在光里、但因为挤着没法撑开"
        //   也会被算成黑暗 → 玩家死在明明亮着的地方。
        bool inDark = !form.IsLit && !form.IsDead;

        if (inDark)
        {
            TimeLeft -= dt;
        }
        else if (!form.IsDead)
        {
            TimeLeft = Mathf.Min(maxShadowTime, TimeLeft + dt * recoverSpeed);
        }

        if (TimeLeft < 0f) TimeLeft = 0f;

        // ---- 唯一的视觉预兆：眼睛在变淡 ----
        // 不要给数字、不要给血条。玩家应该先"感觉不对"，再发现规则。
        //
        // 【为什么只在前 30% 之后才开始变淡】
        //   线性变淡 = 在黑暗里的整整 9 秒都是半透明的鬼影，
        //   那看起来像 bug，不像警告。
        //   现在前 70% 时间是满亮的，只有最后 2~3 秒才开始明显消退 ——
        //   那时候玩家已经该慌了。
        if (visual != null)
        {
            float t = Mathf.Clamp01((Ratio - 0.20f) / 0.80f);
            visual.SetEyeDim(Mathf.Lerp(0.12f, 1f, t));
        }

        // ---- 提示 ----
        if (inDark && TimeLeft <= warnAt)
            GameHUD.StatusText = CatMareText.EyeFading;
        else
            GameHUD.StatusText = null;

        // ---- 走丢 ----
        if (TimeLeft <= 0f && inDark && killOnTimeout)
        {
            GameHUD.Banner(string.IsNullOrEmpty(customLostBanner)
                ? CatMareText.LostInDark
                : customLostBanner, 3f);

            form.Die();
            TimeLeft = maxShadowTime;
            if (visual != null) visual.SetEyeDim(1f);
        }
    }
}
