// ============================================================
// CatMareBuild.cs —— 构建 WebGL 并压成 itch.io 能直接上传的 zip
//
// 菜单：Tools → CatMare → Build → 1. 构建 WebGL 并打包
//                             2. 只打包（用已有的构建结果）
//                             3. 打开输出文件夹
//
// 【为什么值得写这个脚本】
//   手动发一次 itch 要做六件事：切平台、查设置、构建、本地起服务验证、
//   压 zip（而且 index.html 必须在最外层）、上传。
//   其中四件是完全机械的，只有"验证"和"写页面文字"需要人。
//   机器能做的别用手做 —— 尤其是"压 zip 时不要多套一层文件夹"这种
//   看起来无关紧要、但会导致 itch 上黑屏的细节。
//
// 【构建时自动检查的三个坑】
//   ① 压缩格式不是 Gzip / 解压回退没开  → itch 上黑屏，报 Unable to parse
//   ② 场景列表空 或者 MainMenu 不在第一位 → 构建出来是别的场景
//   ③ zip 里多套一层文件夹                → itch 找不到 index.html
//   这三条都属于"报错信息看不出原因"的问题，因此在脚本里做前置检查。
// ============================================================

using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CatMareBuild
{
    // 构建输出放在工程根目录下（不能放 Assets 里，否则 Unity 会把产物当资源导入）
    static string ProjectRoot
    {
        get { return Directory.GetParent(Application.dataPath).FullName; }
    }

    static string BuildDir { get { return Path.Combine(ProjectRoot, "Build/WebGL"); } }
    static string ZipPath { get { return Path.Combine(ProjectRoot, "Build/CatMare-WebGL.zip"); } }

    const string MenuRoot = "Tools/CatMare/Build/";

    // ------------------------------------------------------------
    [MenuItem(MenuRoot + "1. 构建 WebGL 并打包（发 itch 用这个）", false, 1)]
    public static void BuildAndZip()
    {
        if (!CheckPlatform()) return;
        if (!CheckScenes()) return;
        if (!CheckPublishSettings()) return;

        if (!Build()) return;

        MakeZip();
        Report();
    }

    // ------------------------------------------------------------
    // Windows 版：给"没梯子打不开 itch"的朋友用
    //
    // WebGL 版必须挂在网上才能玩，而 itch 在国内时好时坏。
    // 桌面版没有任何网络依赖：解压、双击 exe、就能玩。
    // 发网盘 / 微信 / QQ 都行，对方不需要装任何东西。
    // ------------------------------------------------------------
    [MenuItem(MenuRoot + "2. 构建 Windows 版并打包（发网盘用这个）", false, 2)]
    public static void BuildWindowsAndZip()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            bool go = EditorUtility.DisplayDialog("需要切到 Windows 平台",
                "当前平台是 " + EditorUserBuildSettings.activeBuildTarget + "。\n\n" +
                "构建 Windows 版需要切到 StandaloneWindows64（切换会重新导入资源，几分钟）。\n\n" +
                "现在就切吗？（切完之后再点一次这个菜单）",
                "切过去", "取消");

            if (go) EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

            return;
        }

        if (!CheckScenes()) return;

        string dir = Path.Combine(ProjectRoot, "Build/Windows");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        var list = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) list.Add(s.path);

        var opt = new BuildPlayerOptions
        {
            scenes = list.ToArray(),
            locationPathName = Path.Combine(dir, "CatMare.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opt);
        if (report.summary.result != BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("构建失败",
                report.summary.result + "  错误数：" + report.summary.totalErrors +
                "\n详情看 Console。", "好");
            return;
        }

        string zip = Path.Combine(ProjectRoot, "Build/CatMare-Windows.zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(dir, zip,
                                    System.IO.Compression.CompressionLevel.Optimal, false);

        EditorUtility.RevealInFinder(zip);

        EditorUtility.DisplayDialog("Windows 版好了",
            zip + "\n\n" +
            "大小约 " + (new FileInfo(zip).Length / 1024f / 1024f).ToString("0.0") + " MB\n\n" +
            "发这个 zip 给朋友：解压 → 双击 CatMare.exe → 就能玩。\n" +
            "不需要网络，不需要装任何东西。", "好");
    }

    // ------------------------------------------------------------
    [MenuItem(MenuRoot + "3. 只打包 WebGL（用已有的构建结果）", false, 3)]
    public static void ZipOnly()
    {
        if (!Directory.Exists(BuildDir))
        {
            EditorUtility.DisplayDialog("没有构建结果",
                "找不到：\n" + BuildDir + "\n\n先跑一次「1. 构建 WebGL 并打包」。", "好");
            return;
        }

        MakeZip();
        Report();
    }

    [MenuItem(MenuRoot + "4. 打开输出文件夹", false, 4)]
    public static void OpenFolder()
    {
        string dir = Directory.Exists(BuildDir) ? BuildDir : Path.Combine(ProjectRoot, "Build");
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    // ------------------------------------------------------------
    // 检查
    // ------------------------------------------------------------
    static bool CheckPlatform()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL) return true;

        EditorUtility.DisplayDialog("平台不对",
            "当前平台是 " + EditorUserBuildSettings.activeBuildTarget +
            "，WebGL 构建需要先切过去。\n\n" +
            "File → Build Settings → WebGL → Switch Platform\n" +
            "（切平台要重新导入资源，几分钟）", "好");
        return false;
    }

    static bool CheckScenes()
    {
        var scenes = EditorBuildSettings.scenes;
        int enabled = 0;
        for (int i = 0; i < scenes.Length; i++)
            if (scenes[i].enabled) enabled++;

        if (enabled == 0)
        {
            EditorUtility.DisplayDialog("场景列表是空的",
                "File → Build Settings → Scenes In Build 里一个场景都没有。\n\n" +
                "至少要把 MainMenu 和你的关卡加进去，顺序是：\n" +
                "  [0] MainMenu\n  [1] 关卡", "好");
            return false;
        }

        // MainMenu 不在第一位只是警告，不拦
        string first = Path.GetFileNameWithoutExtension(scenes[0].path);
        if (scenes[0].enabled && first != "MainMenu")
        {
            bool go = EditorUtility.DisplayDialog("第一个场景不是 MainMenu",
                "现在排第一的是：" + first + "\n\n" +
                "游戏启动时会加载它。确定要继续吗？",
                "继续构建", "先停下");
            if (!go) return false;
        }

        return true;
    }

    static bool CheckPublishSettings()
    {
        bool isGzip = PlayerSettings.WebGL.compressionFormat == WebGLCompressionFormat.Gzip;
        bool hasFallback = PlayerSettings.WebGL.decompressionFallback;

        if (isGzip && hasFallback) return true;

        string problems = "";
        if (!isGzip) problems += "  · 压缩格式不是 Gzip（现在是 " + PlayerSettings.WebGL.compressionFormat + "）\n";
        if (!hasFallback) problems += "  · 解压缩回退没打开\n";

        bool fix = EditorUtility.DisplayDialog("两个设置会让 itch 上黑屏",
            problems + "\n症状是：页面打开一片黑，Console 报 `Unable to parse ...`。\n\n" +
            "要我自动改成 Gzip + 打开解压回退吗？",
            "自动修好并继续", "取消");

        if (!fix) return false;

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        AssetDatabase.SaveAssets();

        Debug.Log("[CatMareBuild] 已设置：Compression = Gzip，Decompression Fallback = 开");
        return true;
    }

    // ------------------------------------------------------------
    // 构建
    // ------------------------------------------------------------
    static bool Build()
    {
        if (Directory.Exists(BuildDir)) Directory.Delete(BuildDir, true);
        Directory.CreateDirectory(BuildDir);

        var list = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) list.Add(s.path);

        Debug.Log("[CatMareBuild] 开始构建 WebGL：" + list.Count + " 个场景 → " + BuildDir);

        var opt = new BuildPlayerOptions
        {
            scenes = list.ToArray(),
            locationPathName = BuildDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opt);
        BuildSummary sum = report.summary;

        if (sum.result != BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("构建失败",
                "结果：" + sum.result + "\n错误数：" + sum.totalErrors +
                "\n\n详细错误看 Console。", "好");
            return false;
        }

        Debug.Log("[CatMareBuild] 构建成功，用时 " + sum.totalTime +
                  "，大小 " + (sum.totalSize / 1024f / 1024f).ToString("0.0") + " MB");
        return true;
    }

    // ------------------------------------------------------------
    // 打包
    // ------------------------------------------------------------
    static void MakeZip()
    {
        // 先确认 index.html 在最外层 —— 这是 itch 能不能跑的唯一硬条件
        string index = Path.Combine(BuildDir, "index.html");
        if (!File.Exists(index))
        {
            Debug.LogError("[CatMareBuild] 构建输出里没有 index.html，zip 打出来 itch 也跑不了。");
            EditorUtility.DisplayDialog("找不到 index.html",
                "构建输出里没有 index.html。\n\n" +
                "常见原因：Template 设置不对，或者构建其实没成功。", "好");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ZipPath));

        if (File.Exists(ZipPath)) File.Delete(ZipPath);

        // includeBaseDirectory = false：
        //   zip 里直接是 index.html / Build / TemplateData，
        //   而不是 "WebGL/index.html" 多一层。itch 对多一层的容错时好时坏，别赌。
        // 注意 CompressionLevel 要写全名：UnityEngine 里也有一个同名的枚举
        ZipFile.CreateFromDirectory(BuildDir, ZipPath,
                                    System.IO.Compression.CompressionLevel.Optimal, false);

        Debug.Log("[CatMareBuild] 打包完成：" + ZipPath);
    }

    static void Report()
    {
        long size = 0;
        try { if (File.Exists(ZipPath)) size = new FileInfo(ZipPath).Length; } catch { }

        EditorUtility.RevealInFinder(ZipPath);

        EditorUtility.DisplayDialog("好了",
            "上传用的压缩包：\n" + ZipPath + "\n\n" +
            "大小约 " + (size / 1024f / 1024f).ToString("0.0") + " MB\n\n" +
            "上传步骤：\n" +
            "1. itch.io → Create new project → Kind of project 选 HTML\n" +
            "2. Uploads 里传这个 zip\n" +
            "3. ★ 勾上「This file will be played in the browser」\n" +
            "4. Embed 尺寸填 1920 x 1080，勾 Fullscreen button\n" +
            "5. 先存成 Draft 试玩，没问题再 Public",
            "好");
    }
}
