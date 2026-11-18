// ============================================================
// CatMareFontFix.cs —— 一键把中文字体接到所有界面
//
// 菜单：Tools → CatMare → 中文字体 → 1. 接到所有场景的所有界面
//                                   2. 检查谁还没接
//
// 【为什么需要它】
//   Unity 的内置字体（Arial 那个）里根本没有汉字。
//   任何用 GUIStyle 画中文的地方，只要 uiFont 是空的，
//   汉字就会【完全不画】——不是方块，是空白，甚至连"这里有个字"都看不出来。
//
//   需要接字体的地方有三处，而且分散在不同场景里：
//     GameHUD        左下角操作提示、顶部状态、横幅
//     MainMenu       主菜单按钮
//     TutorialSign   关卡里的教学文字
//   手点的话每个场景每一处都要拖一次，漏一个就有一处中文空白。
//
// 【这个脚本做的事】
//   遍历 Assets/Scenes 下所有场景，把这三个组件上的 uiFont
//   统一设成 Assets/Fonts/SimHei.ttf，然后保存。
//   跑完关掉重开游戏，中文就出来了。
//
// 【关于字体文件的授权（重要）】
//   SimHei.ttf 来自 Windows 系统字体，
//   本地开发和测试没问题，但【不能随游戏发布】——Windows 自带字体
//   没有授予再分发的权利。
//   发布前换成开源字体（思源黑体 / Noto Sans SC，OFL 协议），
//   文件名随便改，这个工具会优先找 Assets/Fonts 下任何一个 TTF/OTF。
// ============================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CatMareFontFix
{
    const string MenuRoot = "Tools/CatMare/中文字体/";
    const string FontDir = "Assets/Fonts";

    [MenuItem(MenuRoot + "1. 接到所有场景的所有界面", false, 1)]
    public static void AssignToAllScenes()
    {
        Font font = FindFont();
        if (font == null)
        {
            EditorUtility.DisplayDialog("找不到字体",
                "Assets/Fonts 里没有 TTF / OTF 字体文件。\n\n" +
                "把一个中文字体（比如思源黑体）拖进 Assets/Fonts 再跑这个菜单。", "好");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string currentPath = EditorSceneManager.GetActiveScene().path;

        List<string> scenes = CollectScenes();
        int touchedScenes = 0;
        int touchedComponents = 0;

        foreach (string path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            bool dirty = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // ---- GameHUD ----
                foreach (GameHUD g in root.GetComponentsInChildren<GameHUD>(true))
                {
                    if (g.uiFont != font) { g.uiFont = font; dirty = true; touchedComponents++; }
                }

                // ---- MainMenu ----
                foreach (MainMenu m in root.GetComponentsInChildren<MainMenu>(true))
                {
                    if (m.uiFont != font) { m.uiFont = font; dirty = true; touchedComponents++; }
                }

                // ---- TutorialSign ----
                foreach (TutorialSign t in root.GetComponentsInChildren<TutorialSign>(true))
                {
                    if (t.uiFont != font) { t.uiFont = font; dirty = true; touchedComponents++; }
                }
            }

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                touchedScenes++;
                Debug.Log("[CatMareFontFix] 已接字体：" + path);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 回到原来那个场景
        if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            EditorSceneManager.OpenScene(currentPath, OpenSceneMode.Single);

        string msg = "字体：" + font.name +
                     "\n改了 " + touchedScenes + " 个场景，" +
                     touchedComponents + " 处组件。\n\n" +
                     "按 Play 就能看到中文了。\n\n" +
                     "⚠ 这个字体如果来自 Windows 系统字体，只能本地用，" +
                     "发布前要换成开源字体（思源黑体 / Noto Sans SC）。";

        Debug.Log("[CatMareFontFix] " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog("接好了", msg, "好");
    }

    [MenuItem(MenuRoot + "2. 检查谁还没接（只报告，不改）", false, 2)]
    public static void Check()
    {
        List<string> scenes = CollectScenes();
        int missing = 0;

        foreach (string path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            string report = "";

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (GameHUD g in root.GetComponentsInChildren<GameHUD>(true))
                    if (g.uiFont == null) { report += "\n    GameHUD"; missing++; }

                foreach (MainMenu m in root.GetComponentsInChildren<MainMenu>(true))
                    if (m.uiFont == null) { report += "\n    MainMenu"; missing++; }

                foreach (TutorialSign t in root.GetComponentsInChildren<TutorialSign>(true))
                    if (t.uiFont == null) { report += "\n    TutorialSign"; missing++; }
            }

            if (report.Length > 0)
                Debug.LogWarning("[CatMareFontFix] 这些地方没接字体，中文会空白：" +
                                 "\n  " + path + report);

            EditorSceneManager.CloseScene(scene, true);
        }

        if (missing == 0) Debug.Log("[CatMareFontFix] 所有界面都接好字体了。");
        else Debug.LogWarning("[CatMareFontFix] 共 " + missing + " 处没接字体。");

        EditorUtility.DisplayDialog("检查完了",
            missing == 0
                ? "所有界面都接好字体了。"
                : "有 " + missing + " 处没接字体，详情看 Console。\n" +
                  "跑「1. 接到所有场景的所有界面」可以一次性修好。",
            "好");
    }

    // ------------------------------------------------------------
    static Font FindFont()
    {
        if (!AssetDatabase.IsValidFolder(FontDir)) return null;

        string[] guids = AssetDatabase.FindAssets("t:Font", new[] { FontDir });
        if (guids.Length == 0) return null;

        // 优先用拼音排序后的第一个（只有一个时就是它）
        Font best = null;
        foreach (string g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            Font f = AssetDatabase.LoadAssetAtPath<Font>(p);
            if (f == null) continue;

            if (best == null || string.CompareOrdinal(f.name, best.name) < 0)
                best = f;
        }

        return best;
    }

    static List<string> CollectScenes()
    {
        var result = new List<string>();

        // 先收集 Build Settings 里的（这些一定会进构建，最重要）
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (!string.IsNullOrEmpty(s.path) && File.Exists(s.path) && !result.Contains(s.path))
                result.Add(s.path);
        }

        // 再把 Assets/Scenes 下所有场景补上（可能有还没加进构建的测试场景）
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(p) && !result.Contains(p)) result.Add(p);
        }

        return result;
    }
}
