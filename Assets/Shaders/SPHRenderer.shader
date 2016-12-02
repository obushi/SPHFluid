Shader "Hidden/SPHRender"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	float3 HSVtoRGB(float3 HSV)
	{
		float3 RGB = 0;
		float C = HSV.z * HSV.y;
		float H = HSV.x * 6;
		float X = C * (1 - abs(fmod(H, 2) - 1));
		if (HSV.y != 0)
		{
			float I = floor(H);
			if (I == 0) { RGB = float3(C, X, 0); }
			else if (I == 1) { RGB = float3(X, C, 0); }
			else if (I == 2) { RGB = float3(0, C, X); }
			else if (I == 3) { RGB = float3(0, X, C); }
			else if (I == 4) { RGB = float3(X, 0, C); }
			else { RGB = float3(C, 0, X); }
		}
		float M = HSV.z - C;
		return RGB + M;
	}

	struct Particle
	{
		float2 position;
		float2 velocity;
	};

	struct ParticleDensity
	{
		float density;
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
	StructuredBuffer<ParticleDensity> _ParticlesDensity;

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
		//o.color = float4(HUEtoRGB(smoothstep(0, 10, length(_ParticlesBuffer[id].velocity))), 0.6);
		// 白 <-- 低  [ 彩度 ] 高 --> 青

		o.color = float4(HSVtoRGB(float3(0.8, smoothstep(2000, 1000, length(_ParticlesDensity[id].density)), 1)), 1.0);
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
		Tags{ "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
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