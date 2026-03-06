# Contributing to Procedural Terrain Toolkit

First off, thank you for considering contributing. It is people like you who help turn this toolkit into a high-performance resource for the Unity community.

This project is currently in **Active Beta** and the `main` branch moves quickly. If you are consuming the package in a production project, prefer a tagged release. If you are contributing code, branch from `main` and expect active iteration.

## How Can I Contribute?

### 1. Reporting Bugs

- **Check for duplicates first.** Search the Issues tab before opening a new report.
- **Be specific.** Include:
  - Unity version
  - render pipeline (Built-in, URP, or HDRP)
  - operating system
  - graphics API when relevant (DX11, DX12, Metal, Vulkan)
  - clear steps to reproduce the crash, stall, artifact, or visual glitch
- **Tell us what you expected to happen** and what actually happened instead.
- **Screenshots and videos help.** If the problem is a terrain artifact, seam, spike, splat issue, or biome mismatch, visual evidence is extremely useful.
- **Attach logs when possible.** Console output, stack traces, and profiler captures help a lot when investigating frame spikes or async-generation failures.

### 2. Suggesting Features

- Open an issue and label it **[Feature Request]**.
- Explain the **why**, not just the **what**:
  - How does this improve runtime performance?
  - How does this improve developer workflow?
  - How does this help large-world streaming or async generation?
- Features that make better use of **DOTS**, **Burst**, **Jobs**, **Compute Shaders**, or **GPU acceleration** are especially high priority.
- If the proposal changes architecture in a major way, describe the expected trade-offs and migration impact.

### 3. Code Contributions

We use a **Fork and Pull** workflow:

1. Fork the repository.
2. Create a branch from `main`.
3. Keep commits small and atomic.
   - Good example: `Fix: guard stale AsyncGPUReadback callback`
   - Good example: `Feat: add biome parity tests for GPU noise`
4. Test your changes locally before opening a Pull Request.
5. Make sure any GitHub Actions checks on your PR are passing before requesting review.
6. Open a Pull Request and link any related issue or discussion.

If your change touches async generation, job scheduling, GPU readback flow, or chunk lifecycle logic, include a short explanation of the failure mode you were addressing.

## Coding Standards

To keep Procedural Terrain Toolkit fast, maintainable, and contributor-friendly, please follow these rules:

### DOTS first

- If a task can be expressed as a job or data-oriented pipeline, prefer that over main-thread polling or heavy `MonoBehaviour.Update` logic.
- Avoid introducing frame-time spikes with synchronous generation or blocking GPU/CPU synchronization.
- Favor explicit job dependencies and deterministic data flow over hidden side effects.

### Naming

- Use **PascalCase** for types, methods, properties, and public fields.
- Use **_camelCase** for private fields.
- Use clear names that communicate data ownership and pipeline stage.

### Safety

- Always handle `AsyncGPUReadback` results defensively.
- Never assume GPU data will be ready on the next frame.
- Guard against stale callbacks, recycled chunks, invalid buffers, and mismatched resolutions.
- Avoid silent fallbacks that hide correctness or performance problems.

### Documentation

- All public methods should have XML comments (`/// <summary>`).
- If a section of code coordinates jobs, async readbacks, or pooled chunk state, leave concise comments explaining the dependency chain.
- Update documentation when behavior or public workflow changes.

### Performance expectations

- Avoid unnecessary managed allocations in streaming paths.
- Reuse buffers and pooled objects whenever possible.
- Keep CPU and GPU generation paths functionally aligned.
- Treat profiler regressions as bugs.

## Pull Request Checklist

Before requesting review, please confirm:

- [ ] The change is scoped to a clear problem or feature.
- [ ] The branch is based on the latest `main`.
- [ ] The code follows the repository naming and documentation rules.
- [ ] Any affected docs were updated.
- [ ] Local validation was performed.
- [ ] Any GitHub Actions checks on the PR are passing.
- [ ] The PR description explains the intent, impact, and testing performed.

## Environment Setup

The toolkit is designed for:

- **Unity version:** 2022.3 LTS or higher
- **Recommended contributor packages:**
  - `com.unity.entities`
  - `com.unity.burst`
  - `com.unity.collections`
  - `com.unity.mathematics`
- **Graphics API / platform support:** DX11, Metal, or Vulkan with compute shader support available for GPU-generation work

If you are contributing to the GPU path, test on hardware that supports compute shaders and `AsyncGPUReadback`. If you are contributing to the CPU path, validate that the Burst/job fallback still behaves correctly when GPU features are unavailable.

## License

By contributing, you agree that your contributions will be licensed under the project's **Apache License 2.0**.

## Need Help?

If you have questions about the architecture or want to discuss a major refactor, please start a discussion in the GitHub Discussions tab before opening a large Pull Request.
