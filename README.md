# AI Usage — your AI limits, always in sight

> Working name. Shows how much of your **ChatGPT (Codex)**, **Claude** and **OpenCode Go** limits you have used — 5-hour, weekly and monthly windows, plus per-model/product breakdowns when the provider exposes them — on a **floating Windows widget**, a **Windows app**, and an **Android app + home-screen widget**.

**Português:** [role para a seção em português](#português)

- **Soft neumorphism** look, light and dark, follows the system theme.
- **Windows floating widget** docked to the middle of the right (or left) screen edge as a thin retracted tab, so it never covers window buttons: hover to expand every window of every provider, hover a provider for a detailed report (all windows, elapsed time, models, 24 h history).
- **Provider icons** (ChatGPT/Codex, Claude/Claude Code mascot, OpenCode or a Go-plan model letter) with an on/off switch, on the widgets and in both apps.
- **Windows app** that reflows from one to three columns, made to live in a split-screen on a second monitor.
- **Android app + widget** that works at home *and* away, with no VPN, no database and no account.
- **Private by design**: it never reads credential files. It asks the official CLIs (`codex`, `claude`, `opencode`) for the numbers, and nothing readable ever leaves your PC (the phone link is end-to-end encrypted).

```
codex / claude / opencode CLIs ─▶ PC app (collector)
                                   ├─ floating widget + app window
                                   └─ encrypted ─▶ relay (blind) ─▶ Android app + widget
```

## Install

**Windows** (needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)): download `UsageMonitor.exe` from the latest release and run it. It lives in the tray and starts with Windows. Make sure the CLIs you use are signed in (`codex login`, `claude auth login`, `opencode auth login`); missing ones are hidden automatically.

**Android** (8.0+): install the APK from the release, open it, then on the PC open **Phone → Generate pairing code** and scan the QR (or paste the code). Use **Add widget** to pin the home-screen widget.

**Build from source**

```powershell
dotnet test                                   # core tests
dotnet publish src/Usage.Windows -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
cd android; ./gradlew :app:testDebugUnitTest :app:assembleDebug
```

## How the phone connects from anywhere

The PC opens an **outbound** WebSocket to a tiny **relay** (works behind routers and CGNAT — no port forwarding, no VPN). The phone connects to the same room. Everything is encrypted end to end with a key derived from the pairing secret (AES-256-GCM, HKDF-SHA256), so the relay only forwards opaque bytes, keeps nothing on disk, and cannot read your data. Each paired phone has its own secret and room; removing it on the PC revokes it. Details: [docs/protocol.md](docs/protocol.md).

- The default relay is a free Cloudflare Worker run by the project maintainer. **You can run your own** in a minute: `cd relay/worker && npx wrangler deploy`, then set the URL in *Settings → Relay* (PC) — pairing links carry it to the phone.
- Want it 24/7 with the PC off? Run the headless hub on a VPS: [docs/vps.md](docs/vps.md).

## Providers

| Provider | Source | Notes |
|---|---|---|
| ChatGPT / Codex | `codex app-server` rate limits | 5 h + weekly, plan, credits |
| Claude | `claude` `get_usage` control request | 5 h + weekly, per-product breakdown, extra usage |
| OpenCode Go | `opencode` local API `usage/limits` ([how](docs/opencode.md)) | 5 h + weekly + monthly. Requires `opencode auth login` with the **OpenCode Console** account |

Adding a provider = one class implementing `IUsageProvider` (see [CONTRIBUTING.md](CONTRIBUTING.md)).

## Limits

- The Android widget refreshes every ~15 min (Android's minimum) and instantly when you tap ↻ while the PC is on.
- With the PC off and no VPS hub, the widget shows the last known data and says so.
- Windows: the floating widget docks to the primary monitor.
- OpenCode exposes no per-model percentages for the Go plan today; per-model spend appears only when budgets exist.

## License

MIT — see [LICENSE](LICENSE).

---

## Português

Mostra quanto você já usou dos limites do **ChatGPT (Codex)**, **Claude** e **OpenCode Go** — janelas de 5 horas, semanal e mensal, e por modelo/produto quando o provedor informa — em um **widget flutuante do Windows**, um **app Windows** e um **app + widget Android**.

- Visual **neumorphism suave**, claro e escuro.
- **Widget flutuante** fixo num canto: passe o mouse para expandir; passe o mouse num provedor para o relatório detalhado.
- **App Windows** responsivo (1 a 3 colunas), feito para ficar numa tela secundária dividida com outros apps.
- **App e widget Android** que funcionam em casa e fora dela, **sem VPN, sem banco de dados e sem conta**.
- **Privado**: nunca lê arquivos de credenciais; pergunta os números às CLIs oficiais. O link com o celular é criptografado de ponta a ponta.

**Instalação:** baixe o `UsageMonitor.exe` (requer .NET 10 Desktop Runtime) e o APK na página de releases. No PC: **Celular → Gerar código de pareamento**; no Android: **Escanear QR** (ou cole o código) e **Adicionar widget**. Faça login nas CLIs que você usa (`codex login`, `claude auth login`, `opencode auth login`).

**Conexão de qualquer lugar:** o PC abre uma conexão *de saída* com um relay minúsculo (passa por roteador/CGNAT, sem abrir portas). Tudo é cifrado com uma chave derivada do código de pareamento; o relay só repassa bytes ilegíveis. Você pode hospedar seu próprio relay (`relay/worker`) ou rodar o hub numa VPS para funcionar 24 h ([docs/vps.md](docs/vps.md)).

**Limitações:** o widget Android atualiza a cada ~15 min (mínimo do Android) e na hora ao tocar ↻ com o PC ligado; com o PC desligado (e sem VPS) mostra o último dado salvo.
