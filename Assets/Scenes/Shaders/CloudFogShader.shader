Shader "Custom/CloudFogShader"
{
    Properties
    {
        _MainTex ("Cloud Texture (Fog1)", 2D) = "white" {}
        _DetailTex ("Detail Texture (Fog2)", 2D) = "white" {}
        _MaskTex ("Current Visibility Mask", 2D) = "white" {}
        _ExploredTex ("Explored Mask", 2D) = "black" {}
        _CloudColor ("Cloud Color", Color) = (0.9, 0.9, 0.9, 1)
        _ShroudColor ("Shroud Color", Color) = (0.2, 0.2, 0.2, 0.5)
        _Speed ("Speed", Float) = 0.05
        _Scale ("Scale", Float) = 15.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            sampler2D _MainTex;
            sampler2D _DetailTex;
            sampler2D _MaskTex;
            sampler2D _ExploredTex;
            fixed4 _CloudColor;
            fixed4 _ShroudColor;
            float _Speed;
            float _Scale;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample exploration / visibility masks (naturally smooth due to bilinear filtering)
                float visible = tex2D(_MaskTex, i.uv).r;
                float explored = tex2D(_ExploredTex, i.uv).r;
                
                // Calculate scrolling UVs for natural cloud movements
                float2 uvMain = i.uv * _Scale + float2(_Time.y * _Speed, _Time.y * _Speed * 0.4);
                float2 uvDetail = i.uv * _Scale * 1.6 + float2(-_Time.y * _Speed * 0.7, _Time.y * _Speed * 0.5);
                
                // Sample both fog textures
                fixed4 colMain = tex2D(_MainTex, uvMain);
                fixed4 colDetail = tex2D(_DetailTex, uvDetail);
                
                // Combine textures for rich, multi-layered cloud density (multiply and amplify)
                float density = colMain.r * colDetail.r * 2.0;
                density = saturate(density);
                
                // Unexplored areas must be completely opaque (alpha = 1.0) so the player cannot see through the fog!
                // We modulate the color slightly based on density to show gorgeous dynamic cloud shapes.
                float unexploredAlpha = lerp(0.98, 1.0, density); // Opaque base
                fixed4 unexploredCol = fixed4(_CloudColor.rgb * (0.85 + 0.15 * density), unexploredAlpha);
                
                // Explored-but-invisible shroud: einmal aufgedeckte Gebiete bleiben
                // dauerhaft aufgedeckt — KEINE animierten Wolken mehr darueber (die
                // rollende Wolkendecke sah aus wie frisch zurueckkommender Nebel).
                // Stattdessen ein deutlich grauer, statischer Schleier: man erkennt
                // den Bereich klar, aber alles darin wirkt "ausgegraut/erinnert".
                float shroudAlpha = 0.5f;
                fixed4 shroudCol = fixed4(0.5, 0.53, 0.58, shroudAlpha);
                
                // Smooth interpolation between unexplored clouds and explored shroud
                fixed4 baseCloud = lerp(unexploredCol, shroudCol, saturate(explored));
                
                // Smoothly fade out fog to transparent inside the visible sight range
                fixed4 finalCol = lerp(baseCloud, fixed4(0, 0, 0, 0), saturate(visible));
                
                return finalCol;
            }
            ENDCG
        }
    }
}
