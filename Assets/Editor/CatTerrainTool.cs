// ============================================================
// CatTerrainTool.cs —— 生成"配置正确"的地形，并检查已有地形
//
// 菜单：Tools → CatMare → 地形 → 1. 生成一圈墙 + 正确的 Grid/Tilemap
//                              2. 体检并修复当前场景的地形配置
//
// 【为什么需要这个】
//   新建一个 Tilemap，Unity 给的全是默认值，而这些默认值在本项目的渲染体系里是错的：
//
//     默认 Order in Layer = 0
//        → 瓦片画在光扇（-5）之上 → 光扇被整块地形盖住 → 画面一片黑
//     默认 Sorting Layer 不一定是 Default
//        → Global Light 2D 的目标排序层只写了 Default，别的层收不到光
//     默认 TilemapCollider2D 没勾「由复合使用」
//        → 每块瓦片一个独立碰撞体，接缝会让角色卡脚
//     默认没有 CompositeCollider2D / 没有 Static 的 Rigidbody2D
//        → 上一行根本没有前提
//     默认层是 Default
//        → 光照遮挡只查 Ground 层，地形不在这一层 → 光会穿墙
//
//   这五条里任何一条不对，症状都很"玄学"：黑屏、卡脚、光穿墙、
//   或者站在地上跳不起来。所以干脆用工具一次配好，并给一个体检功能。
// ============================================================

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class CatTerrainTool
{
    const string MenuRoot = "Tools/CatMare/地形/";
    const string TilePath = "Assets/Tiles/WallTile.asset";
    const string WallSpritePath = "Assets/aseprite/wall1.png";

    // 地形在该场景里应该有的配置（和 Room1 一致）
    const int TerrainSortingOrder = -10;
    const string TerrainSortingLayer = "Default";

    // ------------------------------------------------------------
    [MenuItem(MenuRoot + "1. 生成一圈墙 + 正确的 Grid/Tilemap", false, 1)]
    public static void CreateTerrain()
    {
        int groundLayer = EnsureLayer("Ground");
        int glassLayer = EnsureLayer("Glass");
        Tile tile = GetOrCreateTile();

        if (tile == null)
        {
            EditorUtility.DisplayDialog("没有可用的瓦片",
                "找不到 Assets/aseprite/wall1.png，也没法建内置瓦片。\n\n" +
                "把一张墙的图放到 Assets/aseprite/wall1.png 再试。", "好");
            return;
        }

        // ---- Grid：已存在就复用 ----
        Grid grid = Object.FindObjectOfType<Grid>();
        GameObject gridGO;

        if (grid != null)
        {
            gridGO = grid.gameObject;
            Debug.Log("[CatTerrainTool] 复用了场景里已有的 Grid");
        }
        else
        {
            gridGO = new GameObject("Grid");
            grid = gridGO.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);
        }

        // ---- Tilemap ----
        GameObject tmGO = new GameObject("Wall");
        tmGO.transform.SetParent(gridGO.transform, false);
        tmGO.transform.localPosition = Vector3.zero;

        Tilemap tilemap = tmGO.AddComponent<Tilemap>();
        TilemapRenderer tmr = tmGO.AddComponent<TilemapRenderer>();

        // 这两行就是"黑屏"的解药
        tmr.sortingLayerName = TerrainSortingLayer;
        tmr.sortingOrder = TerrainSortingOrder;

        // ---- 碰撞：TilemapCollider2D + CompositeCollider2D + Static Rigidbody2D ----
        Rigidbody2D rb = tmGO.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;      // Composite 必须有它

        TilemapCollider2D tmc = tmGO.AddComponent<TilemapCollider2D>();
        tmc.usedByComposite = true;                // 不勾就会一格一格卡脚

        CompositeCollider2D comp = tmGO.AddComponent<CompositeCollider2D>();
        comp.geometryType = CompositeCollider2D.GeometryType.Polygons;  // Outlines 是空心的
        comp.generationType = CompositeCollider2D.GenerationType.Synchronous;

        // ---- 层：必须在 Ground，否则光会穿墙 ----
        if (groundLayer > 0) tmGO.layer = groundLayer;

        // ---- 再造一个【玻璃 Tilemap】，同一个 Grid 下的第二个地形 ----
        //
        // 【为什么要用 Tilemap 做玻璃，而不是一块块摆物体】
        //   玻璃的形状常常是"一整条地板"或"一堵墙"，用刷子画最快。
        //   只要这个 Tilemap 不待在 Ground 层，光就会直接穿过去 ——
        //   因为光照遮挡只查 Ground。
        //   于是得到一种地形：挡人、不挡光。
        //
        //   排序必须和 Wall 一样是 -10，否则它会盖住光扇（-5）。
        MakeGlassTilemap(gridGO, glassLayer);

        // ---- 画一圈墙 ----
        // 房间 19 宽 7 高：底下是地板，四周是墙，上面留空给灯光和机关
        int w = 19, h = 7;

        for (int x = 0; x < w; x++)
        {
            tilemap.SetTile(new Vector3Int(x, 0, 0), tile);            // 地板
            tilemap.SetTile(new Vector3Int(x, h - 1, 0), tile);        // 天花板
        }

        for (int y = 1; y < h - 1; y++)
        {
            tilemap.SetTile(new Vector3Int(0, y, 0), tile);            // 左墙
            tilemap.SetTile(new Vector3Int(w - 1, y, 0), tile);        // 右墙
        }

        Undo.RegisterCreatedObjectUndo(tmGO, "创建地形");

        // ---- 示例玻璃：光能穿过、人能站上去 ----
        // 【为什么要在模板里放一块】
        //   "Glass 层"这种东西，光看图层列表是理解不了的。
        //   必须有一块真实的玻璃摆在房间里，才看得出
        //   "灯照在它上面、光直接穿过去、人却站得住"是什么样。
        //   用不上就删掉，它只是个示例。
        GameObject glass = MakeDemoGlassPane(groundLayer);

        EditorSceneManager.MarkSceneDirty(tmGO.scene);

        Debug.Log("[CatTerrainTool] 地形建好了：19×7 的一圈墙。\n" +
                  "  Sorting Layer = " + TerrainSortingLayer + "，Order in Layer = " + TerrainSortingOrder + "\n" +
                  "  层 = Ground，由复合使用 ✓，几何体类型 = Polygons ✓，Rigidbody = Static ✓\n" +
                  (glass != null
                     ? "  另外放了一块【示例玻璃】" + glass.name + "（Glass 层，光能穿、人能站）。用不上直接删。\n"
                     : "  ⚠ 没能建出示例玻璃（Glass 层创建失败？）\n") +
                  "现在去 Hierarchy 里把地形拖到视野中心，然后在地板上刷瓦片。");

        Selection.activeGameObject = tmGO;
        EditorGUIUtility.PingObject(tmGO);
    }

    // ------------------------------------------------------------
    /// <summary>
    /// 造一个空的玻璃 Tilemap：挂在 Glass 层，碰撞齐全，排序和墙一致。
    /// 光会穿过它（因为光照只查 Ground），但人是站得住的。
    /// </summary>
    static GameObject MakeGlassTilemap(GameObject gridGO, int glassLayer)
    {
        if (glassLayer < 0)
        {
            Debug.LogWarning("[CatTerrainTool] Glass 层创建失败，跳过玻璃地形");
            return null;
        }

        // 已经有同名的就复用，避免重复生成
        Transform existing = gridGO.transform.Find("Glass");
        if (existing != null)
        {
            Debug.Log("[CatTerrainTool] 已经有 Glass Tilemap，跳过");
            return existing.gameObject;
        }

        GameObject go = new GameObject("Glass");
        go.transform.SetParent(gridGO.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.layer = glassLayer;

        go.AddComponent<Tilemap>();

        TilemapRenderer tmr = go.AddComponent<TilemapRenderer>();
        tmr.sortingLayerName = TerrainSortingLayer;
        tmr.sortingOrder = TerrainSortingOrder;     // 必须和 Wall 一样，否则会盖住光扇

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;

        TilemapCollider2D tmc = go.AddComponent<TilemapCollider2D>();
        tmc.usedByComposite = true;

        CompositeCollider2D comp = go.AddComponent<CompositeCollider2D>();
        comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
        comp.generationType = CompositeCollider2D.GenerationType.Synchronous;

        Undo.RegisterCreatedObjectUndo(go, "创建玻璃地形");
        return go;
    }

    // ------------------------------------------------------------
    /// <summary>造一块示例玻璃：实心碰撞 + Glass 层 + 不受光照的蓝色材质</summary>
    static GameObject MakeDemoGlassPane(int groundLayer)
    {
        int glassLayer = EnsureLayer("Glass");
        if (glassLayer < 0) return null;

        GameObject go = new GameObject("GlassDemo");
        go.layer = glassLayer;

        // 放在房间内部、离地两格高的位置，横着一条
        go.transform.position = new Vector3(6f, 3.2f, 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        Sprite s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (s == null) s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");

        sr.sprite = s;
        sr.color = new Color(0.55f, 0.78f, 1.00f, 0.35f);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(5f, 0.2f);
        sr.sortingLayerName = TerrainSortingLayer;
        sr.sortingOrder = TerrainSortingOrder;      // 和地形同一层，光扇在它上面

        // 玻璃必须暗处也看得见，否则玩家不知道那儿有块能站的板子
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var m = new Material(sh);
            sr.sharedMaterial = m;
        }

        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(5f, 0.2f);           // 实心：站得上去

        var pane = go.AddComponent<GlassPane>();
        pane.passableInShadow = false;               // 影子也过不去？这块是"能站的板子"

        Undo.RegisterCreatedObjectUndo(go, "创建示例玻璃");

        return go;
    }

    // ------------------------------------------------------------
    [MenuItem(MenuRoot + "2. 场景体检并修复（z / 灯 / 地形 / 层 / 全局光）", false, 2)]
    public static void CheckAndFix()
    {
        int groundLayer = EnsureLayer("Ground");
        string report = "";
        int fixedCount = 0;

        // ---- ⓿ z 深度：最常见、最难查的一类坑 ----
        //   相机在 z = -10。任何 z <= -10 的东西都在相机【背后】：
        //     判定照常工作（光照、碰撞都是纯数学），但屏幕上永远看不到。
        //   症状就是"我明明在光里，却看不到光"，能查一整天。
        foreach (Transform tr in Object.FindObjectsOfType<Transform>())
        {
            if (tr.parent != null) continue;                     // 只看根物体
            if (tr.GetComponent<Camera>() != null) continue;     // 相机本身必须在 -10
            if (tr.name.Contains("Camera")) continue;
            if (tr.name.Contains("Global Light")) continue;      // 全局光的位置无所谓

            if (Mathf.Abs(tr.position.z) < 0.5f) continue;

            report += "\n· " + tr.name + "：z = " + tr.position.z.ToString("0.00") +
                      " → 归零（在相机背后的话画面看不见）";

            Vector3 p = tr.position;
            p.z = 0f;
            Undo.RecordObject(tr, "修正 z");
            tr.position = p;
            fixedCount++;
        }

        // ---- 灯的参数体检 ----
        foreach (LightZone zone in Object.FindObjectsOfType<LightZone>())
        {
            if (zone.radius < 1f)
                report += "\n· 灯「" + zone.name + "」的 radius = " + zone.radius +
                          "，太小了（正常 3~8），几乎照不到东西";

            if (zone.radius > 40f)
                report += "\n· 灯「" + zone.name + "」的 radius = " + zone.radius +
                          "，太大了（正常 3~8），会把整个房间照满，光暗对比会消失";
        }

        // ---- 地形 ----
        TilemapRenderer[] renderers = Object.FindObjectsOfType<TilemapRenderer>();

        foreach (TilemapRenderer tmr in renderers)
        {
            GameObject go = tmr.gameObject;
            string name = go.name;

            // ---- ① 排序层 / 顺序 ----
            bool badLayer = tmr.sortingLayerName != TerrainSortingLayer;
            bool badOrder = tmr.sortingOrder != TerrainSortingOrder;

            if (badLayer || badOrder)
            {
                report += "\n· " + name + "：排序层 " + tmr.sortingLayerName + " / 顺序 " + tmr.sortingOrder +
                          " → 改成 " + TerrainSortingLayer + " / " + TerrainSortingOrder;

                tmr.sortingLayerName = TerrainSortingLayer;
                tmr.sortingOrder = TerrainSortingOrder;
                fixedCount++;
            }

            // ---- ② 层 ----
            // 【只修"没设置过层"的地形】
            //   以前这里是无条件把每块地形都设成 Ground —— 那是错的：
            //   手工创建的【玻璃 Tilemap】在 Glass 层（光要穿过它），
            //   被这个工具一改，玻璃就变成墙，光立刻被挡住。
            //   所以：只有 Default(0) 层的才修，其余层一律不动。
            if (go.layer == 0 && groundLayer > 0)
            {
                report += "\n· " + name + "：层是 Default → 改成 Ground（不改的话光会穿墙）";
                go.layer = groundLayer;
                fixedCount++;
            }
            else if (groundLayer > 0 && go.layer != groundLayer)
            {
                report += "\n· " + name + "：在 " + LayerMask.LayerToName(go.layer) +
                          " 层 —— 保持不动（这一层不挡光，玻璃瓦片就该在这）";
            }

            // ---- ③ 碰撞体 ----
            TilemapCollider2D tmc = go.GetComponent<TilemapCollider2D>();
            CompositeCollider2D comp = go.GetComponent<CompositeCollider2D>();
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();

            if (tmc == null)
            {
                tmc = go.AddComponent<TilemapCollider2D>();
                report += "\n· " + name + "：缺 TilemapCollider2D → 补上了";
                fixedCount++;
            }

            if (!tmc.usedByComposite)
            {
                tmc.usedByComposite = true;
                report += "\n· " + name + "：没勾「由复合使用」→ 勾上（否则会一格一格卡脚）";
                fixedCount++;
            }

            if (comp == null)
            {
                if (rb == null)
                {
                    rb = go.AddComponent<Rigidbody2D>();
                    rb.bodyType = RigidbodyType2D.Static;
                }

                comp = go.AddComponent<CompositeCollider2D>();
                report += "\n· " + name + "：缺 CompositeCollider2D → 补上了";
                fixedCount++;
            }

            if (comp.geometryType != CompositeCollider2D.GeometryType.Polygons)
            {
                comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
                report += "\n· " + name + "：几何体类型不是 Polygons（Outlines 是空心的，地板会站不住）→ 改了";
                fixedCount++;
            }

            if (rb == null)
            {
                rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
                report += "\n· " + name + "：缺 Rigidbody2D → 补了个 Static 的（Composite 的前提）";
                fixedCount++;
            }
            else if (rb.bodyType != RigidbodyType2D.Static)
            {
                rb.bodyType = RigidbodyType2D.Static;
                report += "\n· " + name + "：Rigidbody 不是 Static → 改了";
                fixedCount++;
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // ---- ④ 顺便检查全局光 ----
        var globalLights = Object.FindObjectsOfType<UnityEngine.Rendering.Universal.Light2D>();
        bool hasGlobal = false;
        string lightReport = "";

        foreach (var l in globalLights)
        {
            if (l.lightType != UnityEngine.Rendering.Universal.Light2D.LightType.Global) continue;
            hasGlobal = true;

            if (Mathf.Abs(l.intensity - 0.06f) > 0.001f)
                lightReport += "\n· Global Light 2D 的强度是 " + l.intensity +
                               "（Room1 用 0.06）。太大房间会发灰，光暗对比会消失。";
        }

        if (!hasGlobal)
            lightReport += "\n· 场景里【没有】Global Light 2D —— 没被灯照到的地方会是纯黑。";

        if (fixedCount == 0 && report.Length == 0)
            report = "\n所有配置都是对的。";

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[CatTerrainTool] 场景体检结果：" + report + lightReport);

        EditorUtility.DisplayDialog("场景体检",
            (fixedCount > 0 ? "自动修了 " + fixedCount + " 处。\n" : "没有需要修的。\n") +
            "\n详情看 Console。",
            "好");
    }

    // ------------------------------------------------------------
    static Tile GetOrCreateTile()
    {
        Tile t = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
        if (t != null) return t;

        Sprite s = LoadWallSprite();

        if (s == null)
        {
            // 最后的兜底：用 Unity 内置的一张方块图
            s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        }

        if (s == null) return null;

        Directory.CreateDirectory(Path.GetDirectoryName(TilePath));

        t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = s;
        t.color = Color.white;
        AssetDatabase.CreateAsset(t, TilePath);
        AssetDatabase.SaveAssets();

        Debug.Log("[CatTerrainTool] 新建了瓦片资源：" + TilePath);
        return t;
    }

    static Sprite LoadWallSprite()
    {
        Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(WallSpritePath);
        if (s != null) return s;

        // 如果 wall1.png 不是按 Sprite 导入的，改成 Sprite 再读一次
        TextureImporter ti = AssetImporter.GetAtPath(WallSpritePath) as TextureImporter;
        if (ti == null) return null;

        if (ti.textureType != TextureImporterType.Sprite)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(WallSpritePath);
    }

    static int EnsureLayer(string name)
    {
        int index = LayerMask.NameToLayer(name);
        if (index >= 0) return index;

        // 找第一个空的自定义层
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tagManager.ApplyModifiedProperties();
                Debug.Log("[CatTerrainTool] 新建了 Layer：" + name);
                return i;
            }
        }

        Debug.LogWarning("[CatTerrainTool] 32 个 Layer 都满了，没法新建 " + name);
        return -1;
    }
}
