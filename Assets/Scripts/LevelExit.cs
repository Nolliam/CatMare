// ============================================================
// LevelExit.cs —— 终点：碰到就算通关
//
// 挂在哪：一个带 Trigger Collider2D 的物体（出口的门/光里的一道缝）
//
// 【为什么"通关"这个功能比任何新机制都优先】
//   没有终点，这个游戏就没有"关于什么"。
//   玩家能跑、能跳、能变身，但永远不知道为什么在跑。
//   一个没有结尾的关卡，在别人眼里就是"一个演示"，
//   而不是"一个游戏"。终点是一句话的句号。
//
// 【为什么通关后不切换场景，而是把猫冻住】
//   因为现在只有一个场景。切场景要先加 Build Settings，
//   而且关卡之间的衔接方式尚未确定。
//   先把"到达终点"这件事本身做对，场景流程以后再加。
//   （所以这里是"停下来看着这句话"，不是"下一关"）
//
// 【为什么用 move.IsDead 来冻结，而不是禁用组件】
//   直接 move.enabled = false 会让 CatForm 的重生逻辑
//   找不到 CatMove 了，按 R 之后猫会变成"不能动的活猫"。
//   用它的 IsDead 开关，R 键走正常的死亡→重生路径，一切一致。
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider2D))]
public class LevelExit : MonoBehaviour
{
    [Header("通关")]
    [Tooltip("留空 = 用 CatMareText.ExitReached")]
    public string customMessage = "";
    [Tooltip("横幅停留时间")]
    public float messageTime = 5f;

    [Header("行为")]
    [Tooltip("通关后把猫冻住（不然它还在跑）")]
    public bool freezePlayer = true;
    [Tooltip("是否在横幅里加上死亡次数")]
    public bool showDeathCount = true;

    [Header("进入下一关")]
    [Tooltip("通关后自动进入 Build Settings 里的【下一个场景】")]
    public bool goToNextLevel = true;

    [Tooltip("等多久自动进下一关（秒）。0 = 不自动，只等玩家按键")]
    public float nextLevelDelay = 2.5f;

    [Tooltip("按任意键立刻进入下一关")]
    public bool anyKeyToContinue = true;

    [Tooltip("已经是最后一关时：回主菜单")]
    public bool backToMenuOnLastLevel = true;

    [Tooltip("主菜单的场景名")]
    public string menuScene = "MainMenu";

    [Header("外观")]
    public SpriteRenderer visual;
    public Color idleColor = new Color(0.40f, 0.40f, 0.50f, 1f);
    public Color activeColor = new Color(1f, 0.95f, 0.70f, 1f);

    bool done;

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
        if (done) return;
        done = true;

        string text = string.IsNullOrEmpty(customMessage)
            ? CatMareText.ExitReached
            : customMessage;

        if (showDeathCount && form.DeathCount > 0)
            text += "\n" + string.Format(CatMareText.ExitDeathCount, form.DeathCount);

        GameHUD.Banner(text, messageTime);
        SfxPlayer.PlayWin();

        if (visual != null) visual.color = activeColor;

        if (freezePlayer)
        {
            CatMove move = form.GetComponent<CatMove>();
            if (move != null) move.IsDead = true;
        }

        Debug.Log("[LevelExit] 到达终点，死亡次数 " + form.DeathCount);

        if (goToNextLevel) StartCoroutine(LeaveRoutine());
    }

    // ------------------------------------------------------------
    // 通关之后：等一下 → 进下一关（或回主菜单）
    // ------------------------------------------------------------
    IEnumerator LeaveRoutine()
    {
        int cur = SceneManager.GetActiveScene().buildIndex;
        int next = cur + 1;
        int total = SceneManager.sceneCountInBuildSettings;

        string target = null;

        // Build Settings 里还有下一个场景就进去（不管它是关卡还是主菜单）
        if (cur >= 0 && next < total)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(next);
            if (!string.IsNullOrEmpty(path))
                target = System.IO.Path.GetFileNameWithoutExtension(path);
        }
        else if (backToMenuOnLastLevel)
        {
            // 已经是最后一关：回主菜单
            target = menuScene;
        }

        if (string.IsNullOrEmpty(target))
        {
            Debug.LogWarning("[LevelExit] 没有下一关，也没配 menuScene。停在原地。");
            yield break;
        }

        if (nextLevelDelay > 0f)
        {
            float t = 0f;
            while (t < nextLevelDelay)
            {
                // 玩家按键就提前走（0.35 秒之后才认，避免终点那一下的按键直接跳过）
                if (anyKeyToContinue && t > 0.35f && Input.anyKeyDown) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        else if (anyKeyToContinue)
        {
            while (!Input.anyKeyDown) yield return null;
            yield return null;      // 跳过按下那一帧
        }

        yield return null;

        Time.timeScale = 1f;
        Debug.Log("[LevelExit] 加载：" + target);
        SceneManager.LoadScene(target);
    }

    void OnDrawGizmos()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(1f, 0.95f, 0.70f, 0.5f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
