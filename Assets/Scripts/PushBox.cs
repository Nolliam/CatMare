// ============================================================
// PushBox.cs —— 可以推动的箱子
//
// 挂在哪：箱子（需要 Rigidbody2D + BoxCollider2D）
//
// 【它为什么是这套机制里最值钱的一个机关】
//   因为灯和墙都不会动，所以"光的地图形状"是写死的。
//   一个能推动的箱子，是玩家【亲手重画光的地图】。
//   把箱子推到灯与角色之间 → 那片区域变暗 → 就能变成影子钻过去。
//   这一条把"找光"从解题变成造工具。
//
// 【为什么箱子的层必须是 Ground】
//   因为 LightZone 的遮挡射线只认 Ground 层。
//   箱子不在 Ground 上，光就会穿过它 —— 推箱子挡光就不成立。
//   同时 Ground 层也是猫的落脚层，所以箱子可以站上去。
//
// 【为什么需要这个脚本，而不是纯物理就够】
//   光扇是"算一次"的（性能）。箱子一动，所有灯的光扇就过期了。
//   所以箱子每动一点，就要让附近的灯重算一次。
//   这个脚本就干这一件事：它移动 → 叫所有灯刷新。
//
// 【物理参数怎么设才对】
//   Rigidbody2D：
//     Body Type     Dynamic
//     Gravity Scale 1
//     Freeze Rotation Z  勾上（不然箱子会被推着打滚）
//     Mass          3~6（太轻会被猫一下撞飞，太重推不动）
//     Collision Detection  Continuous（不然推快会穿墙）
//   BoxCollider2D：
//     Material      新建一个摩擦 0.4 / 弹性 0 的物理材质
//                   （猫自己是零摩擦，箱子有摩擦才不会一直滑）
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PushBox : MonoBehaviour
{
    [Header("光扇刷新")]
    [Tooltip("箱子移动超过这个距离，就让所有灯重算光扇")]
    public float refreshStep = 0.02f;

    [Tooltip("留空 = 自动让场景里所有灯重算（推荐）")]
    public LightZone[] lamps;

    Vector2 lastPos;

    void Start()
    {
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        Vector2 p = transform.position;

        if ((p - lastPos).sqrMagnitude < refreshStep * refreshStep) return;

        lastPos = p;

        // 箱子动 = 遮挡关系变了。灯不重算，光就会停在旧位置，
        // 于是出现"视觉上的影子和判定的阴影不是同一块"。
        // 手动指定的 lamps 优先，没指定就让所有灯重算。
        if (lamps != null && lamps.Length > 0)
        {
            for (int i = 0; i < lamps.Length; i++)
                if (lamps[i] != null) lamps[i].Refresh();
        }
        else
        {
            LightZone.RefreshAll();
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.4f);
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
