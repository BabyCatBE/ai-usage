# How the OpenCode Go usage is read

OpenCode has no `/usage` slash command for plan limits, so the numbers are not shown inside the TUI. They are exposed by the **local OpenCode server** that the CLI starts, which publishes its own OpenAPI document.

```bash
opencode api GET /openapi.json          # lists every route of the local server
opencode api GET /api/experimental/usage/limits
```

The route `GET /api/experimental/usage/limits` (operation `experimental.usage.limits`) returns:

```json
{ "location": { "directory": "…" },
  "data": {
    "workspace": { "id": "…", "usageURL": "…" },
    "go": {
      "rolling": { "percent": 7,  "limited": false, "resetsAt": 1793426161000 },   // 5-hour window
      "weekly":  { "percent": 79, "limited": false, "resetsAt": 1793795092000 },
      "monthly": { "percent": 39, "limited": false, "resetsAt": 1795000000000 }
    },
    "budgets": [ { "models": [ { "name": "…", "spent": 0.0 } ], … } ]
  } }
```

`resetsAt` is in milliseconds. `budgets[].models[]` (spend per model) is only filled when the account has budgets configured; with a plain Go subscription it is empty, so there is no per-model breakdown to show.

## Why it can answer `data: null`

The limits belong to your **OpenCode Console** account, not to the API key. With only the `OpenCode Go` key stored the server answers `{"data": null}`. Connect the account once:

```bash
opencode auth login      # choose "OpenCode Console account" (browser/device login)
opencode auth list       # should now list "OpenCode Console"
```

The app maps `data: null` to the error `CONSOLE_LOGIN_REQUIRED` and shows "Connect your OpenCode Console account". The route is marked *experimental* by OpenCode, so its shape may change; the parser (`OpenCodeProvider.Parse`) is covered by tests.
