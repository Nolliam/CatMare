// ============================================================
// Mover.cs —— 让一个物体在两点之间来回移动
//
// 挂在哪：任何需要移动的物体上（移动陷阱、移动的挡光板、移动平台）
//
// 【为什么单独抽一个组件，而不是给每个东西各写一份移动代码】
//   "怎么动"和"动着有什么后果"是两件事。
//   移动的陷阱 = Mover + Trap
//   移动的挡光板 = Mover + 一个在 Ground 层的方块
//   移动平台   = Mover + 一个实心碰撞体
//   拆开之后，移动只写一遍，用在不同东西上。
//
// 【为什么用 FixedUpdate】
//   移动的陷阱要参与物理判定。在 Update 里移动它，
//   同一个物理步里它可能既在这里又在那里 —— 触发器会漏判。
//   固定在 FixedUpdate 移动，物理步和位置就对齐了。
// ============================================================

using UnityEngine;

public class Mover : MonoBehaviour
{
    [Header("路径（相对初始位置的偏移）")]
    [Tooltip("起点偏移。比如 (-2, 0) = 从初始位置往左 2 格开始")]
    public Vector2 pointA = new Vector2(-2f, 0f);

    [Tooltip("终点偏移")]
    public Vector2 pointB = new Vector2(2f, 0f);

    [Header("节奏")]
    [Tooltip("移动速度（世界单位/秒）。1.5 = 每秒走一格半")]
    public float speed = 1.5f;

    [Tooltip("从终点开始（勾上就反着来）")]
    public bool startAtB = false;

    [Tooltip("两端停顿多久（秒）。0 = 不停地来回")]
    public float pause = 0f;

    [Header("进场")]
    [Tooltip("刚进入关卡时先停多久才开始动（秒）。\n" +
             "让玩家有机会先看清它的路线，而不是一进来就被推着走。")]
    public float startDelay = 1.5f;

    [Tooltip("每个物体进场停顿时长随机浮动多少。0 = 全部同时开始（推荐）。\n" +
             "多条巡逻线同时起步看起来最整齐、也最好预判")]
    public float startDelayRandom = 0f;

    Vector3 origin;
    float t;              // 0 = A，1 = B
    int dir = 1;
    float pauseTimer;

    void Start()
    {
        origin = transform.position;

        if (startAtB)
        {
            t = 1f;
            dir = -1;
        }

        // 进场先停一下：位置先摆好，然后计时
        pauseTimer = startDelay;

        if (startDelayRandom > 0.01f)
            pauseTimer += Random.Range(0f, startDelayRandom);

        Apply();
    }

    void FixedUpdate()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            return;
        }

        float len = Mathf.Max(0.0001f, Vector2.Distance(pointA, pointB));
        t += (speed * Time.fixedDeltaTime / len) * dir;

        if (t >= 1f)
        {
            t = 1f;
            dir = -1;
            if (pause > 0f) pauseTimer = pause;
        }
        else if (t <= 0f)
        {
            t = 0f;
            dir = 1;
            if (pause > 0f) pauseTimer = pause;
        }

        Apply();
    }

    void Apply()
    {
        Vector2 target = (Vector2)origin + Vector2.Lerp(pointA, pointB, t);

        // 【有刚体就用 MovePosition，没有才直接改 transform】
        //
        //   直接写 transform.position 在物理引擎眼里是"瞬移"：
        //   它不会把站在上面的物体一起推走，也可能直接穿进猫的身体里。
        //   表现就是"平台升上去了，猫留在原地"，或者"上去了但站不稳、跳不起来"。
        //
        //   MovePosition 是"这一帧我要移动到这儿"，
        //   物理引擎会算出中间的过程，站在上面的东西会被正常推着走。
        //
        //   前提：这个物体上要有一个 Rigidbody2D，并且是 Kinematic。
        //   没有刚体就退回老做法（静态装饰物用不着物理）。
        Rigidbody2D rb = GetComponent<Rigidbody2D>();

        if (rb != null && rb.bodyType == RigidbodyType2D.Kinematic)
        {
            rb.MovePosition(target);
        }
        else
        {
            transform.position = target;
        }
    }

    /// <summary>当前在路径上的比例（0 = A，1 = B）</summary>
    public float Progress { get { return t; } }

    void OnDrawGizmosSelected()
    {
        // 走之前 origin 还没记，用当前位置当参考画一个预览
        Vector3 o = Application.isPlaying ? origin : transform.position;

        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
        Gizmos.DrawLine(o + (Vector3)pointA, o + (Vector3)pointB);
        Gizmos.DrawWireSphere(o + (Vector3)pointA, 0.12f);
        Gizmos.DrawWireSphere(o + (Vector3)pointB, 0.12f);
    }
}
