// ============================================================
// LightZone.cs —— 一个有遮挡的光源
//
// 【核心：真遮挡怎么实现】
//   照亮 = 距离 <= 半径   且   从光源到目标之间没有障碍物
//   用 Physics2D.Raycast 打一条线，撞到 Ground 层就说明被墙挡住。
//   这是确定性判定：同一位置同一场景，结果永远一样。
//   解谜游戏不能忍受"我明明在阴影里却变成猫了"。
//
// ------------------------------------------------------------
// 【这一版修的那个严重错误：光扇被地板盖住了】
//
//   上一版把 32 条光柱的 sortingOrder 设成 -8，而地形是 0。
//   Unity 里 sortingOrder 小的先画、被后画的盖住 —— 所以
//   那 32 条【真的被墙挡住】的光柱，全被地面的灰方块遮住了。
//   屏幕上唯一还亮着的，是 URP 的 Light2D，而 Light2D
//   本身不做遮挡（它要 ShadowCaster2D 才会被挡住，很麻烦）。
//   结果就是：判定是对的（墙后确实是暗的），画面是错的（墙后还在发光）。
//   → 玩家看到的边界 ≠ 判定的边界。这是最坏的一种错。
//
//   修法有两条，必须同时做：
//     1) 光扇画到地形【上面】、猫【下面】。
//        地形 -10 < 光扇 -5 < 尾巴 -1 < 猫 0 < 眼睛 1
//     2) Light2D 只留灯泡附近一小圈，不再往远处洒。
//        远的地方由我们自己的光扇负责，因为只有它知道墙在哪。
//
// ------------------------------------------------------------
// 【为什么从"光柱"改成"一整片扇形网格"】
//   32 条独立长条之间有缝：半径 4 的时候，相邻两条的间隔是
//   2πr/32 ≈ 0.78，比光柱宽 0.22 大得多 —— 看起来是"射线"，
//   不是"光"。而且一条 0.5 宽的墙可能正好落在两条射线之间，
//   光就从墙上漏过去了。
//   所以改成：128 条射线算出外轮廓，连成一个三角扇网格。
//   相邻三角形共用边 → 没有缝；射线从 32 加到 128 → 不漏墙。
//
// 【为什么网格只在开灯时算一次】
//   灯和墙都不动。每帧重算 128 条射线 × 每盏灯，纯属浪费。
//   所以：点亮时算一次，之后只改材质透明度做闪烁。
//   注意这条前提 —— 如果后续做【会动的墙】，要在它移动后
//   调用 lightZone.Refresh()，否则光会被旧的墙形挡住。
//
// 【为什么材质用 Sprites/Default】
//   它是加色混合（先乘 alpha 再累加），正好符合"光只会变亮、
//   不会变暗"，而且带 Cull Off（网格绕序写反了也看得见，
//   不会莫名其妙一片空白）。它不被 2D 光照影响 —— 这正是我们要的：
//   这片光由我们自己画，不交给引擎猜。
// ============================================================

using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class LightZone : MonoBehaviour
{
    /// <summary>光源类型。点光从一点发散，方向光是一束平行光</summary>
    public enum LightMode
    {
        Point,        // 灯泡、台灯：阴影往灯心收
        Directional   // 窗缝、激光：阴影是平行的
    }

    [Header("范围")]
    [Tooltip("照亮半径（世界单位）")]
    public float radius = 3.5f;

    [Tooltip("哪些层会挡住光（一般就是地形层）")]
    public LayerMask blockerLayer;

    [Tooltip("给玩家身上的点留一点余量，避免贴边就灭")]
    public float edgeTolerance = 0.05f;

    [Header("形状")]
    [Tooltip("360 = 整个圆（普通灯泡）；小于 360 = 扇形光束（窗缝、手电、舞台追光）")]
    [Range(10f, 360f)] public float coneAngle = 360f;

    [Tooltip("扇形的朝向。0 = 右，90 = 上，180 = 左，-90 = 下")]
    public float coneRotation = 0f;

    [Header("光源类型")]
    [Tooltip("点光 = 从灯的这一点发散（阴影往灯心收）\n" +
             "方向光 = 一束平行的光（阴影是平行的，像窗缝里的阳光）")]
    public LightMode lightMode = LightMode.Point;

    [Tooltip("方向光的宽度。半径 radius 被当作光束的长度")]
    public float beamWidth = 1.5f;

    [Header("视觉")]
    public Color lightColor = new Color(1f, 0.94f, 0.70f, 1f);

    [Tooltip("光扇的射线数。越多边缘越准、越不漏墙。64–256")]
    public int rayCount = 128;

    [Tooltip("光扇中心的最大亮度")]
    [Range(0f, 1f)] public float fanAlpha = 0.30f;

    [Tooltip("外缘亮度占中心的比例，做出从中心到边缘变淡")]
    [Range(0f, 1f)] public float edgeFalloff = 0.30f;

    [Tooltip("排序顺序：要压在地形上面、猫下面")]
    public int sortingOrder = -5;

    [Header("边缘精度")]
    [Tooltip("相邻两条射线长度差超过这个值，就在中间再插一条 —— 用来找出墙角")]
    public float refineThreshold = 0.04f;
    [Tooltip("最多细分几次。6 = 角度精确到基础步长的 1/64")]
    public int refineDepth = 6;

    [Header("闪烁")]
    public bool flicker = true;
    public float flickerAmount = 0.08f;
    public float flickerSpeed = 5f;

    [Header("对玩家的作用")]
    [Tooltip("这盏灯会不会影响玩家的形态。\n" +
             "勾上（默认）= 正常的光：照到影子会把它变回实体。\n" +
             "取消 = 【只发光、不改形态】：视觉上是一束光，但影子的形态判定不受它影响。\n" +
             "用来做「致命红光」这类东西 —— 它靠光扇的遮挡正确显示，\n" +
             "但不会把影子变成实体，所以影子能被它照到、也就可能被它杀死。")]
    public bool lightUpPlayer = true;

    [Header("自动重算")]
    [Tooltip("每隔多少秒重算一次光扇。0 = 每帧重算（最实时、最贵）。" +
             "为什么必须有这个：光扇是「算一次」的，任何挡住光的物体变化后它都会过期。" +
             "0.05 秒 = 20 次/秒，肉眼已经看不出延迟；电脑吃不消再往大调")]
    public float autoRefreshInterval = 0.05f;

    [Header("可选：URP Light 2D")]
    [Tooltip("挂着 Light 2D 的物体。开关灯时会一起开关。留空则不影响")]
    public GameObject visualLight;

    public bool IsOn { get { return isOn; } }

    bool isOn = true;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Mesh mesh;
    Material fanMaterial;
    SpriteRenderer lampGlow;

    Vector2 lastFanPos;
    float lastFanRot;
    float refreshTimer;

    // Light2D 组件缓存：开关灯只关这一个组件，不动整个 GameObject
    UnityEngine.Rendering.Universal.Light2D visualLight2D;

    // ------------------------------------------------------------
    // 所有灯的静态登记表
    //
    // 【为什么需要它 —— 一个真实的 bug】
    //   光扇是"算一次"的（性能）。但这带来一个漏洞：
    //   任何【挡住光的物体】动了，被它挡的那盏灯的光扇就过期了，
    //   而那盏灯自己没动，所以它不会重算。
    //
    //   踩掉吊灯时观察到的现象就是这个：
    //     吊灯落地了，别的那盏灯的光扇还是旧的
    //      → 旧的影子里还留着一块黑（视觉上影子还在）
    //      → 但判定是每帧实时算的，那块其实是亮的
    //      → 于是"看着是影子，走进去却不变成影子"
    //
    //   所以：会动的遮挡物（吊灯、推箱）在自己移动时，
    //   要叫【所有】灯重算一次。这就是 RefreshAll 的用途。
    // ------------------------------------------------------------
    static readonly List<LightZone> allZones = new List<LightZone>();

    // ------------------------------------------------------------
    // Profiler 标记
    //   打开 Window > Analysis > Profiler，跑一关，就能在 CPU 曲线里
    //   看到 CatMare.LightFan.Build（第一次建网格）和
    //   CatMare.LightFan.Rebuild（灯/墙动了之后重算）各自的毫秒数。
    //   用 ProfilerMarker 而不是 Profiler.BeginSample：前者不产生 GC。
    // ------------------------------------------------------------
    static readonly ProfilerMarker s_buildFan = new ProfilerMarker("CatMare.LightFan.Build");
    static readonly ProfilerMarker s_rebuildFan = new ProfilerMarker("CatMare.LightFan.Rebuild");

    /// <summary>
    /// 场景里所有的灯（只读）。
    /// 教学文字、装饰物这类东西用它判断"我现在这个位置亮不亮" ——
    /// 它们不是玩家、没有 LightSensor，但同样需要知道周围有没有光。
    /// </summary>
    public static IReadOnlyList<LightZone> AllZones { get { return allZones; } }

    /// <summary>让场景里所有灯重算一次光扇。有东西动了（遮挡关系变了）就调它。</summary>
    public static void RefreshAll()
    {
        for (int i = 0; i < allZones.Count; i++)
        {
            if (allZones[i] != null) allZones[i].Refresh();
        }
    }

    void OnEnable()
    {
        if (!allZones.Contains(this)) allZones.Add(this);
    }

    void OnDisable()
    {
        allZones.Remove(this);
    }

    void Awake()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = radius;
        // 这个碰撞体不用于判定，保留只是为了在 Scene 里看范围
        col.enabled = false;

        BuildFan();
        BuildLampGlow();

        // 错开每盏灯的第一次重算，避免所有灯在同一帧一起算
        refreshTimer = Random.value * Mathf.Max(0.01f, autoRefreshInterval);
    }

    // ------------------------------------------------------------
    // 光扇网格
    // ------------------------------------------------------------
    void BuildFan()
    {
        using (s_buildFan.Auto())
        {
            BuildFanCore();
        }
    }

    void BuildFanCore()
    {
        GameObject go = new GameObject("LightFan");
        go.transform.SetParent(transform, false);

        meshFilter = go.AddComponent<MeshFilter>();
        meshRenderer = go.AddComponent<MeshRenderer>();

        mesh = new Mesh();
        mesh.name = "LightFanMesh";
        meshFilter.mesh = mesh;

        // 优先用自己写的"取较亮者"着色器：两盏灯的光扇重叠时不会叠加变白。
        // 找不到才退回精灵默认材质（那是加色混合，重叠会变白，属于降级）。
        //
        // 【为什么先试 Resources.Load 再 Shader.Find —— 这是个坑，记住】
        //   Shader.Find 只能找到"被打进包里"的着色器。而只靠 Shader.Find 引用、
        //   没有任何材质资源引用的着色器，打包时会被剥离。
        //   结果就是：编辑器里一切正常，导出网页后光重叠变白、猫不再前倾。
        //   把着色器放进 Assets/Resources/ 它一定会进包，
        //   这里再用 Resources.Load 显式加载一次，双保险。
        Shader sh = Resources.Load<Shader>("Shaders/LightFanMax");
        if (sh == null) sh = Shader.Find("CatMare/LightFanMax");

        if (sh == null)
        {
            Debug.LogWarning("[LightZone] 没找到 CatMare/LightFanMax，退回默认材质：光重叠的地方会变白");
            sh = Shader.Find("Sprites/Default");
        }
        if (sh == null) sh = Shader.Find("UI/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        if (sh != null)
        {
            fanMaterial = new Material(sh);
            fanMaterial.name = "LightFanMaterial";
            fanMaterial.SetColor("_Color", Color.white);
            meshRenderer.sharedMaterial = fanMaterial;
        }
        else
        {
            Debug.LogWarning("[LightZone] 找不到可用的 Shader，光扇不会显示（判定仍然正常）");
        }

        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.enabled = false;
    }

    /// <summary>重新计算光扇形状。灯或墙动了之后要调一次。</summary>
    public void Refresh()
    {
        using (s_rebuildFan.Auto())
        {
            RefreshCore();
        }
    }

    void RefreshCore()
    {
        if (meshRenderer == null) return;

        if (!isOn)
        {
            meshRenderer.enabled = false;
            return;
        }

        // 【把构建过程包起来】
        //   光扇是在这里算出来、画出来的。这里一旦抛异常：
        //     · 网格不更新（残留旧形状，或者干脆没有）
        //     · 但 Illuminates() 走的是另一条路 —— 判定依然正常
        //   于是现象是"我明明在光里，屏幕上却一片黑"，而且极难定位。
        //   所以这里把异常点名报出来（每盏灯只报一次，不会刷屏）。
        try
        {
            BuildMesh();
            meshErrorLogged = false;
        }
        catch (System.Exception e)
        {
            if (!meshErrorLogged)
            {
                meshErrorLogged = true;
                Debug.LogError("[LightZone] 「" + name + "」的光扇构建失败，所以屏幕上不会有光：" +
                               e.GetType().Name + " " + e.Message + "\n" + e.StackTrace);
            }

            if (meshRenderer != null) meshRenderer.enabled = false;
        }
    }

    bool meshErrorLogged;

    void BuildMesh()
    {
        // 顶点是按世界方向算的，而网格是灯的子物体。
        // 所以每次重算都要把它的位置/旋转钉回"灯的正身"，
        // 否则吊灯一摆动，已经算好的光扇会跟着歪 —— 又是"看到的 ≠ 判定的"。
        Transform ft = meshRenderer.transform;
        ft.position = transform.position;
        ft.rotation = Quaternion.identity;

        if (lightMode == LightMode.Directional)
        {
            BuildBeamMesh();
            return;
        }

        int n = Mathf.Clamp(rayCount, 8, 512);

        // ---- 0. 这盏灯是整圈还是扇形 ----
        // coneAngle = 360 就是普通灯泡；小于 360 就是窗缝、手电筒、舞台追光。
        // 整圈要首尾相连，扇形不能连（连上就把整个圆填满了）。
        bool fullCircle = coneAngle >= 359.5f;

        float halfRad = fullCircle ? Mathf.PI : (coneAngle * 0.5f) * Mathf.Deg2Rad;
        float centerRad = coneRotation * Mathf.Deg2Rad;

        float startA = fullCircle ? 0f : (centerRad - halfRad);
        float endA = fullCircle ? (Mathf.PI * 2f) : (centerRad + halfRad);

        // ---- 1. 先按固定角度采一遍 ----
        List<float> angs = new List<float>(n + 64);
        List<float> lens = new List<float>(n + 64);

        float pa = startA;
        float pd = CastAt(startA);
        angs.Add(pa);
        lens.Add(pd);

        for (int i = 1; i <= n; i++)
        {
            float a = Mathf.Lerp(startA, endA, i / (float)n);
            float d = CastAt(a);

            // ---- 2. 在长度突变的地方细分，找出墙角 ----
            Refine(angs, lens, pa, pd, a, d, 0);

            angs.Add(a);
            lens.Add(d);
            pa = a;
            pd = d;
        }

        // 整圈时最后一个采样在 2π 处，和 0 处同方向，去掉避免重复顶点。
        // 扇形时必须保留末端，否则光会比该有的少一块。
        int m = fullCircle ? (angs.Count - 1) : angs.Count;

        if (m < 3)
        {
            meshRenderer.enabled = false;
            return;
        }

        // ---- 3. 连成三角扇 ----
        Vector3[] verts = new Vector3[m + 1];
        Color32[] cols = new Color32[m + 1];

        // 整圈：m 个三角形（最后一个绕回起点）
        // 扇形：m-1 个三角形（不绕回）
        int triCount = fullCircle ? m : (m - 1);
        int[] tris = new int[triCount * 3];

        Color cCenter = Tint(1f);
        Color cEdge = Tint(edgeFalloff);

        verts[0] = Vector3.zero;
        cols[0] = cCenter;

        for (int i = 0; i < m; i++)
        {
            Vector2 dir = new Vector2(Mathf.Cos(angs[i]), Mathf.Sin(angs[i]));
            verts[i + 1] = dir * lens[i];
            cols[i + 1] = cEdge;
        }

        for (int i = 0; i < triCount; i++)
        {
            int b = i + 1;
            int c = fullCircle ? (((i + 1) % m) + 1) : (i + 2);

            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = b;
            tris[i * 3 + 2] = c;
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.colors32 = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        meshRenderer.enabled = true;

        lastFanPos = transform.position;
        lastFanRot = transform.eulerAngles.z;
    }

    /// <summary>
    /// 方向光的光束网格：一条矩形长条，宽度方向分成 n 列，
    /// 每列打一条【平行】射线，找到被挡住的位置，然后把相邻两列连成两个三角形。
    ///
    /// 为什么不像点光那样用三角扇：
    ///   三角扇的所有三角形共用一个圆心，形状必然是"从一个点散开"的。
    ///   平行光束没有那个圆心 —— 它是一列一列的矩形拼起来的。
    /// </summary>
    void BuildBeamMesh()
    {
        int n = Mathf.Clamp(rayCount, 8, 512);

        float rad = coneRotation * Mathf.Deg2Rad;
        Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 perp = new Vector2(-axis.y, axis.x);

        Vector2 origin = transform.position;
        float half = beamWidth * 0.5f;

        int maxSeg = Mathf.Max(1, maxBounces + 1);
        int cols = n + 1;

        // 每一列光线的折线：最多 maxSeg+1 个点（起点 + 每次反弹的落点）
        Vector2[,] chain = new Vector2[cols, maxSeg + 1];
        int[] segs = new int[cols];
        float[] totalLen = new float[cols];

        // ---- 1. 逐列追踪：撞到镜子就折，撞到墙就停 ----
        for (int i = 0; i < cols; i++)
        {
            float u = Mathf.Lerp(-half, half, i / (float)n);

            Vector2 p = origin + perp * u;
            Vector2 d = axis;
            float budget = radius;
            float travelled = 0f;
            int s = 0;

            chain[i, 0] = p;

            while (s < maxSeg)
            {
                RaycastHit2D hit = CastRay(p, d, budget);

                if (hit.collider == null)
                {
                    // 没撞到任何东西：走到头
                    p += d * budget;
                    travelled += budget;
                    budget = 0f;
                    s++;
                    chain[i, s] = p;
                    break;
                }

                travelled += hit.distance;
                budget -= hit.distance;
                p = hit.point;

                s++;
                chain[i, s] = p;

                // 是镜子就折，是墙就停
                MirrorSurface m = hit.collider.GetComponent<MirrorSurface>();
                if (m == null) break;

                d = Vector2.Reflect(d, m.Normal);
                p += d * 0.01f;      // 离开镜面一点，避免下一次立刻撞到自己
                budget -= 0.01f;

                if (budget <= 0.02f) break;
            }

            // 用最后一个点补齐，后面拼四边形时退化成零面积，无害
            for (int k = s + 1; k <= maxSeg; k++) chain[i, k] = chain[i, s];

            segs[i] = s;
            totalLen[i] = travelled;
        }

        // ---- 2. 拼四边形：每段一层 ----
        List<Vector3> verts = new List<Vector3>();
        List<Color32> colList = new List<Color32>();
        List<int> tris = new List<int>();
        List<int> triPath = new List<int>();     // 每个三角形属于第几段光路

        for (int s = 0; s < maxSeg; s++)
        {
            // 这一段的亮度：反弹一次衰减一次
            float bright = Mathf.Pow(bounceFalloff, s);
            float brightNext = Mathf.Pow(bounceFalloff, s + 1);

            Color cThis = Tint(bright);
            Color cNext = Tint(brightNext * edgeFalloff);

            for (int i = 0; i < cols - 1; i++)
            {
                Vector2 a = chain[i, s];
                Vector2 b = chain[i, s + 1];
                Vector2 c = chain[i + 1, s];
                Vector2 d2 = chain[i + 1, s + 1];

                // 零面积（这一列在这一段已经结束了）就跳过
                if ((a - b).sqrMagnitude < 0.000001f || (c - d2).sqrMagnitude < 0.000001f) continue;

                int idx = verts.Count;

                // 顶点用"相对灯"的坐标（网格挂在灯上，旋转锁成 identity）
                verts.Add(a - origin); colList.Add(cThis);
                verts.Add(b - origin); colList.Add(cNext);
                verts.Add(c - origin); colList.Add(cThis);
                verts.Add(d2 - origin); colList.Add(cNext);

                tris.Add(idx + 0); tris.Add(idx + 1); tris.Add(idx + 3);
                tris.Add(idx + 0); tris.Add(idx + 3); tris.Add(idx + 2);

                // 记录这两个三角形属于第 s 段光路（0 = 直射，1 = 第一次反射…）
                triPath.Add(s);
                triPath.Add(s);
            }
        }

        if (verts.Count == 0)
        {
            // 光被完全挡住：判定也要跟着清空，否则会用上一次的旧形状
            judgeVerts.Clear();
            judgeTris.Clear();
            judgeTriPath.Clear();

            meshRenderer.enabled = false;
            return;
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.colors32 = colList.ToArray();
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // 把刚建好的几何留一份给判定用 —— 判定和画面从此是同一份数据
        judgeVerts.Clear();
        judgeVerts.AddRange(verts);

        judgeTris.Clear();
        judgeTris.AddRange(tris);

        // 每两个三角形（一个四边形）记录一次它属于第几段光路
        judgeTriPath.Clear();
        judgeTriPath.AddRange(triPath);

        meshRenderer.enabled = true;

        lastFanPos = transform.position;
        lastFanRot = transform.eulerAngles.z;
    }

    [Header("反射")]
    [Tooltip("光束最多反弹几次。0 = 不反弹（不认镜子，直接穿过去/被挡住）\n" +
             "只对【方向光】生效：平行光打到平面镜上还是平行光，几何上能精确算。\n" +
             "点光的反射会散开，需要另做一套，暂时不认。")]
    public int maxBounces = 2;

    [Tooltip("每次反射后的亮度衰减。0.85 = 反射段比入射段暗一点。\n" +
             "这个衰减很重要：两段一样亮，看起来就是两盏灯；\n" +
             "有衰减，才像同一条光被折了一下")]
    [Range(0.2f, 1f)] public float bounceFalloff = 0.8f;

    /// <summary>
    /// 这些碰撞体【不算遮挡】。
    ///
    /// 【为什么需要】
    ///   反射光必须从镜面本身出发，否则中间会留缝。
    ///   贴着镜面出发时，第一条射线会立刻撞到镜子自己 —— 光扇缩成一个点。
    ///   所以把镜面自己拉进这个名单。
    /// </summary>
    [Tooltip("这些碰撞体不算遮挡")]
    public Collider2D[] ignoredColliders;

    bool IsIgnored(Collider2D c)
    {
        if (c == null || ignoredColliders == null) return false;

        for (int i = 0; i < ignoredColliders.Length; i++)
        {
            if (ignoredColliders[i] == c) return true;
        }
        return false;
    }

    /// <summary>
    /// 打一条射线，跳过忽略列表和【指定的那一面镜子】。
    ///
    /// 【为什么要能跳过镜子】
    ///   反射路径的两个端点都落在镜面上：
    ///     入射段从镜面往回打 → 起点在板子里面（板子有厚度）
    ///     反射段从目标点打回镜面 → 终点也在板子里面
    ///   不跳过它，这两次遮挡检查都会撞到镜子自己，
    ///   表现就是"光在画面上折过去了，判定却说入射段被挡"。
    /// </summary>
    RaycastHit2D CastRay(Vector2 from, Vector2 dir, float dist, MirrorSurface skipMirror = null)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(from, dir, dist, blockerLayer);

        RaycastHit2D best = default(RaycastHit2D);
        float bestD = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null) continue;
            if (IsIgnored(hits[i].collider)) continue;

            if (skipMirror != null)
            {
                MirrorSurface ms = hits[i].collider.GetComponentInParent<MirrorSurface>();
                if (ms == skipMirror) continue;
            }

            if (hits[i].distance < bestD)
            {
                bestD = hits[i].distance;
                best = hits[i];
            }
        }

        return best;
    }

    /// <summary>从灯沿某个角度打一条射线，返回能走多远（跳过忽略的碰撞体）</summary>
    float CastAt(float ang)
    {
        Vector2 origin = transform.position;
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

        // 用 RaycastAll 才能跳过"忽略列表"：单条 Raycast 只会返回最近那个，
        // 而最近那个正好是镜面自己。
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, radius, blockerLayer);

        float best = radius;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null) continue;
            if (IsIgnored(hits[i].collider)) continue;

            if (hits[i].distance < best) best = hits[i].distance;
        }

        return Mathf.Max(0.02f, best - 0.01f);
    }

    /// <summary>
    /// 二分细分。
    ///
    /// 【为什么必须做这一步】
    ///   两条相邻射线，一条撞在平台上（长 0.5），一条擦过平台边缘打到底下地板（长 4）。
    ///   直接连成一个三角形的话，这个三角形会从短的那条一直铺到长的那条，
    ///   把本该全黑的一块"墙角阴影"涂亮 —— 也就是光越过墙角一小块。
    ///   细分会不断往这两条之间插射线，bisect 到最后就落在墙角上，
    ///   那段多出来的亮边被压到只有几个像素宽。
    /// </summary>
    void Refine(List<float> angs, List<float> lens, float a0, float d0, float a1, float d1, int depth)
    {
        if (depth >= refineDepth) return;
        if (angs.Count > 4000) return;
        if (Mathf.Abs(d1 - d0) < refineThreshold) return;

        float am = (a0 + a1) * 0.5f;
        float dm = CastAt(am);

        Refine(angs, lens, a0, d0, am, dm, depth + 1);
        angs.Add(am);
        lens.Add(dm);
        Refine(angs, lens, am, dm, a1, d1, depth + 1);
    }

    Color Tint(float alphaScale)
    {
        return new Color(lightColor.r, lightColor.g, lightColor.b,
                         Mathf.Clamp01(fanAlpha * alphaScale));
    }

    // ------------------------------------------------------------
    // 灯泡本体的那团光（很小，不负责照亮地面）
    // ------------------------------------------------------------
    void BuildLampGlow()
    {
        GameObject go = new GameObject("LampGlow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        lampGlow = go.AddComponent<SpriteRenderer>();
        lampGlow.sprite = MakeCircleSprite(48);
        lampGlow.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0.55f);
        lampGlow.sortingOrder = sortingOrder + 1;      // 压在光扇上一点
        lampGlow.transform.localScale = Vector3.one * 1.1f;

        // 灯泡本体不吃光照：否则环境光 0.06 的时候，灯泡自己会是暗的
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            Material m = new Material(sh);
            m.name = "LampGlowUnlit";
            m.hideFlags = HideFlags.HideAndDontSave;
            lampGlow.sharedMaterial = m;
        }
    }

    Sprite MakeCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.HideAndDontSave;

        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        tex.Apply();

        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    void Start()
    {
        Refresh();
    }

    void LateUpdate()
    {
        if (lampGlow != null) lampGlow.enabled = isOn;

        // ---- 灯动了就重算光扇 ----
        // 这是"踩吊灯让它掉下来"那类玩法的前提：
        // 灯的位置一变，照亮范围就变了，光扇必须跟着变。
        // 静止的灯永远不会进这个分支，所以没有额外开销。
        if (isOn && meshRenderer != null)
        {
            // 旋转每帧都钉死，避免随吊灯摆动
            meshRenderer.transform.rotation = Quaternion.identity;

            Vector2 p = transform.position;
            float rot = transform.eulerAngles.z;

            bool moved = (p - lastFanPos).sqrMagnitude > 0.0009f ||
                         Mathf.Abs(Mathf.DeltaAngle(rot, lastFanRot)) > 0.5f;

            // 定时重算：光扇是"算一次"的。任何挡住光的物体变了
            // （挪走、关掉、销毁、开关一盏灯），没动的那盏灯的光扇就会过期 ——
            // 于是"看着是影子，走进去却不变成影子"。
            // 靠别的东西主动通知不可靠（永远会漏一种情况），所以定期重算。
            bool timed = false;

            if (autoRefreshInterval > 0.001f)
            {
                refreshTimer -= Time.deltaTime;
                if (refreshTimer <= 0f)
                {
                    refreshTimer = autoRefreshInterval;
                    timed = true;
                }
            }

            // 一帧最多重算一次，别让"移动"和"定时"重复算两遍
            if (moved || timed || autoRefreshInterval <= 0.001f)
            {
                Refresh();
            }
        }

        if (fanMaterial == null) return;

        // 闪烁只改整体亮度，不重算网格
        float s = 1f;
        if (flicker)
        {
            s = 1f + Mathf.Sin(Time.time * flickerSpeed) * flickerAmount
                   + Mathf.Sin(Time.time * flickerSpeed * 2.3f) * flickerAmount * 0.5f;
        }

        fanMaterial.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(s)));
    }

    // ------------------------------------------------------------
    // 判定
    // ------------------------------------------------------------
    /// <summary>
    /// 诊断：这个点为什么没被照亮。
    /// 把判定流程里每一步的关键数字写成人话，给调试面板用。
    //
    // 【为什么要做这个】
    //   "光能看到却不显形"这种问题，可能是 7 个条件里任意一个不满足，
    //   而每一个都只表现成"false"，没有任何信息。
    //   与其反复猜，不如让判定自己把过程打印出来。
    /// </summary>
    public string ExplainPoint(Vector2 point)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("灯「").Append(name).Append("」 isOn=").Append(isOn);
        sb.Append("  mode=").Append(lightMode);
        sb.Append("  Radius=").Append(radius.ToString("0.0"));
        sb.Append("  lightUpPlayer=").Append(lightUpPlayer);
        sb.Append('\n');

        if (!lightUpPlayer)
            sb.Append("  ⚠ 这盏灯【不参与形态判定】（lightUpPlayer 关着）" +
                      " —— 它照亮了也不会把你变成实体\n");

        if (!isOn) { sb.Append("  → 灯没开\n"); return sb.ToString(); }

        if (lightMode != LightMode.Directional)
        {
            sb.Append("  → 不是方向光，镜子对它无效（只对 Directional 生效）\n");
            return sb.ToString();
        }

        Vector2 origin = transform.position;
        float rad = coneRotation * Mathf.Deg2Rad;
        Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 perp = new Vector2(-axis.y, axis.x);
        Vector2 rel = point - origin;

        float t = Vector2.Dot(rel, axis);
        float u = Vector2.Dot(rel, perp);

        sb.Append("  点 ").Append(point.ToString("0.00"));
        sb.Append("  t=").Append(t.ToString("0.00"));
        sb.Append("  u=").Append(u.ToString("0.00"));
        sb.Append("  半宽=").Append((beamWidth * 0.5f).ToString("0.00")).Append('\n');

        bool inStrip = (t >= -edgeTolerance && t <= radius + edgeTolerance) &&
                       (Mathf.Abs(u) <= beamWidth * 0.5f + edgeTolerance);

        if (inStrip)
        {
            if (t < 0.001f) { sb.Append("  → 在光柱内，直射命中\n"); return sb.ToString(); }

            RaycastHit2D h = CastRay(point, -axis, t);
            if (h.collider == null) { sb.Append("  → 在光柱内，直射命中\n"); return sb.ToString(); }

            sb.Append("  直射被挡：").Append(h.collider.gameObject.name).Append('\n');
        }
        else
        {
            sb.Append("  不在光柱内，转去算反射\n");
        }

        // ---- 反射 ----
        sb.Append("  镜面数量 = ").Append(MirrorSurface.All.Count).Append('\n');

        for (int i = 0; i < MirrorSurface.All.Count; i++)
        {
            MirrorSurface m = MirrorSurface.All[i];
            if (m == null) continue;

            Vector2 mp = m.transform.position;
            Vector2 n = m.Normal;

            sb.Append("  [").Append(m.name).Append("] mp=").Append(mp.ToString("0.00"))
              .Append(" n=").Append(n.ToString("0.00")).Append('\n');

            float nd = Vector2.Dot(n, axis);
            sb.Append("     nd=").Append(nd.ToString("0.000"));
            if (Mathf.Abs(nd) < 0.05f) { sb.Append(" → 光平行于镜面，跳过\n"); continue; }

            float sd = Vector2.Dot(point - mp, n);
            Vector2 mirrored = point - 2f * sd * n;

            float tB = -Vector2.Dot(mirrored - mp, n) / nd;
            sb.Append("  sd=").Append(sd.ToString("0.00"));
            sb.Append("  tB=").Append(tB.ToString("0.00")).Append('\n');

            Vector2 B = mirrored + axis * tB;
            bool onMirror = m.ContainsPoint(B);
            sb.Append("\n     B=").Append(B.ToString("0.00")).Append("  在镜面上=").Append(onMirror).Append('\n');

            if (!onMirror) continue;

            float inLen = Vector2.Dot(B - origin, axis);
            float refLen = Vector2.Distance(B, point);
            sb.Append("     入射段=").Append(inLen.ToString("0.00"))
              .Append("  反射段=").Append(refLen.ToString("0.00"))
              .Append("  合计=").Append((inLen + refLen).ToString("0.00"))
              .Append(" / Radius=").Append(radius.ToString("0.00")).Append('\n');

            if (inLen < 0.02f) { sb.Append("     → 入射段太短\n"); continue; }
            if (inLen + refLen > radius) { sb.Append("     → 总长超过 Radius\n"); continue; }

            RaycastHit2D h1 = CastRay(B - axis * 0.03f, -axis, inLen - 0.03f, m);
            if (h1.collider != null)
            {
                sb.Append("     → 入射段被挡：").Append(h1.collider.gameObject.name).Append('\n');
                continue;
            }

            Vector2 pb = point - B;
            float pl = pb.magnitude;
            if (pl > 0.04f)
            {
                RaycastHit2D h2 = CastRay(point, -pb / pl, pl - 0.03f, m);
                if (h2.collider != null)
                {
                    sb.Append("     → 反射段被挡：").Append(h2.collider.gameObject.name).Append('\n');
                    continue;
                }
            }

            sb.Append("     → 反射命中 ✓\n");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------
    // 网格命中测试（只用于方向光）
    // ------------------------------------------------------------
    List<Vector3> judgeVerts = new List<Vector3>();
    List<int> judgeTris = new List<int>();
    List<int> judgeTriPath = new List<int>();     // 每个三角形属于第几段光路（0=直射，1=第一次反射…）
    readonly bool[] pathSeen = new bool[16];

    /// <summary>
    /// 这个点被【几条独立光路】照到。
    ///
    /// 【为什么需要"条数"而不是"有没有"】
    ///   强光敏开关要的是"两条光同时到"。
    ///   如果按灯来数，"同一盏灯折了两次打到同一点"就永远只能算一条 ——
    ///   而那恰恰是最有意思的一种谜题（让光绕一圈回来）。
    ///   所以按【光路】数：直射算 1 条，每折一次算新的一条。
    ///
    ///   网格里每个四边形的段号就是它的光路号，
    ///   所以"这个点落在几段不同的光里" = "被几条光路照到"。
    /// </summary>
    public int CountLightPaths(Vector2 worldPoint)
    {
        if (!isOn) return 0;

        if (lightMode != LightMode.Directional)
            return Illuminates(worldPoint) ? 1 : 0;

        if (judgeTris.Count < 3) return 0;

        Vector2 p = worldPoint - (Vector2)transform.position;

        for (int i = 0; i < pathSeen.Length; i++) pathSeen[i] = false;

        int paths = 0;

        for (int t = 0; t + 2 < judgeTris.Count; t += 3)
        {
            Vector3 a = judgeVerts[judgeTris[t]];
            Vector3 b = judgeVerts[judgeTris[t + 1]];
            Vector3 c = judgeVerts[judgeTris[t + 2]];

            if (!PointInTriangle(p, a, b, c)) continue;

            int path = (t / 3 < judgeTriPath.Count) ? judgeTriPath[t / 3] : 0;
            if (path < 0 || path >= pathSeen.Length) path = 0;

            if (pathSeen[path]) continue;

            pathSeen[path] = true;
            paths++;
        }

        return paths;
    }

    /// <summary>
    /// 这个点在不在【画出来的那片光】里。
    ///
    /// 顶点是以灯为原点的局部坐标（网格挂在灯上、旋转锁成 identity），
    /// 所以世界点减掉灯的位置就能用。
    /// </summary>
    bool MeshContains(Vector2 worldPoint)
    {
        Vector2 p = worldPoint - (Vector2)transform.position;

        for (int t = 0; t + 2 < judgeTris.Count; t += 3)
        {
            Vector3 a = judgeVerts[judgeTris[t]];
            Vector3 b = judgeVerts[judgeTris[t + 1]];
            Vector3 c = judgeVerts[judgeTris[t + 2]];

            if (PointInTriangle(p, a, b, c)) return true;
        }

        return false;
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // 用三个叉积的符号判断（同一侧 = 在三角形内）
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);

        bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
        bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);

        return !(hasNeg && hasPos);
    }

    static float Cross(Vector2 u, Vector2 v)
    {
        return u.x * v.y - u.y * v.x;
    }

    /// <summary>这个点被这盏灯照亮了吗（考虑遮挡）</summary>
    public bool Illuminates(Vector2 point)
    {
        if (!isOn) return false;

        Vector2 origin = transform.position;

        // ---- 方向光 ----
        //
        // 【判定直接用"画出来的那个网格"】
        //
        //   以前是"把点投影到光束轴上 + 回打射线"的解析判定。
        //   加了反射之后，解析判定要处理：折线、镜面范围、反弹次数、
        //   射程预算…… 每一条都可能和网格算得不一样，
        //   于是反复出现"画面上有光、人不变实体"或者"黑暗里变实体"。
        //
        //   现在干脆用网格本身做命中测试：
        //   网格是怎么画出来的，判定就是怎么算的 ——
        //   两者【不可能】不一致，因为它们是同一份数据。
        //
        //   代价：网格每 autoRefreshInterval（默认 0.05 秒）才重建一次，
        //   所以判定最多滞后两三帧。对"站在光里变实体"这种事察觉不到。
        if (lightMode == LightMode.Directional)
        {
            if (judgeTris != null && judgeTris.Count >= 3)
                return MeshContains(point);

            // 还没建过网格（第一帧）：退回解析判定
            return IsLitByBeam(origin, point);
        }

        Vector2 delta = point - origin;
        float dist = delta.magnitude;

        if (dist > radius + edgeTolerance) return false;
        if (dist < 0.001f) return true;

        // 扇形光：不在张角内的点根本没被照到，连射线都不用打
        if (!InCone(delta)) return false;

        // 视线检测：中间被墙挡住 = 没被照亮
        RaycastHit2D hit = Physics2D.Raycast(origin, delta.normalized, dist, blockerLayer);
        return hit.collider == null;
    }

    /// <summary>方向光的光束判定：先试直射，再试反射</summary>
    bool IsLitByBeam(Vector2 origin, Vector2 point)
    {
        float rad = coneRotation * Mathf.Deg2Rad;
        Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 perp = new Vector2(-axis.y, axis.x);

        Vector2 rel = point - origin;

        // 沿光束方向的距离（0 = 灯的位置，radius = 光束末端）
        float t = Vector2.Dot(rel, axis);

        // 垂直于光束的偏移（超出半宽就在光束外面）
        float u = Vector2.Dot(rel, perp);

        bool inStrip = (t >= -edgeTolerance && t <= radius + edgeTolerance) &&
                       (Mathf.Abs(u) <= beamWidth * 0.5f + edgeTolerance);

        // ---- 1. 直射 ----
        //
        // 【这里以前是致命的顺序错误】
        //   原代码在上面两行就 `return false` 了：
        //     if (t < -tol || t > radius + tol) return false;
        //     if (|u| > halfWidth + tol)       return false;
        //   可反射光【本来就不在原来的光柱里】—— 那正是"被折出去"的意思。
        //   于是所有反射段里的点都在这里被否掉，根本走不到下面的反射判定。
        //   表现就是：画面里光明明折过去了，站在里面却不变实体。
        //
        //   现在：不在光柱里不再直接 return false，而是继续去试反射。
        if (inStrip)
        {
            if (t < 0.001f) return true;

            // 【关键】从目标点沿【固定方向】回打，而不是朝灯心打。
            //   朝灯心打 = 阴影往灯心收；沿固定方向打 = 阴影平行。
            if (CastRay(point, -axis, t).collider == null) return true;
        }

        // ---- 2. 反射 ----
        return IsLitByBounce(point, axis);
    }

    /// <summary>
    /// 反射光的判定：把目标点沿镜面【翻折】到镜子另一侧，
    /// 再看那个翻折点是否在直射光柱里。
    ///
    /// 【为什么这样等价】
    ///   平面镜的反射路径长度 = 从翻折点到光源的直线长度
    ///   （经典的光学作图：镜像法）。
    ///   而方向光的入射段是平行光，所以只要把点翻过去、
    ///   它落在原来的光柱（含入射方向）里，就说明它确实被这面镜子照到了。
    ///
    ///   这样判定和网格用的是同一套几何 —— 还是那条不变量：
    ///   看到的边界 = 判定的边界。
    /// </summary>
    bool IsLitByBounce(Vector2 point, Vector2 axis)
    {
        if (maxBounces <= 0) return false;

        Vector2 origin = transform.position;

        for (int i = 0; i < MirrorSurface.All.Count; i++)
        {
            MirrorSurface m = MirrorSurface.All[i];
            if (m == null) continue;

            Vector2 mp = m.transform.position;
            Vector2 n = m.Normal;

            // 光几乎平行于镜面 → 不算反射（会算出天大的距离）
            float nd = Vector2.Dot(n, axis);
            if (Mathf.Abs(nd) < 0.05f) continue;

            // 1. 把目标点翻折到镜面另一侧（镜像法）
            float sd = Vector2.Dot(point - mp, n);
            Vector2 mirrored = point - 2f * sd * n;

            // 2. 沿【入射方向】把翻折点推到镜面上，得到真正的反射点 B
            //
            // 【tB 是负数才是正常的】
            //   翻折点是"镜面另一侧"的点，而入射方向是朝着镜面去的。
            //   从翻折点出发沿入射方向走，要走【反方向】才能回到镜面 ——
            //   所以 tB < 0 是教科书里的正常结果。
            //
            //   以前这里写了 `if (tB <= 0f) continue;`，
            //   等于把所有正常的反射都否掉了：判定永远走不到下面那几行。
            //   真正该检查的是"入射段长度为正"，那是下一行的 inLen。
            float tB = -Vector2.Dot(mirrored - mp, n) / nd;

            Vector2 B = mirrored + axis * tB;

            // 3. 反射点必须真的落在镜面上
            if (!m.ContainsPoint(B)) continue;

            // 4. 入射段长度 + 反射段长度，不能超过射程（和网格用的是同一个 budget）
            float inLen = Vector2.Dot(B - origin, axis);
            if (inLen < 0.02f) continue;

            float refLen = Vector2.Distance(B, point);
            if (inLen + refLen > radius) continue;

            // 5. 遮挡检查：入射段（从反射点往上游回打）
            if (CastRay(B - axis * 0.03f, -axis, inLen - 0.03f).collider != null) continue;

            // 6. 遮挡检查：反射段（从目标点往回打，终点在镜面前一点）
            Vector2 pb = point - B;
            float pl = pb.magnitude;

            if (pl > 0.04f)
            {
                if (CastRay(point, -pb / pl, pl - 0.03f).collider != null) continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>这个方向在扇形张角内吗（整圈灯永远返回 true）</summary>
    bool InCone(Vector2 delta)
    {
        if (coneAngle >= 359.5f) return true;
        if (delta.sqrMagnitude < 0.000001f) return true;

        float ang = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(ang, coneRotation)) <= coneAngle * 0.5f;
    }

    public void SetEnabled(bool on)
    {
        isOn = on;

        if (lampGlow != null) lampGlow.enabled = on;

        // 【这一行以前是 visualLight.SetActive(on)，那是致命 bug】
        //   visualLight 指向的就是【灯自己那个 GameObject】——
        //   SetActive(false) 等于把 LightZone、以及挂在同一个物体上的
        //   TimerLight / LightSwitch 一起禁用掉。
        //   TimerLight 的 Update 停了，就再也没人去把它点亮 ——
        //   于是"定时灯灭了之后永远不亮"。
        //
        //   正确做法：只关 Light2D 这一个组件，灯物体本身保持启用。
        if (visualLight2D == null && visualLight != null)
            visualLight2D = visualLight.GetComponent<UnityEngine.Rendering.Universal.Light2D>();
        if (visualLight2D != null) visualLight2D.enabled = on;

        if (on) Refresh();
        else if (meshRenderer != null) meshRenderer.enabled = false;
    }

    public void Toggle() { SetEnabled(!isOn); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
