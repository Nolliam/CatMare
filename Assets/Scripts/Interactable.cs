// ============================================================
// Interactable.cs —— 所有"可以按 E 互动"的东西的基类
//
// 【为什么要一个基类】
//   后续会有很多种互动：关灯、拉窗帘、推箱子、开机关、捡东西。
//   如果每种各写一套"检测玩家靠近 + 显示提示 + 按键触发"，
//   代码会重复五六遍，而且以后改提示样式要改六处。
//
//   抽成一个基类之后：
//     检测、提示、按键 → 由 PlayerInteract 统一处理
//     实际效果（关灯/拉窗帘） → 各自重写 Interact()
//
//   这就是"把不变的部分抽出来，把会变的部分留成虚函数"。
//
// 【为什么需要 Collider2D】
//   PlayerInteract 是用 Physics2D.OverlapCircle 找附近物体的，
//   没有碰撞体就找不到。给互动物加一个 Trigger 的 Collider2D 即可。
// ============================================================

using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    [Tooltip("玩家靠近时显示的提示文字。不要写按键，按键由 HUD 统一加")]
    public string prompt = CatMareText.InteractDefault;

    [Tooltip("玩家在范围内是否能互动（可以被子类重写来加条件）")]
    public bool enabledInteract = true;

    [Tooltip("是否只有猫形态才能互动（影形态是一团没有实体的东西，推不动开关）")]
    public bool requiresCatForm = true;

    /// <summary>现在能不能互动</summary>
    public virtual bool CanInteract()
    {
        return enabledInteract && isActiveAndEnabled;
    }

    /// <summary>真正发生什么，由子类实现</summary>
    public abstract void Interact();

    void OnDrawGizmosSelected()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
