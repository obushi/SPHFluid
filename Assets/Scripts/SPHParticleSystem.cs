using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SPHFluid
{
    public class SPHParticleSystem : MonoBehaviour
    {
        public struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Vector2 Acceleration;
            public float Density;
        }

        [SerializeField]
        ComputeShader SPHComputeShader;

        [SerializeField]
        Texture2D particleTexture;

        [SerializeField]
        Shader particleRenderShader;

        ComputeBuffer SPHParticlesRead;
        ComputeBuffer SPHParticlesWrite;

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

        [SerializeField]
        Vector2 MinBoundary = new Vector2(0.0f, 0.0f);

        [SerializeField]
        Vector2 MaxBoundary = new Vector2(3.0f, 3.0f);

        [SerializeField]
        Vector2 MinInitBoundary = new Vector2(0.0f, 0.0f);

        [SerializeField]
        Vector2 MaxInitBoundary = new Vector2(0.5f, 1.0f);

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

        int maxParticles;

        #endregion

        #region Coefficients of Kernel Function

        float Poly6Kernel { get { return 315.0f / (64.0f * Mathf.PI * Mathf.Pow(EffectiveRadius, 9.0f)); } }
        float SpikeyKernel { get { return -45.0f / (Mathf.PI * Mathf.Pow(EffectiveRadius, 6.0f)); } }
        float LapKernel { get { return 45.0f / (Mathf.PI * Mathf.Pow(EffectiveRadius, 6.0f)); } }

        #endregion

        // Use this for initialization
        void Start()
        {
            particleMaterial = new Material(particleRenderShader);
            particleMaterial.hideFlags = HideFlags.HideAndDontSave;

            var particles = InitializeParticles();
            maxParticles = particles.Length;

            SPHParticlesRead = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(Particle)));
            SPHParticlesWrite = new ComputeBuffer(maxParticles, Marshal.SizeOf(typeof(Particle)));

            SPHParticlesRead.SetData(particles);
            SPHParticlesWrite.SetData(particles);
        }

        // Update is called once per frame
        void Update()
        {

        }

        void OnDrawGizmos()
        {
            Gizmos.DrawWireCube((MaxBoundary + MinBoundary) / 2, MaxBoundary - MinBoundary);
        }

        void OnRenderObject()
        {
            Setants();
            int kernelId;

            // Density
            kernelId= SPHComputeShader.FindKernel("Density");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHParticlesWrite);
            SPHComputeShader.Dispatch(kernelId, Mathf.CeilToInt(maxParticles / 32) + 1, 1, 1);

            // Force
            kernelId = SPHComputeShader.FindKernel("Force");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHParticlesWrite);
            SPHComputeShader.Dispatch(kernelId, Mathf.CeilToInt(maxParticles / 32) + 1, 1, 1);

            // Integration
            kernelId = SPHComputeShader.FindKernel("Integrate");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHParticlesWrite);
            SPHComputeShader.Dispatch(kernelId, Mathf.CeilToInt(maxParticles / 32) + 1, 1, 1);

            // Render
            particleMaterial.SetPass(0);
            particleMaterial.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);
            particleMaterial.SetTexture("_ParticleTexture", particleTexture);
            particleMaterial.SetFloat("_ParticleSize", ParticleRadius);
            particleMaterial.SetBuffer("_ParticleBuffer", SPHParticlesWrite);
            Graphics.DrawProcedural(MeshTopology.Points, maxParticles);

            var particles = new Particle[maxParticles];
            SPHParticlesWrite.GetData(particles);

            Swap(ref SPHParticlesRead, ref SPHParticlesWrite);
        }

        static void Swap<T> (ref T lhs, ref T rhs)
        {
            T tmp = lhs;
            lhs = rhs;
            rhs = tmp;
        }

        Particle[] InitializeParticles()
        {
            List<Particle> particles = new List<Particle>();
            float d = ParticleInitGap;
            for (float y = MinInitBoundary.y + d; y <= MaxInitBoundary.y - d; y += d)
            {
                for (float x = MinInitBoundary.x + d; x <= MaxInitBoundary.x - d; x += d)
                {
                    Particle p = new Particle();
                    p.Position = new Vector2(x, y);
                    p.Velocity = Vector2.zero;
                    p.Acceleration = Vector2.zero;
                    p.Density = 0.0f;
                    particles.Add(p);
                }
            }
            return particles.ToArray();
        }

        void Setants()
        {
            SPHComputeShader.SetFloat("_RestDensity",     RestDensity);
            SPHComputeShader.SetFloat("_PressureCoef",    PressureCoef);
            SPHComputeShader.SetFloat("_Mass",            Mass);
            SPHComputeShader.SetFloat("_EffectiveRadius", EffectiveRadius);
            SPHComputeShader.SetFloat("_TimeStep",        TimeStep);
            SPHComputeShader.SetFloat("_Viscosity",       ViscosityCoef);
            SPHComputeShader.SetFloat("_WallStiffness",   WallStiffness);
            SPHComputeShader.SetFloat("_ParticleGap",     ParticleInitGap);
            SPHComputeShader.SetVector("_Gravity",        Gravity);
            SPHComputeShader.SetVector("_MinBoundary",    MinBoundary);
            SPHComputeShader.SetVector("_MaxBoundary",    MaxBoundary);
            SPHComputeShader.SetInt("_MaxParticles",      maxParticles);
            SPHComputeShader.SetFloat("_Poly6Kernel",     Poly6Kernel);
            SPHComputeShader.SetFloat("_SpikeyKernel",    SpikeyKernel);
            SPHComputeShader.SetFloat("_LapKernel",       LapKernel);
            SPHComputeShader.SetFloats("_WallNormals",    WallNormals);
        }

        void OnDisable()
        {
            SPHParticlesRead.Release();
            SPHParticlesWrite.Release();
        }
    }
}