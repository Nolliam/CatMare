// ============================================================
// LightFanMax.shader —— 光扇专用：重叠时"取较亮者"，不叠加
//
// 【为什么不能用默认的精灵材质】
//   精灵材质是加色混合（src + dst）。两盏灯的光扇重叠时，
//   重叠区域 = 灯A + 灯B，比任何一盏都亮 —— 于是画面上出现
//   一块"更白的区域"，看起来像多了一个更强的光源。
//   但那只是两个普通灯光碰在一起，物理上不该更亮。
//
// 【BlendOp Max 就是答案】
//   逐通道取 max(源, 目标)。两盏灯重叠时结果仍然是一盏灯的亮度。
//   这也是真实相机拍两个同色温光源重叠区域的表现（过曝除外）。
//
// 【为什么要把 rgb 先乘 alpha】
//   Blend One One 会把源颜色原样参与运算，不看 alpha。
//   如果直接把 (1, 0.94, 0.7) 丢进去，不管 alpha 多小，
//   max 出来都是满亮度 —— 整片光扇会变成纯白。
//   所以先做 rgb *= a，让顶点透明度真正变成"亮度"。
//   顶点色 alpha 里已经包含 fanAlpha、外缘衰减、闪烁。
//
// 【为什么 Cull Off】
//   三角扇的绕序写反了就会整片消失，而且排查起来极其费时。
//   双面渲染对这个需求（平面光源）没有任何坏处。
// ============================================================

Shader "CatMare/LightFanMax"
{
    Properties
    {
        _Color ("Tint (alpha = 整体亮度)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend One One
        BlendOp Max
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = i.color;

                // 把透明度折进颜色：这样 alpha 才是"亮度"
                c.rgb *= c.a;
                c.a = 1;

                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
