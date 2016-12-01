Shader "Hidden/SPHRender"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	float3 HUEtoRGB(in float H)
	{
		float R = abs(H * 6 - 3) - 1;
		float G = 2 - abs(H * 6 - 2);
		float B = 2 - abs(H * 6 - 4);
		return saturate(float3(R, G, B));
	}

	struct Particle
	{
		float2 position;
		float2 velocity;
	};

	struct v2g
	{
		float3 position : TEXCOORD0;
		float4 color : COLOR;
	};

	struct g2f
	{
		float4 position : POSITION;
		float2 texcoord : TEXCOORD0;
		float4 color : COLOR;
	};

	StructuredBuffer<Particle> _ParticlesBuffer;

	sampler2D _ParticleTexture;
	float4x4 _InvViewMatrix;
	float4 _DropTexture_ST;
	float _ParticleSize;

	static const float2 g_positions[4] =
	{
		float2(-1, 1),
		float2(1, 1),
		float2(-1,-1),
		float2(1,-1)
	};

	static const float2 g_texcoords[4] =
	{
		float2(0, 0),
		float2(1, 0),
		float2(0, 1),
		float2(1, 1),
	};

	v2g vert(uint id : SV_VertexID)
	{
		v2g o;
		o.position.xy = _ParticlesBuffer[id].position;
		o.position.z = _ParticleSize;
		o.color = float4(HUEtoRGB(smoothstep(0, 10, length(_ParticlesBuffer[id].velocity))), 1);
		return o;
	}

	[maxvertexcount(4)]
	void geom(point v2g In[1], inout TriangleStream<g2f> SpriteStream)
	{
		g2f o;
		[unroll]
		for (int i = 0; i < 4; i++)
		{
			float3 position = float3(g_positions[i], 0) * In[0].position.z * 1;
			position = mul(_InvViewMatrix, position) + float3(In[0].position.xy, 0);
			o.position = mul(UNITY_MATRIX_MVP, float4(position, 1.0));
			o.color = In[0].color;
			o.texcoord = g_texcoords[i];

			SpriteStream.Append(o);
		}

		SpriteStream.RestartStrip();
	}

	fixed4 frag(g2f i) : SV_Target
	{
		//return float4(1, 1, 1, 1);
		return tex2D(_ParticleTexture, i.texcoord.xy) * i.color;
	}

	ENDCG

	SubShader
	{
		Tags{ "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
			Zwrite Off
			Blend One One
			Cull Off

		Pass
		{
			CGPROGRAM
			#pragma target 5.0
			#pragma vertex vert
			#pragma geometry geom
			#pragma fragment frag
			ENDCG
		}
	}
}