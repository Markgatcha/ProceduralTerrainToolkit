using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralTerrainToolkit
{
    [Serializable]
    public sealed class TerrainSurfaceSettings
    {
        [Range(0f, 1f)]
        [Tooltip("Slope value where rock blending begins.")]
        public float rockSlopeStart = 0.25f;

        [Range(0f, 1f)]
        [Tooltip("Slope value where rock becomes dominant.")]
        public float rockSlopeEnd = 0.65f;

        [Range(0f, 1f)]
        [Tooltip("Lower bound for sand blending near sea-level style terrain.")]
        public float sandHeightStart = 0.02f;

        [Range(0f, 1f)]
        [Tooltip("Upper bound for sand blending.")]
        public float sandHeightEnd = 0.22f;

        [Range(0f, 1f)]
        [Tooltip("Dryness threshold where sand blending starts.")]
        public float sandMoistureStart = 0.1f;

        [Range(0f, 1f)]
        [Tooltip("Dryness threshold where sand reaches full strength.")]
        public float sandMoistureEnd = 0.4f;

        [Range(0f, 1f)]
        [Tooltip("Height where snow begins to appear.")]
        public float snowHeightStart = 0.72f;

        [Range(0f, 1f)]
        [Tooltip("Height where snow reaches full strength.")]
        public float snowHeightEnd = 0.92f;

        [Range(0f, 1f)]
        [Tooltip("Temperature below this value strongly favors snow.")]
        public float snowTemperatureStart = 0.05f;

        [Range(0f, 1f)]
        [Tooltip("Temperature above this value removes snow contribution.")]
        public float snowTemperatureEnd = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Moisture threshold where grass becomes denser.")]
        public float grassMoistureStart = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Moisture threshold where grass reaches maximum lushness.")]
        public float grassMoistureEnd = 0.75f;

        public void Validate()
        {
            rockSlopeStart = Mathf.Clamp01(rockSlopeStart);
            rockSlopeEnd = Mathf.Clamp(rockSlopeEnd, rockSlopeStart, 1f);

            sandHeightStart = Mathf.Clamp01(sandHeightStart);
            sandHeightEnd = Mathf.Clamp(sandHeightEnd, sandHeightStart, 1f);
            sandMoistureStart = Mathf.Clamp01(sandMoistureStart);
            sandMoistureEnd = Mathf.Clamp(sandMoistureEnd, sandMoistureStart, 1f);

            snowHeightStart = Mathf.Clamp01(snowHeightStart);
            snowHeightEnd = Mathf.Clamp(snowHeightEnd, snowHeightStart, 1f);
            snowTemperatureStart = Mathf.Clamp01(snowTemperatureStart);
            snowTemperatureEnd = Mathf.Clamp(snowTemperatureEnd, snowTemperatureStart, 1f);

            grassMoistureStart = Mathf.Clamp01(grassMoistureStart);
            grassMoistureEnd = Mathf.Clamp(grassMoistureEnd, grassMoistureStart, 1f);
        }

        public TerrainSurfaceParameters ToParameters()
        {
            return new TerrainSurfaceParameters(
                rockSlopeStart,
                rockSlopeEnd,
                sandHeightStart,
                sandHeightEnd,
                sandMoistureStart,
                sandMoistureEnd,
                snowHeightStart,
                snowHeightEnd,
                snowTemperatureStart,
                snowTemperatureEnd,
                grassMoistureStart,
                grassMoistureEnd);
        }
    }

    public readonly struct TerrainSurfaceParameters
    {
        public TerrainSurfaceParameters(
            float rockSlopeStart,
            float rockSlopeEnd,
            float sandHeightStart,
            float sandHeightEnd,
            float sandMoistureStart,
            float sandMoistureEnd,
            float snowHeightStart,
            float snowHeightEnd,
            float snowTemperatureStart,
            float snowTemperatureEnd,
            float grassMoistureStart,
            float grassMoistureEnd)
        {
            RockSlopeStart = rockSlopeStart;
            RockSlopeEnd = rockSlopeEnd;
            SandHeightStart = sandHeightStart;
            SandHeightEnd = sandHeightEnd;
            SandMoistureStart = sandMoistureStart;
            SandMoistureEnd = sandMoistureEnd;
            SnowHeightStart = snowHeightStart;
            SnowHeightEnd = snowHeightEnd;
            SnowTemperatureStart = snowTemperatureStart;
            SnowTemperatureEnd = snowTemperatureEnd;
            GrassMoistureStart = grassMoistureStart;
            GrassMoistureEnd = grassMoistureEnd;
        }

        public float RockSlopeStart { get; }
        public float RockSlopeEnd { get; }
        public float SandHeightStart { get; }
        public float SandHeightEnd { get; }
        public float SandMoistureStart { get; }
        public float SandMoistureEnd { get; }
        public float SnowHeightStart { get; }
        public float SnowHeightEnd { get; }
        public float SnowTemperatureStart { get; }
        public float SnowTemperatureEnd { get; }
        public float GrassMoistureStart { get; }
        public float GrassMoistureEnd { get; }
    }

    [Serializable]
    public sealed class TerrainChunkSettings
    {
        [Min(1f)]
        [Tooltip("World-space width and depth of one streamed chunk.")]
        public float chunkSize = 128f;

        [Min(2)]
        [Tooltip("Vertex count per axis. Higher values increase detail and generation cost.")]
        public int vertexResolution = 65;

        [Min(0.01f)]
        [Tooltip("Multiplier applied to normalized height samples.")]
        public float heightScale = 32f;

        [Tooltip("UV repetition across the chunk surface.")]
        public Vector2 uvScale = Vector2.one;

        [Min(1)]
        [Tooltip("Parallel-for inner loop batch size used when scheduling jobs.")]
        public int jobBatchSize = 64;

        [Tooltip("Optional collider generation. Disable this for the cheapest streaming path.")]
        public bool generateCollider = false;

        [Tooltip("Shadow casting mode used by the chunk renderer.")]
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;

        [Tooltip("Whether chunk renderers should receive scene shadows.")]
        public bool receiveShadows = true;

        [Tooltip("Vertex color output acts as splat weights: R=grass, G=rock, B=sand, A=snow.")]
        public TerrainSurfaceSettings surface = new TerrainSurfaceSettings();

        public float CellSize => chunkSize / (vertexResolution - 1);
        public int VertexCount => vertexResolution * vertexResolution;
        public int QuadCount => (vertexResolution - 1) * (vertexResolution - 1);
        public int TriangleIndexCount => QuadCount * 6;

        public void Validate()
        {
            chunkSize = Mathf.Max(1f, chunkSize);
            vertexResolution = Mathf.Max(2, vertexResolution);
            heightScale = Mathf.Max(0.01f, heightScale);
            jobBatchSize = Mathf.Max(1, jobBatchSize);

            if (uvScale.x <= 0f)
            {
                uvScale.x = 1f;
            }

            if (uvScale.y <= 0f)
            {
                uvScale.y = 1f;
            }

            if (surface == null)
            {
                surface = new TerrainSurfaceSettings();
            }

            surface.Validate();
        }
    }

    [DisallowMultipleComponent]
    public sealed class TerrainChunk : MonoBehaviour
    {
        private struct BuildRequest
        {
            public Vector2Int Coordinate;
            public TerrainNoiseParameters NoiseParameters;
            public bool UseGpuBackend;
        }

        [BurstCompile]
        private struct SampleTerrainMapsJob : IJobParallelFor
        {
            [WriteOnly]
            public NativeArray<float3> TerrainSamples;

            public NoiseChannelParameters HeightParameters;
            public NoiseChannelParameters MoistureParameters;
            public NoiseChannelParameters TemperatureParameters;
            public int Resolution;
            public float CellSize;
            public float2 WorldOrigin;

            public void Execute(int index)
            {
                int x = index % Resolution;
                int z = index / Resolution;
                float2 worldPosition = WorldOrigin + new float2(x * CellSize, z * CellSize);

                TerrainSamples[index] = new float3(
                    NoiseGenerator.SampleNormalized(worldPosition, HeightParameters),
                    NoiseGenerator.SampleNormalized(worldPosition, MoistureParameters),
                    NoiseGenerator.SampleNormalized(worldPosition, TemperatureParameters));
            }
        }

        [BurstCompile]
        private struct CopyTerrainSamplesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> SourceSamples;

            [WriteOnly]
            public NativeArray<float3> DestinationSamples;

            public void Execute(int index)
            {
                DestinationSamples[index] = SourceSamples[index];
            }
        }

        [BurstCompile]
        private struct BuildTrianglesJob : IJobParallelFor
        {
            [WriteOnly]
            public NativeArray<int> Triangles;

            public int Resolution;

            public void Execute(int quadIndex)
            {
                int quadsPerRow = Resolution - 1;
                int x = quadIndex % quadsPerRow;
                int z = quadIndex / quadsPerRow;
                int vertexIndex = (z * Resolution) + x;
                int triangleIndex = quadIndex * 6;

                // Counterclockwise winding keeps the generated normals facing upward.
                Triangles[triangleIndex] = vertexIndex;
                Triangles[triangleIndex + 1] = vertexIndex + Resolution;
                Triangles[triangleIndex + 2] = vertexIndex + Resolution + 1;

                Triangles[triangleIndex + 3] = vertexIndex;
                Triangles[triangleIndex + 4] = vertexIndex + Resolution + 1;
                Triangles[triangleIndex + 5] = vertexIndex + 1;
            }
        }

        [BurstCompile]
        private struct BuildVertexDataJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float3> TerrainSamples;

            [WriteOnly]
            public NativeArray<Vector3> Vertices;

            [WriteOnly]
            public NativeArray<Vector3> Normals;

            [WriteOnly]
            public NativeArray<Vector2> Uvs;

            [WriteOnly]
            public NativeArray<Color32> Colors;

            public int Resolution;
            public float CellSize;
            public float HeightScale;
            public float2 UvScale;
            public TerrainSurfaceParameters SurfaceParameters;

            public void Execute(int index)
            {
                int x = index % Resolution;
                int z = index / Resolution;
                float3 biomeSample = TerrainSamples[index];

                float worldHeight = biomeSample.x * HeightScale;
                float localX = x * CellSize;
                float localZ = z * CellSize;

                float leftHeight = SampleHeight(x - 1, z);
                float rightHeight = SampleHeight(x + 1, z);
                float backHeight = SampleHeight(x, z - 1);
                float forwardHeight = SampleHeight(x, z + 1);

                float3 tangent = new float3(CellSize * 2f, rightHeight - leftHeight, 0f);
                float3 bitangent = new float3(0f, forwardHeight - backHeight, CellSize * 2f);
                float3 normal = math.normalize(math.cross(bitangent, tangent));
                float slope = 1f - math.saturate(normal.y);

                float4 splatWeights = EvaluateSurfaceWeights(
                    biomeSample.x,
                    slope,
                    biomeSample.y,
                    biomeSample.z,
                    SurfaceParameters);

                Vertices[index] = new Vector3(localX, worldHeight, localZ);
                Normals[index] = new Vector3(normal.x, normal.y, normal.z);
                Uvs[index] = new Vector2(
                    (x / (float)(Resolution - 1)) * UvScale.x,
                    (z / (float)(Resolution - 1)) * UvScale.y);
                Colors[index] = PackWeights(splatWeights);
            }

            private float SampleHeight(int x, int z)
            {
                int clampedX = math.clamp(x, 0, Resolution - 1);
                int clampedZ = math.clamp(z, 0, Resolution - 1);
                return TerrainSamples[(clampedZ * Resolution) + clampedX].x * HeightScale;
            }

            private static float4 EvaluateSurfaceWeights(
                float height,
                float slope,
                float moisture,
                float temperature,
                in TerrainSurfaceParameters surfaceParameters)
            {
                float rock = SmoothThreshold(slope, surfaceParameters.RockSlopeStart, surfaceParameters.RockSlopeEnd);

                float sandHeight = 1f - SmoothThreshold(height, surfaceParameters.SandHeightStart, surfaceParameters.SandHeightEnd);
                float sandDryness = 1f - SmoothThreshold(moisture, surfaceParameters.SandMoistureStart, surfaceParameters.SandMoistureEnd);
                float sand = sandHeight * sandDryness * (1f - rock);

                float snowHeight = SmoothThreshold(height, surfaceParameters.SnowHeightStart, surfaceParameters.SnowHeightEnd);
                float snowColdness = 1f - SmoothThreshold(temperature, surfaceParameters.SnowTemperatureStart, surfaceParameters.SnowTemperatureEnd);
                float snow = snowHeight * snowColdness * (1f - rock);

                float lushGrass = SmoothThreshold(moisture, surfaceParameters.GrassMoistureStart, surfaceParameters.GrassMoistureEnd);
                float grass = (1f - rock) * (1f - math.max(sand, snow));
                grass *= math.lerp(0.65f, 1f, lushGrass);

                float4 weights = new float4(grass, rock, sand, snow);
                float total = math.csum(weights);

                if (total <= 0.0001f)
                {
                    return new float4(1f, 0f, 0f, 0f);
                }

                return math.saturate(weights / total);
            }

            private static float SmoothThreshold(float value, float start, float end)
            {
                if (math.abs(end - start) <= 0.0001f)
                {
                    return value >= end ? 1f : 0f;
                }

                return math.smoothstep(start, end, value);
            }

            private static Color32 PackWeights(float4 weights)
            {
                float4 packed = math.clamp(weights * 255f, 0f, 255f);

                return new Color32(
                    (byte)math.round(packed.x),
                    (byte)math.round(packed.y),
                    (byte)math.round(packed.z),
                    (byte)math.round(packed.w));
            }
        }

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh mesh;
        private TerrainChunkSettings settings;
        private Material sharedMaterial;
        private Bounds localMeshBounds;
        private GpuNoiseGenerator gpuNoiseGenerator;

        private NativeArray<float3> terrainSamples;
        private NativeArray<Vector3> vertices;
        private NativeArray<Vector3> normals;
        private NativeArray<Vector2> uvs;
        private NativeArray<Color32> colors;
        private NativeArray<int> triangles;

        private JobHandle activeBuildHandle;
        private bool hasBuildHandle;
        private bool awaitingGpuReadback;
        private bool recycleWhenReady;
        private bool hasQueuedRequest;
        private bool hasValidMeshData;
        private bool pendingBufferRefresh;
        private int generationVersion;
        private BuildRequest queuedRequest;

        public Vector2Int Coordinate { get; private set; }
        public Bounds WorldBounds { get; private set; }
        public bool IsVisible => gameObject.activeSelf && meshRenderer != null && meshRenderer.enabled;
        public bool CanEnterPool => !HasPendingAsyncWork && !hasQueuedRequest;

        private bool HasPendingAsyncWork => awaitingGpuReadback || hasBuildHandle;

        public void Configure(TerrainChunkSettings chunkSettings, Material material)
        {
            if (chunkSettings == null)
            {
                throw new ArgumentNullException(nameof(chunkSettings));
            }

            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            chunkSettings.Validate();

            settings = chunkSettings;
            sharedMaterial = material;
            localMeshBounds = CreateLocalMeshBounds();

            EnsureComponents();
            EnsureMesh();

            meshRenderer.sharedMaterial = sharedMaterial;
            meshRenderer.shadowCastingMode = settings.shadowCastingMode;
            meshRenderer.receiveShadows = settings.receiveShadows;

            ConfigureColliderState();

            if (HasPendingAsyncWork)
            {
                pendingBufferRefresh = true;
            }
            else
            {
                EnsureNativeBuffers();
                pendingBufferRefresh = false;
            }
        }

        public void RequestBuild(Vector2Int coordinate, in TerrainNoiseParameters noiseParameters, GpuNoiseGenerator gpuNoiseDispatcher)
        {
            if (settings == null)
            {
                throw new InvalidOperationException("TerrainChunk must be configured before RequestBuild is called.");
            }

            BuildRequest request = new BuildRequest
            {
                Coordinate = coordinate,
                NoiseParameters = noiseParameters,
                UseGpuBackend = gpuNoiseDispatcher != null &&
                                gpuNoiseDispatcher.IsSupported &&
                                noiseParameters.GenerationBackend == TerrainGenerationBackend.GpuComputeAsync
            };

            gpuNoiseGenerator = gpuNoiseDispatcher;
            recycleWhenReady = false;

            if (HasPendingAsyncWork)
            {
                // Bumping the version here invalidates any in-flight GPU callback or completed job result
                // that still belongs to the older request. The queued request will receive a fresh version
                // again when StartBuild actually schedules its own work.
                generationVersion++;
                queuedRequest = request;
                hasQueuedRequest = true;
                return;
            }

            StartBuild(request);
        }

        public void TickGeneration()
        {
            if (hasBuildHandle && activeBuildHandle.IsCompleted)
            {
                activeBuildHandle.Complete();
                hasBuildHandle = false;

                if (recycleWhenReady)
                {
                    FinalizeRelease();
                }
                else if (hasQueuedRequest)
                {
                    BuildRequest nextRequest = queuedRequest;
                    hasQueuedRequest = false;
                    StartBuild(nextRequest);
                }
                else
                {
                    ApplyMeshData();
                }
            }

            if (!HasPendingAsyncWork)
            {
                if (recycleWhenReady)
                {
                    FinalizeRelease();
                }
                else if (hasQueuedRequest)
                {
                    BuildRequest nextRequest = queuedRequest;
                    hasQueuedRequest = false;
                    StartBuild(nextRequest);
                }
            }
        }

        public void ReleaseToPool()
        {
            recycleWhenReady = true;
            hasQueuedRequest = false;
            DisablePresentation();
            SetVisible(false);

            if (!HasPendingAsyncWork)
            {
                FinalizeRelease();
            }
        }

        public void SetVisible(bool isVisible)
        {
            if (gameObject.activeSelf != isVisible)
            {
                gameObject.SetActive(isVisible);
            }
        }

        private void StartBuild(in BuildRequest request)
        {
            if (!request.NoiseParameters.SupportsAsyncChunkGeneration)
            {
                throw new InvalidOperationException("Asynchronous chunk generation currently supports only Global normalization for height, moisture, and temperature maps.");
            }

            if (pendingBufferRefresh)
            {
                EnsureNativeBuffers();
                pendingBufferRefresh = false;
            }

            Vector2Int previousCoordinate = Coordinate;
            bool canKeepShowingCurrentMesh = hasValidMeshData && previousCoordinate == request.Coordinate && meshRenderer.enabled;

            Coordinate = request.Coordinate;
            transform.position = new Vector3(request.Coordinate.x * settings.chunkSize, 0f, request.Coordinate.y * settings.chunkSize);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            gameObject.name = $"TerrainChunk [{Coordinate.x}, {Coordinate.y}]";
            SetVisible(true);

            if (!canKeepShowingCurrentMesh)
            {
                DisablePresentation();
            }

            int requestVersion = ++generationVersion;

            if (request.UseGpuBackend && gpuNoiseGenerator != null)
            {
                GpuNoiseRequest gpuRequest = new GpuNoiseRequest(
                    settings.vertexResolution,
                    settings.CellSize,
                    new Vector2(transform.position.x, transform.position.z),
                    request.NoiseParameters);

                // The dispatch returns immediately. TerrainChunk keeps the current mesh (if any) alive while the
                // GPU works in the background, and only schedules follow-up jobs after the readback callback fires.
                if (gpuNoiseGenerator.TryDispatch(gpuRequest, result => OnGpuNoiseReadback(requestVersion, request.NoiseParameters, result)))
                {
                    awaitingGpuReadback = true;
                    return;
                }
            }

            ScheduleCpuGeneration(request.NoiseParameters);
        }

        private void OnGpuNoiseReadback(int requestVersion, TerrainNoiseParameters noiseParameters, GpuNoiseReadbackResult result)
        {
            if (this == null)
            {
                result.Dispose();
                return;
            }

            awaitingGpuReadback = false;

            if (requestVersion != generationVersion || recycleWhenReady)
            {
                result.Dispose();
                return;
            }

            if (!result.Success)
            {
                result.Dispose();
                ScheduleCpuGeneration(noiseParameters);
                return;
            }

            CopyTerrainSamplesJob copyJob = new CopyTerrainSamplesJob
            {
                SourceSamples = result.Samples,
                DestinationSamples = terrainSamples
            };

            JobHandle copyHandle = copyJob.Schedule(settings.VertexCount, settings.jobBatchSize);
            JobHandle disposeHandle = result.Dispose(copyHandle);
            ScheduleMeshJobs(copyHandle, disposeHandle);
        }

        private void ScheduleCpuGeneration(in TerrainNoiseParameters noiseParameters)
        {
            SampleTerrainMapsJob sampleJob = new SampleTerrainMapsJob
            {
                TerrainSamples = terrainSamples,
                HeightParameters = noiseParameters.Height,
                MoistureParameters = noiseParameters.Moisture,
                TemperatureParameters = noiseParameters.Temperature,
                Resolution = settings.vertexResolution,
                CellSize = settings.CellSize,
                WorldOrigin = new float2(transform.position.x, transform.position.z)
            };

            JobHandle sampleHandle = sampleJob.Schedule(settings.VertexCount, settings.jobBatchSize);
            ScheduleMeshJobs(sampleHandle, default);
        }

        private void ScheduleMeshJobs(JobHandle sampleDependency, JobHandle trailingDependency)
        {
            BuildVertexDataJob vertexJob = new BuildVertexDataJob
            {
                TerrainSamples = terrainSamples,
                Vertices = vertices,
                Normals = normals,
                Uvs = uvs,
                Colors = colors,
                Resolution = settings.vertexResolution,
                CellSize = settings.CellSize,
                HeightScale = settings.heightScale,
                UvScale = new float2(settings.uvScale.x, settings.uvScale.y),
                SurfaceParameters = settings.surface.ToParameters()
            };

            BuildTrianglesJob trianglesJob = new BuildTrianglesJob
            {
                Triangles = triangles,
                Resolution = settings.vertexResolution
            };

            JobHandle vertexHandle = vertexJob.Schedule(settings.VertexCount, settings.jobBatchSize, sampleDependency);
            JobHandle triangleHandle = trianglesJob.Schedule(settings.QuadCount, settings.jobBatchSize);
            JobHandle geometryHandle = JobHandle.CombineDependencies(vertexHandle, triangleHandle);

            activeBuildHandle = JobHandle.CombineDependencies(geometryHandle, trailingDependency);
            hasBuildHandle = true;
        }

        private void ApplyMeshData()
        {
            mesh.Clear(false);
            mesh.indexFormat = settings.VertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetIndices(triangles, MeshTopology.Triangles, 0, false);
            mesh.bounds = localMeshBounds;

            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = sharedMaterial;
            meshRenderer.enabled = true;

            if (meshCollider != null && settings.generateCollider)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                meshCollider.enabled = true;
            }

            WorldBounds = ConvertLocalBoundsToWorldSpace(localMeshBounds);
            hasValidMeshData = true;
        }

        private void EnsureComponents()
        {
            if (!TryGetComponent(out meshFilter))
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            if (!TryGetComponent(out meshRenderer))
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }
        }

        private void EnsureMesh()
        {
            if (mesh != null)
            {
                return;
            }

            mesh = new Mesh
            {
                name = "TerrainChunkMesh"
            };

            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;
        }

        private void EnsureNativeBuffers()
        {
            int vertexCount = settings.VertexCount;
            int triangleIndexCount = settings.TriangleIndexCount;

            EnsureNativeBuffer(ref terrainSamples, vertexCount);
            EnsureNativeBuffer(ref vertices, vertexCount);
            EnsureNativeBuffer(ref normals, vertexCount);
            EnsureNativeBuffer(ref uvs, vertexCount);
            EnsureNativeBuffer(ref colors, vertexCount);
            EnsureNativeBuffer(ref triangles, triangleIndexCount);
        }

        private void ConfigureColliderState()
        {
            if (settings.generateCollider)
            {
                if (!TryGetComponent(out meshCollider))
                {
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.enabled = false;
                return;
            }

            if (meshCollider != null)
            {
                meshCollider.enabled = false;
                meshCollider.sharedMesh = null;
            }
        }

        private void DisablePresentation()
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }

            if (meshCollider != null)
            {
                meshCollider.enabled = false;
                meshCollider.sharedMesh = null;
            }
        }

        private void FinalizeRelease()
        {
            recycleWhenReady = false;
            DisablePresentation();
            Coordinate = default;
            WorldBounds = default;
            SetVisible(false);
        }

        private Bounds CreateLocalMeshBounds()
        {
            return new Bounds(
                new Vector3(settings.chunkSize * 0.5f, settings.heightScale * 0.5f, settings.chunkSize * 0.5f),
                new Vector3(settings.chunkSize, settings.heightScale, settings.chunkSize));
        }

        private Bounds ConvertLocalBoundsToWorldSpace(Bounds localBounds)
        {
            Vector3 worldCenter = transform.TransformPoint(localBounds.center);
            Vector3 scale = transform.lossyScale;
            Vector3 worldSize = Vector3.Scale(localBounds.size, new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)));

            return new Bounds(worldCenter, worldSize);
        }

        private void OnDestroy()
        {
            if (hasBuildHandle)
            {
                activeBuildHandle.Complete();
                hasBuildHandle = false;
            }

            DisposeNativeBuffer(ref terrainSamples);
            DisposeNativeBuffer(ref vertices);
            DisposeNativeBuffer(ref normals);
            DisposeNativeBuffer(ref uvs);
            DisposeNativeBuffer(ref colors);
            DisposeNativeBuffer(ref triangles);

            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }

        private static void EnsureNativeBuffer<T>(ref NativeArray<T> buffer, int length) where T : struct
        {
            if (buffer.IsCreated && buffer.Length == length)
            {
                return;
            }

            if (buffer.IsCreated)
            {
                buffer.Dispose();
            }

            buffer = new NativeArray<T>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void DisposeNativeBuffer<T>(ref NativeArray<T> buffer) where T : struct
        {
            if (!buffer.IsCreated)
            {
                return;
            }

            buffer.Dispose();
            buffer = default;
        }
    }
}
