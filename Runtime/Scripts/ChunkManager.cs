using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralTerrainToolkit
{
    [DisallowMultipleComponent]
    public sealed class ChunkManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField]
        [Tooltip("Player, vehicle, or camera transform that drives chunk streaming.")]
        private Transform viewer;

        [SerializeField]
        [Tooltip("Shared material assigned to every generated chunk.")]
        private Material terrainMaterial;

        [SerializeField]
        [Tooltip("Optional compute shader used when GPU terrain noise generation is enabled.")]
        private ComputeShader noiseComputeShader;

        [Header("Streaming")]
        [SerializeField]
        [Min(0)]
        [Tooltip("How many chunk rings should remain active around the viewer.")]
        private int visibleChunkRadius = 4;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Minimum time between visibility refreshes.")]
        private float updateIntervalSeconds = 0.1f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Distance the viewer must move before forcing an early refresh.")]
        private float viewerMovementThreshold = 8f;

        [Header("Generation")]
        [SerializeField]
        private TerrainChunkSettings terrainChunkSettings = new TerrainChunkSettings();

        [SerializeField]
        private NoiseSettings noiseSettings = new NoiseSettings();

        private readonly Dictionary<Vector2Int, TerrainChunk> activeChunks = new Dictionary<Vector2Int, TerrainChunk>();
        private readonly Queue<TerrainChunk> pooledChunks = new Queue<TerrainChunk>();
        private readonly HashSet<Vector2Int> requiredCoordinates = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> recycleBuffer = new List<Vector2Int>();
        private readonly List<TerrainChunk> coolingDownChunks = new List<TerrainChunk>();

        private TerrainNoiseParameters noiseParameters;
        private GpuNoiseGenerator gpuNoiseGenerator;
        private Transform chunkRoot;
        private Vector3 lastViewerPosition;
        private float nextRefreshTime;
        private bool runtimeReady;
        private bool settingsDirty;
        private bool hasLoggedGpuFallback;
        private int createdChunkCount;

        public int ActiveChunkCount => activeChunks.Count;
        public int PooledChunkCount => pooledChunks.Count;
        public Transform Viewer => viewer;
        public TerrainChunkSettings ChunkSettings => terrainChunkSettings;
        public NoiseSettings NoiseSettings => noiseSettings;

        private void Awake()
        {
            EnsureSerializableSettings();
        }

        private void Start()
        {
            runtimeReady = PrepareRuntime();
            if (runtimeReady)
            {
                ForceRefresh();
            }
        }

        private void Update()
        {
            if (!runtimeReady)
            {
                return;
            }

            ServiceChunkPipelines();

            if (settingsDirty)
            {
                if (!RefreshNoiseConfiguration())
                {
                    return;
                }

                RefreshVisibleChunks(forceRebuildExisting: true);
                settingsDirty = false;
                lastViewerPosition = viewer.position;
                nextRefreshTime = Time.unscaledTime + updateIntervalSeconds;
                return;
            }

            Vector3 currentViewerPosition = viewer.position;
            float thresholdSqr = viewerMovementThreshold * viewerMovementThreshold;
            bool movedFarEnough = (currentViewerPosition - lastViewerPosition).sqrMagnitude >= thresholdSqr;
            bool refreshDue = Time.unscaledTime >= nextRefreshTime;

            if (!movedFarEnough && !refreshDue)
            {
                return;
            }

            RefreshVisibleChunks(forceRebuildExisting: false);
            lastViewerPosition = currentViewerPosition;
            nextRefreshTime = Time.unscaledTime + updateIntervalSeconds;
        }

        private void OnValidate()
        {
            EnsureSerializableSettings();
            terrainChunkSettings.Validate();
            noiseSettings.Validate();

            visibleChunkRadius = Mathf.Max(0, visibleChunkRadius);
            updateIntervalSeconds = Mathf.Max(0.01f, updateIntervalSeconds);
            viewerMovementThreshold = Mathf.Max(0f, viewerMovementThreshold);
            settingsDirty = true;
        }

        private void OnDestroy()
        {
            gpuNoiseGenerator?.Dispose();
        }

        [ContextMenu("Force Refresh Visible Chunks")]
        public void ForceRefresh()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("ChunkManager refreshes terrain in Play Mode. Enter Play Mode to generate streamed chunks.", this);
                return;
            }

            if (!runtimeReady)
            {
                runtimeReady = PrepareRuntime();
                if (!runtimeReady)
                {
                    return;
                }
            }

            if (!RefreshNoiseConfiguration())
            {
                return;
            }

            RefreshVisibleChunks(forceRebuildExisting: true);
            settingsDirty = false;
            lastViewerPosition = viewer.position;
            nextRefreshTime = Time.unscaledTime + updateIntervalSeconds;
        }

        public void SetViewer(Transform viewerTransform)
        {
            if (viewerTransform == null)
            {
                throw new ArgumentNullException(nameof(viewerTransform));
            }

            viewer = viewerTransform;
            settingsDirty = true;

            if (runtimeReady)
            {
                lastViewerPosition = viewer.position;
                nextRefreshTime = 0f;
            }
        }

        public void SetTerrainMaterial(Material material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            terrainMaterial = material;
            settingsDirty = true;
            nextRefreshTime = 0f;
        }

        public void MarkSettingsDirty()
        {
            settingsDirty = true;
            nextRefreshTime = 0f;
        }

        private bool PrepareRuntime()
        {
            EnsureSerializableSettings();
            terrainChunkSettings.Validate();
            noiseSettings.Validate();

            visibleChunkRadius = Mathf.Max(0, visibleChunkRadius);
            updateIntervalSeconds = Mathf.Max(0.01f, updateIntervalSeconds);
            viewerMovementThreshold = Mathf.Max(0f, viewerMovementThreshold);

            if (viewer == null)
            {
                Debug.LogError("ChunkManager requires a viewer Transform before runtime streaming can begin.", this);
                enabled = false;
                return false;
            }

            if (terrainMaterial == null)
            {
                Debug.LogError("ChunkManager requires a terrain Material before runtime streaming can begin.", this);
                enabled = false;
                return false;
            }

            EnsureChunkRoot();

            if (!RefreshNoiseConfiguration())
            {
                enabled = false;
                return false;
            }

            lastViewerPosition = viewer.position;
            nextRefreshTime = 0f;
            enabled = true;
            return true;
        }

        private bool RefreshNoiseConfiguration()
        {
            noiseSettings.Validate();
            noiseParameters = NoiseGenerator.CreateParameters(noiseSettings);

            if (!noiseParameters.SupportsAsyncChunkGeneration)
            {
                Debug.LogError(
                    "Asynchronous chunk generation currently supports only Global normalization for height, moisture, and temperature maps.",
                    this);
                return false;
            }

            gpuNoiseGenerator?.Dispose();
            gpuNoiseGenerator = null;

            if (noiseParameters.GenerationBackend != TerrainGenerationBackend.GpuComputeAsync)
            {
                hasLoggedGpuFallback = false;
                return true;
            }

            if (noiseComputeShader == null)
            {
                LogGpuFallback("GPU noise generation is enabled, but no NoiseGenerator.compute shader has been assigned. Falling back to Burst CPU jobs.");
                return true;
            }

            gpuNoiseGenerator = new GpuNoiseGenerator(noiseComputeShader);
            if (!gpuNoiseGenerator.IsSupported)
            {
                gpuNoiseGenerator.Dispose();
                gpuNoiseGenerator = null;
                LogGpuFallback("GPU noise generation is enabled, but this device does not support compute shaders with AsyncGPUReadback. Falling back to Burst CPU jobs.");
                return true;
            }

            hasLoggedGpuFallback = false;
            return true;
        }

        private void RefreshVisibleChunks(bool forceRebuildExisting)
        {
            requiredCoordinates.Clear();
            recycleBuffer.Clear();

            Vector2Int viewerChunkCoordinate = WorldToChunkCoordinate(viewer.position);
            float chunkSize = terrainChunkSettings.chunkSize;
            float maxVisibleDistance = visibleChunkRadius * chunkSize;
            float maxVisibleDistanceSqr = maxVisibleDistance * maxVisibleDistance;

            for (int zOffset = -visibleChunkRadius; zOffset <= visibleChunkRadius; zOffset++)
            {
                for (int xOffset = -visibleChunkRadius; xOffset <= visibleChunkRadius; xOffset++)
                {
                    Vector2Int coordinate = new Vector2Int(
                        viewerChunkCoordinate.x + xOffset,
                        viewerChunkCoordinate.y + zOffset);

                    if (GetHorizontalSqrDistanceToChunk(viewer.position, coordinate, chunkSize) > maxVisibleDistanceSqr)
                    {
                        continue;
                    }

                    requiredCoordinates.Add(coordinate);

                    if (activeChunks.TryGetValue(coordinate, out TerrainChunk existingChunk))
                    {
                        if (forceRebuildExisting)
                        {
                            existingChunk.Configure(terrainChunkSettings, terrainMaterial);
                            existingChunk.RequestBuild(coordinate, noiseParameters, gpuNoiseGenerator);
                        }
                        else
                        {
                            existingChunk.SetVisible(true);
                        }

                        continue;
                    }

                    TerrainChunk chunk = AcquireChunk();
                    chunk.Configure(terrainChunkSettings, terrainMaterial);
                    chunk.RequestBuild(coordinate, noiseParameters, gpuNoiseGenerator);
                    activeChunks.Add(coordinate, chunk);
                }
            }

            foreach (KeyValuePair<Vector2Int, TerrainChunk> pair in activeChunks)
            {
                if (!requiredCoordinates.Contains(pair.Key))
                {
                    recycleBuffer.Add(pair.Key);
                }
            }

            for (int index = 0; index < recycleBuffer.Count; index++)
            {
                RecycleChunk(recycleBuffer[index]);
            }
        }

        private void ServiceChunkPipelines()
        {
            foreach (KeyValuePair<Vector2Int, TerrainChunk> pair in activeChunks)
            {
                pair.Value.TickGeneration();
            }

            for (int index = coolingDownChunks.Count - 1; index >= 0; index--)
            {
                TerrainChunk chunk = coolingDownChunks[index];
                chunk.TickGeneration();

                if (chunk.CanEnterPool)
                {
                    coolingDownChunks.RemoveAt(index);
                    pooledChunks.Enqueue(chunk);
                }
            }
        }

        private TerrainChunk AcquireChunk()
        {
            TerrainChunk chunk = pooledChunks.Count > 0 ? pooledChunks.Dequeue() : CreateChunkInstance();
            chunk.transform.SetParent(chunkRoot, false);
            chunk.transform.localRotation = Quaternion.identity;
            chunk.transform.localScale = Vector3.one;
            chunk.SetVisible(true);
            return chunk;
        }

        private TerrainChunk CreateChunkInstance()
        {
            GameObject chunkObject = new GameObject($"TerrainChunk Pool Entry {createdChunkCount}");
            chunkObject.transform.SetParent(chunkRoot, false);
            createdChunkCount++;
            return chunkObject.AddComponent<TerrainChunk>();
        }

        private void RecycleChunk(Vector2Int coordinate)
        {
            TerrainChunk chunk = activeChunks[coordinate];
            activeChunks.Remove(coordinate);
            chunk.ReleaseToPool();

            if (chunk.CanEnterPool)
            {
                pooledChunks.Enqueue(chunk);
            }
            else
            {
                coolingDownChunks.Add(chunk);
            }
        }

        private Vector2Int WorldToChunkCoordinate(Vector3 worldPosition)
        {
            int x = Mathf.FloorToInt(worldPosition.x / terrainChunkSettings.chunkSize);
            int z = Mathf.FloorToInt(worldPosition.z / terrainChunkSettings.chunkSize);
            return new Vector2Int(x, z);
        }

        private static float GetHorizontalSqrDistanceToChunk(Vector3 viewerPosition, Vector2Int coordinate, float chunkSize)
        {
            float minX = coordinate.x * chunkSize;
            float maxX = minX + chunkSize;
            float minZ = coordinate.y * chunkSize;
            float maxZ = minZ + chunkSize;

            float deltaX = 0f;
            if (viewerPosition.x < minX)
            {
                deltaX = minX - viewerPosition.x;
            }
            else if (viewerPosition.x > maxX)
            {
                deltaX = viewerPosition.x - maxX;
            }

            float deltaZ = 0f;
            if (viewerPosition.z < minZ)
            {
                deltaZ = minZ - viewerPosition.z;
            }
            else if (viewerPosition.z > maxZ)
            {
                deltaZ = viewerPosition.z - maxZ;
            }

            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private void EnsureChunkRoot()
        {
            if (chunkRoot != null)
            {
                return;
            }

            GameObject rootObject = new GameObject("Terrain Chunks");
            rootObject.transform.SetParent(transform, false);
            chunkRoot = rootObject.transform;
        }

        private void EnsureSerializableSettings()
        {
            if (terrainChunkSettings == null)
            {
                terrainChunkSettings = new TerrainChunkSettings();
                Debug.LogWarning("ChunkManager restored missing TerrainChunkSettings with default values.", this);
            }

            if (noiseSettings == null)
            {
                noiseSettings = new NoiseSettings();
                Debug.LogWarning("ChunkManager restored missing NoiseSettings with default values.", this);
            }
        }

        private void LogGpuFallback(string message)
        {
            if (hasLoggedGpuFallback)
            {
                return;
            }

            hasLoggedGpuFallback = true;
            Debug.LogWarning(message, this);
        }
    }
}
