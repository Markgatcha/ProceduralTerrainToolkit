# Procedural Terrain Toolkit

Procedural Terrain Toolkit is a beta-stage Unity Package Manager package for streaming chunked terrain in traversal-heavy games without blocking the main thread. The package combines Burst-compiled jobs, optional GPU noise generation through compute shaders and `AsyncGPUReadback`, dual-noise biome signals, and vertex-color splat weights for lightweight terrain shading workflows.

If you want to contribute, please read [`CONTRIBUTING.md`](CONTRIBUTING.md) before opening a Pull Request.

## Beta Highlights

- **Burst + Jobs mesh generation** using `IJobParallelFor` for noise sampling, vertices, normals, UVs, and triangle buffers
- **Optional GPU noise generation** via `NoiseGenerator.compute` and an async C# dispatcher
- **Dual-noise biomes** with synchronized **moisture** and **temperature** maps alongside terrain height
- **Procedural splat weights** encoded into vertex colors:
  - **R** = grass
  - **G** = rock
  - **B** = sand
  - **A** = snow
- **Chunk pooling and async completion** designed for high-speed traversal
- **GitHub beta release automation** for tagged prereleases

## Package Layout

```text
ProceduralTerrainToolkit/
├── .github/
│   └── workflows/
│       └── release.yml
├── package.json
├── README.md
└── Runtime/
    ├── ProceduralTerrainToolkit.asmdef
    ├── Scripts/
    │   ├── ChunkManager.cs
    │   ├── GpuNoiseGenerator.cs
    │   ├── NoiseGenerator.cs
    │   └── TerrainChunk.cs
    └── Shaders/
        └── NoiseGenerator.compute
```

## Runtime Architecture

### 1. ChunkManager

`ChunkManager` is the streaming coordinator. It:

- tracks a viewer transform
- determines which chunk coordinates should be visible
- reuses chunk GameObjects from a pool
- services async chunk pipelines every frame
- rebuilds visible chunks when generation settings change
- chooses between CPU Burst jobs and GPU compute noise generation

### 2. TerrainChunk

`TerrainChunk` owns the per-chunk generation state. It:

- schedules Burst jobs for CPU terrain sampling and mesh buffer generation
- waits for `AsyncGPUReadback` completion without blocking the main thread
- schedules follow-up jobs after GPU data becomes available
- applies mesh data only after jobs finish
- delays pool reuse when a chunk is still finishing background work

### 3. NoiseGenerator

`NoiseGenerator` converts authoring settings into runtime noise parameters and exposes Burst-safe sampling helpers. The CPU and GPU paths share the same layered value-noise model so biome data remains consistent regardless of the backend.

### 4. GpuNoiseGenerator

`GpuNoiseGenerator` dispatches the compute shader, allocates the temporary GPU buffer, and converts the async readback into a persistent native array so `TerrainChunk` can continue the pipeline through jobs.

## Async Generation Flow

### CPU Burst path

1. `ChunkManager` requests a build for a chunk.
2. `TerrainChunk` schedules a Burst job to sample:
   - height
   - moisture
   - temperature
3. A second Burst job converts those samples into:
   - vertices
   - normals
   - UVs
   - vertex-color splat weights
4. A triangle job fills the index buffer in parallel.
5. The chunk applies the finished mesh only after `JobHandle.IsCompleted` reports ready.

### GPU compute path

1. `ChunkManager` requests a build with GPU generation enabled.
2. `TerrainChunk` asks `GpuNoiseGenerator` to dispatch `NoiseGenerator.compute`.
3. The compute shader writes height/moisture/temperature samples into a structured buffer.
4. `AsyncGPUReadback.Request` returns immediately, so the frame continues.
5. Once Unity reports the readback complete, `TerrainChunk` schedules jobs that copy the samples into its persistent buffers and builds the mesh.
6. If GPU support is unavailable, the system logs a warning and falls back to the Burst CPU path.

## Noise Model

The toolkit uses layered, deterministic value noise instead of a Unity `Terrain` component dependency. Each channel is computed as fractal noise:

```text
channel(x, z) = Σ(amplitude_i * valueNoise(frequency_i * (world + offset)))
```

Each octave uses:

```text
amplitude_(i+1) = amplitude_i * persistence
frequency_(i+1) = frequency_i * lacunarity
```

The resulting raw value is normalized against the theoretical maximum amplitude sum, which is why **Global normalization** is required for the async streaming path. Every chunk agrees on the same range, so seams remain stable while chunks are built independently.

## Biome Maps

Every sample stores three terrain signals:

- **Height**: drives mesh displacement
- **Moisture**: favors grass or sand depending on altitude
- **Temperature**: favors snow in colder, higher regions

These channels are computed in lockstep so biome classification does not require a second sampling pass on the main thread.

## Procedural Splat Weights

The default surface classifier produces four blend weights:

- **Grass**: moderate slopes with decent moisture
- **Rock**: steep slopes
- **Sand**: low altitude and dry areas
- **Snow**: high altitude and cold areas

The weights are stored in vertex colors so you can plug them into a custom shader, Shader Graph, URP/HDRP lit shader extension, or material property workflow without needing Unity's terrain splat system.

## Installation

### Option 1: Install from a tagged Git URL

Using an explicit tag is the safest way to choose between the stable channel and preview builds.

1. Open your Unity project.
2. Open `Window > Package Manager`.
3. Click the `+` menu.
4. Choose **Add package from git URL...**
5. Enter one of these URLs:

```text
Stable release:
https://github.com/Markgatcha/ProceduralTerrainToolkit.git#v0.1.0

Latest beta prerelease:
https://github.com/Markgatcha/ProceduralTerrainToolkit.git#v0.2.0-beta.1
```

If you install from the bare repository URL without `#tag`, Unity follows the repository's default branch, which may contain beta or in-progress work.

### Option 2: Local development package

Edit your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.proceduralterraintoolkit.core": "file:../ProceduralTerrainToolkit"
  }
}
```

## Package Dependencies

The beta package declares:

- `com.unity.burst`
- `com.unity.collections`
- `com.unity.mathematics`

Target editor version: **Unity 2022.3.17 LTS or newer in the 2022.3 line**

## Scene Setup

1. Create an empty GameObject such as `Terrain System`.
2. Add `ChunkManager`.
3. Assign:
   - a viewer transform
   - a terrain material
   - optionally `Runtime/Shaders/NoiseGenerator.compute` when using GPU generation
4. Configure `TerrainChunkSettings` and `NoiseSettings`.
5. Press Play.

## Recommended Beta Defaults

### Chunk settings

- `Chunk Size`: `128`
- `Vertex Resolution`: `65`
- `Height Scale`: `32`
- `Job Batch Size`: `64`
- `Generate Collider`: `false`

### Streaming settings

- `Visible Chunk Radius`: `4`
- `Update Interval Seconds`: `0.1`
- `Viewer Movement Threshold`: `8`

### Noise settings

- Backend: `CpuBurstJobs` for universal compatibility
- Height scale: `128`
- Moisture scale: `192`
- Temperature scale: `256`
- All channels: `Global` normalization

## Material / Shader Integration

The mesh renderer receives vertex colors as splat weights. A typical shader setup is:

- sample 4 terrain textures
- multiply each sample by the corresponding vertex-color channel
- normalize or blend in the fragment stage

This keeps the package renderer-agnostic and works in Built-in, URP, or HDRP with a compatible material setup.

## GitHub Release Workflow

The repository now includes `.github/workflows/release.yml`.

When you push a tag such as:

```text
v0.1.0
v0.2.0-beta.1
```

the workflow:

1. checks out the repository
2. validates that the tag matches `package.json`
3. copies the package contents into a staging directory
4. builds `.zip` and `.tgz` archives
5. creates a GitHub release and uploads the package archives

Tags containing a prerelease suffix such as `-beta.1` are published as **prereleases**. Plain semantic-version tags such as `v0.1.0` are published as **regular releases**.

## Current Trade-Offs

- Mesh application still occurs on the main thread because Unity mesh objects are main-thread-owned.
- GPU readback is asynchronous, but the copied data still needs to be consumed by jobs before rendering.
- Global normalization is required for the async terrain path.
- Collider generation is still more expensive than render-only chunks; keep it disabled unless the gameplay layer requires it.

## Suggested Next Milestones

1. chunk LODs
2. background normal-map baking
3. collision-only low-resolution meshes
4. ECS/DOTS world streaming wrappers
5. shader graph sample assets under `Samples~`

