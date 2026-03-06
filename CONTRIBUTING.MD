# Contributing to Procedural Terrain Toolkit

First off, thank you for considering contributing! It’s people like you who make this toolkit a powerful resource for the Unity community.

As this project is currently in Active Beta, we are moving fast. Please follow these guidelines to help us maintain high performance and code quality.

## 🛠 How Can I Contribute?

### 1. Reporting Bugs

- **Check for duplicates:** Search the Issues tab before opening a new one.
- **Be specific:** Include your Unity version, Render Pipeline (URP/HDRP), and clear steps to reproduce the crash or glitch.
- **Screenshots help:** If it's a procedural generation artifact (weird terrain spikes), a picture is worth a thousand lines of code.

### 2. Suggesting Features

- Open an issue with the tag `[Feature Request]`.
- **Explain the why:** How does this improve performance or developer workflow?
- Since we use Unity DOTS and Compute Shaders, suggestions that leverage GPU acceleration are highly prioritized.

### 3. Code Contributions

We follow a "Fork and Pull" workflow:

1. Fork the repo and create your branch from `main`.
2. Commit small, atomic changes. Example: `Fix: Offset bug in NoiseGenerator.compute`.
3. Test: Since the project uses GitHub Actions for CI/CD, ensure your PR passes the automated build check before requesting a review.
4. Submit: Open a Pull Request and link it to any relevant issue.

## 📐 Coding Standards

To keep the Procedural Terrain Toolkit fast and readable, please adhere to these rules:

- **DOTS First:** If a task can be done in a `SystemBase` or `IJobEntity`, do it there. Avoid `MonoBehaviour.Update` for heavy logic.
- **Naming:** Use `PascalCase` for methods and public variables; prefix private fields with an underscore followed by `camelCase` (for example, `_privateField`).
- **Safety:** Always handle `AsyncGPUReadback` requests safely. Never assume the data is ready on the next frame.
- **Documentation:** All public methods must have XML comments using `///`.
