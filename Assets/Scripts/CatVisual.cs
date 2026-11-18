// ============================================================
// CatVisual.cs —— 黑猫的外观 + 形态过渡 + 形变 + 粒子
//
// 【为什么要分三层：VisualRoot / Body / Eyes】
//   Cat 根节点上有 Rigidbody2D 和两个碰撞体。如果直接在根节点上做旋转、
//   缩放，碰撞体也会跟着转、跟着缩 —— 物理就乱了。
//   所以真正的形变作用在一个子物体 VisualRoot 上，碰撞体不受影响。
//
// 【"动态"的感觉怎么来 —— 三个叠加】
//   ① 挤压拉伸（squash & stretch）
//        下落快 → 竖着拉长；落地 → 瞬间压扁再弹回
//        横向跑动 → 轻微向后倾
//      这一条贡献了 70% 的"活物感"，而且只有十几行代码。
//
//   ② 过渡（不是瞬间切换）
//        身体透明度用 0.22 秒渐变，不是一帧之内变过去。
//        因为有光→暗的判定是瞬时的，视觉上必须有缓冲，否则很刺眼。
//
//   ③ 粒子
//        融入/脱离时喷一圈小方块，向外散开并淡出。
//        为什么自己写而不用 ParticleSystem：
//        URP 下 ParticleSystem 的默认材质可能渲染成粉色，要额外配材质。
//        这个小粒子系统只有 12 个方块，用 Sprite 池更稳、更可控。
// ============================================================

using UnityEngine;

public class CatVisual : MonoBehaviour
{
    [Header("美术素材（留空则用程序生成的占位猫）")]
    [Tooltip("身体 + 腿。注意：不要包含眼睛，眼睛是独立一层")]
    public Sprite bodySprite;
    [Tooltip("耳朵 + 头。同样不要包含眼睛")]
    public Sprite headSprite;
    [Tooltip("眼睛底色（黄色眼珠），【不含瞳孔】。两只眼都画在同一张 18×24 里")]
    public Sprite eyeSprite;

    [Tooltip("【暗处】的瞳孔：圆。只有瞳孔，其余透明，同一张 18×24 画布")]
    public Sprite pupilSpriteRound;

    [Tooltip("【光里】的瞳孔：细竖。只有瞳孔，其余透明")]
    public Sprite pupilSpriteSlit;

    [Tooltip("瞳孔跟着移动方向偏移的最大距离（世界单位）。太大瞳孔会跑到眼白外面")]
    public float pupilLookMax = 0.05f;

    [Tooltip("每单位速度产生多少偏移，配合上面的上限")]
    public float pupilLookPerSpeed = 0.015f;

    [Tooltip("如果你的眼睛图里【两只眼都画了】，勾上这个。勾上后只用一张，位置自动居中")]
    public bool eyeSpriteIsBothEyes = false;

    [Header("猫形态颜色")]
    [Tooltip("身体和头的颜色。不吃光照，所以这就是它在屏幕上的实际亮度；太黑会看不见自己")]
    public Color bodyColor = new Color(0.10f, 0.10f, 0.14f, 1f);
    public Color eyeColor = new Color(1f, 0.86f, 0.18f, 1f);

    [Header("暗影形态")]
    [Tooltip("影形态下身体的透明度（0 = 完全看不见）")]
    [Range(0f, 1f)] public float shadowBodyAlpha = 0f;

    [Tooltip("影形态下头部的透明度。头必须留下来：不然浮动时只剩两点眼睛，没有参照物，看起来在闪")]
    [Range(0f, 1f)] public float shadowHeadAlpha = 0.92f;

    [Tooltip("影形态下头部的颜色。要比背景亮，否则黑头在黑暗里等于没有")]
    public Color shadowHeadColor = new Color(0.14f, 0.15f, 0.22f, 1f);

    [Header("暗影晕（身体外面那圈黑）")]
    [Tooltip("猫周围那圈偏紫的暗色。影形态下它长大，把猫和周围的黑暗连成一片")]
    public Color haloColor = new Color(0.04f, 0.02f, 0.08f, 1f);
    public float haloSizeX = 3.2f;
    public float haloSizeY = 3.8f;
    [Tooltip("猫形态下的晕（很淡，只做一圈暗边）")]
    [Range(0f, 1f)] public float haloAlphaIdle = 0.10f;
    [Tooltip("影形态下的晕。这是「猫和黑暗连起来」的关键数字")]
    [Range(0f, 1f)] public float haloAlphaShadow = 0.85f;

    [Header("过渡")]
    [Tooltip("形态切换用多久（秒）。0 = 瞬间，会非常刺眼")]
    public float transitionDuration = 0.22f;

    [Header("尺寸")]
    [Tooltip("必须和 Cat 上的猫碰撞体一致（现在是 1.10 × 1.50）")]
    public float bodyWidth = 1.10f;
    public float bodyHeight = 1.50f;
    public float eyeWidth = 0.20f;
    public float eyeHeight = 0.13f;
    public float eyeGap = 0.26f;
    public float eyeUp = 0.42f;
    public float eyeForward = 0.14f;

    [Header("形变（动态感的主要来源）")]
    [Tooltip("下落速度换算成竖向拉伸的比例")]
    public float stretchPerFallSpeed = 0.028f;
    [Tooltip("最大拉伸量")]
    [Range(0f, 0.6f)] public float maxStretch = 0.35f;

    [Tooltip("跑动时的剪切量（每单位速度）。这就是「前倾」，用剪切而不是旋转")]
    public float shearPerSpeed = 0.06f;
    [Tooltip("最大剪切量。0.35 已经很明显，别超过 0.5")]
    [Range(0f, 0.8f)] public float maxShear = 0.28f;

    [Tooltip("旋转倾斜。像素完美下建议保持 0——旋转会把像素转糊，前倾请用剪切")]
    public float leanPerSpeed = 1.2f;
    [Tooltip("最大倾斜角度。默认 0 = 关闭旋转，改用剪切")]
    public float maxLean = 0f;
    [Tooltip("落地压扁后回弹的弹性")]
    public float squashSpring = 14f;

    [Header("粒子")]
    public int particleCount = 12;
    public float particleLife = 0.45f;
    public float particleSpeed = 2.2f;
    public float particleSize = 0.11f;
    [Tooltip("粒子偏向运动反方向的强度（0=四散，1=完全逆着运动方向拖尾）")]
    [Range(0f, 1f)] public float particleTrailBias = 0.8f;

    [Header("像素颗粒感（默认关闭）")]
    [Tooltip("每世界单位多少像素。和你的瓦片尺寸保持一致，一般是 16")]
    public float pixelsPerUnit = 16f;
    [Tooltip("把视觉位置吸附到像素网格。要顺滑就关掉；要像素绝对干净才开——两者不可兼得")]
    public bool snapToPixelGrid = false;
    [Tooltip("把旋转量化成固定档位。前倾请用剪切，不要用旋转")]
    public bool snapRotation = false;
    [Tooltip("旋转量化的档数（12 = 每 30 度一档）")]
    public int rotationSteps = 12;

    /// <summary>当前身体透明度。尾巴要读它，才能在暗态时一起消失</summary>
    public float BodyAlpha { get { return bodyAlpha; } }

    Transform visualRoot;
    Transform particleRoot;
    SpriteRenderer body;
    SpriteRenderer head;
    SpriteRenderer halo;
    SpriteRenderer eyeL;
    SpriteRenderer eyeR;
    SpriteRenderer pupilRound;     // 暗处：圆瞳（挂在 eyeR 下面）
    SpriteRenderer pupilSlit;      // 光里：竖瞳
    Sprite whiteSprite;
    Material unlitMat;
    Material shearMat;             // 身体和头专用（带剪切）
    float shear;                   // 当前剪切量（平滑过渡用）

    bool isShadow;
    bool isDead;
    float facing = 1f;

    float bodyAlpha = 1f;          // 当前身体透明度
    float headAlpha = 1f;          // 当前头部透明度
    float eyeDim = 1f;             // 眼睛亮度倍率。1 = 正常，0 = 已经完全融进黑暗（走丢）
    float baseScaleX = 1f;         // 身体/头的基准缩放（程序猫要拉成 1.10×1.50，你自己的图不用拉）
    float baseScaleY = 1f;
    float eyeBaseW = 1f;           // 眼睛的基准尺寸
    float eyeBaseH = 1f;
    float pupilLookX;              // 瞳孔当前偏移（平滑用）
    float pupilLookY;    float formT;                   // 0 = 猫形态，1 = 影形态（一个数驱动所有过渡，保证同步）
    float squash;                  // 当前压扁量（弹簧）
    float squashVel;

    Rigidbody2D rb;
    bool wasGrounded = true;
    float lastFallSpeed;

    // ---- 自制粒子池 ----
    SpriteRenderer[] particles;
    Vector2[] particleVel;
    float[] particleAge;
    int particleCursor;

    void Awake()
    {
        whiteSprite = MakeWhiteSprite();
        rb = GetComponent<Rigidbody2D>();

        // VisualRoot：所有形变作用在它身上，不影响碰撞体
        GameObject root = new GameObject("VisualRoot");
        root.transform.SetParent(transform, false);
        visualRoot = root.transform;

        // 【为什么猫要拆成三块】
        //   身体 / 头 / 眼睛 分开画，才能各自做透明度：
        //     猫形态：三块都在
        //     影形态：身体淡出（0）、头留着、眼睛永远全亮
        //   只用一个精灵的话，"头留着但身体消失"根本做不到。
        //   眼睛还必须单独一层并且【不吃光照】—— 否则环境光只有 0.06 的时候，
        //   那双黄眼睛会跟场景一起被压暗，而它应该是全屏唯一确定亮着的东西。
        body = MakeSprite("Body", bodyColor, 0, visualRoot);
        body.sprite = (bodySprite != null) ? bodySprite : MakeCatSprite(false);

        head = MakeSprite("Head", bodyColor, 0, visualRoot);
        head.sprite = (headSprite != null) ? headSprite : MakeCatSprite(true);

        // 【为什么要区分"程序生成的猫"和"美术图"】
        //   程序生成的猫是 16×16，正好 1 个世界单位，所以要被拉成 1.10×1.50。
        //   而美术图尺寸由素材决定（18×24 像素 @ PPU16 = 1.125×1.5）。
        //   如果代码再拉一次，它就会变成两倍大，而且像素被拉歪。
        //   所以：用自己的图 = 不缩放，素材本身多大就是多大。
        if (bodySprite != null)
        {
            baseScaleX = 1f;
            baseScaleY = 1f;
        }
        else
        {
            baseScaleX = bodyWidth;
            baseScaleY = bodyHeight;
        }

        body.transform.localScale = new Vector3(baseScaleX, baseScaleY, 1f);
        head.transform.localScale = new Vector3(baseScaleX, baseScaleY, 1f);

        eyeL = MakeSprite("EyeL", eyeColor, 1, visualRoot);
        eyeR = MakeSprite("EyeR", eyeColor, 1, visualRoot);

        if (eyeSprite != null)
        {
            eyeL.sprite = eyeSprite;
            eyeR.sprite = eyeSprite;
            eyeBaseW = 1f;          // 尺寸由素材决定
            eyeBaseH = 1f;
        }
        else
        {
            eyeBaseW = eyeWidth;
            eyeBaseH = eyeHeight;
        }

        // ---- 瞳孔：独立两层 ----
        // 【为什么瞳孔必须从眼图里分出来】
        //   因为它需要"随移动方向偏移"。如果瞳孔烧在眼图里，
        //   想移动瞳孔就得移动整张眼睛 —— 那是眼球在动，看起来像翻白眼。
        //   分成独立一层之后，瞳孔可以在眼白内部自由滑动。
        //
        // 【为什么是两张图（圆 / 竖）而不是把圆压窄】
        //   压窄会让像素边缘重采样成糊的斜边。像素画的瞳孔必须"换图"。
        //
        // 【为什么挂成 eyeR 的子物体】
        //   自动继承位置、缩放、朝向，一行布局代码都不用写，
        //   以后改眼睛大小两张瞳孔永远同步。
        pupilRound = MakePupil("PupilRound", pupilSpriteRound, 2);
        pupilSlit = MakePupil("PupilSlit", pupilSpriteSlit, 3);

        // 身体和头换上有剪切的材质（眼睛和晕不需要，它们太小/太软看不出来）
        shearMat = MakeShearMaterial();
        if (shearMat != null)
        {
            body.sharedMaterial = shearMat;
            head.sharedMaterial = shearMat;   // 共享一份：两者必须同步剪切，不然会错开
        }

        // 暗影晕：画在光扇之上、身体之下（order -3），中间实、边缘软。
        // 它的作用不是"好看"，是【让猫的边界和黑暗的边界变成同一条线】：
        // 影形态下这团黑长大，把猫的轮廓吃掉，玩家看到的不再是"一个形状在黑暗里"，
        // 而是"黑暗里有一块更深的地方，那里有两只眼睛"。
        halo = MakeSprite("ShadowHalo", haloColor, -3, visualRoot);
        halo.sprite = MakeHaloSprite(64);
        halo.transform.localScale = new Vector3(haloSizeX, haloSizeY, 1f);

        BuildParticles();
        LayoutEyes();
    }

    Sprite MakeWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.filterMode = FilterMode.Point;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    // ------------------------------------------------------------
    // 程序生成一只 16×16 的像素猫（占位美术）
    //
    // 【为什么要真的生成一张像素图，而不是用一个方块】
    //   像素吸附、挤压拉伸、"颗粒感"这些东西，只有在精灵【本身有像素结构】时
    //   才看得出来。一个纯色方块吸附到网格，和丝滑移动在视觉上没区别。
    //
    //   这张图只用 '#' 表示猫身，其余是透明。眼睛仍然是独立的 Sprite，
    //   这样眼睛还能单独呼吸、单独放大。
    //
    //   后续接入真美术时，把该方法改成加载对应 Sprite 即可，其他代码不用动。
    // ------------------------------------------------------------
    /// <summary>
    /// 程序生成像素猫。headPart = true 只生成头（耳朵 + 头），false 只生成身体和腿。
    /// 两块用同一个画布尺寸和同一个轴心，所以天然对齐，不需要算偏移。
    /// </summary>
    Sprite MakeCatSprite(bool headPart)
    {
        string[] pattern = new string[]
        {
            "................",
            "...##......##...",
            "..####....####..",
            "..############..",
            "..############..",
            "..############..",
            "..############..",
            "..############..",
            "...##########...",
            "...##########...",
            "...##########...",
            "...##########...",
            "...##......##...",
            "...##......##...",
            "................",
            "................",
        };

        int h = pattern.Length;
        int w = pattern[0].Length;

        // 第 8 行是分界：上面是耳朵和头，下面是身体和腿
        const int headSplitRow = 8;

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        for (int row = 0; row < h; row++)
        {
            // 贴图的 y 轴朝上，图案的第 0 行是最上面 → 要翻转
            int ty = h - 1 - row;
            string line = pattern[row];

            for (int x = 0; x < w; x++)
            {
                bool solid = x < line.Length && line[x] == '#';

                if (headPart && row >= headSplitRow) solid = false;
                if (!headPart && row < headSplitRow) solid = false;

                tex.SetPixel(x, ty, solid ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        }
        tex.Apply();

        // pixelsPerUnit = 16 → 这张 16px 的图正好 1 个世界单位
        Sprite s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    Sprite MakeHaloSprite(int size)
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

                // 连续衰减：从中心到边缘一直变淡，没有任何"实心中间那块"。
                // 之前用的是 1 - SmoothStep(0.30, 1, d)，d<0.3 全是满不透明 ——
                // 那会在中间形成一块实心圆饼，边缘看得见一圈硬边，
                // 看起来是"一个黑圆"，不是"晕染"。
                float a = Mathf.Clamp01(1f - d);
                a = a * a;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    /// <summary>生成一层瞳孔，挂成 eyeR 的子物体（自动继承位置/缩放/朝向）</summary>
    SpriteRenderer MakePupil(string name, Sprite s, int order)
    {
        if (s == null || eyeR == null) return null;

        GameObject go = new GameObject(name);
        go.transform.SetParent(eyeR.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingOrder = order;

        Material m = MakeUnlitMaterial();
        if (m != null) sr.sharedMaterial = m;

        return sr;
    }

    /// <summary>按权重设置一层瞳孔的显隐和透明度（weight 由 formT 派生）</summary>
    void SetPupil(SpriteRenderer sr, float weight, Color tint)
    {
        if (sr == null) return;

        sr.enabled = weight > 0.02f;
        if (!sr.enabled) return;

        sr.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(tint.a * weight));
    }

    Material MakeShearMaterial()
    {
        // 先 Resources.Load：只用 Shader.Find 引用的着色器，打包时会被剥离，
        // 编辑器里能前倾、导出网页就不前倾了。
        Shader sh = Resources.Load<Shader>("Shaders/CatSpriteShear");
        if (sh == null) sh = Shader.Find("CatMare/SpriteShear");

        if (sh == null)
        {
            Debug.LogWarning("[CatVisual] 找不到 CatMare/SpriteShear，前倾剪切不会生效");
            return null;
        }

        Material m = new Material(sh);
        m.name = "CatShear";
        m.hideFlags = HideFlags.HideAndDontSave;
        return m;
    }

    /// <summary>
    /// 猫身上所有部件都用【不受光照影响】的材质。
    /// 为什么：场景环境光只有 0.06，正常精灵会被一起压暗，
    /// 连黄眼睛都会变成暗黄色。而这只猫的关键识别点就是"黑暗里两点黄眼睛"，
    /// 它必须是屏幕上唯一确定亮着的东西。
    /// </summary>
    Material MakeUnlitMaterial()
    {
        if (unlitMat != null) return unlitMat;

        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) return null;

        unlitMat = new Material(sh);
        unlitMat.name = "CatUnlit";
        unlitMat.hideFlags = HideFlags.HideAndDontSave;
        return unlitMat;
    }

    SpriteRenderer MakeSprite(string name, Color c, int order, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = whiteSprite;
        sr.color = c;
        sr.sortingOrder = order;

        Material m = MakeUnlitMaterial();
        if (m != null) sr.sharedMaterial = m;      // 不吃光照，猫永远是自己那个亮度

        return sr;
    }

    void BuildParticles()
    {
        // 粒子放在一个独立容器里，不挂在猫身上。
        // 为什么：挂在猫身上 = 粒子跟着猫走，看起来像粘在身上的碎屑，
        // 而不是"散落在空间里的烟"。所以要世界空间。
        GameObject container = new GameObject("CatParticles");
        particleRoot = container.transform;

        particles = new SpriteRenderer[Mathf.Max(4, particleCount)];
        particleVel = new Vector2[particles.Length];
        particleAge = new float[particles.Length];

        for (int i = 0; i < particles.Length; i++)
        {
            SpriteRenderer sr = MakeSprite("P" + i, new Color(0.15f, 0.15f, 0.2f, 0f), 5, particleRoot);
            sr.enabled = false;
            particles[i] = sr;
            particleAge[i] = 999f;
        }
    }

    void LayoutEyes()
    {
        if (eyeL == null) return;

        // 【使用美术眼睛图时，代码不能再加偏移】
        //   cateyes 是 18×24 的整画布，眼睛已经画在正确位置上了。
        //   如果代码再按 eyeUp / eyeForward 把它推一下，眼睛就会飘到头外面去
        //   —— 这会造成"眼睛位置不对"的观感。
        //   所以：用自己的图 = 位置归零、尺寸不缩、只画一张。
        bool custom = (eyeSprite != null);

        float gap = custom ? 0f : eyeGap * 0.5f;

        Vector3 center = custom
            ? Vector3.zero
            : new Vector3(facing * eyeForward, eyeUp, 0f);

        eyeL.transform.localPosition = center + new Vector3(-gap, 0f, 0f);
        eyeR.transform.localPosition = center + new Vector3(gap, 0f, 0f);

        // 【注意】这里不再设置 scale。
        // 之前是在这里直接把眼睛缩放到 1.35 倍 —— 那是"切换瞬间跳过去"，
        // 因为形态判定是瞬时的，而这里的缩放也是瞬时的，所以看起来一崩。
        // 现在尺寸统一由 Update 里跟着 formT 平滑过渡，这里只管位置。

        // 用自己的图时只用一张（图里两只眼都画好了）
        eyeL.enabled = !custom;
    }

    void Update()
    {
        if (body == null) return;

        float dt = Time.deltaTime;

        // ---- 朝向 ----
        if (rb != null && Mathf.Abs(rb.velocity.x) > 0.1f)
        {
            float nf = Mathf.Sign(rb.velocity.x);
            if (!Mathf.Approximately(nf, facing))
            {
                facing = nf;
                LayoutEyes();
            }
        }

        // ---- 形态过渡 ----
        // 【为什么用一个 formT 驱动所有变化】
        //   身体淡出、头部淡化、头部变色，如果各写一套缓动，
        //   三段动画会以不同速度跑完，切换过程中看起来是"错位的"。
        //   全部由同一个 formT 派生，就永远不会错位。
        float step = (transitionDuration <= 0.001f) ? 1f : dt / transitionDuration;
        formT = Mathf.MoveTowards(formT, isShadow ? 1f : 0f, step);

        // 死亡时整体压暗（只留眼睛），比"整只变灰"更像被黑暗收走
        float deadMul = isDead ? 0.12f : 1f;

        bodyAlpha = Mathf.Lerp(1f, shadowBodyAlpha, formT) * deadMul;
        headAlpha = Mathf.Lerp(1f, shadowHeadAlpha, formT) * deadMul;

        if (body != null)
        {
            body.color = new Color(bodyColor.r, bodyColor.g, bodyColor.b, bodyAlpha);
            body.enabled = bodyAlpha > 0.01f;
        }

        if (head != null)
        {
            // 影形态下头不是"消失"，而是换成一个比背景亮的冷色剪影。
            // 【头不再跟着眼睛一起变淡】
            // 之前头也乘 eyeDim，结果是：在黑暗里的整整 9 秒，
            // 从头到尾都是一个半透明的鬼影 —— 那看起来像 bug，不像计时。
            // 计时器只该由眼睛一个东西承担：眼睛是唯一传达"还剩多少"的元素。
            Color hc = Color.Lerp(bodyColor, shadowHeadColor, formT);
            head.color = new Color(hc.r, hc.g, hc.b, headAlpha);
            head.enabled = headAlpha > 0.01f;
        }

        if (halo != null)
        {
            // 影形态下长大，把猫的轮廓和周围的黑暗糊成一片
            float a = Mathf.Lerp(haloAlphaIdle, haloAlphaShadow, formT) * deadMul;
            halo.color = new Color(haloColor.r, haloColor.g, haloColor.b, a);
        }

        // ---- 挤压拉伸 ----
        if (rb != null)
        {
            bool grounded = Mathf.Abs(rb.velocity.y) < 0.01f;

            // 落地瞬间给一个压扁冲量
            if (grounded && !wasGrounded)
            {
                squashVel = -Mathf.Abs(lastFallSpeed) * 0.03f;
            }
            wasGrounded = grounded;
            lastFallSpeed = rb.velocity.y;

            // 弹簧回到 0
            squashVel += -squash * squashSpring * dt;
            squashVel *= Mathf.Clamp01(1f - 8f * dt);
            squash += squashVel * dt;

            // 下落时的竖向拉伸
            float fallStretch = Mathf.Clamp(-rb.velocity.y * stretchPerFallSpeed, 0f, maxStretch);

            float sx = 1f - fallStretch * 0.5f + squash * 0.8f;
            float sy = 1f + fallStretch + squash * 1.2f;

            body.transform.localScale = new Vector3(baseScaleX * sx, baseScaleY * sy, 1f);
            if (head != null)
                head.transform.localScale = new Vector3(baseScaleX * sx, baseScaleY * sy, 1f);

            // 倾斜
            float lean = Mathf.Clamp(-rb.velocity.x * leanPerSpeed, -maxLean, maxLean);
            if (Mathf.Abs(lean) < 0.01f) lean = 0f;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, lean);

            // ---- 前倾：用剪切，不用旋转 ----
            // 往右跑 → 上半身往右探出去 → 轮廓从"等腰"变成"直角"。
            // 用指数插值而不是 Lerp，是为了不受帧率影响。
            float wantShear = Mathf.Clamp(rb.velocity.x * shearPerSpeed, -maxShear, maxShear);
            shear = Mathf.Lerp(shear, wantShear, 1f - Mathf.Exp(-14f * dt));

            if (shearMat != null) shearMat.SetFloat("_Shear", shear);
        }

        // ---- 眼睛 ----
        // 颜色每帧都写一遍：死亡时压暗，活着时永远回到 eyeColor。
        // 再乘一个 eyeDim：黑暗待太久，它会被 ShadowTimer 一点点调到 0 ——
        // 那是"猫融进黑暗、走丢了"的唯一视觉预兆，必须看得见。
        if (eyeL != null && eyeR != null)
        {
            float dim = Mathf.Clamp01(eyeDim);

            Color ec = isDead
                ? new Color(eyeColor.r * 0.3f, eyeColor.g * 0.3f, eyeColor.b * 0.3f, 1f)
                : eyeColor;

            ec = new Color(ec.r * dim, ec.g * dim, ec.b * dim, dim);

            // ---- 瞳孔：光里竖、暗里圆 ----
            // 真猫就是这样：强光下竖瞳减少进光，暗处圆瞳放大。
            // 所以这条规则玩家不用学，看一眼就懂。
            eyeL.color = ec;
            eyeR.color = ec;

            float darkW = formT;          // 暗处（圆瞳）
            float lightW = 1f - formT;    // 光里（竖瞳）

            SetPupil(pupilRound, darkW, ec);
            SetPupil(pupilSlit, lightW, ec);
        }

        // ---- 瞳孔跟着移动方向偏移 ----
        // 【为什么瞳孔要会动】
        //   静态的眼睛是"画上去的"，会动的眼睛是"在看"。
        //   猫盯住一个方向时，瞳孔会朝那边偏一点 —— 这一下就有生命了。
        //   而且它能免费告诉玩家"我正在往哪走"，尤其是在黑到看不见身体的时候。
        if (rb != null && (pupilRound != null || pupilSlit != null))
        {
            float tx = Mathf.Clamp(rb.velocity.x * pupilLookPerSpeed,
                                   -pupilLookMax, pupilLookMax);

            // 上下只给一半幅度：横着看是"注视"，竖着大幅偏会像翻白眼
            float ty = Mathf.Clamp(rb.velocity.y * pupilLookPerSpeed * 0.5f,
                                   -pupilLookMax * 0.5f, pupilLookMax * 0.5f);

            float k = 1f - Mathf.Exp(-14f * dt);
            pupilLookX = Mathf.Lerp(pupilLookX, tx, k);
            pupilLookY = Mathf.Lerp(pupilLookY, ty, k);

            Vector3 pp = new Vector3(pupilLookX, pupilLookY, 0f);
            if (pupilRound != null) pupilRound.transform.localPosition = pp;
            if (pupilSlit != null) pupilSlit.transform.localPosition = pp;
        }

        // ---- 眼睛尺寸（全项目唯一一处写眼睛 scale 的地方） ----
        // 影形态放大必须跟着 formT 平滑走。
        // 之前是在 LayoutEyes 里直接给 1.35 —— 形态判定是瞬时的，
        // 缩放也是瞬时的，所以那一下看起来是"崩"而不是"长"。
        // 影形态不再放大眼睛（之前是 1.35 倍，看起来像"崩了一下"）。
        // 影子状态靠"身体消失、只剩眼睛"来读，不靠放大。
        if (eyeL != null && eyeR != null)
        {
            float grow = 1f;

            // 呼吸只在猫形态、活着的时候有
            float pulse = (!isShadow && !isDead) ? 1f + Mathf.Sin(Time.time * 3f) * 0.05f : 1f;

            float w = eyeBaseW * grow * pulse;
            float h = eyeBaseH * grow * pulse;

            eyeL.transform.localScale = new Vector3(w, h, 1f);
            eyeR.transform.localScale = new Vector3(w, h, 1f);
        }

        ApplyPixelSnap();
        UpdateParticles(dt);
    }

    // ------------------------------------------------------------
    // 像素吸附：这是"沙砾感"的来源
    //
    // 物理位置是连续的（3.472 这种小数），如果视觉也跟着连续移动，
    // 精灵会在两个像素之间不断重采样 —— 看起来是"平滑"，不是像素。
    //
    // 做法：把【视觉】的世界位置四舍五入到 1/pixelsPerUnit 的网格上，
    // 再换回 localPosition（因为 VisualRoot 的父节点是物理对象，位置随时在动）。
    //
    // 只动视觉，不动物理 —— 否则物理模拟会被量化，手感变糟。
    // ------------------------------------------------------------
    void ApplyPixelSnap()
    {
        if (visualRoot == null) return;

        if (snapToPixelGrid && pixelsPerUnit > 0.01f)
        {
            float unit = 1f / pixelsPerUnit;
            Vector3 world = transform.position;

            Vector3 snapped = new Vector3(
                Mathf.Round(world.x / unit) * unit,
                Mathf.Round(world.y / unit) * unit,
                world.z);

            // 父节点可能本身有旋转/缩放（这里父节点就是 Cat 根节点，通常是 identity）
            visualRoot.localPosition = transform.InverseTransformPoint(snapped);
        }
        else
        {
            visualRoot.localPosition = Vector3.zero;
        }

        if (snapRotation && rotationSteps > 0)
        {
            float stepDeg = 360f / rotationSteps;
            Vector3 e = visualRoot.localEulerAngles;
            float z = Mathf.Round(Mathf.DeltaAngle(0f, e.z) / stepDeg) * stepDeg;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, z);
        }
    }

    // ------------------------------------------------------------
    void UpdateParticles(float dt)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            if (particleAge[i] >= particleLife) continue;

            particleAge[i] += dt;
            float t = Mathf.Clamp01(particleAge[i] / particleLife);

            // 世界空间移动
            particles[i].transform.position += (Vector3)(particleVel[i] * dt);
            particleVel[i] *= Mathf.Clamp01(1f - 3f * dt);

            float a = (1f - t) * 0.8f;
            particles[i].color = new Color(0.15f, 0.15f, 0.2f, a);
            particles[i].transform.localScale = Vector3.one * particleSize * (1f - t * 0.5f);

            if (t >= 1f) particles[i].enabled = false;
        }
    }

    /// <summary>
    /// 喷粒子。inward = true 时向内收（变回猫），false 时向外散（融入黑暗）。
    /// 【逆着运动方向】—— 猫往右跑，粒子往左甩，形成拖尾感。
    /// </summary>
    void EmitParticles(int count, bool inward)
    {
        Vector2 bias = Vector2.zero;
        if (rb != null && rb.velocity.sqrMagnitude > 0.04f)
        {
            bias = -rb.velocity.normalized;      // 和运动方向相反
        }

        Vector3 origin = transform.position;

        for (int k = 0; k < count; k++)
        {
            int i = particleCursor;
            particleCursor = (particleCursor + 1) % particles.Length;

            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector2 rnd = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 dir = (rnd + bias * (particleTrailBias * 1.5f)).normalized;

            particles[i].enabled = true;
            particles[i].transform.position = origin + (Vector3)(dir * 0.22f);

            float speed = (inward ? -particleSpeed * 0.6f : particleSpeed) * Random.Range(0.6f, 1.25f);
            particleVel[i] = dir * speed;

            particleAge[i] = 0f;
        }
    }

    // ------------------------------------------------------------
    public void SetShadow(bool shadow)
    {
        if (isShadow == shadow) return;
        isShadow = shadow;

        LayoutEyes();

        // 融入黑暗：粒子向外散；变回猫：粒子向内收（像被吸回去）
        EmitParticles(Mathf.Max(4, particles.Length / 2), !shadow);
    }

    /// <summary>
    /// 眼睛亮度倍率。1 = 正常，0 = 完全融进黑暗。
    /// 由 ShadowTimer 每帧驱动：在黑暗里待得越久，这两点黄越淡，
    /// 直到消失 —— 那就是"走丢了"。玩家唯一的预兆就是它。
    /// </summary>
    public void SetEyeDim(float dim)
    {
        eyeDim = Mathf.Clamp01(dim);
    }

    public void SetDead(bool dead)
    {
        // 颜色和透明度都由 Update 每帧根据 isDead 重算，
        // 这里只需要记下状态。（之前在这里改颜色，下一帧就被覆盖了，
        // 等于那句代码从来没生效过。）
        isDead = dead;
    }
}
