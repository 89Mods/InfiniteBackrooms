Shader "Tholin/BlackSky"
{
	Properties
	{
	}
	SubShader
	{
		Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
		Cull Off ZWrite Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct appdata_t {
					float4 vertex : POSITION;
					UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			struct v2f {
					float4 vertex : SV_POSITION;
					UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata_t v)
			{
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					o.vertex = UnityObjectToClipPos(v.vertex);
					return o;
			}

			half4 frag(v2f i) : SV_Target
			{
					return half4(0, 0, 0, 1);
			}
			ENDCG
		}
	}
}
