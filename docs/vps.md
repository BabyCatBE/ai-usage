# Running the hub 24/7 on a VPS

The headless hub (`Usage.Hub`) collects usage and serves paired phones through the relay, with no UI. Put it on any Linux VPS to keep the phone widget fresh while your PC is off.

1. Install the CLIs you use on the VPS and sign in **there** (`codex login --device-auth`, `claude auth login`, `opencode auth login`). The hub never reads their credential files; it only runs them.
2. Publish and copy the hub:

   ```bash
   dotnet publish src/Usage.Hub -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o out
   scp -r out/ user@vps:~/usage-hub
   ```
3. Pair a phone (prints a code and a `usage://` link — paste the code in the app):

   ```bash
   ~/usage-hub/Usage.Hub pair "My phone"
   ```
4. Keep it running with systemd (`/etc/systemd/system/usage-hub.service`):

   ```ini
   [Unit]
   Description=AI Usage hub
   After=network-online.target

   [Service]
   User=youruser
   ExecStart=/home/youruser/usage-hub/Usage.Hub serve
   Restart=always
   RestartSec=5

   [Install]
   WantedBy=multi-user.target
   ```

   ```bash
   sudo systemctl enable --now usage-hub
   ```

Other commands: `devices`, `rename <id> <new name>`, `revoke <id>`, `once` (prints one snapshot as JSON). Settings and paired devices live in `~/.local/share/UsageMonitor`. Use `USAGE_DATA_DIR` and `USAGE_RELAY_URL` to override the data folder and the relay.

If both your PC and the VPS are paired with the same phone, the phone simply shows whichever answers (each pairing is its own room — pair twice).

## Self-hosting the relay

```bash
cd relay/worker && npm i && npx wrangler deploy
```

The Worker uses a Durable Object **without** storage; rooms live in memory only. Point the PC (*Settings → Relay*) or `USAGE_RELAY_URL` at your `wss://…workers.dev`.
