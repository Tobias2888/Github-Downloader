---
name: Context7 docs lookup
description: Fetch current, version-specific library documentation and code examples with Context7 instead of relying on training data
---

Use this skill when a question involves a library or framework API, version-specific behavior, or when an API may not exist. Trigger it when the user says "use context7", names a library they want docs for, or asks for examples with a specific package version.

## Availability

Context7 tools are provided by the `context7` MCP server (endpoint `https://mcp.context7.com/mcp`). In Code Mode, discover the exact tool names and signatures with `search({ namespace: "context7" })`. The usual tools are `resolve-library-id` (library name -> Context7 ID such as `/serilog/serilog`) and `query-docs` (ID + question -> relevant documentation snippets). If the server is not connected, set it up with `npx ctx7 setup --opencode`, or add it with `opencode mcp add context7 --global --url https://mcp.context7.com/mcp`.

If the library ID is already known, skip the resolution step.

## Rules

- Ask a specific "how do I do X" question, not a general "tell me about Y". Include the version when it matters.
- Resolve the library ID first unless the user supplied one or it is already known from an earlier lookup in this session.
- Treat returned snippets as guidance, not proof: verify APIs against the project's actual package versions (Rider `get_project_dependencies`) and compile before relying on them.
- If the first result is off-target, refine the query or ask for a different part of the docs rather than guessing from memory.
