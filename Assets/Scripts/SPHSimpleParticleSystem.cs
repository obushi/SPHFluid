using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SPHFluid
{
    public class SPHSimpleParticleSystem : MonoBehaviour
    {
        public struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
        }

        public struct ParticleForce
        {
            public Vector2 Acceleration;
        }

        public struct ParticleDensity
        {
            public float Density;
        }

        [SerializeField]
        ComputeShader SPHComputeShader;

        [SerializeField]
        Texture2D particleTexture;

        [SerializeField]
        RenderTexture backRenderTexture;

        [SerializeField]
        RenderTexture frontRenderTexture;

        [SerializeField]
        Shader particleRenderShader;

        ComputeBuffer SPHParticlesRead;

        ComputeBuffer SPHParticlesWrite;

        ComputeBuffer SPHParticlesForce;

        ComputeBuffer SPHParticlesDensity;

        Material particleMaterial;

        #region Parameters

        [SerializeField]
        float RestDensity = 1000.0f;        // 定常密度   [kg / m^3]

        [SerializeField]
        float PressureCoef = 200.0f;        // 圧力係数

        [SerializeField]
        float Mass = 0.0002f;               // 粒子の質量 [kg]

        [SerializeField]
        float EffectiveRadius = 0.012f;     // 有効半径   [m]

        [SerializeField]
        float TimeStep = 0.005f;            // 微小時間   [s]

        [SerializeField]
        float ViscosityCoef = 0.1f;         // 粘性係数   [Pa * s = 1 kg / (m * s)]

        [SerializeField]
        float ParticleRadius = 0.003f;      // レンダリング時の粒子の半径

        [SerializeField]
        float WallStiffness = 10000.0f;     // 境界条件の係数

        [SerializeField]
        float ParticleInitGap = 0.0045f;    // 初期化時の隙間

        [SerializeField]
        Vector2 Gravity = new Vector2(0, -9.8f);

        [SerializeField, Range(0.01f, 1.0f)]
        float contourMinThreshold = 0.5f;

        [SerializeField, Range(0.01f, 1.0f)]
        float contourMaxThreshold = 0.9f;

        Vector4 GridDim
        {
            get
            {
                return new Vector4(1 / EffectiveRadius, 1 / EffectiveRadius, 0, 0);
            }
        }

        [SerializeField]
        Vector2 MinBoundary = new Vector2(0.0f, 0.0f);

        [SerializeField]
        Vector2 MaxBoundary = new Vector2(3.0f, 3.0f);

        [SerializeField]
        Vector2 EmitPosition = new Vector2(0.0f, 0.0f);

        float[] WallNormals
        {
            get
            {
                return new[] {
                1f,   0,  -MinBoundary.x,  0,
                 0,  1f,  -MinBoundary.y,  0,
               -1f,   0,   MaxBoundary.x,  0,
                 0, -1f,   MaxBoundary.y,  0, };
            }
        }

        [SerializeField]
        int maxParticles = 32768;

        #endregion

        #region Coefficients of Kernel Function

        float Poly6Kernel { get { return 315.0f / (64.0f * Mathf.PI * Mathf.Pow(EffectiveRadius, 9.0f)); } }
        float SpikeyKernel { get { return -45.0f / (Mathf.PI * Mathf.Pow(EffectiveRadius, 6.0f)); } }
        float LapKernel { get { return 45.0f / (Mathf.PI * Mathf.Pow(EffectiveRadius, 6.0f)); } }

        [SerializeField]
        Camera metaballResultCamera;
        
        

        #endregion

        // Use this for initialization
        void Start()
        {
            particleMaterial = new Material(particleRenderShader);
            particleMaterial.hideFlags = HideFlags.HideAndDontSave;
            backRenderTexture = new RenderTexture(Screen.width, Screen.height, 0);
            frontRenderTexture = new RenderTexture(Screen.width, Screen.height, 0);

            InitializeComputeBuffers();
        }

        // Update is called once per frame
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
                Gravity = new Vector2(9.8f, 0);
            else if (Input.GetKeyDown(KeyCode.UpArrow))
                Gravity = new Vector2(0, 9.8f);
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
                Gravity = new Vector2(-9.8f, 0);
            else if (Input.GetKeyDown(KeyCode.DownArrow))
                Gravity = new Vector2(0, -9.8f);

            
        }

        void OnDrawGizmos()
        {
            Gizmos.DrawWireCube((MaxBoundary + MinBoundary) / 2, MaxBoundary - MinBoundary);
        }

        void OnRenderObject()
        {
            SetConstants();
            int kernelId;

            // Density
            kernelId = SPHComputeShader.FindKernel("Density");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesDensityWrite", SPHParticlesDensity);
            SPHComputeShader.Dispatch(kernelId, maxParticles / 32, 1, 1);

            // Force
            kernelId = SPHComputeShader.FindKernel("Force");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesDensityRead", SPHParticlesDensity);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesForceWrite", SPHParticlesForce);
            SPHComputeShader.Dispatch(kernelId, maxParticles / 32, 1, 1);

            // Integration
            kernelId = SPHComputeShader.FindKernel("Integrate");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesForceRead", SPHParticlesForce);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHParticlesWrite);
            SPHComputeShader.Dispatch(kernelId, maxParticles / 32, 1, 1);

            // Render
            
            particleMaterial.SetPass(0);
            particleMaterial.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);
            particleMaterial.SetTexture("_ParticleTexture", particleTexture);
            particleMaterial.SetFloat("_ParticleRadius", ParticleRadius);
            particleMaterial.SetBuffer("_ParticlesBuffer", SPHParticlesWrite);
            particleMaterial.SetBuffer("_ParticlesDensity", SPHParticlesDensity);

            particleMaterial.SetVector("_MaxBoundary", MaxBoundary);
            particleMaterial.SetVector("_MinBoundary", MinBoundary);

            Graphics.SetRenderTarget(backRenderTexture);
            GL.Clear(true, true, Color.black);
            Graphics.DrawProcedural(MeshTopology.Points, maxParticles);


            particleMaterial.SetPass(1);
            particleMaterial.SetFloat("_ContourMinThreshold", contourMinThreshold);
            particleMaterial.SetFloat("_ContourMaxThreshold", contourMaxThreshold);
            particleMaterial.SetFloat("_UVPosMaxY", Camera.main.WorldToViewportPoint(MaxBoundary).y);
            particleMaterial.SetFloat("_UVPosMinY", Camera.main.WorldToViewportPoint(MinBoundary).y);
            Graphics.Blit(backRenderTexture, null, particleMaterial, 1);

            Swap(ref SPHParticlesRead, ref SPHParticlesWrite);
        }

        static void Swap<T>(ref T lhs, ref T rhs)
        {
            T tmp = lhs;
            lhs = rhs;
            rhs = tmp;
        }

        void InitializeComputeBuffers()
        {
            Particle[] particles = new Particle[maxParticles];
            for (int i = 0; i < maxParticles; i++)
            {
                float x = EmitPosition.x + ParticleInitGap + ParticleInitGap * (i % 128);
                float y = EmitPosition.y + ParticleInitGap + ParticleInitGap * (i / 128);
                particles[i].Position = new Vector2(x, y);
            }

            ParticleForce[] particlesForce = new ParticleForce[maxParticles];
            ParticleDensity[] particlesDensity = new ParticleDensity[maxParticles];

            SPHParticlesRead = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(Particle)));
            SPHParticlesWrite = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(Particle)));
            SPHParticlesForce = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(ParticleForce)));
            SPHParticlesDensity = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(ParticleDensity)));

            SPHParticlesRead.SetData(particles);
            SPHParticlesWrite.SetData(particles);
            SPHParticlesForce.SetData(particlesForce);
            SPHParticlesDensity.SetData(particlesDensity);
        }

        void SetConstants()
        {
            SPHComputeShader.SetFloat("_RestDensity", RestDensity);
            SPHComputeShader.SetFloat("_PressureCoef", PressureCoef);
            SPHComputeShader.SetFloat("_Mass", Mass);
            SPHComputeShader.SetFloat("_EffectiveRadius", EffectiveRadius);
            SPHComputeShader.SetFloat("_TimeStep", TimeStep);
            SPHComputeShader.SetFloat("_Viscosity", ViscosityCoef);
            SPHComputeShader.SetFloat("_WallStiffness", WallStiffness);
            SPHComputeShader.SetFloat("_ParticleGap", ParticleInitGap);
            SPHComputeShader.SetVector("_Gravity", Gravity);
            SPHComputeShader.SetVector("_MinBoundary", MinBoundary);
            SPHComputeShader.SetVector("_MaxBoundary", MaxBoundary);
            SPHComputeShader.SetInt("_MaxParticles", maxParticles);
            SPHComputeShader.SetFloat("_Poly6Kernel", Poly6Kernel);
            SPHComputeShader.SetFloat("_SpikeyKernel", SpikeyKernel);
            SPHComputeShader.SetFloat("_LapKernel", LapKernel);
            SPHComputeShader.SetFloats("_WallNormals", WallNormals);

            Vector3 mousePos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, -Camera.main.transform.position.z);
            Vector3 worldMousePos = Camera.main.ScreenToWorldPoint(mousePos);
            worldMousePos.z = 3.0f;
            SPHComputeShader.SetVector("_MousePosition", worldMousePos);
        }

        void OnDisable()
        {
            SPHParticlesRead.Release();
            SPHParticlesWrite.Release();
            SPHParticlesForce.Release();
            SPHParticlesDensity.Release();
        }
    }
}