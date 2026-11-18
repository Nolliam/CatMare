// ============================================================
// Checkpoint.cs —— 检查点：碰到之后，死了从这里重生
//
// 挂在哪：一个带 Trigger Collider2D 的物体（地上的一盏小灯/一个爪印）
//
// 【为什么必须有它】
//   以前死了永远回到出生点。关卡一长，玩家死一次要从头跑——
//   这不会让人"更小心"，只会让人退游戏。
//   检查点不是仁慈，它是让玩家敢去试错的前提。
//   玩家不敢死，就不会去试探那些"这个缝我能不能钻"的地方，
//   而试探正是这个游戏的全部乐趣。
//
// 【为什么默认只触发一次】
//   反复触发会不断播提示音、刷横幅，很吵。
//   触发一次后它一直是"当前重生点"，功能并没有失效。
//
// 【为什么重生位置上要加一点偏移】
//   重生点是检查点物体的中心。如果它正好在地面上，
//   猫的碰撞体会和地面重叠，物理引擎会把它顶出来。
//   所以往上抬半个身高，落下去正好站稳。
// ============================================================

using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Checkpoint : MonoBehaviour
{
    [Header("表现")]
    [Tooltip("碰到时屏幕中间显示的话。留空 = 用 CatMareText.CheckpointSaved")]
    public string customMessage = "";
    [Tooltip("显示多久")]
    public float messageTime = 2f;

    [Header("外观")]
    public SpriteRenderer visual;
    public Color idleColor = new Color(0.30f, 0.30f, 0.38f, 1f);
    public Color activeColor = new Color(0.55f, 1f, 0.72f, 1f);

    [Header("重生位置")]
    [Tooltip("重生点相对本物体的偏移。往上抬半个猫身，别卡进地面")]
    public Vector2 spawnOffset = new Vector2(0f, 0.9f);

    bool used;

    void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (visual != null) visual.color = idleColor;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        CatForm form = other.GetComponentInParent<CatForm>();
        if (form == null) return;

        // 已经用过的检查点不用再触发，但重生点本来就是它，不影响功能
        if (used) return;
        used = true;

        form.SetSpawnPoint(transform.position + (Vector3)spawnOffset);

        if (visual != null) visual.color = activeColor;

        SfxPlayer.PlayCheckpoint();

        // 留空就用 CatMareText 里的默认台词
        string text = string.IsNullOrEmpty(customMessage)
            ? CatMareText.CheckpointSaved
            : customMessage;

        if (!string.IsNullOrEmpty(text))
            GameHUD.Banner(text, messageTime);

        Debug.Log("[Checkpoint] 重生点已更新：" + name);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.55f, 1f, 0.72f, 0.6f);
        Gizmos.DrawWireSphere(transform.position + (Vector3)spawnOffset, 0.16f);
    }
}
