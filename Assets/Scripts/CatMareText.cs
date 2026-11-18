// ============================================================
// CatMareText.cs —— 所有给玩家看的字，全在这一个文件里
//
// 【为什么要单独一个文件】
//   台词会改二十遍。如果它们散在 8 个脚本里，
//   每次改一句台词都要先找它在哪个文件、哪一行，
//   改到后面很容易放弃维护。
//   集中在一个文件里，改台词就变成"只打开这一个文件"。
//
// 【怎么改】
//   直接改下面这些字符串。改完 Unity 自动重新编译，Play 就生效。
//
// 【想给某个东西单独写一句怎么办】
//   Checkpoint / LevelExit / ShadowTimer 上的 message 字段：
//   留空 = 用这里的默认台词；
//   填了 = 使用该句（覆盖默认）。
//   这样既能统一改，也能给关卡的某一处写特例。
//
// 【写台词的三个原则（这个游戏尤其）
//   1. 不要解释规则。玩家看得见"我过不去"，不需要人告诉他。
//   2. 不要替玩家激动。"太棒了！"这种句子会把气氛毁掉。
//   3. 短。屏幕上同时出现超过 12 个字，就没人读完了。
// ============================================================

public static class CatMareText
{
    // ---------------- 互动提示（屏幕下方） ----------------
    // 格式：第一个 {0} 是按键，第二个 {0} 是互动物的名字
    public const string PromptFormat = "[{0}]  {1}";

    public const string LightOn = "开灯";
    public const string LightOff = "关灯";
    public const string InteractDefault = "互动";

    // ---------------- 操作提示（左下角） ----------------
    public const string HintBar = "A / D  移动    空格  跳 / 上浮    S  下沉    E  互动    {0}  重来";
    public const string ShadowHint = "影形态：推不动开关，也碰不到陷阱";

    // ---------------- 顶部状态 ----------------
    public const string EyeFading = "我chovy你要没了";

    // ---------------- 横幅 ----------------
    public const string CheckpointSaved = "你存档了";
    public const string LostInDark = "哪没路躲";
    public const string LampFell = "灯掉下来了";
    public const string ExitReached = "OHHHHHHHHHH";

    // 终点横幅下面那行"死了几次"。{0} = 次数
    public const string ExitDeathCount = "死了 {0} 次";
}
