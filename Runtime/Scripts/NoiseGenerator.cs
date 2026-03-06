using System;
using UnityEngine;

namespace ProceduralTerrainToolkit
{
    public enum NoiseNormalizationMode
    {
        Local,
        Global
    }

    [Serializable]
    public sealed class NoiseSettings
    {
        [Tooltip("Seed used to build deterministic octave offsets.")]
        public int seed = 1337;

        [Min(0.0001f)]
        [Tooltip("Larger values zoom the noise out. Smaller values make terrain detail denser.")]
        public float scale = 128f;

        [Min(1)]
        [Tooltip("Number of layered Perlin noise octaves blended together.")]
        public int octaves = 4;

        [Range(0f, 1f)]
        [Tooltip("Amplitude multiplier applied after each octave.")]
        public float persistence = 0.5f;

        [Min(1f)]
        [Tooltip("Frequency multiplier applied after each octave.")]
        public float lacunarity = 2f;

        [Tooltip("Global offset applied before seeded octave offsets are added.")]
        public Vector2 offset = Vector2.zero;

        [Tooltip("Global normalization preserves seamless streaming. Local normalization increases per-chunk contrast.")]
        public NoiseNormalizationMode normalizationMode = NoiseNormalizationMode.Global;

        public void Validate()
        {
            scale = Mathf.Max(0.0001f, scale);
            octaves = Mathf.Max(1, octaves);
            persistence = Mathf.Clamp01(persistence);
            lacunarity = Mathf.Max(1f, lacunarity);
        }
    }

    public readonly struct NoiseSamplingContext
    {
        private readonly Vector2[] octaveOffsets;

        public int Seed { get; }
        public float Scale { get; }
        public int Octaves { get; }
        public float Persistence { get; }
        public float Lacunarity { get; }
        public NoiseNormalizationMode NormalizationMode { get; }
        public float MaxPossibleHeight { get; }
        public bool IsValid => octaveOffsets != null && octaveOffsets.Length == Octaves && Octaves > 0 && Scale > 0f;

        public NoiseSamplingContext(NoiseSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();

            Seed = settings.seed;
            Scale = settings.scale;
            Octaves = settings.octaves;
            Persistence = settings.persistence;
            Lacunarity = settings.lacunarity;
            NormalizationMode = settings.normalizationMode;
            octaveOffsets = new Vector2[Octaves];

            System.Random random = new System.Random(Seed);
            float amplitude = 1f;
            float maxPossibleHeight = 0f;

            for (int octaveIndex = 0; octaveIndex < Octaves; octaveIndex++)
            {
                float offsetX = random.Next(-100000, 100001) + settings.offset.x;
                float offsetY = random.Next(-100000, 100001) + settings.offset.y;
                octaveOffsets[octaveIndex] = new Vector2(offsetX, offsetY);

                maxPossibleHeight += amplitude;
                amplitude *= Persistence;
            }

            MaxPossibleHeight = Mathf.Max(maxPossibleHeight, 0.0001f);
        }

        public float SampleRaw(float worldX, float worldZ)
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("NoiseSamplingContext is not initialized. Create it with NoiseGenerator.CreateContext.");
            }

            float amplitude = 1f;
            float frequency = 1f;
            float noiseHeight = 0f;

            for (int octaveIndex = 0; octaveIndex < Octaves; octaveIndex++)
            {
                Vector2 octaveOffset = octaveOffsets[octaveIndex];
                float sampleX = ((worldX + octaveOffset.x) / Scale) * frequency;
                float sampleZ = ((worldZ + octaveOffset.y) / Scale) * frequency;
                float perlinValue = (Mathf.PerlinNoise(sampleX, sampleZ) * 2f) - 1f;

                noiseHeight += perlinValue * amplitude;
                amplitude *= Persistence;
                frequency *= Lacunarity;
            }

            return noiseHeight;
        }

        public float SampleNormalized(float worldX, float worldZ)
        {
            if (NormalizationMode == NoiseNormalizationMode.Local)
            {
                throw new InvalidOperationException("Local normalization requires a sampled map. Use NoiseGenerator.FillHeightMap or GenerateHeightMap instead.");
            }

            return NoiseGenerator.NormalizeGlobal(SampleRaw(worldX, worldZ), MaxPossibleHeight);
        }
    }

    public static class NoiseGenerator
    {
        public static NoiseSamplingContext CreateContext(NoiseSettings settings)
        {
            return new NoiseSamplingContext(settings);
        }

        public static float SampleHeight(float worldX, float worldZ, NoiseSettings settings)
        {
            NoiseSamplingContext context = CreateContext(settings);

            if (context.NormalizationMode == NoiseNormalizationMode.Local)
            {
                throw new InvalidOperationException("NoiseGenerator.SampleHeight does not support Local normalization because a single sample has no map-wide min/max range. Use SampleRawHeight, FillHeightMap, or GenerateHeightMap instead.");
            }

            return context.SampleNormalized(worldX, worldZ);
        }

        public static float SampleRawHeight(float worldX, float worldZ, NoiseSettings settings)
        {
            NoiseSamplingContext context = CreateContext(settings);
            return context.SampleRaw(worldX, worldZ);
        }

        public static float[] GenerateHeightMap(int width, int height, Vector2 worldOrigin, Vector2 sampleSpacing, NoiseSettings settings)
        {
            NoiseSamplingContext context = CreateContext(settings);
            float[] heightMap = new float[width * height];
            FillHeightMap(heightMap, width, height, worldOrigin, sampleSpacing, in context);
            return heightMap;
        }

        public static void FillHeightMap(float[] destination, int width, int height, Vector2 worldOrigin, Vector2 sampleSpacing, in NoiseSamplingContext context)
        {
            ValidateMapArguments(destination, width, height);

            if (!context.IsValid)
            {
                throw new InvalidOperationException("NoiseSamplingContext is not initialized. Create it with NoiseGenerator.CreateContext.");
            }

            if (context.NormalizationMode == NoiseNormalizationMode.Local)
            {
                FillLocalNormalizedHeightMap(destination, width, height, worldOrigin, sampleSpacing, in context);
                return;
            }

            int index = 0;
            for (int y = 0; y < height; y++)
            {
                float sampleZ = worldOrigin.y + (y * sampleSpacing.y);

                for (int x = 0; x < width; x++)
                {
                    float sampleX = worldOrigin.x + (x * sampleSpacing.x);
                    destination[index] = context.SampleNormalized(sampleX, sampleZ);
                    index++;
                }
            }
        }

        public static float NormalizeGlobal(float rawNoiseHeight, float maxPossibleHeight)
        {
            float safeMaxHeight = Mathf.Max(maxPossibleHeight, 0.0001f);
            return Mathf.Clamp01((rawNoiseHeight + safeMaxHeight) / (2f * safeMaxHeight));
        }

        private static void FillLocalNormalizedHeightMap(float[] destination, int width, int height, Vector2 worldOrigin, Vector2 sampleSpacing, in NoiseSamplingContext context)
        {
            int requiredLength = width * height;
            float minimumHeight = float.PositiveInfinity;
            float maximumHeight = float.NegativeInfinity;
            int index = 0;

            for (int y = 0; y < height; y++)
            {
                float sampleZ = worldOrigin.y + (y * sampleSpacing.y);

                for (int x = 0; x < width; x++)
                {
                    float sampleX = worldOrigin.x + (x * sampleSpacing.x);
                    float rawHeight = context.SampleRaw(sampleX, sampleZ);

                    destination[index] = rawHeight;
                    minimumHeight = Mathf.Min(minimumHeight, rawHeight);
                    maximumHeight = Mathf.Max(maximumHeight, rawHeight);
                    index++;
                }
            }

            if (Mathf.Approximately(minimumHeight, maximumHeight))
            {
                for (int i = 0; i < requiredLength; i++)
                {
                    destination[i] = 0.5f;
                }

                return;
            }

            for (int i = 0; i < requiredLength; i++)
            {
                destination[i] = Mathf.InverseLerp(minimumHeight, maximumHeight, destination[i]);
            }
        }

        private static void ValidateMapArguments(float[] destination, int width, int height)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (width < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Height map width must be at least 1.");
            }

            if (height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Height map height must be at least 1.");
            }

            int requiredLength = width * height;
            if (destination.Length < requiredLength)
            {
                throw new ArgumentException($"Destination array is too small. Required {requiredLength} entries but received {destination.Length}.", nameof(destination));
            }
        }
    }
}
