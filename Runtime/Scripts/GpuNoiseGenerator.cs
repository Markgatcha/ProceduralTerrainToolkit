using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralTerrainToolkit
{
    public readonly struct GpuNoiseRequest
    {
        public GpuNoiseRequest(int resolution, float cellSize, Vector2 worldOrigin, TerrainNoiseParameters noiseParameters)
        {
            Resolution = resolution;
            CellSize = cellSize;
            WorldOrigin = worldOrigin;
            NoiseParameters = noiseParameters;
        }

        public int Resolution { get; }
        public float CellSize { get; }
        public Vector2 WorldOrigin { get; }
        public TerrainNoiseParameters NoiseParameters { get; }
        public int SampleCount => Resolution * Resolution;
    }

    public readonly struct GpuNoiseReadbackResult : IDisposable
    {
        private GpuNoiseReadbackResult(bool success, string errorMessage, NativeArray<float3> samples)
        {
            Success = success;
            ErrorMessage = errorMessage;
            Samples = samples;
        }

        public bool Success { get; }
        public string ErrorMessage { get; }
        public NativeArray<float3> Samples { get; }

        public static GpuNoiseReadbackResult CreateFailure(string errorMessage)
        {
            return new GpuNoiseReadbackResult(false, errorMessage, default);
        }

        public static GpuNoiseReadbackResult CreateSuccess(NativeArray<float3> samples)
        {
            return new GpuNoiseReadbackResult(true, string.Empty, samples);
        }

        public JobHandle Dispose(JobHandle dependency)
        {
            if (!Samples.IsCreated)
            {
                return dependency;
            }

            return Samples.Dispose(dependency);
        }

        public void Dispose()
        {
            if (Samples.IsCreated)
            {
                Samples.Dispose();
            }
        }
    }

    public sealed class GpuNoiseGenerator : IDisposable
    {
        private const int ThreadGroupSize = 8;

        private static class ShaderIds
        {
            public static readonly int Resolution = Shader.PropertyToID("_Resolution");
            public static readonly int CellSize = Shader.PropertyToID("_CellSize");
            public static readonly int WorldOrigin = Shader.PropertyToID("_WorldOrigin");
            public static readonly int TerrainSamples = Shader.PropertyToID("_TerrainSamples");

            public static readonly int HeightSeed = Shader.PropertyToID("_HeightSeed");
            public static readonly int HeightScale = Shader.PropertyToID("_HeightScale");
            public static readonly int HeightOctaves = Shader.PropertyToID("_HeightOctaves");
            public static readonly int HeightPersistence = Shader.PropertyToID("_HeightPersistence");
            public static readonly int HeightLacunarity = Shader.PropertyToID("_HeightLacunarity");
            public static readonly int HeightOffset = Shader.PropertyToID("_HeightOffset");
            public static readonly int HeightMaxPossible = Shader.PropertyToID("_HeightMaxPossible");

            public static readonly int MoistureSeed = Shader.PropertyToID("_MoistureSeed");
            public static readonly int MoistureScale = Shader.PropertyToID("_MoistureScale");
            public static readonly int MoistureOctaves = Shader.PropertyToID("_MoistureOctaves");
            public static readonly int MoisturePersistence = Shader.PropertyToID("_MoisturePersistence");
            public static readonly int MoistureLacunarity = Shader.PropertyToID("_MoistureLacunarity");
            public static readonly int MoistureOffset = Shader.PropertyToID("_MoistureOffset");
            public static readonly int MoistureMaxPossible = Shader.PropertyToID("_MoistureMaxPossible");

            public static readonly int TemperatureSeed = Shader.PropertyToID("_TemperatureSeed");
            public static readonly int TemperatureScale = Shader.PropertyToID("_TemperatureScale");
            public static readonly int TemperatureOctaves = Shader.PropertyToID("_TemperatureOctaves");
            public static readonly int TemperaturePersistence = Shader.PropertyToID("_TemperaturePersistence");
            public static readonly int TemperatureLacunarity = Shader.PropertyToID("_TemperatureLacunarity");
            public static readonly int TemperatureOffset = Shader.PropertyToID("_TemperatureOffset");
            public static readonly int TemperatureMaxPossible = Shader.PropertyToID("_TemperatureMaxPossible");
        }

        private readonly ComputeShader computeShader;
        private readonly int kernelIndex;
        private bool disposed;

        public GpuNoiseGenerator(ComputeShader computeShader)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            kernelIndex = this.computeShader.FindKernel("GenerateNoiseMaps");
        }

        public bool IsSupported =>
            !disposed &&
            computeShader != null &&
            SystemInfo.supportsComputeShaders &&
            SystemInfo.supportsAsyncGPUReadback;

        public bool TryDispatch(in GpuNoiseRequest request, Action<GpuNoiseReadbackResult> onCompleted)
        {
            if (!IsSupported || !request.NoiseParameters.SupportsAsyncChunkGeneration)
            {
                return false;
            }

            ComputeBuffer resultBuffer = new ComputeBuffer(request.SampleCount, sizeof(float) * 3);
            ConfigureShader(in request, resultBuffer);

            int groupsX = Mathf.CeilToInt(request.Resolution / (float)ThreadGroupSize);
            int groupsY = Mathf.CeilToInt(request.Resolution / (float)ThreadGroupSize);
            computeShader.Dispatch(kernelIndex, groupsX, groupsY, 1);

            // AsyncGPUReadback is the key to making the compute path non-blocking for traversal-heavy scenes.
            // The request returns immediately, Unity resolves the GPU work later, and this callback only runs
            // once the data is safe to access without stalling the render pipeline.
            AsyncGPUReadback.Request(resultBuffer, readbackRequest =>
            {
                GpuNoiseReadbackResult result;

                try
                {
                    if (disposed)
                    {
                        result = GpuNoiseReadbackResult.CreateFailure("GPU noise generator was disposed before the readback completed.");
                    }
                    else if (readbackRequest.hasError)
                    {
                        result = GpuNoiseReadbackResult.CreateFailure("Async GPU readback failed for the terrain noise request.");
                    }
                    else
                    {
                        NativeArray<float3> readbackSamples = readbackRequest.GetData<float3>();
                        NativeArray<float3> persistentSamples = new NativeArray<float3>(
                            readbackSamples.Length,
                            Allocator.Persistent,
                            NativeArrayOptions.UninitializedMemory);

                        NativeArray<float3>.Copy(readbackSamples, persistentSamples, readbackSamples.Length);
                        result = GpuNoiseReadbackResult.CreateSuccess(persistentSamples);
                    }
                }
                finally
                {
                    resultBuffer.Release();
                }

                if (onCompleted != null)
                {
                    onCompleted(result);
                }
                else
                {
                    result.Dispose();
                }
            });

            return true;
        }

        public void Dispose()
        {
            disposed = true;
        }

        private void ConfigureShader(in GpuNoiseRequest request, ComputeBuffer resultBuffer)
        {
            computeShader.SetInt(ShaderIds.Resolution, request.Resolution);
            computeShader.SetFloat(ShaderIds.CellSize, request.CellSize);
            computeShader.SetVector(ShaderIds.WorldOrigin, new Vector4(request.WorldOrigin.x, request.WorldOrigin.y, 0f, 0f));
            computeShader.SetBuffer(kernelIndex, ShaderIds.TerrainSamples, resultBuffer);

            ApplyChannelParameters(
                request.NoiseParameters.Height,
                ShaderIds.HeightSeed,
                ShaderIds.HeightScale,
                ShaderIds.HeightOctaves,
                ShaderIds.HeightPersistence,
                ShaderIds.HeightLacunarity,
                ShaderIds.HeightOffset,
                ShaderIds.HeightMaxPossible);

            ApplyChannelParameters(
                request.NoiseParameters.Moisture,
                ShaderIds.MoistureSeed,
                ShaderIds.MoistureScale,
                ShaderIds.MoistureOctaves,
                ShaderIds.MoisturePersistence,
                ShaderIds.MoistureLacunarity,
                ShaderIds.MoistureOffset,
                ShaderIds.MoistureMaxPossible);

            ApplyChannelParameters(
                request.NoiseParameters.Temperature,
                ShaderIds.TemperatureSeed,
                ShaderIds.TemperatureScale,
                ShaderIds.TemperatureOctaves,
                ShaderIds.TemperaturePersistence,
                ShaderIds.TemperatureLacunarity,
                ShaderIds.TemperatureOffset,
                ShaderIds.TemperatureMaxPossible);
        }

        private void ApplyChannelParameters(
            in NoiseChannelParameters parameters,
            int seedId,
            int scaleId,
            int octavesId,
            int persistenceId,
            int lacunarityId,
            int offsetId,
            int maxPossibleId)
        {
            computeShader.SetInt(seedId, parameters.Seed);
            computeShader.SetFloat(scaleId, parameters.Scale);
            computeShader.SetInt(octavesId, parameters.Octaves);
            computeShader.SetFloat(persistenceId, parameters.Persistence);
            computeShader.SetFloat(lacunarityId, parameters.Lacunarity);
            computeShader.SetVector(offsetId, new Vector4(parameters.Offset.x, parameters.Offset.y, 0f, 0f));
            computeShader.SetFloat(maxPossibleId, parameters.MaxPossibleHeight);
        }
    }
}
