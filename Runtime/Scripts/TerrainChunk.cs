using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralTerrainToolkit
{
    [Serializable]
    public sealed class TerrainChunkSettings
    {
        [Min(1f)]
        [Tooltip("World-space size of one chunk along the X and Z axes.")]
        public float chunkSize = 128f;

        [Min(2)]
        [Tooltip("Number of vertices generated per axis. Higher values increase detail and cost.")]
        public int vertexResolution = 65;

        [Min(0.01f)]
        [Tooltip("Multiplier applied to normalized noise values when building the mesh.")]
        public float heightScale = 32f;

        [Tooltip("Tiling applied to chunk UVs. Larger values repeat the material more frequently.")]
        public Vector2 uvScale = Vector2.one;

        [Tooltip("Optional collider generation. Disabled by default for lower runtime cost.")]
        public bool generateCollider = false;

        [Tooltip("Shadow casting mode used by the chunk renderer.")]
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;

        [Tooltip("Whether chunk renderers should receive scene shadows.")]
        public bool receiveShadows = true;

        public float CellSize => chunkSize / (vertexResolution - 1);
        public int VertexCount => vertexResolution * vertexResolution;
        public int TriangleIndexCount => (vertexResolution - 1) * (vertexResolution - 1) * 6;

        public void Validate()
        {
            chunkSize = Mathf.Max(1f, chunkSize);
            vertexResolution = Mathf.Max(2, vertexResolution);
            heightScale = Mathf.Max(0.01f, heightScale);

            if (uvScale.x <= 0f)
            {
                uvScale.x = 1f;
            }

            if (uvScale.y <= 0f)
            {
                uvScale.y = 1f;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class TerrainChunk : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh mesh;
        private TerrainChunkSettings settings;
        private Material sharedMaterial;
        private Vector3[] vertices;
        private Vector2[] uvs;
        private int[] triangles;
        private float[] heightSamples;

        public Vector2Int Coordinate { get; private set; }
        public Bounds WorldBounds { get; private set; }
        public bool IsVisible => gameObject.activeSelf;

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

            EnsureComponents();
            EnsureMesh();
            EnsureGeometryBuffers();

            meshRenderer.sharedMaterial = sharedMaterial;
            meshRenderer.shadowCastingMode = settings.shadowCastingMode;
            meshRenderer.receiveShadows = settings.receiveShadows;

            ConfigureColliderState();
        }

        public void Build(Vector2Int coordinate, in NoiseSamplingContext noiseContext)
        {
            if (settings == null)
            {
                throw new InvalidOperationException("TerrainChunk must be configured before Build is called.");
            }

            if (!noiseContext.IsValid)
            {
                throw new InvalidOperationException("A valid NoiseSamplingContext is required to build a terrain chunk.");
            }

            SetVisible(true);
            Coordinate = coordinate;
            transform.position = new Vector3(coordinate.x * settings.chunkSize, 0f, coordinate.y * settings.chunkSize);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            gameObject.name = $"TerrainChunk [{Coordinate.x}, {Coordinate.y}]";

            float cellSize = settings.CellSize;
            int resolution = settings.vertexResolution;

            // A reusable sample buffer avoids allocating a temporary height map on every rebuild.
            NoiseGenerator.FillHeightMap(
                heightSamples,
                resolution,
                resolution,
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(cellSize, cellSize),
                in noiseContext);

            int index = 0;
            for (int z = 0; z < resolution; z++)
            {
                float localZ = z * cellSize;

                for (int x = 0; x < resolution; x++)
                {
                    float localX = x * cellSize;
                    float vertexHeight = heightSamples[index] * settings.heightScale;

                    vertices[index] = new Vector3(localX, vertexHeight, localZ);
                    index++;
                }
            }

            ApplyMeshData();
        }

        public void ReleaseToPool()
        {
            Coordinate = default;

            if (meshCollider != null)
            {
                meshCollider.enabled = false;
                meshCollider.sharedMesh = null;
            }

            SetVisible(false);
        }

        public void SetVisible(bool isVisible)
        {
            if (gameObject.activeSelf != isVisible)
            {
                gameObject.SetActive(isVisible);
            }
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

        private void EnsureGeometryBuffers()
        {
            int vertexCount = settings.VertexCount;
            if (vertices == null || vertices.Length != vertexCount ||
                uvs == null || uvs.Length != vertexCount ||
                heightSamples == null || heightSamples.Length != vertexCount)
            {
                vertices = new Vector3[vertexCount];
                uvs = new Vector2[vertexCount];
                heightSamples = new float[vertexCount];
            }

            int triangleIndexCount = settings.TriangleIndexCount;
            if (triangles == null || triangles.Length != triangleIndexCount)
            {
                triangles = new int[triangleIndexCount];
                PopulateTriangleBuffer();
            }

            mesh.indexFormat = vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            PopulateUvBuffer();
        }

        private void PopulateTriangleBuffer()
        {
            int triangleIndex = 0;
            int resolution = settings.vertexResolution;

            // The winding order is intentionally counterclockwise from above so recalculated normals face upward.
            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int vertexIndex = (z * resolution) + x;

                    triangles[triangleIndex++] = vertexIndex;
                    triangles[triangleIndex++] = vertexIndex + resolution;
                    triangles[triangleIndex++] = vertexIndex + resolution + 1;

                    triangles[triangleIndex++] = vertexIndex;
                    triangles[triangleIndex++] = vertexIndex + resolution + 1;
                    triangles[triangleIndex++] = vertexIndex + 1;
                }
            }
        }

        private void PopulateUvBuffer()
        {
            int resolution = settings.vertexResolution;
            float uvStepX = settings.uvScale.x / (resolution - 1);
            float uvStepY = settings.uvScale.y / (resolution - 1);
            int index = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    uvs[index] = new Vector2(x * uvStepX, z * uvStepY);
                    index++;
                }
            }
        }

        private void ConfigureColliderState()
        {
            if (settings.generateCollider)
            {
                if (!TryGetComponent(out meshCollider))
                {
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.enabled = true;
                return;
            }

            if (meshCollider != null)
            {
                meshCollider.enabled = false;
                meshCollider.sharedMesh = null;
            }
        }

        private void ApplyMeshData()
        {
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.sharedMesh = mesh;

            if (meshCollider != null && settings.generateCollider)
            {
                meshCollider.enabled = true;
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
            }

            WorldBounds = ConvertLocalBoundsToWorldSpace(mesh.bounds);
        }

        private void OnDestroy()
        {
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
    }
}
