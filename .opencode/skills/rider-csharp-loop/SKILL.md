---
name: Rider C# edit loop
description: Verify C# edits in Rider with inspections, build, and tests before reporting done
---

Use this skill after changing C# files in the Rider solution. All tools come from the `rider` MCP server and are available in Code Mode as `tools.rider.*`. Paths passed to Rider tools are relative to the solution root unless stated otherwise.

## Workflow

1. **Collect changed C# files** from the edits made in this session or from `git status`.
2. **Inspect** each changed file, then fix and re-run until clean:
   - Single file: `tools.rider.get_file_problems({ filePath, errorsOnly: false })`
   - Several files: `tools.rider.lint_files({ files, min_severity: "warning" })`
   - Treat `timedOut: true` as incomplete results; retry with a larger `timeout`.
3. **Build**:
   - `const build = await tools.rider.build_solution_start({ filesToRebuild: files })`
   - Poll `tools.rider.build_solution_state({ sessionId: build.sessionId })` until `state` is terminal.
   - Require `buildIsSuccess === true` and no `problems`. Otherwise fix the reported file/line errors and rebuild.
4. **Test**:
   - Start targeted first: `tools.rider.test_run_start({ paths: files })`. Use `fullyQualifiedNames` for specific tests or `includeSolution: true` for the whole solution.
   - Poll `tools.rider.test_run_state({ sessionId, failedOnly: true })` until complete (`timeout` may be raised up to 600000).
   - Require `summary.allPassed !== false`, `summary.failed === 0`, `summary.buildErrors === 0`, and no `launchErrors`.
5. **Optional solution-wide check**: `tools.rider.get_project_problems({ severity: "Error" })`. Fix any new errors.
6. **Completion rule**: do not report "done" until build and tests are clean. If they are not, report concrete file:line problems and what remains to fix.

## Style

Follow `AGENTS.md`: use explicit types in C#. Never use `var`, including local declarations, `using`, `foreach`, `out` arguments, and tuple deconstruction.
