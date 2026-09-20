# AI test generation and MCP setup

Choose Tools > AI test generator. Copy its prompt and template, then supply API requirements or a specification. Output is schema 1.4 JSON saved as `.jsee`, without credentials. The [prompt](test-generation-prompt.md) and [skill](../skills/e2e-tester-author/SKILL.md) are included in the distribution.

From the unpacked single-file distribution:

```powershell
./skills/e2e-tester-author/scripts/Setup-Agent.ps1 -McpExecutable ./mcp/E2ETester.Mcp.exe -BaseUrl https://api.example.test -ConfigureCodex
```

Setup installs the skill and a stable per-user MCP executable, then registers e2e-tester through Codex CLI. Existing host configuration and unrelated settings are preserved. Without ConfigureCodex it produces generic mcp-client.json. Restart the agent to discover new capabilities. See [MCP configuration](mcp.md).

Authenticated MCP execution needs E2ETESTER_CLOUD_TOKEN supplied locally in the server process environment. Setup never stores or asks for the token. Desktop Swagger cookies are not automatically accessible to MCP. Without a token, the agent can author/validate and the user can execute in the desktop. Passwords, tokens and MFA codes must not enter chat.

The skill validates packages without executing them unless requested. Execution uses operation IDs and canonical reports. HTTP permission is scoped to the supplied base URL; SQL execution remains disabled by default. [Official Codex MCP setup](https://developers.openai.com/codex/mcp).
