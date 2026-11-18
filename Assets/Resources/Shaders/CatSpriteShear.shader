// ============================================================
// CatSpriteShear.shader —— 精灵 + 剪切（斜着跑）
//
// 【为什么用"剪切"而不是"旋转"来做前倾】
//   旋转是把整个矩形转过去，横线变成斜线 —— 像素行不再水平，
//   每个像素被重采样成糊的噪点。像素游戏里最忌讳这个。
//
//   剪切是把每一行整体往左/右平移，行仍然是水平的。
//   于是像素网格还在，看起来只是"上半身探出去了一点"。
//   这就是"等腰三角形变成直角三角形"在代码里的真身：
//   它其实是平行四边形，但斜率一大，视觉上就是那个意思。
//
// 【代价，先说明白】
//   剪切仍然是线性变换，纹理被斜着采样。
//   用 Point (no filter) 的时候不会糊，而是变成一级一级的阶梯 ——
//   这恰好是像素画里手画前倾时本来的样子，所以可以接受。
//   如果你把角度调得很大，阶梯会很明显，那就把 maxShear 调小。
//
// 【为什么 _MainTex 要加 [PerRendererData]】
//   SpriteRenderer 每帧会把这个贴图和 UV 通过 MaterialPropertyBlock 塞进来。
//   加了 [PerRendererData] 之后材质面板里不显示这一项，
//   而且多个精灵可以共用同一个材质 —— 所以猫的身体和头能共享一份。
// ============================================================

Shader "CatMare/SpriteShear"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Shear ("Shear", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _Shear;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float4 p = v.vertex;

                // 剪切：越靠上，横向偏移越大。
                // p.y 在精灵本地空间里是 -0.5 ~ 0.5（1 单位的精灵）
                p.x += p.y * _Shear;

                o.pos = UnityObjectToClipPos(p);
                o.uv = v.uv;
                o.color = v.color * _Color;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
