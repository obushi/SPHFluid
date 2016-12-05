using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SPHFluid
{
    public class SPHAdvancedParticleSystem : MonoBehaviour
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
        ComputeShader BitonicSortComputeShader;

        [SerializeField]
        Texture2D particleTexture;

        [SerializeField]
        Shader particleRenderShader;

        ComputeBuffer SPHGrid;

        ComputeBuffer SPHGridTemp;

        ComputeBuffer SPHGridIndices;

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
        uint numParticles = 32768;

        const uint BlockSize = 256;

        uint numGrids { get { return BlockSize * BlockSize; } }

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

            InitializeComputeBuffers();
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

            int kernelId;

            //-----------------------------------------------------------------------------------
            // Sorting Grid
            //-----------------------------------------------------------------------------------

            // Build Grid
            SetConstants();

            kernelId = SPHComputeShader.FindKernel("BuildGrid");
            SPHComputeShader.SetBuffer(kernelId, "_GridWrite", SPHGrid);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);

            var particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);

            var grid = new uint[numGrids];
            SPHGrid.GetData(grid);

            // Sort Grid
            GPUSort(ref SPHGrid, ref SPHGrid, ref SPHGridTemp, ref SPHGridTemp);

            // BuildGridIndices - Clear
            kernelId = SPHComputeShader.FindKernel("ClearGridIndices");
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesWrite", SPHGridIndices);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);

            particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);

            grid = new uint[numGrids];
            SPHGrid.GetData(grid);

            var pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);

            // BuildGridIndices - Build
            kernelId = SPHComputeShader.FindKernel("BuildGridIndices");
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesWrite", SPHGridIndices);
            SPHComputeShader.SetBuffer(kernelId, "_GridRead", SPHGrid);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);

            particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);
            pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);

            // Rearrange
            kernelId = SPHComputeShader.FindKernel("RearrangeParticles");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHParticlesWrite);
            SPHComputeShader.SetBuffer(kernelId, "_GridRead", SPHGrid);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);

            particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);
            pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);

            //-----------------------------------------------------------------------------------
            // Calculation 
            //-----------------------------------------------------------------------------------

            // Density
            kernelId = SPHComputeShader.FindKernel("Density");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesRead", SPHGridIndices);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesDensityWrite", SPHParticlesDensity);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);

            particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);

            var pd = new ParticleDensity[numParticles];
            SPHParticlesDensity.GetData(pd);
            pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);

            // Force
            kernelId = SPHComputeShader.FindKernel("Force");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesRead", SPHGridIndices);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesDensityRead", SPHParticlesDensity);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesForceWrite", SPHParticlesForce);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);

            particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);
            pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);

            // Integration
            kernelId = SPHComputeShader.FindKernel("Integrate");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticlesRead);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesForceRead", SPHParticlesForce);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHParticlesWrite);
            SPHComputeShader.Dispatch(kernelId, (int)numParticles / 32, 1, 1);


            // Render
            particleMaterial.SetPass(0);
            particleMaterial.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);
            particleMaterial.SetTexture("_ParticleTexture", particleTexture);
            particleMaterial.SetFloat("_ParticleSize", ParticleRadius);
            particleMaterial.SetBuffer("_ParticlesBuffer", SPHParticlesWrite);
            particleMaterial.SetBuffer("_ParticlesDensity", SPHParticlesDensity);
            Graphics.DrawProcedural(MeshTopology.Points, (int)numParticles);

            particles = new Particle[numParticles];
            SPHParticlesRead.GetData(particles);
            pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);

            Swap(ref SPHParticlesRead, ref SPHParticlesWrite);


            particles = new Particle[numParticles];
            SPHParticlesWrite.GetData(particles);
            SPHParticlesRead.GetData(particles);
            pf = new ParticleForce[numParticles];
            SPHParticlesForce.GetData(pf);
        }

        static void Swap<T>(ref T lhs, ref T rhs)
        {
            T tmp = lhs;
            lhs = rhs;
            rhs = tmp;
        }

        void GPUSort(ref ComputeBuffer GridWrite, ref ComputeBuffer GridRead, ref ComputeBuffer GridTempWrite, ref ComputeBuffer GridTempRead)
        {
            uint numElements = (uint)numParticles;
            uint matrixWidth = BlockSize;
            uint matrixHeight = (uint)(numParticles / BlockSize);

            //SetConstants();
            for (uint level = 2; level <= BlockSize; level<<= 1)
            {
                // Sort the row data
                int kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.SetInt("_Level", (int)level);
                BitonicSortComputeShader.SetInt("_LevelMask", (int)level);
                BitonicSortComputeShader.SetInt("_Width", (int)matrixWidth);
                BitonicSortComputeShader.SetInt("_Height", (int)matrixHeight);
                //BitonicSortComputeShader.SetBuffer(kernelId, "_Data", SPHGrid);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numParticles / BlockSize), 1, 1);
            }

            // Then sort the rows and columns for the levels > than the block size
            // Transpose. Sort the Columns. Transpose. Sort the Rows.
            for (uint level = (BlockSize << 1); level <= numParticles; level <<= 1)
            {
                int kernelId;

                kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.SetInt("_Level", (int)(level / BlockSize));
                BitonicSortComputeShader.SetInt("_LevelMask", (int)((level & ~numParticles) / BlockSize));
                BitonicSortComputeShader.SetInt("_Width", (int)matrixWidth);
                BitonicSortComputeShader.SetInt("_Height", (int)matrixHeight);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numParticles / BlockSize), 1, 1);

                // Transpose the data from buffer 1 into buffer 2
                kernelId = BitonicSortComputeShader.FindKernel("TransposeMatrix");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", GridTempWrite);
                BitonicSortComputeShader.SetBuffer(kernelId, "_Input", GridRead);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numParticles / BlockSize), (int)(numParticles / BlockSize), 1);

                // Sort the transposed column data.
                kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numParticles / BlockSize), 1, 1);

                // Transpose the data from buffer 2 back into buffer 1
                kernelId = BitonicSortComputeShader.FindKernel("TransposeMatrix");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", GridWrite);
                BitonicSortComputeShader.SetBuffer(kernelId, "_Input", GridTempRead);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(matrixHeight / BlockSize), (int)(matrixWidth / BlockSize), 1);

                // Sort the row data
                kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numElements / BlockSize), 1, 1);
            }
        }

        void InitializeComputeBuffers()
        {
            Particle[] particles = new Particle[numParticles];
            for (int i = 0; i < numParticles; i++)
            {
                float x = EmitPosition.x + ParticleInitGap + ParticleInitGap * (i % 128);
                float y = EmitPosition.y + ParticleInitGap + ParticleInitGap * (i / 128);
                particles[i].Position = new Vector2(x, y);
            }

            ParticleForce[] particlesForce = new ParticleForce[numParticles];
            ParticleDensity[] particlesDensity = new ParticleDensity[numParticles];

            SPHParticlesRead = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(Particle)));
            SPHParticlesWrite = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(Particle)));
            SPHParticlesForce = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(ParticleForce)));
            SPHParticlesDensity = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(ParticleDensity)));

            SPHParticlesRead.SetData(particles);
            SPHParticlesWrite.SetData(particles);
            SPHParticlesForce.SetData(particlesForce);
            SPHParticlesDensity.SetData(particlesDensity);

            SPHGrid = new ComputeBuffer((int)numGrids, sizeof(uint));
            SPHGridTemp = new ComputeBuffer((int)numGrids, sizeof(uint));
            SPHGridIndices = new ComputeBuffer((int)numGrids, sizeof(uint) * 2);

            uint[] grids = new uint[numGrids];
            SPHGrid.SetData(grids);
            SPHGridTemp.SetData(grids);
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
            SPHComputeShader.SetVector("_GridDim", GridDim);
            SPHComputeShader.SetVector("_MinBoundary", MinBoundary);
            SPHComputeShader.SetVector("_MaxBoundary", MaxBoundary);
            SPHComputeShader.SetInt("_MaxParticles", (int)numParticles);
            SPHComputeShader.SetFloat("_Poly6Kernel", Poly6Kernel);
            SPHComputeShader.SetFloat("_SpikeyKernel", SpikeyKernel);
            SPHComputeShader.SetFloat("_LapKernel", LapKernel);
            SPHComputeShader.SetFloats("_WallNormals", WallNormals);
        }

        void OnDisable()
        {
            SPHGrid.Release();
            SPHGridTemp.Release();
            SPHGridIndices.Release();
            SPHParticlesRead.Release();
            SPHParticlesWrite.Release();
            SPHParticlesForce.Release();
            SPHParticlesDensity.Release();
        }
    }
}