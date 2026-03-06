using System;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralTerrainToolkit
{
    public enum NoiseNormalizationMode
    {
        Global,
        Local
    }

    public enum TerrainGenerationBackend
    {
        CpuBurstJobs,
        GpuComputeAsync
    }

    [Serializable]
    public sealed class NoiseChannelSettings
    {
        [Tooltip("Offset added to the shared terrain seed so each channel remains decorrelated.")]
        public int seedOffset;

        [Min(0.0001f)]
        [Tooltip("Larger values spread the pattern over a wider area.")]
        public float scale = 128f;

        [Min(1)]
        [Tooltip("Number of fractal octaves blended together.")]
        public int octaves = 4;

        [Range(0f, 1f)]
        [Tooltip("Amplitude decay applied after each octave.")]
        public float persistence = 0.5f;

        [Min(1f)]
        [Tooltip("Frequency multiplier applied after each octave.")]
        public float lacunarity = 2f;

        [Tooltip("Additional world-space offset for this noise channel.")]
        public Vector2 offset = Vector2.zero;

        [Tooltip("Async chunk generation currently requires global normalization so each chunk agrees on the same range.")]
        public NoiseNormalizationMode normalizationMode = NoiseNormalizationMode.Global;

        public void Validate()
        {
            scale = Mathf.Max(0.0001f, scale);
            octaves = Mathf.Clamp(octaves, 1, 12);
            persistence = Mathf.Clamp01(persistence);
            lacunarity = Mathf.Max(1f, lacunarity);
        }
    }

    [Serializable]
    public sealed class NoiseSettings
    {
        [Tooltip("Base seed shared by height, moisture, and temperature channels.")]
        public int baseSeed = 1337;

        [Tooltip("CPU Burst jobs are the universal fallback. GPU compute is used when requested and supported.")]
        public TerrainGenerationBackend generationBackend = TerrainGenerationBackend.CpuBurstJobs;

        [Tooltip("Height map controls the terrain shape that drives mesh displacement.")]
        public NoiseChannelSettings height = new NoiseChannelSettings();

        [Tooltip("Moisture map controls how wet, grassy, or sandy a surface should appear.")]
        public NoiseChannelSettings moisture = new NoiseChannelSettings
        {
            seedOffset = 701,
            scale = 192f,
            octaves = 4,
            persistence = 0.55f,
            lacunarity = 2.05f,
            offset = new Vector2(61f, 19f),
            normalizationMode = NoiseNormalizationMode.Global
        };

        [Tooltip("Temperature map controls hot/cold biome blending and snow placement.")]
        public NoiseChannelSettings temperature = new NoiseChannelSettings
        {
            seedOffset = 1543,
            scale = 256f,
            octaves = 3,
            persistence = 0.6f,
            lacunarity = 1.95f,
            offset = new Vector2(-37f, 83f),
            normalizationMode = NoiseNormalizationMode.Global
        };

        public void Validate()
        {
            if (height == null)
            {
                height = new NoiseChannelSettings();
            }

            if (moisture == null)
            {
                moisture = new NoiseChannelSettings();
            }

            if (temperature == null)
            {
                temperature = new NoiseChannelSettings();
            }

            height.Validate();
            moisture.Validate();
            temperature.Validate();
        }
    }

    public readonly struct NoiseChannelParameters
    {
        public NoiseChannelParameters(
            int seed,
            float scale,
            int octaves,
            float persistence,
            float lacunarity,
            float2 offset,
            NoiseNormalizationMode normalizationMode,
            float maxPossibleHeight)
        {
            Seed = seed;
            Scale = scale;
            Octaves = octaves;
            Persistence = persistence;
            Lacunarity = lacunarity;
            Offset = offset;
            NormalizationMode = normalizationMode;
            MaxPossibleHeight = math.max(maxPossibleHeight, 0.0001f);
        }

        public int Seed { get; }
        public float Scale { get; }
        public int Octaves { get; }
        public float Persistence { get; }
        public float Lacunarity { get; }
        public float2 Offset { get; }
        public NoiseNormalizationMode NormalizationMode { get; }
        public float MaxPossibleHeight { get; }
        public bool SupportsAsyncSampling => NormalizationMode == NoiseNormalizationMode.Global;
    }

    public readonly struct TerrainNoiseParameters
    {
        public TerrainNoiseParameters(
            TerrainGenerationBackend generationBackend,
            NoiseChannelParameters height,
            NoiseChannelParameters moisture,
            NoiseChannelParameters temperature)
        {
            GenerationBackend = generationBackend;
            Height = height;
            Moisture = moisture;
            Temperature = temperature;
        }

        public TerrainGenerationBackend GenerationBackend { get; }
        public NoiseChannelParameters Height { get; }
        public NoiseChannelParameters Moisture { get; }
        public NoiseChannelParameters Temperature { get; }

        public bool SupportsAsyncChunkGeneration =>
            Height.SupportsAsyncSampling &&
            Moisture.SupportsAsyncSampling &&
            Temperature.SupportsAsyncSampling;
    }

    public static class NoiseGenerator
    {
        public static TerrainNoiseParameters CreateParameters(NoiseSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();

            return new TerrainNoiseParameters(
                settings.generationBackend,
                CreateChannelParameters(settings.baseSeed, settings.height),
                CreateChannelParameters(settings.baseSeed, settings.moisture),
                CreateChannelParameters(settings.baseSeed, settings.temperature));
        }

        public static NoiseChannelParameters CreateChannelParameters(int baseSeed, NoiseChannelSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();

            int channelSeed = baseSeed + settings.seedOffset;
            float maxPossibleHeight = CalculateMaxPossibleAmplitude(settings.octaves, settings.persistence);

            return new NoiseChannelParameters(
                channelSeed,
                settings.scale,
                settings.octaves,
                settings.persistence,
                settings.lacunarity,
                new float2(settings.offset.x, settings.offset.y),
                settings.normalizationMode,
                maxPossibleHeight);
        }

        public static float3 SampleTerrain(float2 worldPosition, in TerrainNoiseParameters parameters)
        {
            return new float3(
                SampleNormalized(worldPosition, parameters.Height),
                SampleNormalized(worldPosition, parameters.Moisture),
                SampleNormalized(worldPosition, parameters.Temperature));
        }

        public static float SampleNormalized(float2 worldPosition, in NoiseChannelParameters parameters)
        {
            if (!parameters.SupportsAsyncSampling)
            {
                throw new InvalidOperationException("Asynchronous terrain generation requires Global normalization so streamed chunks agree on a shared value range.");
            }

            return NormalizeGlobal(SampleRaw(worldPosition, parameters), parameters.MaxPossibleHeight);
        }

        public static float SampleRaw(float2 worldPosition, in NoiseChannelParameters parameters)
        {
            float amplitude = 1f;
            float frequency = 1f;
            float noiseValue = 0f;

            for (int octaveIndex = 0; octaveIndex < parameters.Octaves; octaveIndex++)
            {
                float2 samplePosition = ((worldPosition + parameters.Offset) / parameters.Scale) * frequency;
                noiseValue += SampleSmoothValueNoise(samplePosition, parameters.Seed + (octaveIndex * 1013)) * amplitude;

                amplitude *= parameters.Persistence;
                frequency *= parameters.Lacunarity;
            }

            return noiseValue;
        }

        public static float NormalizeGlobal(float rawNoiseValue, float maxPossibleHeight)
        {
            float safeMaxHeight = math.max(maxPossibleHeight, 0.0001f);
            return math.saturate((rawNoiseValue + safeMaxHeight) / (2f * safeMaxHeight));
        }

        public static float CalculateMaxPossibleAmplitude(int octaves, float persistence)
        {
            float amplitude = 1f;
            float maxPossibleHeight = 0f;

            for (int octaveIndex = 0; octaveIndex < math.max(1, octaves); octaveIndex++)
            {
                maxPossibleHeight += amplitude;
                amplitude *= math.saturate(persistence);
            }

            return math.max(maxPossibleHeight, 0.0001f);
        }

        public static float SampleSmoothValueNoise(float2 samplePosition, int seed)
        {
            int2 cell = (int2)math.floor(samplePosition);
            float2 fraction = math.frac(samplePosition);
            float2 smoothed = fraction * fraction * (3f - (2f * fraction));

            float value00 = Hash01(cell, seed);
            float value10 = Hash01(cell + new int2(1, 0), seed);
            float value01 = Hash01(cell + new int2(0, 1), seed);
            float value11 = Hash01(cell + new int2(1, 1), seed);

            float bottom = math.lerp(value00, value10, smoothed.x);
            float top = math.lerp(value01, value11, smoothed.x);

            return (math.lerp(bottom, top, smoothed.y) * 2f) - 1f;
        }

        private static float Hash01(int2 lattice, int seed)
        {
            uint x = (uint)lattice.x;
            uint y = (uint)lattice.y;
            uint s = (uint)seed;

            uint hash = x * 0x1f123bb5u;
            hash ^= y * 0x05491333u;
            hash ^= s * 0x9e3779b9u;
            hash ^= hash >> 15;
            hash *= 0x85ebca6bu;
            hash ^= hash >> 13;
            hash *= 0xc2b2ae35u;
            hash ^= hash >> 16;

            return (hash & 0x00ffffffu) / 16777215f;
        }
    }
}
