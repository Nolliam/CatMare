// ============================================================
// CatFont.cs —— 自动找中文字体，谁都不用拖
//
// 【为什么要有这个文件】
//   Unity 的内置字体（Arial）里没有汉字。任何 GUIStyle 画中文的地方，
//   只要字体是空的，汉字就【完全不画】—— 不是方块，是空白。
//
//   以前的做法是"在 Inspector 里把字体拖到每个组件的 uiFont 上"。
//   问题是这种接线任务有三个坑：
//     · 三处要拖（GameHUD / MainMenu / TutorialSign），而且分散在不同场景
//     · 漏一处就有一处中文空白，而且没有任何报错
//     · 换字体时要再全拖一遍
//
//   所以改成"代码自己从 Resources 里找"：
//     字体放在 Assets/Resources/Fonts/ 下任何位置
//     → Resources.Load 一定能拿到
//     → 而且 Resources 里的东西【一定会打进构建包】，不用额外配置
//   组件上的 uiFont 字段保留：填了就优先使用该字体（比如某个界面要特殊字体）。
//
// 【换字体怎么做】
//   把新字体丢进 Assets/Resources/Fonts/，删掉旧的，什么都不用改。
//   想指定用哪一个：把它的路径写进 CatFont.PrimaryPath。
// ============================================================

using UnityEngine;

public static class CatFont
{
    /// <summary>
    /// 优先尝试的路径（相对 Resources，不带扩展名）。
    /// 找不到就自动扫 Resources/Fonts 下所有字体，取第一个。
    ///
    /// 【为什么换成 NotoSansSC】
    ///   原来用的是 SimHei（Windows 系统字体），微软的授权不允许再分发 ——
    ///   一旦把构建公开到 itch / GitHub，就等于分发了一个专有字体。
    ///   Noto Sans SC 是 OFL 协议，可以随便分发、商用、修改。
    ///
    ///   而且原始字体 16.9 MB（变量字体），已经按"游戏里实际用到的字"
    ///   子集化到 383 KB —— WebGL 首包也跟着小了很多。
    /// </summary>
    public const string PrimaryPath = "Fonts/NotoSansSC";

    static Font cached;
    static bool tried;

    /// <summary>拿到能显示中文的字体。找不到就返回 null（会退回内置字体）</summary>
    public static Font Get()
    {
        if (tried) return cached;
        tried = true;

        cached = Resources.Load<Font>(PrimaryPath);
        if (cached != null) return cached;

        // 没找到指定路径：扫一遍 Resources/Fonts
        Font[] all = Resources.LoadAll<Font>("Fonts");
        if (all != null && all.Length > 0)
        {
            cached = all[0];
            Debug.Log("[CatFont] 用 Resources/Fonts/" + cached.name + " 显示中文");
            return cached;
        }

        Debug.LogWarning("[CatFont] Resources/Fonts 里没有字体文件，中文会显示为空白。" +
                         "把一个中文 TTF 放进 Assets/Resources/Fonts/ 即可。");
        return null;
    }

    /// <summary>给一个样式套上字体（包括它的副本），传进来的字体优先</summary>
    public static void Apply(GUIStyle style, Font preferred)
    {
        if (style == null) return;

        Font f = (preferred != null) ? preferred : Get();
        if (f != null) style.font = f;
    }
}
