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
        RenderTexture backRenderTexture;

        [SerializeField]
        Shader particleRenderShader;

        ComputeBuffer SPHGrid;

        ComputeBuffer SPHGridTemp;

        ComputeBuffer SPHGridIndices;

        ComputeBuffer SPHParticles;

        ComputeBuffer SPHSortedParticles;

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

        [SerializeField, Range(0.01f, 1.0f)]
        float contourMinThreshold = 0.5f;

        [SerializeField, Range(0.01f, 1.0f)]
        float contourMaxThreshold = 0.9f;

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

        uint numParticles = 65536;

        const uint BlockSize = 256;
        const uint BitonicBlockSize = 512;
        const uint TransposeBlockSize = 16;

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
            backRenderTexture = new RenderTexture(Screen.width, Screen.height, 0);

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
            
            SetConstants();
            int threadGroupsX = (int)(numParticles / BlockSize);

            // Build Grid
            kernelId = SPHComputeShader.FindKernel("BuildGrid");
            SPHComputeShader.SetBuffer(kernelId, "_GridWrite",     SPHGrid);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead", SPHParticles);
            SPHComputeShader.Dispatch(kernelId, threadGroupsX, 1, 1);

            // Sort Grid
            GPUSort(SPHGrid, SPHGridTemp);

            // BuildGridIndices - Clear
            kernelId = SPHComputeShader.FindKernel("ClearGridIndices");
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesWrite", SPHGridIndices);
            SPHComputeShader.Dispatch(kernelId, threadGroupsX, 1, 1);

            // BuildGridIndices - Build
            kernelId = SPHComputeShader.FindKernel("BuildGridIndices");
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesWrite", SPHGridIndices);
            SPHComputeShader.SetBuffer(kernelId, "_GridRead",         SPHGrid);
            SPHComputeShader.Dispatch(kernelId, (int)(numGrids / BlockSize), 1, 1);

            // Rearrange
            kernelId = SPHComputeShader.FindKernel("RearrangeParticles");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead",  SPHParticles);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite", SPHSortedParticles);
            SPHComputeShader.SetBuffer(kernelId, "_GridRead",       SPHGrid);
            SPHComputeShader.Dispatch(kernelId, threadGroupsX, 1, 1);

            //-----------------------------------------------------------------------------------
            // Calculation 
            //-----------------------------------------------------------------------------------

            // Density
            kernelId = SPHComputeShader.FindKernel("Density");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead",         SPHSortedParticles);
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesRead",       SPHGridIndices);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesDensityWrite", SPHParticlesDensity);
            SPHComputeShader.Dispatch(kernelId, threadGroupsX, 1, 1);

            // Force
            kernelId = SPHComputeShader.FindKernel("Force");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead",        SPHSortedParticles);
            SPHComputeShader.SetBuffer(kernelId, "_GridIndicesRead",      SPHGridIndices);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesDensityRead", SPHParticlesDensity);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesForceWrite",  SPHParticlesForce);
            SPHComputeShader.Dispatch(kernelId, threadGroupsX, 1, 1);

            // Integration
            kernelId = SPHComputeShader.FindKernel("Integrate");
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesRead",      SPHSortedParticles);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesForceRead", SPHParticlesForce);
            SPHComputeShader.SetBuffer(kernelId, "_ParticlesWrite",     SPHParticles);
            SPHComputeShader.Dispatch(kernelId, threadGroupsX, 1, 1);

            // Rendering
            particleMaterial.SetPass(0);
            particleMaterial.SetMatrix("_InvViewMatrix", Camera.main.worldToCameraMatrix.inverse);
            particleMaterial.SetTexture("_ParticleTexture", particleTexture);
            particleMaterial.SetFloat("_ParticleRadius", ParticleRadius);
            particleMaterial.SetBuffer("_ParticlesBuffer", SPHSortedParticles);
            particleMaterial.SetBuffer("_ParticlesDensity", SPHParticlesDensity);

            particleMaterial.SetVector("_MaxBoundary", MaxBoundary);
            particleMaterial.SetVector("_MinBoundary", MinBoundary);

            Graphics.SetRenderTarget(backRenderTexture);
            GL.Clear(true, true, Color.black);
            Graphics.DrawProcedural(MeshTopology.Points, (int)numParticles);


            particleMaterial.SetPass(1);
            particleMaterial.SetFloat("_ContourMinThreshold", contourMinThreshold);
            particleMaterial.SetFloat("_ContourMaxThreshold", contourMaxThreshold);
            particleMaterial.SetFloat("_UVPosMaxY", Camera.main.WorldToViewportPoint(MaxBoundary).y);
            particleMaterial.SetFloat("_UVPosMinY", Camera.main.WorldToViewportPoint(MinBoundary).y);
            Graphics.Blit(backRenderTexture, null, particleMaterial, 1);
        }

        static void Swap<T>(ref T lhs, ref T rhs)
        {
            T tmp = lhs;
            lhs = rhs;
            rhs = tmp;
        }

        void GPUSort(ComputeBuffer inBuffer, ComputeBuffer tempBuffer)
        {
            uint numElements  = numParticles;
            uint matrixWidth  = BitonicBlockSize;
            uint matrixHeight = numElements / BitonicBlockSize;

            //SetConstants();
            for (uint level = 2; level <= BitonicBlockSize; level<<= 1)
            {
                // Sort the row data
                BitonicSortComputeShader.SetInt("_Level", (int)level);
                BitonicSortComputeShader.SetInt("_LevelMask", (int)level);
                BitonicSortComputeShader.SetInt("_Width", (int)matrixHeight);
                BitonicSortComputeShader.SetInt("_Height", (int)matrixWidth);
                
                int kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", SPHGrid);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numElements / BitonicBlockSize), 1, 1);
            }

            // Then sort the rows and columns for the levels > than the block size
            // Transpose. Sort the Columns. Transpose. Sort the Rows.
            for (uint level = (BitonicBlockSize << 1); level <= numElements; level <<= 1)
            {
                int kernelId;
                
                // -------------------------------------------------------------------------------
                BitonicSortComputeShader.SetInt("_Level", (int)(level / BitonicBlockSize));
                BitonicSortComputeShader.SetInt("_LevelMask", (int)((level & ~numElements) / BitonicBlockSize));
                BitonicSortComputeShader.SetInt("_Width", (int)matrixWidth);
                BitonicSortComputeShader.SetInt("_Height", (int)matrixHeight);
                // -------------------------------------------------------------------------------

                // Transpose the data from buffer 1 into buffer 2
                kernelId = BitonicSortComputeShader.FindKernel("TransposeMatrix");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", tempBuffer);
                BitonicSortComputeShader.SetBuffer(kernelId, "_Input", inBuffer);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(matrixWidth / TransposeBlockSize), (int)(matrixHeight / TransposeBlockSize), 1);

                // Sort the transposed column data.
                kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", tempBuffer);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numElements / BitonicBlockSize), 1, 1);

                // -------------------------------------------------------------------------------
                BitonicSortComputeShader.SetInt("_Level", (int)(BitonicBlockSize));
                BitonicSortComputeShader.SetInt("_LevelMask", (int)level);
                BitonicSortComputeShader.SetInt("_Width", (int)matrixHeight);
                BitonicSortComputeShader.SetInt("_Height", (int)matrixWidth);
                // -------------------------------------------------------------------------------

                // Transpose the data from buffer 2 back into buffer 1
                kernelId = BitonicSortComputeShader.FindKernel("TransposeMatrix");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", inBuffer);
                BitonicSortComputeShader.SetBuffer(kernelId, "_Input", tempBuffer);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(matrixHeight / TransposeBlockSize), (int)(matrixWidth / TransposeBlockSize), 1);

                // Sort the row data
                kernelId = BitonicSortComputeShader.FindKernel("BitonicSort");
                BitonicSortComputeShader.SetBuffer(kernelId, "_Data", inBuffer);
                BitonicSortComputeShader.Dispatch(kernelId, (int)(numElements / BitonicBlockSize), 1, 1);
            }
        }

        void InitializeComputeBuffers()
        {
            SPHParticles        = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(Particle)));
            SPHSortedParticles  = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(Particle)));
            SPHParticlesForce   = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(ParticleForce)));
            SPHParticlesDensity = new ComputeBuffer((int)numParticles, Marshal.SizeOf(typeof(ParticleDensity)));
            SPHGrid             = new ComputeBuffer((int)numGrids,     Marshal.SizeOf(typeof(uint)));
            SPHGridTemp         = new ComputeBuffer((int)numGrids,     Marshal.SizeOf(typeof(uint)));
            SPHGridIndices      = new ComputeBuffer((int)numGrids,     Marshal.SizeOf(typeof(uint)) * 2);


            int startingWidth = (int)Mathf.Sqrt(numParticles);
            Particle[] particles = new Particle[numParticles];
            for (int i = 0; i < numParticles; i++)
            {
                int x = i % startingWidth;
                int y = Mathf.FloorToInt(i / (float)startingWidth);
                particles[i].Position = new Vector2(ParticleInitGap * x, ParticleInitGap * y);
            }

            SPHParticles.SetData(particles);
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
            SPHComputeShader.SetVector("_Gravity", Gravity);
            SPHComputeShader.SetVector("_GridDim", GridDim);
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
            SPHParticles.Release();
            SPHSortedParticles.Release();
            SPHParticlesForce.Release();
            SPHParticlesDensity.Release();
        }
    }
}