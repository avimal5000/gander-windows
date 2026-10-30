# Sway for Windows: working rules

Read PLAN.md, SPEC.md, and CONTRACT.md first. This is the Windows port of Sway for macOS (`avimal5000/sway`, private).

- The orchestrator creates and edits solution and project files, CI, and the docs. Builders implement one module per
  PR and do not touch `*.csproj`, `*.sln`, `Directory.Build.props`, or `.github/`.
- Do not edit PLAN.md.
- Keep STATUS.md current. Tell the user clearly what only they can do: running builds on the Windows desktop, camera
  and monitor checks, anything visual.
- Never put the CI-skip marker (the bracketed words "skip ci") in any commit message or PR body. It silences CI, and a
  squash merge copies it into main. Merge with `--delete-branch` and an explicit clean `--body`.
- Merging needs the user's approval each time.
- Privacy is enforced: `scripts/privacy-guard.sh` must pass. No networking, no saved frames, no synthesized or captured
  input, cursor never moved.
- Build and test locally with `dotnet build` and `dotnet test tests/Sway.Core.Tests` (the .NET 8 SDK is in
  `~/.dotnet` on the Mac; add it to PATH). The Windows-only projects compile on macOS but run only on Windows CI.
- Lessons from the Mac project: sticky suspend for lock and sleep, camera-loss turns Tracking off, adaptive frame rate,
  verified focus (raise, check, fall back), and ask before changing a privacy promise.
