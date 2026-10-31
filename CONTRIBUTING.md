# Contributing

Thanks for helping! Bug reports, providers and translations are all welcome.

## Layout

| Path | What |
|---|---|
| `src/Usage.Core` | providers, collector service, pairing/crypto, relay client (no UI) |
| `src/Usage.Hub` | headless hub / CLI (`serve`, `pair`, `devices`, `revoke`, `once`) |
| `src/Usage.Windows` | WPF app: floating widget, main window, tray |
| `android/` | Kotlin app + home-screen widget (Canvas-drawn, no Compose) |
| `relay/worker` | Cloudflare Worker blind relay |
| `tests/` | xUnit tests and the Node end-to-end phone simulator |

## Adding a provider

1. Create `src/Usage.Core/Providers/YourProvider.cs` implementing `IUsageProvider` (`Id`, `Name`, `CollectAsync`).
2. Locate the official CLI with `Cli.Find(...)` and talk to it through `Cli.RunAsync` / `Cli.JsonRpc`. **Never read credential files** and never surface raw stdout/stderr — throw `UsageException("SAFE_CODE")` instead.
3. Return `ProviderReading` with windows using the ids `five_hour`, `seven_day`, `monthly` when they apply (others are shown with their label).
4. Put the parsing in a `static Parse(JsonNode?)` method and cover it with a test in `tests/Usage.Tests` using a real (sanitised) response.
5. Register it in `AppPaths.DefaultProviders()`, and give it an accent colour in `Theme.Accent` (Windows) and `Palette.accent` (Android).

## Checks

```powershell
dotnet test
cd android; ./gradlew :app:testDebugUnitTest
node tests/e2e/phone-sim.cjs <relay-url> <pairing-code>   # needs a running hub paired with that code
```

Keep user-facing text bilingual (`Loc.T(pt, en)` on Windows, `Fmt` on Android).
