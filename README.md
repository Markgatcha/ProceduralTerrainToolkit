# Procedural Terrain Toolkit

Procedural Terrain Toolkit is an open-source Unity package for streaming deterministic terrain chunks around a moving viewer without constant instantiate/destroy churn. It is designed as a foundational terrain engine for fast traversal games such as off-road driving simulators, survival sandboxes, and physics-heavy open worlds where terrain needs to appear continuous while runtime spikes stay under control.

## Goals

- Stream terrain around a player or vehicle in chunk-sized regions
- Reuse chunk GameObjects through pooling to reduce garbage collection pressure
- Generate deterministic terrain from layered Perlin noise so chunks stitch together seamlessly
- Keep the runtime API modular so contributors can extend it with biomes, LODs, splat maps, Jobs/Burst, or physics features
- Ship as a lightweight Unity Package Manager package that can be dropped into a fresh repository

## Included Files

```text
ProceduralTerrainToolkit/
├── package.json
├── README.md
└── Runtime/
    ├── ProceduralTerrainToolkit.asmdef
    └── Scripts/
        ├── ChunkManager.cs
        ├── NoiseGenerator.cs
        └── TerrainChunk.cs
```

## Feature Overview

### `NoiseGenerator.cs`

`NoiseGenerator` is a static utility that creates deterministic height data using layered Perlin noise. It supports:

- seed-based octave offsets
- octave blending
- persistence and lacunarity
- global or local normalization
- reusable sampling contexts for lower runtime overhead
- bulk height-map generation for chunk builds

### `TerrainChunk.cs`

`TerrainChunk` represents one pooled terrain tile. Each chunk:

- caches its `Mesh`, `MeshFilter`, and `MeshRenderer`
- builds a grid mesh from normalized height samples
- reuses managed buffers for heights, vertices, UVs, and triangle indices
- can optionally generate a `MeshCollider`
- exposes chunk coordinate and world bounds information

### `ChunkManager.cs`

`ChunkManager` drives the system at runtime. It:

- tracks a viewer transform
- converts world coordinates into chunk coordinates
- keeps active chunks in a dictionary keyed by `Vector2Int`
- recycles out-of-range chunks into a pool
- refreshes visible chunks on a configurable cadence or after meaningful viewer movement
- rebuilds chunks when terrain or noise settings change

## Why Chunked Streaming Matters

Infinite terrain is rarely truly infinite. Instead, the world is segmented into fixed-size chunks and only the region near the viewer stays active. This approach is critical for fast traversal because it:

1. bounds the number of active meshes in memory
2. avoids rebuilding the entire world as the viewer moves
3. allows chunk reuse through pooling
4. makes future LOD and background generation strategies easier to layer in

When a vehicle crosses chunk boundaries at speed, the manager computes which chunk coordinates should remain visible and either:

- reuses an existing active chunk,
- reactivates a pooled chunk, or
- instantiates a new chunk only when the pool is empty.

That reuse pattern is one of the easiest ways to reduce GC spikes in an infinite world prototype.

## Mathematical Foundation

The toolkit currently uses octave-based Perlin noise, where each octave contributes a higher-frequency, lower-amplitude detail layer.

For each sample position `(x, z)`:

```text
noise(x, z) = Σ(A_i * (2 * Perlin(F_i * x', F_i * z') - 1))
```

Where:

- `A_i` is the octave amplitude
- `F_i` is the octave frequency
- `x'` and `z'` are world coordinates adjusted by seed-derived offsets

The amplitude and frequency progressions follow:

```text
A_0 = 1
A_i = A_(i-1) * persistence

F_0 = 1
F_i = F_(i-1) * lacunarity
```

### What this means visually

- **higher octaves** add smaller terrain detail
- **higher persistence** keeps later octaves stronger, producing rougher terrain
- **higher lacunarity** increases frequency faster, producing denser fine detail
- **larger scale** zooms out the terrain pattern

### Global vs Local Normalization

The toolkit exposes two normalization modes:

- **Global**: uses the theoretical maximum octave amplitude to normalize values consistently across chunks
- **Local**: normalizes from the minimum and maximum values found in the current sampled map

For streamed terrain, **Global normalization is the correct default** because it preserves continuity across chunk boundaries. Local normalization is included for other workflows, but it can make neighboring chunks appear to shift in relative height.

## Chunk Coordinate Mapping

The manager maps world position to chunk coordinates with floor division:

```text
chunkX = floor(worldX / chunkSize)
chunkZ = floor(worldZ / chunkSize)
```

This matters because the same world position always resolves to the same chunk coordinate, including in negative world space. That deterministic mapping is what allows:

- pooling without losing track of logical world placement
- seamless noise sampling at chunk edges
- consistent rebuilds when a chunk is recycled

## Mesh Generation Strategy

Each chunk generates a regular grid mesh:

- `vertexResolution x vertexResolution` vertices
- `(vertexResolution - 1)^2 * 2` triangles
- UVs based on configurable tiling

Heights are sampled in world space rather than chunk-local pseudo-random space, which means the final edge row of one chunk lines up with the first edge row of the next chunk. That is the key requirement for seamless chunk stitching.

## Performance Considerations

This package is written for clarity and production-minded extension, but it already includes several practical performance choices:

- chunk pooling instead of frequent `Instantiate` and `Destroy`
- reusable managed buffers inside `TerrainChunk`
- shared material assignment across chunks
- refresh throttling via `updateIntervalSeconds`
- early refreshes only when the viewer moves far enough
- reusable noise sampling context derived from validated settings

### What it does **not** do yet

This first version intentionally stops short of more specialized systems such as:

- burst-compiled mesh jobs
- background worker threads
- distance-based LOD meshes
- biome blending
- splat/texture painting
- vegetation placement
- road carving or erosion simulation

Those are good future extensions once the package contract is stable.

## Installation

### Option 1: Git URL via Unity Package Manager

1. Push this folder structure to a Git repository.
2. Open your Unity project.
3. Open `Window > Package Manager`.
4. Click the `+` button.
5. Choose **Add package from git URL...**
6. Paste the repository URL.

Example:

```text
https://github.com/your-org/procedural-terrain-toolkit.git
```

### Option 2: Local package

1. Copy the `ProceduralTerrainToolkit` folder somewhere accessible on disk.
2. In your Unity project, open `Packages/manifest.json`.
3. Add a local file dependency:

```json
{
  "dependencies": {
    "com.proceduralterraintoolkit.core": "file:../ProceduralTerrainToolkit"
  }
}
```

### Option 3: Embedded package

Place the folder directly under your Unity project's `Packages/` directory:

```text
YourUnityProject/
└── Packages/
    └── ProceduralTerrainToolkit/
```

## Unity Version

- Minimum target: **Unity 2022.3.17 LTS**

The package has been structured around standard runtime APIs available in Unity 2022 LTS.

## Basic Usage

### 1. Import the package

Install the package through one of the methods above.

### 2. Create a terrain material

Create or assign a material that can be shared across all streamed chunks. A simple lit material is enough for initial testing.

### 3. Add `ChunkManager` to a scene object

Create an empty GameObject such as `Terrain System`, then add the `ChunkManager` component.

### 4. Assign the viewer

Drag your player, vehicle, or camera rig transform into the `Viewer` field.

### 5. Configure chunk settings

Recommended starting values:

- `Chunk Size`: `128`
- `Vertex Resolution`: `65`
- `Height Scale`: `32`
- `Generate Collider`: `false`
- `Visible Chunk Radius`: `4`
- `Update Interval Seconds`: `0.1`
- `Viewer Movement Threshold`: `8`

### 6. Configure noise settings

Recommended starting values:

- `Scale`: `128`
- `Octaves`: `4`
- `Persistence`: `0.5`
- `Lacunarity`: `2.0`
- `Normalization Mode`: `Global`

### 7. Press Play

When the scene starts:

- the manager validates the configuration,
- creates a reusable chunk root,
- generates the noise sampling context,
- builds the initial visible chunk set,
- and then streams chunks as the viewer moves.

## Runtime Scripting Example

The package is primarily inspector-driven, but the manager also exposes runtime hooks:

```csharp
using ProceduralTerrainToolkit;
using UnityEngine;

public sealed class TerrainBootstrapExample : MonoBehaviour
{
    [SerializeField] private ChunkManager chunkManager;
    [SerializeField] private Transform vehicle;
    [SerializeField] private Material terrainMaterial;

    private void Start()
    {
        chunkManager.SetViewer(vehicle);
        chunkManager.SetTerrainMaterial(terrainMaterial);

        chunkManager.NoiseSettings.seed = 2026;
        chunkManager.NoiseSettings.scale = 180f;
        chunkManager.ChunkSettings.chunkSize = 160f;

        chunkManager.MarkSettingsDirty();
        chunkManager.ForceRefresh();
    }
}
```

## Extension Ideas

### Add colliders only where needed

Collider generation is off by default because physics mesh updates are more expensive than render mesh updates. For driving gameplay, a common next step is to enable colliders only:

- near the viewer,
- on gameplay-critical chunks,
- or on a lower-resolution collision mesh.

### Add biomes

You can extend the system by sampling additional noise fields for:

- moisture
- heat
- erosion masks
- biome selection
- material blending

### Add LOD

The current grid mesh generation is a clean foundation for chunk LODs. The typical next step is to:

1. keep high-resolution chunks near the viewer
2. generate lower-resolution meshes farther away
3. blend or stitch LOD boundaries carefully

### Move generation off the main thread

Once the API stabilizes, the mesh-data stage can be moved into:

- Unity Jobs
- Burst-compiled jobs
- task-based worker pipelines

The current package keeps data ownership simple so that refactor is straightforward later.

## Contributor Notes

If you plan to fork or expand the toolkit:

- keep world-space sampling deterministic
- preserve seam alignment at chunk borders
- avoid per-frame allocations in the streaming path
- prefer explicit validation over silent fallbacks
- test negative-world coordinates, not just positive positions

## Known Trade-Offs

- Extremely high chunk resolutions will still be expensive without multithreaded generation.
- `Mesh.RecalculateNormals()` is clear and reliable, but it is not the final word in performance optimization.
- Local normalization is included for completeness, but streamed worlds should stay on Global normalization.

## Next Recommended Milestones

1. add optional LOD meshes
2. add biome layers
3. add asynchronous mesh-data generation
4. add sample scenes and automated play-mode validation

## License

Choose the license that fits your repository goals before publishing (for example MIT or Apache-2.0).
