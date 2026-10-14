# Align agent guidance with LunaArc

- Time: 2026-10-14 14:45:04 (Asia/Bangkok, UTC+07:00).
- Request: update the agent document for the current project.
- Root cause: AGENT.md contained VideoAI/Laravel instructions unrelated to LunaArc.
- Files: AGENT.md; this change log.
- Before: web backend, database, browser-bridge and video-pipeline guidance from another project.
- After: C#/.NET 8, Core/CLI/Studio/Shell, Avalonia MVVM, archive compatibility, filesystem safety, build and Windows/Linux verification guidance.
- Limits documented at that time: TAR GZip/TarZst, ZIP passwords, multi-volume reading, .larc memory usage, live editing, PasswordVault and missing tests.
- Verification at that time: reviewed solution/project files, contracts, format handlers, security, pipeline, VFS, CLI, Studio, Shell and publish scripts; checked documented paths, UTF-8 and removal of unrelated project references. Documentation checks passed.
- Build/test/UI were not run: this was a Markdown-only update and dotnet/git were not available in PATH during that session.
- Limits: runtime observations were based on source review, not runtime tests. No application code changed in this task.
- Release status: local documentation only; no deployment or public release.
- This historical entry was translated to English during the subsequent build-readiness task; it describes the earlier verification state, not the current state.
