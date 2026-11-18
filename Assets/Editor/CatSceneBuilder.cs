// ============================================================
// CatSceneBuilder.cs —— 一键生成 CatMare 测试场景
//
// 菜单：Tools → CatMare → 1. 生成基础场景
//                       2. 生成三房间测试关卡
//
// 它会自动完成：
//   1. 创建 Ground / Light 两个 Layer（缺了就补）
//   2. 新建场景
//   3. 相机：正交 Size 5 + CameraFollow
//   4. Global Light 2D（环境光，很暗，定基调）
//   5. 地面（+ 三房间时：隔墙、窄缝、陷阱、光源、开关）
//   6. 猫：Rigidbody2D + 两个碰撞体 + 六个脚本 + GroundCheck
//   7. 全部引用连好，保存成 Assets/Scenes/CatTest.unity
//
// 【为什么用脚本建场景，而不是手把手点】
//   场景有 20 多个引用要连。手点一遍要 20 分钟，漏一个就报空引用。
//   脚本建一次 3 秒，改了参数可以重跑。
// ============================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class CatSceneBuilder
{
    const string ScenePath = "Assets/Scenes/CatTest.unity";
    const string TemplatePath = "Assets/Scenes/RoomTemplate.unity";
    const string MaterialPath = "Assets/NoFriction.physicsMaterial2D";

    [MenuItem("Tools/CatMare/12. 生成空白房间模板（全黑，地形自己摆）", false, 120)]
    public static void BuildRoomTemplate()
    {
        BuildScene(false, true);
    }

    [MenuItem("Tools/CatMare/13. 在当前视野中心放一盏台灯（点光）", false, 130)]
    public static void PlacePlainLamp()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        LightZone zone = MakeLamp(UniqueName("Lamp"), p, 3.0f,
                                  groundLayer > 0 ? (1 << groundLayer) : 0,
                                  GetSquareSprite());

        FinishPlacement(zone.gameObject);
        Debug.Log("[CatSceneBuilder] 台灯已放好。调这三个：radius 照亮半径 / coneAngle 360=圆 / fanAlpha 亮度");
    }

    [MenuItem("Tools/CatMare/14. 在当前视野中心放一个关灯开关（先选中灯可自动连上）", false, 140)]
    public static void PlaceLightSwitch()
    {
        Vector3 p = SceneViewCenter();

        // 先记下当前选中的灯 —— 如果选的是灯，生成的开关自动连上它
        LightZone selectedLamp = null;
        if (Selection.activeGameObject != null)
            selectedLamp = Selection.activeGameObject.GetComponent<LightZone>();

        GameObject go = new GameObject(UniqueName("LightSwitch"));
        go.transform.position = p;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.8f, 0.8f);
        col.isTrigger = true;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = new Color(0.9f, 0.85f, 0.55f);
        sr.sortingOrder = 2;

        // 交互物不吃光照，否则暗处看不见
        Material um = GetUnlitSpriteMaterial();
        if (um != null) sr.sharedMaterial = um;

        LightSwitch sw = go.AddComponent<LightSwitch>();
        sw.switchVisual = sr;

        if (selectedLamp != null)
        {
            sw.lights = new LightZone[] { selectedLamp };
            Debug.Log("[CatSceneBuilder] 开关已自动连上 " + selectedLamp.name);
        }
        else
        {
            Debug.Log("[CatSceneBuilder] 开关没连灯。把灯拖到 Light Switch 的 lights 数组里");
        }

        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/15. 在当前视野中心放一个陷阱（碰到就死）", false, 150)]
    public static void PlaceTrap()
    {
        Vector3 p = SceneViewCenter();

        // MakeBox 的 isTrigger = true 会一次性配好三件事：
        //   ① 碰撞体是 Trigger（不挡路，走进去才死）
        //   ② 排序 -2（在地形和光扇之上，看得见）
        //   ③ 不受光照的材质（暗处不会变成一块看不见的黑）
        // 层用 Default：陷阱不能放 Ground，否则它会被当成挡光物，
        // 而且光扇会在它身上切开一道阴影。
        GameObject go = MakeBox(UniqueName("Trap"), GetSquareSprite(), p,
                                new Vector2(1.0f, 0.6f), 0,
                                new Color(0.85f, 0.22f, 0.22f), null, true);

        go.AddComponent<Trap>();

        Debug.Log("[CatSceneBuilder] 陷阱放好了。红方块 = 碰到就死；" +
                  "影形态免疫（Trap.cs 里那块逻辑）。");

        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/16. 放一盏可以拿起放下的灯（提灯）", false, 160)]
    public static void PlaceCarryLamp()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        LightZone zone = MakeLamp(UniqueName("CarryLamp"), p, 3.0f,
                                  groundLayer > 0 ? (1 << groundLayer) : 0, GetSquareSprite());

        // 互动物需要一个能被扫描到的碰撞体（PlayerInteract 用 OverlapCircleAll 找）
        BoxCollider2D col = zone.gameObject.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.9f, 0.9f);
        col.isTrigger = true;

        CarryLamp carry = zone.gameObject.AddComponent<CarryLamp>();
        carry.lamp = zone;

        Debug.Log("[CatSceneBuilder] 提灯放好了。靠近按 E 拿起，再按 E 放下。" +
                  "拿着灯 = 永远是实体（能按开关，但钻不过缝）。");

        FinishPlacement(zone.gameObject);
    }

    [MenuItem("Tools/CatMare/17. 放一个光敏开关（被照亮时才触发）", false, 170)]
    public static void PlaceLightSwitchByLight()
    {
        Vector3 p = SceneViewCenter();

        GameObject go = MakeBox(UniqueName("LightSensorSwitch"), GetSquareSprite(), p,
                                new Vector2(0.6f, 0.6f), 0,
                                new Color(0.35f, 0.33f, 0.28f), null, true);

        LightSwitchByLight sw = go.AddComponent<LightSwitchByLight>();
        sw.switchVisual = go.GetComponent<SpriteRenderer>();

        LightZone[] all = Object.FindObjectsOfType<LightZone>();
        if (all != null && all.Length > 0) sw.sensors = all;

        Debug.Log("[CatSceneBuilder] 光敏开关放好了。被光照到时，它会把 toggleLights 打开、" +
                  "把 toggleObjects 激活。sensors 留空 = 场景里所有的灯。");

        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/18. 放一束直线光（阴影是平行的）", false, 180)]
    public static void PlaceDirectionalBeam()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        LightZone zone = MakeLamp(UniqueName("BeamLight"), p, 6.0f,
                                  groundLayer > 0 ? (1 << groundLayer) : 0, GetSquareSprite());

        zone.lightMode = LightZone.LightMode.Directional;
        zone.beamWidth = 1.5f;
        zone.coneRotation = -90f;      // 朝下
        zone.radius = 6f;              // 方向光里 radius = 光束长度
        zone.rayCount = 96;

        Debug.Log("[CatSceneBuilder] 直线光放好了。coneRotation 控方向（-90 朝下，0 朝右），" +
                  "beamWidth 控宽度，radius 控长度。阴影是平行的，不会往灯心收。");

        FinishPlacement(zone.gameObject);
    }

    [MenuItem("Tools/CatMare/19. 放一个移动的陷阱（左右巡逻）", false, 190)]
    public static void PlaceMovingTrap()
    {
        Vector3 p = SceneViewCenter();

        GameObject go = MakeBox(UniqueName("MovingTrap"), GetSquareSprite(), p,
                                new Vector2(1.0f, 0.6f), 0,
                                new Color(0.85f, 0.22f, 0.22f), null, true);

        Trap trap = go.AddComponent<Trap>();

        // 【必须开】移动的触发器靠 OnTriggerEnter2D 会漏判
        trap.detectByOverlap = true;

        Mover mover = go.AddComponent<Mover>();
        mover.pointA = new Vector2(-2.5f, 0f);
        mover.pointB = new Vector2(2.5f, 0f);
        mover.speed = 2f;

        Debug.Log("[CatSceneBuilder] 移动陷阱放好了。路线是相对初始位置的两端偏移，" +
                  "在 Mover 里改 pointA / pointB。影形态依然免疫。");

        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/23. 放一束致命红光（影子碰到就死）", false, 230)]
    public static void PlaceDeadlyBeam()
    {
        Vector3 p = SceneViewCenter();

        // 用现成的灯来做：光扇的遮挡（墙挡光、墙角细分）全都能免费复用
        //
        // 【blockerLayer 必须给 Ground，不能给 0】
        //   0 = Nothing = 没有任何东西能挡住这束光 →
        //   光扇会直接穿墙画过去，判定也会穿墙杀人。
        //   这束光的"看到的形状"和"判定的形状"是同一次射线算出来的，
        //   所以遮挡层给对了，两边就一起对。
        int groundLayer = EnsureLayer("Ground");
        LightZone zone = MakeLamp(UniqueName("DeadlyBeam"), p, 7.0f,
                                  groundLayer > 0 ? (1 << groundLayer) : ~0, GetSquareSprite());

        // ---- 关键：它【不参与玩家的形态判定】----
        //   否则影子一进去就被照成实体，永远死不了。
        //   它照样发光、照样被墙挡住，只是不改形态。
        zone.lightUpPlayer = false;

        // ---- 形状：一束方向光（平行光），红色 ----
        zone.lightMode = LightZone.LightMode.Directional;
        zone.beamWidth = 2.2f;
        zone.coneRotation = -90f;            // 朝下
        zone.radius = 7f;                    // 方向光里 radius = 光束长度
        zone.rayCount = 96;
        zone.fanAlpha = 0.55f;
        zone.edgeFalloff = 0.55f;
        zone.flicker = true;
        zone.flickerAmount = 0.10f;
        zone.flickerSpeed = 7f;

        zone.lightColor = new Color(1f, 0.15f, 0.15f, 1f);

        // 灯泡光晕也改成红色，免得一盏红灯配一圈暖黄光
        var l2d = zone.GetComponent<UnityEngine.Rendering.Universal.Light2D>();
        if (l2d != null)
        {
            l2d.color = new Color(1f, 0.20f, 0.18f);
            l2d.intensity = 1.1f;
        }

        // 杀手组件
        DeadlyBeam killer = zone.gameObject.AddComponent<DeadlyBeam>();
        killer.zone = zone;

        Debug.Log("[CatSceneBuilder] 致命红光放好了。\n" +
                  "  · 视觉是一束红色的方向光，会被墙挡住、也会被箱子挡住\n" +
                  "  · 但它【不参与形态判定】（lightUpPlayer 已关）：影子在里面仍是影子\n" +
                  "  · 影子被照到就死；实体可以随便穿过（killsCat 默认关）\n" +
                  "  调形状：Light Zone 的 Beam Width / Radius / Cone Rotation（-90 朝下）");

        FinishPlacement(zone.gameObject);
    }

    [MenuItem("Tools/CatMare/24. 放一颗星星（拿到就加分）", false, 240)]
    public static void PlaceStar()
    {
        Vector3 p = SceneViewCenter();

        GameObject go = new GameObject(UniqueName("Star"));
        go.transform.position = p;
        go.transform.rotation = Quaternion.Euler(0f, 0f, 45f);   // 菱形比方块更像"可拾取物"

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetPlainSprite();
        sr.color = new Color(1f, 0.88f, 0.35f, 1f);              // 金色
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 1;

        // 收藏品必须暗处也看得见，否则玩家不知道那里有东西
        Material um = GetUnlitSpriteMaterial();
        if (um != null) sr.sharedMaterial = um;

        // 世界尺寸固定成 0.3 × 0.3（别用 localScale 手填，见 ScaleSprite 的说明）
        ScaleSpriteToWorld(sr, 0.3f, 0.3f);

        // 触发器：碰到就吃，不挡路
        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(0.6f, 0.6f);   // 判定比外观大一点，好拾取

        Star star = go.AddComponent<Star>();
        star.visual = sr;

        FinishPlacement(go);
    }

    /// <summary>
    /// 把一张精灵缩放到指定的世界尺寸（宽 × 高）。
    ///
    /// 【为什么不能直接 localScale = (0.3, 0.3)】
    ///   localScale 是倍率。一张 8 像素的图在 PPU=100 时世界尺寸只有 0.08 格，
    ///   乘 0.3 之后是 0.024 格 —— 比像素还小，等于看不见。
    ///   必须先用 sprite.bounds.size 换算出"这张图本来多大"。
    /// </summary>
    static void ScaleSpriteToWorld(SpriteRenderer sr, float worldW, float worldH)
    {
        if (sr == null) return;

        Vector2 s = (sr.sprite != null) ? (Vector2)sr.sprite.bounds.size : Vector2.one;
        float sx = Mathf.Max(0.0001f, s.x);
        float sy = Mathf.Max(0.0001f, s.y);

        sr.transform.localScale = new Vector3(worldW / sx, worldH / sy, 1f);
    }

    // ------------------------------------------------------------
    // 26. 会升降的玻璃平台（站上去能跳）
    //
    // 【为什么"有时候能跳有时候不能跳"】
    //   跳跃判定只查 Ground + Glass 两层（Cat.prefab 里 Ground Layer = 1280）。
    //   升降台的【机身】在 Default 层 → 站上去不算站在地上 → 跳不了；
    //   而它上面又叠了一块 Glass 层的玻璃 → 踩到玻璃时就算站在地上。
    //   于是结果取决于"脚那一刻踩在机身上还是玻璃上" —— 时好时坏。
    //
    // 【这个菜单一次把五件事配好】
    //   ① 父物体：Kinematic 刚体 + Mover（竖着走）→ 真的会带着载物移动，不是瞬移
    //   ② 平台面：Glass 层（在跳跃判定里 ✓ 同时光能穿过 ✓）
    //   ③ 玻璃板：passableInShadow = 关（影子也能站）
    //   ④ 不挂 Trap（挂上去就会一站就死）
    //   ⑤ Mover 的 A/B 是【相对偏移】，默认从当前位置往上升 6 格
    // ------------------------------------------------------------
    [MenuItem("Tools/CatMare/26. 放一部会升降的玻璃平台（站上去能跳）", false, 260)]
    public static void PlaceElevator()
    {
        Vector3 p = SceneViewCenter();

        int glassLayer = EnsureLayer("Glass");

        float w = 3f;        // 平台宽（世界格）
        float h = 0.25f;     // 平台厚
        float rise = 6f;     // 上升高度

        // ---- 父物体：只负责"移动"----
        GameObject root = new GameObject(UniqueName("Elevator"));
        root.transform.position = p;

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;   // 关键：Kinematic 才会带着载物走
        rb.gravityScale = 0f;
        rb.simulated = true;

        Mover mover = root.AddComponent<Mover>();
        mover.pointA = new Vector2(0f, 0f);        // 相对起始位置 —— 这里是井底
        mover.pointB = new Vector2(0f, rise);      // 升 rise 格
        mover.speed = 2f;
        mover.startDelay = 1.5f;
        mover.pause = 0.6f;                        // 到顶/到底各停 0.6 秒，方便上下

        // ---- 平台面：玻璃 ----
        GameObject plate = new GameObject("GlassPlatform");
        plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = Vector3.zero;

        if (glassLayer >= 0) plate.layer = glassLayer;   // ← 跳跃判定认这一层

        SpriteRenderer sr = plate.AddComponent<SpriteRenderer>();
        sr.sprite = GetPlainSprite();
        sr.drawMode = SpriteDrawMode.Simple;
        sr.color = new Color(0.55f, 0.78f, 1.00f, 0.45f);
        sr.sortingLayerName = "Default";
        sr.sortingOrder = -9;                            // 地形(-10)之上、光扇(-5)之下

        Material um = GetUnlitSpriteMaterial();
        if (um != null) sr.sharedMaterial = um;

        // 把精灵缩放到 3 × 0.25 世界格
        ScaleSpriteToWorld(sr, w, h);

        // 碰撞体尺寸是【本地】的，要除以缩放，世界尺寸才是 3 × 0.25
        Vector3 sc = plate.transform.localScale;
        BoxCollider2D col = plate.AddComponent<BoxCollider2D>();
        col.size = new Vector2(w / Mathf.Max(0.0001f, sc.x), h / Mathf.Max(0.0001f, sc.y));
        col.isTrigger = false;                           // 实心，站得上去

        GlassPane pane = plate.AddComponent<GlassPane>();
        pane.passableInShadow = false;                   // 影子也能站，否则黑暗里会掉下去

        Undo.RegisterCreatedObjectUndo(root, "创建升降平台");

        Debug.Log("[CatSceneBuilder] 升降平台放好了：" + root.name + "\n" +
                  "  · 父物体：Kinematic 刚体 + Mover → 会带着你上下（不是瞬移）\n" +
                  "  · 平台面在 Glass 层 → 站上去算站在地上（能跳），而且光能穿过\n" +
                  "  · 玻璃板 passableInShadow = 关 → 影子也能站\n" +
                  "  · 没有 Trap → 不会一站就死\n" +
                  "  上升高度 " + rise + " 格（Mover 的 pointB 是相对偏移，不是绝对坐标）");

        FinishPlacement(root);
    }

    // ------------------------------------------------------------
    // 27. 放一面可推的反射镜（真反射，不需要第二盏灯）
    //
    //   原理：LightZone 的方向光会认 MirrorSurface，
    //   射到镜面就【真的拐弯】，网格和判定走同一套折线几何。
    //   所以这里只需要：一个箱子 + 一块带 MirrorSurface 的斜板。
    //
    //   推箱子 → 板子跟着动 → 光柱跟着拐弯。没有第二盏灯，没有重叠。
    // ------------------------------------------------------------
    [MenuItem("Tools/CatMare/27. 放一面可推的反射镜（真反射）", false, 270)]
    public static void PlaceMirror()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        // ---- 箱子：Ground 层（它会挡光，也会挡住猫）----
        GameObject box = MakeBox(UniqueName("MirrorBox"), GetSquareSprite(), p,
                                 new Vector2(1.0f, 1.0f), groundLayer > 0 ? groundLayer : 0,
                                 new Color(0.30f, 0.32f, 0.38f), null, false);

        Rigidbody2D rb = box.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
        rb.freezeRotation = true;
        rb.mass = 4f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        box.AddComponent<PushBox>();

        // ---- 镜面：45° 的板，撞上去光会拐 90° ----
        GameObject plate = new GameObject("MirrorPlate");
        plate.transform.SetParent(box.transform, false);
        plate.transform.localPosition = Vector3.zero;
        plate.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        if (groundLayer >= 0) plate.layer = groundLayer;

        SpriteRenderer psr = plate.AddComponent<SpriteRenderer>();
        psr.sprite = GetPlainSprite();
        psr.drawMode = SpriteDrawMode.Simple;
        psr.color = new Color(0.88f, 0.96f, 1.00f, 0.95f);
        psr.sortingLayerName = "Default";
        psr.sortingOrder = 1;

        Material um = GetUnlitSpriteMaterial();
        if (um != null) psr.sharedMaterial = um;

        ScaleSpriteToWorld(psr, 1.35f, 0.10f);

        BoxCollider2D plateCol = plate.AddComponent<BoxCollider2D>();
        plateCol.size = new Vector2(1.35f, 0.10f);

        // 这一行让它成为镜子。没有它，光只会被挡住、不会拐弯。
        plate.AddComponent<MirrorSurface>();

        // 入射光要认镜子，得把反弹次数打开
        LightZone sel = (Selection.activeGameObject != null)
            ? Selection.activeGameObject.GetComponentInParent<LightZone>()
            : null;

        if (sel != null)
        {
            sel.maxBounces = Mathf.Max(2, sel.maxBounces);

            if (sel.lightMode != LightZone.LightMode.Directional)
            {
                Debug.LogWarning("[CatSceneBuilder] 「" + sel.name + "」现在是点光（Point）。" +
                                 "镜子只对【方向光 Directional】生效 —— \n" +
                                 "点光的反射会散开，需要另做一套。请把它改成 Directional，\n" +
                                 "或者用菜单 18 放一束直线光。");
            }
        }

        Undo.RegisterCreatedObjectUndo(box, "创建反射镜");

        Debug.Log("[CatSceneBuilder] 反射镜（真反射）放好了：" + box.name + "\n" +
                  "  · 方向光打到这块板上会真的拐弯：网格和判定用同一套折线\n" +
                  "  · 反射段会比入射段暗一点（Bounce Falloff），看起来才像同一条光\n" +
                  "  · 45° 板 → 水平光拐 90°；想拐另一边就把 Z 旋转改成 -45\n" +
                  "  · 入射光需要 Light Zone → Max Bounces ≥ 1");

        FinishPlacement(box);
    }
    [MenuItem("Tools/CatMare/21. 生成主菜单场景（MainMenu）", false, 210)]
    public static void BuildMainMenuScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // ---- 1. 背景图：确保它导入成 Texture（MainMenu 用 Texture2D 字段接） ----
        Texture2D bg = null;
        string[] candidates = new string[]
        {
            "Assets/aseprite/CatMare.jpg",
            "Assets/aseprite/CatMare.png",
            "Assets/CatMare.jpg",
        };

        for (int i = 0; i < candidates.Length && bg == null; i++)
        {
            TextureImporter ti = AssetImporter.GetAtPath(candidates[i]) as TextureImporter;
            if (ti == null) continue;

            // 如果被导入成了 Sprite，MainMenu 的 Texture2D 字段就接不上 —— 改回 Default
            if (ti.textureType != TextureImporterType.Default)
            {
                ti.textureType = TextureImporterType.Default;
                ti.SaveAndReimport();
            }

            bg = AssetDatabase.LoadAssetAtPath<Texture2D>(candidates[i]);
        }

        if (bg == null)
            Debug.LogWarning("[CatSceneBuilder] 没找到标题背景图。" +
                             "把图放到 Assets/aseprite/CatMare.jpg，或者生成后在 MainMenu 里手动指定。");

        // ---- 2. 新建空场景 ----
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 3. 相机：纯色背景，正交，不跟随 ----
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0f, 0f, -10f);

        Camera cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGO.AddComponent<AudioListener>();

        // ---- 4. 菜单逻辑 ----
        var menuGO = new GameObject("MainMenu");
        MainMenu menu = menuGO.AddComponent<MainMenu>();
        menu.background = bg;
        menu.dimAmount = 0f;          // 不压暗背景
        menu.buttonX = 0.07f;         // 左下角
        menu.buttonY = 0.62f;
        menu.buttonW = 0.20f;
        menu.buttonH = 0.065f;
        menu.startLabelText = "START";
        menu.levelsLabelText = "LEVELS";
        menu.quitLabelText = "QUIT";

        // 关卡列表：把 Assets/Scenes 里所有 Room*.unity 自动收进来
        var levelList = new System.Collections.Generic.List<string>();
        foreach (string g in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            string nm = System.IO.Path.GetFileNameWithoutExtension(path);
            if (nm.StartsWith("Room") && nm != "RoomTemplate") levelList.Add(nm);
        }

        levelList.Sort();
        if (levelList.Count > 0)
        {
            menu.levels = levelList.ToArray();
            menu.nextScene = levelList[0];
            Debug.Log("[CatSceneBuilder] 菜单关卡列表：" + string.Join(" / ", levelList));
        }
        else
        {
            Debug.LogWarning("[CatSceneBuilder] Assets/Scenes 里没找到 Room*.unity，" +
                             "关卡列表是空的。手动往 MainMenu.levels 里填场景名。");
        }

        // ---- 5. 保存 ----
        const string menuPath = "Assets/Scenes/MainMenu.unity";
        EditorSceneManager.SaveScene(scene, menuPath);
        AssetDatabase.Refresh();

        // ---- 6. 不动 Build Settings ----
        //   之前的版本会自动把 MainMenu 塞进 Build Settings 并排到第一位。
        //   那是错的：Build Settings 是项目级的配置，
        //   一个"生成场景"的工具不该顺手改它（会打乱手动排好的顺序）。
        //   要加就自己加：File → Build Settings → Add Open Scenes。

        Debug.Log("[CatSceneBuilder] 主菜单已生成：" + menuPath +
                  " ｜ 我没有动 Build Settings，需要你自己加进去。" +
                  (bg != null ? " 背景图已接上。" : " 背景图没接上，见上面的警告。"));

        EditorUtility.DisplayDialog("主菜单已生成",
            "场景保存到 Assets/Scenes/MainMenu.unity\n\n" +
            "Build Settings 我没动。要让它成为启动场景：\n" +
            "  File → Build Settings → Add Open Scenes\n" +
            "  然后把 MainMenu 拖到列表第一位\n\n" +
            "注意：nextScene 现在是 " + menu.nextScene + "\n" +
            "如果关卡场景不叫这个名字，点 START 会报 scene not found。",
            "好");
    }

    [MenuItem("Tools/CatMare/22. 放一块教学文字（会随光自动反色）", false, 220)]
    public static void PlaceTutorialSign()
    {
        Vector3 p = SceneViewCenter();

        // 钉到 z = 0：关卡里的东西都在这个平面。
        // SceneViewCenter 常常返回相机平面附近（z ≈ -10），
        // 那里的物体会跑到相机背后，屏幕空间的文字就画不出来。
        p.z = 0f;

        var go = new GameObject(UniqueName("TutorialSign"));
        go.transform.position = p;

        TutorialSign sign = go.AddComponent<TutorialSign>();
        sign.message = "按 A / D 移动\n按空格跳跃";

        Debug.Log("[CatSceneBuilder] 教学文字放好了。它会自己判断这个位置有没有光：" +
                  "有光画黑字、无光画白字，永远看得见。");

        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/1. 生成基础场景（只有障碍物）", false, 10)]
    public static void BuildSimple()
    {
        BuildScene(false);
    }

    [MenuItem("Tools/CatMare/2. 生成三房间测试关卡", false, 20)]
    public static void BuildRooms()
    {
        BuildScene(true);
    }

    // ------------------------------------------------------------
    // 3~5：在【已有场景】里补充物件，不会重建场景。
    //
    // 为什么要单独做这几个：
    //   1、2 会新建场景 —— 已摆好的 Tilemap 会被清空。
    //   而手工搭图主要依赖"只补不改"的入口。
    // ------------------------------------------------------------
    [MenuItem("Tools/CatMare/3. 补齐全局物件（音效池 / HUD）", false, 30)]
    public static void PatchGlobalObjects()
    {
        UnityEngine.SceneManagement.Scene scene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.isLoaded)
        {
            EditorUtility.DisplayDialog("等等", "先打开一个场景。", "好");
            return;
        }

        int added = 0;

        if (GameObject.Find("Sfx") == null)
        {
            new GameObject("Sfx").AddComponent<SfxPlayer>();
            added++;
        }

        if (GameObject.Find("HUD") == null)
        {
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<GameHUD>();
            hud.player = UnityEngine.Object.FindObjectOfType<PlayerInteract>();
            hud.form = UnityEngine.Object.FindObjectOfType<CatForm>();
            added++;
        }

        EditorSceneManager.MarkSceneDirty(scene);

        string msg = (added > 0)
            ? ("补了 " + added + " 个全局物件。记得 Ctrl+S 保存场景。")
            : "Sfx 和 HUD 都已经有了，没动。";

        Debug.Log("[CatSceneBuilder] " + msg);
        EditorUtility.DisplayDialog("完成", msg, "好");
    }

    [MenuItem("Tools/CatMare/4. 在当前视野中心放一个检查点", false, 40)]
    public static void PlaceCheckpoint()
    {
        PlaceFlowObject("Checkpoint", false);
    }

    [MenuItem("Tools/CatMare/5. 在当前视野中心放一个终点", false, 50)]
    public static void PlaceExit()
    {
        PlaceFlowObject("Exit", true);
    }

    // ------------------------------------------------------------
    // 6~9：四个新机关。放在 Scene 视图中心，层和组件都已配好，
    //      放完直接拖到位置上就行，不用再手动设置。
    // ------------------------------------------------------------
    [MenuItem("Tools/CatMare/6. 在当前视野中心放一个压力板", false, 60)]
    public static void PlacePressurePlate()
    {
        Vector3 p = SceneViewCenter();

        GameObject go = MakeBox(UniqueName("PressurePlate"), GetSquareSprite(), p,
                                new Vector2(1.0f, 0.20f), 0,
                                new Color(0.45f, 0.42f, 0.35f), null, true);

        go.AddComponent<PressurePlate>();
        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/7. 在当前视野中心放一盏定时灯", false, 70)]
    public static void PlaceTimerLightLamp()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        LightZone zone = MakeLamp(UniqueName("TimerLamp"), p, 3.0f,
                                  groundLayer > 0 ? (1 << groundLayer) : 0, GetSquareSprite());

        TimerLight tl = zone.gameObject.AddComponent<TimerLight>();
        tl.target = zone;

        FinishPlacement(zone.gameObject);
    }

    [MenuItem("Tools/CatMare/8. 在当前视野中心放一个推箱", false, 80)]
    public static void PlacePushBox()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        // 箱子必须在 Ground 层：灯只认这一层做遮挡，
        // 不在这一层，光会直接穿过箱子，"推箱子挡光"就不成立。
        GameObject go = MakeBox(UniqueName("PushBox"), GetSquareSprite(), p,
                                new Vector2(1.0f, 1.0f), groundLayer > 0 ? groundLayer : 0,
                                new Color(0.62f, 0.50f, 0.36f), null, false);

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
        rb.freezeRotation = true;          // 不然会被推着打滚
        rb.mass = 4f;                      // 太轻会被撞飞，太重推不动
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        go.AddComponent<PushBox>();
        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/9. 在当前视野中心放一块透光玻璃", false, 90)]
    public static void PlaceGlassPane()
    {
        int glassLayer = EnsureLayer("Glass");
        Vector3 p = SceneViewCenter();

        // 【玻璃放在独立的 Glass 层，不是 Ground】
        //
        //   光照的遮挡只查 Ground 层（Light Zone 的 Blocker Layer）——
        //   所以把玻璃放到 Glass 层，光就直接穿过去了。
        //   同时物理上它还是实心的：猫站得住、影子靠 passableInShadow 穿过去。
        //
        //   之前这里放的是 Ground 层，于是它和墙一样挡光 ——
        //   "透光玻璃"名不副实。
        //
        //   三层各管一件事：
        //     Ground  挡光 + 挡路        ← 墙、地板、瓦片
        //     Glass   只挡实体，不挡光    ← 这块玻璃
        //     Default 都不挡（除了挡猫） ← 机关、触发器
        GameObject go = MakeBox(UniqueName("GlassPane"), GetSquareSprite(), p,
                                new Vector2(0.20f, 3.0f), glassLayer > 0 ? glassLayer : 0,
                                new Color(0.55f, 0.78f, 1.00f, 0.35f), null, false);

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            // 玻璃必须暗处也看得见，否则玩家不知道那里有一块能站的板子
            Material um = GetUnlitSpriteMaterial();
            if (um != null) sr.sharedMaterial = um;
        }

        GlassPane gp = go.AddComponent<GlassPane>();

        // 玻璃对三种东西说三句话，互不矛盾：
        //   挡身体   实体过不去（碰撞体是实心的）
        //   不挡光   Glass 层不在灯的 Blocker Layer 里
        //   不挡影子 shadowCollider 会忽略它
        gp.passableInShadow = true;

        Debug.Log("[CatSceneBuilder] 玻璃放好了：猫过不去、影子穿得过、光穿得过。" +
                  "如果它还是挡光，检查 Light Zone 的 Blocker Layer 有没有勾到 Glass。");

        FinishPlacement(go);
    }

    [MenuItem("Tools/CatMare/10. 放一盏吊灯（能当平台，踩两下掉下来）", false, 100)]
    public static void PlaceHangingLamp()
    {
        int groundLayer = EnsureLayer("Ground");
        Vector3 p = SceneViewCenter();

        GameObject go = MakeBox(UniqueName("HangingLamp"), GetSquareSprite(), p,
                                new Vector2(1.0f, 1.0f), groundLayer > 0 ? groundLayer : 0,
                                new Color(1f, 0.92f, 0.66f), null, false);

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
        rb.mass = 2f;
        rb.freezeRotation = false;         // 吊灯要能摆
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // 灯点放在灯体下方：灯自己的碰撞体也在 Ground 层，
        // 如果灯点放在灯体中心，灯会把自己的光整个挡住（射线起点在体内）。
        GameObject lightPoint = new GameObject("LightPoint");
        lightPoint.transform.SetParent(go.transform, false);
        lightPoint.transform.localPosition = new Vector3(0f, -0.62f, 0f);

        LightZone zone = MakeLampOn(lightPoint, 3.0f, groundLayer > 0 ? (1 << groundLayer) : 0);

        HangingLamp hl = go.AddComponent<HangingLamp>();
        hl.lamp = zone;

        FinishPlacement(go);
    }

    static string UniqueName(string baseName)
    {
        string n = baseName;
        int i = 2;
        while (GameObject.Find(n) != null)
        {
            n = baseName + "_" + i;
            i++;
        }
        return n;
    }

    /// <summary>按几个候选路径找一张 Sprite，找不到返回 null</summary>
    static Sprite TryLoadSprite(params string[] paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
            if (s != null) return s;
        }
        return null;
    }

    // ------------------------------------------------------------
    // 11. 只给隔墙开门，别的都不动
    //
    // 为什么单独做一个：菜单 2 会新建场景，猫身上的美术设置会全部归零。
    // 如果只是想给墙开个门，不值得把整个场景重建一遍。
    // 这个菜单只改 WallMid1 / WallMid2 的位置和尺寸。
    // ------------------------------------------------------------
    [MenuItem("Tools/CatMare/11. 只给房间隔墙开门（不碰猫的美术）", false, 110)]
    public static void OpenDoorways()
    {
        int n = 0;
        if (CarveDoor("WallMid1") ) n++;
        if (CarveDoor("WallMid2") ) n++;

        UnityEngine.SceneManagement.Scene scene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);

        if (n == 0)
        {
            EditorUtility.DisplayDialog("没找到墙",
                "场景里没有 WallMid1 / WallMid2。\n\n" +
                "如果你是自己搭的关卡，直接在墙上留一个 2 格高的洞就行。", "好");
        }
        else
        {
            EditorUtility.DisplayDialog("门开好了",
                "改了 " + n + " 面墙。\n\n" +
                "门洞高 2.0（2 格），猫 1.5 高能过；\n" +
                "门楣一直做到屋顶，影形态飘不过去。\n\n" +
                "记得 Ctrl+S 保存。", "好");
        }
    }

    static bool CarveDoor(string wallName)
    {
        GameObject go = GameObject.Find(wallName);
        if (go == null) return false;

        // 地面顶面 -2.70，门洞 2.0 高 → 洞从 -2.70 到 -0.70
        // 门楣从 -0.70 做到 3.10 → 高 3.8，中心 1.2
        Vector3 p = go.transform.position;
        go.transform.position = new Vector3(p.x, 1.2f, p.z);

        Vector2 size = new Vector2(0.5f, 3.8f);

        BoxCollider2D col = go.GetComponent<BoxCollider2D>();
        if (col != null) col.size = size;

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.size = size;

        return true;
    }

    static void FinishPlacement(GameObject go)
    {
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        UnityEngine.SceneManagement.Scene scene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[CatSceneBuilder] 已放置 " + go.name + "（用移动工具挪到位置上）");
    }

    static void PlaceFlowObject(string baseName, bool isExit)
    {
        UnityEngine.SceneManagement.Scene scene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.isLoaded)
        {
            EditorUtility.DisplayDialog("等等", "先打开一个场景。", "好");
            return;
        }

        Vector3 p = SceneViewCenter();

        string n = baseName;
        int i = 2;
        while (GameObject.Find(n) != null)
        {
            n = baseName + "_" + i;
            i++;
        }

        GameObject go;
        if (isExit)
        {
            go = MakeBox(n, GetSquareSprite(), p, new Vector2(1.0f, 2.2f), 0,
                         new Color(0.40f, 0.40f, 0.50f), null, true);
            go.AddComponent<LevelExit>();
        }
        else
        {
            go = MakeBox(n, GetSquareSprite(), p, new Vector2(0.7f, 0.7f), 0,
                         new Color(0.30f, 0.30f, 0.38f), null, true);
            go.AddComponent<Checkpoint>();
        }

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[CatSceneBuilder] 放置 " + n + " 于 " + p + "（可以用移动工具挪位置）");
    }

    /// <summary>
    /// Scene 视图中心；没打开 Scene 视图就退回主相机。
    ///
    /// 【z 一律钉成 0 —— 这是必须的】
    ///   SceneView.pivot 的 z 常常落在相机平面附近（z ≈ -10）。
    ///   用这个 z 放出来的东西会跑到相机【背后】：
    ///     · 判定照常工作（光照、碰撞都是纯数学，不看相机）
    ///     · 但屏幕上完全看不见（在相机背后，被近裁剪面剪掉）
    ///   症状就是"我明明在光里，却看不到光"，而且查一小时也查不出来。
    ///   关卡里的东西本来就都在 z = 0 这个平面上，所以这里统一压平。
    /// </summary>
    static Vector3 SceneViewCenter()
    {
        Vector3 p;

        SceneView sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            p = sv.pivot;
        }
        else
        {
            Camera cam = Camera.main;
            p = (cam != null)
                ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f)
                : Vector3.zero;
        }

        p.z = 0f;
        return p;
    }

    static void BuildScene(bool withRooms, bool blankRoom = false)
    {
        // ---- 1. 确保 Layer 存在 ----
        int groundLayer = EnsureLayer("Ground");
        int lightLayer = EnsureLayer("Light");

        // Glass：透光玻璃专用层。光照遮挡只查 Ground，
        // 所以玻璃挂在这一层上既能站人、又不挡光。
        // 模板里也一并建好，避免新建关卡时遗漏。
        int glassLayer = EnsureLayer("Glass");

        if (groundLayer < 0 || lightLayer < 0)
        {
            Debug.LogError("[CatSceneBuilder] 无法创建 Layer，中止");
            return;
        }

        Debug.Log("[CatSceneBuilder] 图层：Ground=" + groundLayer +
                  "  Light=" + lightLayer +
                  "  Glass=" + (glassLayer >= 0 ? glassLayer.ToString() : "创建失败"));

        // ---- 2. 新建场景 ----
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        PhysicsMaterial2D noFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
        if (noFriction == null)
            Debug.LogWarning("[CatSceneBuilder] 找不到 NoFriction 材质，摩擦会偏大");

        Sprite sprite = GetSquareSprite();

        // ---- 3. 环境光 ----
        // 【这个数字决定"暗处到底有多黑"，是全项目最重要的一个数】
        //   它乘上物体的颜色，就是未被照亮时的亮度。
        //   线性色彩空间下，0.18 的白色地板会显示成 #4A4A4A —— 看起来"底下是亮的"，
        //   哪怕那里根本没有灯照到。画面上"底下莫名发亮"就是这么来的。
        //   0.05 左右才是"只有灯下是亮的"。
        //   想更黑就往 0.02 调；想看清整张地图就往 0.10 调。
        var globalLightGo = new GameObject("Global Light 2D");
        var globalLight = globalLightGo.AddComponent<Light2D>();
        globalLight.lightType = Light2D.LightType.Global;
        globalLight.intensity = 0.06f;
        globalLight.color = new Color(0.55f, 0.62f, 0.85f);

        // ---- 4. 地形 ----
        // blankRoom = 空白房间模板：什么都不放，地形需手工摆放。
        if (!blankRoom)
        {
            // 不用纯白：纯白会被环境光和灯双重放大，很容易"哪里都亮"。
            MakeBox("Ground", sprite, new Vector3(12f, -3.2f, 0f),
                    new Vector2(46f, 1f), groundLayer, new Color(0.70f, 0.72f, 0.78f), noFriction, false);

            if (withRooms)
            {
                BuildThreeRooms(sprite, groundLayer, noFriction, lightLayer);
            }
            else
            {
                MakeBox("WallA", sprite, new Vector3(1.8f, -1.4f, 0f),
                        new Vector2(0.8f, 2.6f), groundLayer, new Color(0.55f, 0.55f, 0.6f), noFriction, false);
                MakeBox("WallB", sprite, new Vector3(-2.2f, -1.4f, 0f),
                        new Vector2(0.8f, 2.6f), groundLayer, new Color(0.55f, 0.55f, 0.6f), noFriction, false);
            }
        }
        else
        {
            // 空白模板给一个 Level 容器：以后所有地形都拖成它的子物体，
            // 好处是一键隐藏、一键整体移动、Hierarchy 不会炸。
            var levelRoot = new GameObject("Level");
            levelRoot.transform.position = Vector3.zero;
        }

        // ---- 5. 猫 ----
        var cat = new GameObject("Cat");
        cat.tag = "Player";
        cat.transform.position = new Vector3(0f, -1.5f, 0f);

        var rb = cat.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // ---- 尺寸（对应 1 格 = 1.0 世界单位的 Tilemap）----
        // 这三个数决定了整个关卡设计的语言，见 MakeBox 上方的说明。
        const float catW = 1.10f;
        const float catH = 1.50f;
        const float shadowW = 0.70f;
        const float shadowH = 0.10f;

        var catCol = cat.AddComponent<BoxCollider2D>();
        catCol.size = new Vector2(catW, catH);
        if (noFriction != null) catCol.sharedMaterial = noFriction;

        var shadowCol = cat.AddComponent<BoxCollider2D>();
        shadowCol.size = new Vector2(shadowW, shadowH);
        shadowCol.enabled = false;                   // 默认关闭
        if (noFriction != null) shadowCol.sharedMaterial = noFriction;

        var groundCheck = new GameObject("GroundCheck");
        groundCheck.transform.SetParent(cat.transform, false);
        groundCheck.transform.localPosition = new Vector3(0f, -catH * 0.5f, 0f);

        var catMove = cat.AddComponent<CatMove>();
        catMove.groundCheck = groundCheck.transform;
        catMove.groundLayer = 1 << groundLayer;

        var catVisual = cat.AddComponent<CatVisual>();

        // ---- 如果 Assets/aseprite 里已有三张美术图，自动挂上 ----
        // 【为什么要在生成器里做这件事】
        //   一键生成会新建场景，猫身上的美术设置全都归零。
        //   每次重新生成都要重拖三张图、重设四个颜色，太蠢了。
        //   所以在生成时按路径找一遍：找到就用美术图，找不到就用程序占位猫。
        Sprite bodyArt = TryLoadSprite("Assets/aseprite/catbody.png", "Assets/Art/catbody.png");
        Sprite headArt = TryLoadSprite("Assets/aseprite/cathead.png", "Assets/Art/cathead.png");
        Sprite eyeArt = TryLoadSprite("Assets/aseprite/cateyes.png", "Assets/Art/cateyes.png");

        if (bodyArt != null || headArt != null || eyeArt != null)
        {
            catVisual.bodySprite = bodyArt;
            catVisual.headSprite = headArt;
            catVisual.eyeSprite = eyeArt;
            if (eyeArt != null) catVisual.eyeSpriteIsBothEyes = true;

            // 用自己的美术图时，颜色必须纯白。
            // 这几个字段是"乘在贴图上的色调"，留着深色会把自己的图乘成一块黑。
            catVisual.bodyColor = Color.white;
            catVisual.eyeColor = Color.white;
            catVisual.shadowHeadColor = Color.white;
            catVisual.shadowHeadAlpha = 0.80f;

            Debug.Log("[CatSceneBuilder] 已自动挂上 aseprite 里的猫美术");
        }

        var catForm = cat.AddComponent<CatForm>();
        catForm.catCollider = catCol;
        catForm.shadowCollider = shadowCol;
        catForm.move = catMove;
        catForm.visual = catVisual;
        catForm.solidLayer = 1 << groundLayer;

        // 在黑暗里待太久会走丢（眼睛熄灭）
        cat.AddComponent<ShadowTimer>();

        var lightSensor = cat.AddComponent<LightSensor>();
        lightSensor.autoFindLights = true;
        lightSensor.solidLayer = 1 << groundLayer;   // 埋在墙里的采样点要靠它排除

        cat.AddComponent<PlayerInteract>();

        // ---- 5.5 全局物件：音效池 + HUD ----
        // 这两个跟关卡布局无关，放在哪都行，所以由建造器统一创建，无需手动处理。
        var sfxGo = new GameObject("Sfx");
        sfxGo.AddComponent<SfxPlayer>();

        var hudGo = new GameObject("HUD");
        var hud = hudGo.AddComponent<GameHUD>();
        hud.player = cat.GetComponent<PlayerInteract>();
        hud.form = catForm;

        // ---- 6. 基础场景的灯、开关、陷阱 ----
        // 空白房间模板什么都不放：灯和机关由关卡作者自行添加。
        if (!blankRoom && !withRooms)
        {
            var zone = MakeLamp("LampA", new Vector3(4.5f, -1.2f, 0f), 3.5f, 1 << groundLayer, sprite);
            MakeSwitch("LampSwitch", new Vector3(2.8f, -2.4f, 0f), new LightZone[] { zone }, sprite);

            var trap = MakeBox("Trap", sprite, new Vector3(-4.6f, -2.4f, 0f),
                               new Vector2(1.2f, 0.5f), 0, new Color(0.9f, 0.25f, 0.25f), null, true);
            trap.AddComponent<Trap>();

            var cp = MakeBox("Checkpoint", sprite, new Vector3(1.2f, -2.45f, 0f),
                             new Vector2(0.7f, 0.7f), 0, new Color(0.3f, 0.3f, 0.38f), null, true);
            cp.AddComponent<Checkpoint>();

            var exit = MakeBox("Exit", sprite, new Vector3(9.5f, -1.6f, 0f),
                               new Vector2(1.0f, 2.2f), 0, new Color(0.4f, 0.4f, 0.5f), null, true);
            exit.AddComponent<LevelExit>();
        }

        // ---- 7. 相机 ----
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0f, -0.5f, -10f);

        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        // 这个值只是编辑器里的预览。运行时 CameraFollow 的像素完美会重算它，
        // 真正的视野由 targetWorldHeight（默认 12 个单位高）决定。
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.07f);   // 几乎全黑

        camGo.AddComponent<AudioListener>();

        var follow = camGo.AddComponent<CameraFollow>();
        follow.target = cat.transform;

        // ---- 8. 保存 ----
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        string savePath = blankRoom ? TemplatePath : ScenePath;

        EditorSceneManager.SaveScene(scene, savePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[CatSceneBuilder] 场景已生成：" + savePath);

        string msg;
        if (blankRoom)
        {
            msg = "空白房间模板已生成：" + savePath + "\n\n" +
                  "里面只有：\n" +
                  "  Cat（猫，全部组件和你的美术图都配好了）\n" +
                  "  Main Camera（像素完美相机）\n" +
                  "  Sfx / HUD / Global Light 2D（环境光 0.06，很暗）\n" +
                  "  Level（空容器，以后地形都拖进它里面）\n\n" +
                  "图层已经建好了三个：Ground / Light / Glass\n" +
                  "  Ground 挡光 + 挡路（地板、墙）\n" +
                  "  Glass  只挡路、不挡光（透光玻璃）\n" +
                  "  Light  灯的光晕（不参与碰撞）\n\n" +
                  "【没有任何地形、没有任何灯】—— 由你自己摆。\n\n" +
                  "⚠ 第一件事是摆地板。不然按 Play 之后猫会一直往下掉。\n\n" +
                  "摆方块的三个必查项：\n" +
                  "  Layer          = Ground\n" +
                  "  Order in Layer = -10\n" +
                  "  Is Trigger     = 不勾\n\n" +
                  "Position 填【中心点】，Size 填【跨度】。\n" +
                  "例：x 从 0 到 19、y 从 0 到 1 的地板\n" +
                  "    Position (9.5, 0.5)   Size (19, 1)";
        }
        else if (withRooms)
        {
            msg = "三房间关卡已生成：" + ScenePath + "\n\n" +
                  "按 Play 试（A/D 移动，空格跳，E 互动，R 重来）：\n\n" +
                  "Room A（左边）钻窄缝\n" +
                  "  通道只有 0.45 高，猫过不去\n" +
                  "  但通道上方有灯 → 先按 E 关掉 A_Switch\n" +
                  "  关灯后变成影形态，飘过去\n\n" +
                  "Room B（中间）能力互斥\n" +
                  "  红色陷阱横在路上，B_Switch 在光圈里\n" +
                  "  影形态能安全穿陷阱，但按不了开关\n" +
                  "  必须走进光里变回猫才能按\n\n" +
                  "Room C（右边）墙制造阴影\n" +
                  "  灯在 C_Blocker 后面，通道天然是暗的\n" +
                  "  不用关灯，直接走进去就变影形态钻过去\n\n" +
                  "新增：\n" +
                  "  蓝色小块 = 检查点，踩过之后死了从那里重生\n" +
                  "  最右边 = 终点，走进去会显示通关横幅\n" +
                  "  R = 回到重生点（卡进碗里出不来时用）\n" +
                  "  关卡有屋顶和两端墙，影形态飘不出去了";
        }
        else
        {
            msg = "基础场景已生成：" + ScenePath + "\n\n" +
                  "按 Play 试：\n" +
                  "  A/D 移动，空格跳\n" +
                  "  走进 LampA 的光 → 变回猫（身体显形）\n" +
                  "  按 E 关灯 → 变成只有黄眼睛的影形态\n" +
                  "  影形态：空格上浮、S 下沉、默认悬停\n" +
                  "  走到墙后面 → 光被挡住 → 自动变影形态\n" +
                  "  影形态免疫陷阱；变回猫碰陷阱会死";
        }

        EditorUtility.DisplayDialog("完成", msg, "好");
    }

    // ------------------------------------------------------------
    // 三间房：教学用的最小关卡
    //
    //   Room A  钻窄缝 —— 缝隙 0.45，猫过不去，影形态能过。
    //           通道上方有灯，所以必须先关灯。
    //           教的是：光会把影子变回实体，因此要设法消除光。
    //
    //   Room B  能力互斥 —— 陷阱横在路上，开关在光里。
    //           影形态能安全穿过陷阱，但按不了开关。
    //           教的是：影形态不是无脑更强。
    //
    //   Room C  遮挡 —— 灯在墙后面，通道天然是暗的。
    //           教的是：墙就是阴影制造器。
    // ------------------------------------------------------------
    static void BuildThreeRooms(Sprite sprite, int groundLayer, PhysicsMaterial2D mat, int lightLayer)
    {
        Color wallColor = new Color(0.46f, 0.48f, 0.56f);
        Color trapColor = new Color(0.9f, 0.25f, 0.25f);

        // ---- 房间隔墙（带门洞）----
        // 【门洞怎么算】
        //   地面顶面 y = -2.70，门洞做 2.0 高（= 2 格，猫 1.5 高能过）
        //   → 门洞从 -2.70 到 -0.70
        //   → 剩下的"门楣"从 -0.70 一直做到 3.10，和屋顶（下表面 2.6）重叠
        //   为什么不能只做半截墙：影形态能飘，墙要是只到腰高，
        //   玩家一关灯就从墙顶飘过去了，整间房白做。
        //   所以要"下面开门、上面做到顶"。
        MakeBox("WallMid1", sprite, new Vector3(8.5f, 1.2f, 0f),
                new Vector2(0.5f, 3.8f), groundLayer, wallColor, mat, false);

        MakeBox("WallMid2", sprite, new Vector3(17.5f, 1.2f, 0f),
                new Vector2(0.5f, 3.8f), groundLayer, wallColor, mat, false);

        // ---- 外壳：屋顶 + 两端 ----
        // 没有屋顶，影形态可以一路飘出关卡顶部。那不是难度，那是漏洞。
        MakeBox("Roof", sprite, new Vector3(12f, 2.9f, 0f),
                new Vector2(48f, 0.6f), groundLayer, wallColor, mat, false);

        MakeBox("WallLeft", sprite, new Vector3(-11.3f, -0.1f, 0f),
                new Vector2(0.6f, 6.4f), groundLayer, wallColor, mat, false);

        MakeBox("WallRight", sprite, new Vector3(35.3f, -0.1f, 0f),
                new Vector2(0.6f, 6.4f), groundLayer, wallColor, mat, false);

        // ---- 出生点的灯 ----
        // 没有灯的话玩家一出生就是影形态，第一眼不知道"猫形态"长什么样。
        MakeLamp("Start_Lamp", new Vector3(0.5f, -1.0f, 0f), 2.4f, 1 << groundLayer, sprite);

        // ---- Room A：1 格窄缝 ----
        // 地面顶面 y = -2.70。天花板底面放在 -1.70 → 缝隙正好 1.0（一格）
        // 猫 1.50 高 → 过不去；影子 0.10 高 → 轻松过。
        // 这就是整套设计的语言：1 格 = 只有影子，2 格 = 猫也能过。
        MakeBox("A_Ceiling", sprite, new Vector3(5.0f, -1.25f, 0f),
                new Vector2(4.0f, 0.9f), groundLayer, wallColor, mat, false);

        var aLamp = MakeLamp("A_Lamp", new Vector3(5.0f, -1.2f, 0f), 3.2f, 1 << groundLayer, sprite);
        MakeSwitch("A_Switch", new Vector3(1.6f, -2.4f, 0f), new LightZone[] { aLamp }, sprite);

        // ---- Room B：陷阱 + 光里的开关 ----
        var trap = MakeBox("B_Trap", sprite, new Vector3(11.5f, -2.35f, 0f),
                           new Vector2(2.4f, 0.7f), 0, trapColor, null, true);
        trap.AddComponent<Trap>();

        var bLamp = MakeLamp("B_Lamp", new Vector3(15.0f, -1.0f, 0f), 2.8f, 1 << groundLayer, sprite);
        MakeSwitch("B_Switch", new Vector3(15.0f, -2.4f, 0f), new LightZone[] { bLamp }, sprite);

        // ---- Room C：墙制造阴影 ----
        var cLamp = MakeLamp("C_Lamp", new Vector3(20.0f, 0.6f, 0f), 4.0f, 1 << groundLayer, sprite);

        MakeBox("C_Blocker", sprite, new Vector3(21.4f, -1.2f, 0f),
                new Vector2(0.5f, 3.2f), groundLayer, wallColor, mat, false);

        MakeBox("C_Ceiling", sprite, new Vector3(23.6f, -1.25f, 0f),
                new Vector2(3.6f, 0.9f), groundLayer, wallColor, mat, false);

        // ---- 检查点与终点 ----
        // 检查点放在两个房间的交界外侧：玩家每"过完一间房"就被记住一次。
        // 不是仁慈，是让他敢去试那些"这缝能不能钻"的地方——
        // 不敢死，就不会试探，而试探是这个游戏的全部乐趣。
        var cpA = MakeBox("Checkpoint_A", sprite, new Vector3(7.2f, -2.45f, 0f),
                          new Vector2(0.7f, 0.7f), 0, new Color(0.30f, 0.30f, 0.38f), null, true);
        cpA.AddComponent<Checkpoint>();

        var cpB = MakeBox("Checkpoint_B", sprite, new Vector3(16.4f, -2.45f, 0f),
                          new Vector2(0.7f, 0.7f), 0, new Color(0.30f, 0.30f, 0.38f), null, true);
        cpB.AddComponent<Checkpoint>();

        var exit = MakeBox("Exit", sprite, new Vector3(33.6f, -1.6f, 0f),
                           new Vector2(1.0f, 2.2f), 0, new Color(0.40f, 0.40f, 0.50f), null, true);
        exit.AddComponent<LevelExit>();

        Debug.Log("[CatSceneBuilder] 三房间关卡已放置（含屋顶、检查点、终点）");
    }

    static LightZone MakeLamp(string name, Vector3 pos, float radius, LayerMask blocker, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        return MakeLampOn(go, radius, blocker);
    }

    /// <summary>给一个已存在的物体装上灯：Light2D + LightZone，参数和阻挡层都配好</summary>
    static LightZone MakeLampOn(GameObject go, float radius, LayerMask blocker)
    {
        var l2d = go.AddComponent<Light2D>();
        l2d.lightType = Light2D.LightType.Point;
        l2d.color = new Color(1f, 0.92f, 0.66f);

        // 【关键】Light2D 默认不做遮挡（要额外挂 ShadowCaster2D 才会被墙挡）。
        // 如果把它的半径设成真实照亮半径，光就会穿墙：
        // 画面上墙后是亮的，判定上墙后是暗的，玩家彻底迷失。
        // 所以它只负责"灯泡本体 + 灯附近一小圈光晕"，
        // 真正的"照到哪里"由 LightZone 自己用 raycast 画的光扇负责。
        l2d.intensity = 1.3f;
        l2d.pointLightInnerRadius = 0.12f;
        l2d.pointLightOuterRadius = 0.45f;

        var zone = go.AddComponent<LightZone>();
        zone.radius = radius;
        zone.blockerLayer = blocker;
        zone.visualLight = go;
        zone.rayCount = 96;
        zone.fanAlpha = 0.40f;
        zone.edgeFalloff = 0.35f;

        return zone;
    }

    static void MakeSwitch(string name, Vector3 pos, LightZone[] lights, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.position = pos;

        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.8f, 0.8f);
        col.isTrigger = true;

        if (sprite != null)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(0.9f, 0.85f, 0.55f);
            sr.sortingOrder = 2;

            Material um = GetUnlitSpriteMaterial();
            if (um != null) sr.sharedMaterial = um;
        }

        var sw = go.AddComponent<LightSwitch>();
        sw.lights = lights;
    }

    static GameObject MakeBox(string name, Sprite sprite, Vector3 pos, Vector2 size,
                              int layer, Color color, PhysicsMaterial2D mat, bool isTrigger)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        if (layer > 0) go.layer = layer;

        if (sprite != null)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;

            // 排序分层（数字小的先画，会被后画的盖住）：
            //   地形 -10 → 光扇 -5 → 陷阱/检查点/终点 -2 → 尾巴 -1 → 猫 0 → 眼睛 1
            // 光扇必须在地形上面，否则它会被地面方块盖住，
            // 屏幕上就只剩下不做遮挡的 Light2D —— 光穿墙的老毛病。
            sr.sortingOrder = isTrigger ? -2 : -10;

            // 交互物不吃光照：环境光只有 0.06，吃光的红色陷阱会变成一块看不见的黑。
            // 玩家必须能看见陷阱和开关——看不见的陷阱不叫难度，叫不讲理。
            if (isTrigger)
            {
                Material um = GetUnlitSpriteMaterial();
                if (um != null) sr.sharedMaterial = um;
            }
        }

        var col = go.AddComponent<BoxCollider2D>();
        col.size = size;
        col.isTrigger = isTrigger;
        if (mat != null) col.sharedMaterial = mat;

        return go;
    }

    static Sprite GetSquareSprite()
    {
        Sprite s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (s == null) s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        if (s == null) Debug.LogWarning("[CatSceneBuilder] 找不到内置 Sprite，方块会不可见（但碰撞体在）");
        return s;
    }

    /// <summary>
    /// 一张【纯白、无边框】的方块图。
    ///
    /// UISprite 是九宫格（带圆角和边框）。需要"任意拉伸都纯粹"的东西
    /// （细长条、光束、激光）时用它，否则 Unity 会警告
    /// "Sprite Tiling might not appear correctly"。
    /// </summary>
    static Sprite GetPlainSprite()
    {
        Sprite s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        if (s == null) s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        return s;
    }

    const string UnlitMaterialPath = "Assets/CatUnlitSprite.mat";

    /// <summary>
    /// 交互物（陷阱 / 开关 / 检查点 / 终点）用的"不吃光照"材质。
    ///
    /// 为什么需要：环境光只有 0.06，吃光照的红色陷阱会变成一块几乎看不见的黑。
    /// 玩家必须能看见陷阱在哪里 —— 看不见的陷阱不叫难度，叫不讲理。
    ///
    /// 为什么要存成 .mat 资源而不是 new 一个：材质不是场景里的物体，
    /// 不存成资源的话，下次打开场景引用会变成 Missing。
    /// </summary>
    static Material GetUnlitSpriteMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
        if (m != null) return m;

        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null)
        {
            Debug.LogWarning("[CatSceneBuilder] 找不到无光照 Sprite Shader，交互物会跟着环境变暗");
            return null;
        }

        m = new Material(sh);
        m.name = "CatUnlitSprite";
        AssetDatabase.CreateAsset(m, UnlitMaterialPath);
        AssetDatabase.SaveAssets();

        return m;
    }

    /// <summary>确保某个 Layer 存在，返回它的 index；失败返回 -1</summary>
    static int EnsureLayer(string name)
    {
        int idx = LayerMask.NameToLayer(name);
        if (idx >= 0) return idx;

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[CatSceneBuilder] 读不到 TagManager.asset");
            return -1;
        }

        var so = new SerializedObject(assets[0]);
        SerializedProperty layers = so.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log("[CatSceneBuilder] 已创建 Layer " + name + " (index " + i + ")");
                return i;
            }
        }

        Debug.LogError("[CatSceneBuilder] 没有空余的 User Layer 位置了");
        return -1;
    }
}
