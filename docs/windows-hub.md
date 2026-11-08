# Running the hub 24/7 on Windows

The headless hub (`Usage.Hub`) collects usage and serves paired phones through the relay, with no UI. A Windows machine that stays on (a desktop, a home server, a laptop with the lid open) can keep the phone widget fresh while your main PC is off.

1. Install the CLIs you use on that Windows machine and sign in **there**, in the same user account that will run the hub:

   ```powershell
   codex login
   claude auth login
   opencode auth login
   ```

   The hub never reads their credential files; it only runs them.

2. Publish and copy the hub:

   ```powershell
   dotnet publish src/Usage.Hub -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out
   ```

   Copy `out\` to a folder such as `C:\usage-hub`.

3. Pair a phone (prints a code and a `usage://` link — paste the code in the app):

   ```powershell
   C:\usage-hub\Usage.Hub.exe pair "My phone"
   ```

4. Run it at logon. Starting a console app directly from Task Scheduler can end with `0xC000013A` when the console window is closed; use a tiny VBS wrapper that waits for a `.cmd`:

   `hub-serve.cmd`:

   ```bat
   @echo off
   "C:\usage-hub\Usage.Hub.exe" serve
   ```

   `hub-serve.vbs`:

   ```vbs
   Set shell = CreateObject("WScript.Shell")
   shell.Run """C:\usage-hub\hub-serve.cmd""", 0, True
   ```

   Task Scheduler: **At log on**, in the user account that signed in to the CLIs (not SYSTEM), action `wscript.exe "C:\usage-hub\hub-serve.vbs"`, start in `C:\usage-hub`, no time limit, do not stop on battery or idle.

5. Keep the machine awake: disable sleep/hibernate (or set the power plan to never sleep) while the hub is meant to be online.

Other commands: `devices`, `rename <id> <new name>`, `revoke <id>`, `once` (prints one snapshot as JSON). Settings and paired devices live in `%LOCALAPPDATA%\UsageMonitor` on Windows. Use `USAGE_DATA_DIR` and `USAGE_RELAY_URL` to override the data folder and the relay.

## Points of attention

- The CLI logins live in the Windows user profile. If one expires, the provider shows an authentication error on the phone; sign in again **on that machine**.
- A new pairing is only read by the hub after a restart.
- The hub looks for OpenCode in the `@opencode/cli` package (OpenCode 2.x). A 1.x install (`opencode-ai`) is not found.
- Run the task in the same user account that signed in to the CLIs; SYSTEM has a different profile and no logins.
- The relay is an outbound WebSocket: no port forwarding is needed.
