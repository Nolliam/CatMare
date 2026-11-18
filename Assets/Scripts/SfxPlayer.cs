// ============================================================
// SfxPlayer.cs —— 全局音效播放器（一个池子，8 个声道）
//
// 挂在哪：场景里随便一个空物体，叫 Sfx 就行（每个场景放一个）
//
// 【为什么要有"池子"，而不是每次 new 一个 AudioSource】
//   音效是高频的：跳、落地、开关、死亡……每帧都可能触发。
//   每次 new GameObject + AddComponent + Destroy，会产生大量 GC，
//   在跑动时表现为周期性卡顿（尤其手机）。
//   固定几个声道轮流用（round-robin），是音效最省的标准做法。
//
// 【为什么留空也不报错】
//   音频素材可能尚未提供。如果代码里写"必须有 clip"，
//   那会先卡在空引用上，而不是先听到声音。
//   所以所有 Play 方法在 clip 为 null 时【静默返回】——
//   等素材拖进 Inspector，声音就会自动出现，一行代码都不用改。
//   这叫"把内容延迟到数据层"，代码先跑通。
//
// 【为什么音高要随机一点点】
//   同一个音效连续播放会听起来像机关枪。
//   每次随机 ±5% 的音高，玩家的耳朵就不会注意到"这是同一个文件"。
// ============================================================

using UnityEngine;

public class SfxPlayer : MonoBehaviour
{
    public static SfxPlayer Instance;

    [Header("音效素材（留空 = 静音，不报错）")]
    public AudioClip jump;
    public AudioClip land;
    public AudioClip toShadow;
    public AudioClip toCat;
    public AudioClip die;
    public AudioClip respawn;
    public AudioClip lightOn;
    public AudioClip lightOff;
    public AudioClip checkpoint;
    public AudioClip win;
    [Tooltip("按下互动键时的一声轻响")]
    public AudioClip click;

    [Header("混音")]
    [Range(0f, 1f)] public float masterVolume = 0.85f;
    [Tooltip("每次播放的音高随机范围，避免重复音听起来机械")]
    [Range(0f, 0.3f)] public float pitchJitter = 0.05f;
    [Tooltip("同时能响几个音。跳跃+落地+开关叠在一起也够用")]
    public int voices = 8;

    AudioSource[] pool;
    int next;

    /// <summary>
    /// 自动装音效：Inspector 里没拖的槽，按【字段名】去 Resources/Audio/Sfx 里找。
    ///
    /// 【为什么做这件事】
    ///   这个游戏有 8 个场景，每个场景里都有一个 Sfx 物体。
    ///   手动拖 = 8 × 11 = 88 次拖拽，以后加音效还要再来一遍。
    ///   约定"文件名 = 字段名"之后，只把 wav 放进一个文件夹，所有场景一起生效。
    ///
    ///   字段名：jump / land / toShadow / toCat / die / respawn /
    ///           lightOn / lightOff / checkpoint / win / click
    ///
    ///   Inspector 里手动拖的优先 —— 没拖的才走这里。
    /// </summary>
    void AutoLoadClips()
    {
        var fields = typeof(SfxPlayer).GetFields(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance);

        int loaded = 0;
        int skipped = 0;
        string missingNames = "";

        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.FieldType != typeof(AudioClip)) continue;
            if (f.GetValue(this) != null) { skipped++; continue; }      // 手动配过就不动

            // 大小写两个都试一遍。
            // Resources.Load 的路径区分大小写，而 Windows 资源管理器不区分 ——
            // 于是"文件夹叫 sfx、代码写 Sfx"这种事在编辑器里看毫无异常，
            // 却一个都加载不到，而且不报错。两个都试，这类坑就绝了。
            AudioClip c = Resources.Load<AudioClip>("Audio/Sfx/" + f.Name);
            if (c == null) c = Resources.Load<AudioClip>("Audio/sfx/" + f.Name);
            if (c == null) c = Resources.Load<AudioClip>("Sfx/" + f.Name);

            if (c != null)
            {
                f.SetValue(this, c);
                loaded++;
            }
            else
            {
                missingNames += (missingNames.Length > 0 ? ", " : "") + f.Name;
            }
        }

        // 无论成功失败都打一行 —— 静默失败是排查音效时最费时间的事
        AudioClip[] found = Resources.LoadAll<AudioClip>("Audio/Sfx");

        string fieldNames = "";
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].FieldType != typeof(AudioClip)) continue;
            fieldNames += (fieldNames.Length > 0 ? "," : "") + fields[i].Name;
        }

        Debug.Log("[SfxPlayer] 装载 " + loaded +
                  "  |  跳过(已有值)=" + skipped +
                  "  |  字段数=" + (fieldNames.Length > 0 ? fieldNames.Split(',').Length : 0) +
                  "  |  Resources 里的 clip 数=" + found.Length +
                  "  |  jump: " + (jump == null ? "null"
                      : ("长度=" + jump.length.ToString("0.00") + "s  状态=" + jump.loadState)) +
                  "  |  masterVolume=" + masterVolume +
                  (missingNames.Length > 0 ? "  |  找不到: " + missingNames : ""));
    }

    /// <summary>
    /// 按 F9 直接播一次 jump。
    ///
    /// 【为什么要这个】
    ///   "没声音"有两个完全不同的原因：
    ///     ① clip 没装 / 是空的   → 日志里的 长度/状态 会露出来
    ///     ② 输出被静音（编辑器 Mute Audio / 系统音量合成器）→ 这里能播但听不见
    ///   按一下 F9 就能把这两类分开。
    /// </summary>
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9)) return;

        Debug.Log("[SfxPlayer] F9 测试：jump=" +
                  (jump == null ? "null（没有 clip，当然没声音）" : jump.name) +
                  "  pool=" + (pool != null ? pool.Length : 0) +
                  "  masterVolume=" + masterVolume +
                  "  输出采样率=" + AudioSettings.outputSampleRate);

        PlayJump();
    }

    void Awake()
    {
        AutoLoadClips();

        Instance = this;

        int n = Mathf.Max(2, voices);
        pool = new AudioSource[n];

        for (int i = 0; i < n; i++)
        {
            GameObject go = new GameObject("Voice" + i);
            go.transform.SetParent(transform, false);

            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;        // 纯 2D，不衰减
            src.bypassEffects = true;
            src.bypassListenerEffects = true;
            src.ignoreListenerPause = true;
            src.ignoreListenerVolume = false;

            pool[i] = src;
        }

        ValidateClips();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>启动时检查一遍素材配置，只警告不报错</summary>
    void ValidateClips()
    {
        int missing = 0;
        if (jump == null) missing++;
        if (land == null) missing++;
        if (toShadow == null) missing++;
        if (toCat == null) missing++;
        if (die == null) missing++;
        if (respawn == null) missing++;
        if (lightOn == null || lightOff == null) missing++;
        if (checkpoint == null) missing++;
        if (win == null) missing++;

        if (missing > 0)
            Debug.Log("[SfxPlayer] 还有 " + missing + " 组音效没配，现在是静音状态（不影响运行）");
    }

    void PlayClip(AudioClip clip, float volumeScale)
    {
        if (clip == null || pool == null || pool.Length == 0) return;

        AudioSource src = pool[next];
        next = (next + 1) % pool.Length;

        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.volume = Mathf.Clamp01(masterVolume * Mathf.Max(0f, volumeScale));
        src.clip = clip;
        src.Play();
    }

    // ------------------------------------------------------------
    // 给别的脚本调用的静态入口。
    // 场景里没有 SfxPlayer 时全部静默返回，不会空引用。
    // ------------------------------------------------------------
    public static void PlayJump()
    {
        if (Instance != null) Instance.PlayClip(Instance.jump, 1f);
    }

    public static void PlayLand(float fallSpeed)
    {
        if (Instance == null) return;
        // 掉得越狠越响，轻轻落地就不响
        float v = Mathf.InverseLerp(6f, 26f, Mathf.Abs(fallSpeed));
        if (v <= 0.01f) return;
        Instance.PlayClip(Instance.land, 0.35f + v * 0.65f);
    }

    public static void PlayToShadow()
    {
        if (Instance != null) Instance.PlayClip(Instance.toShadow, 1f);
    }

    public static void PlayToCat()
    {
        if (Instance != null) Instance.PlayClip(Instance.toCat, 1f);
    }

    public static void PlayDie()
    {
        if (Instance != null) Instance.PlayClip(Instance.die, 1f);
    }

    public static void PlayRespawn()
    {
        if (Instance != null) Instance.PlayClip(Instance.respawn, 1f);
    }

    public static void PlayLight(bool on)
    {
        if (Instance != null) Instance.PlayClip(on ? Instance.lightOn : Instance.lightOff, 1f);
    }

    public static void PlayCheckpoint()
    {
        if (Instance != null) Instance.PlayClip(Instance.checkpoint, 1f);
    }

    public static void PlayWin()
    {
        if (Instance != null) Instance.PlayClip(Instance.win, 1f);
    }

    public static void PlayClick()
    {
        if (Instance != null) Instance.PlayClip(Instance.click, 0.5f);
    }
}
