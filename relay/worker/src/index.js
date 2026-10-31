// Blind relay: forwards opaque frames between one "host" (the PC) and its "client" phones in a room.
// It never decrypts, parses or stores payloads. Rooms live in memory only (no storage API is used).
import { DurableObject } from "cloudflare:workers";

const ROOM_RE = /^[0-9a-f]{32}$/;
const MAX_FRAME = 64 * 1024;
const MAX_CLIENTS = 8;
const MAX_MSGS_PER_MIN = 120;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/" || url.pathname === "/health") {
      return new Response("usage-relay ok\n", { headers: { "content-type": "text/plain" } });
    }
    const m = url.pathname.match(/^\/r\/([0-9a-f]{32})$/);
    if (!m || !ROOM_RE.test(m[1])) return new Response("not found", { status: 404 });
    if (request.headers.get("Upgrade") !== "websocket") return new Response("expected websocket", { status: 426 });
    const role = url.searchParams.get("role");
    if (role !== "host" && role !== "client") return new Response("bad role", { status: 400 });
    return env.ROOMS.get(env.ROOMS.idFromName(m[1])).fetch(request);
  },
};

export class Room extends DurableObject {
  async fetch(request) {
    const role = new URL(request.url).searchParams.get("role");
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);

    if (role === "host") {
      // A new host replaces the previous one (reconnects after network changes).
      for (const old of this.ctx.getWebSockets("host")) {
        try { old.close(4000, "replaced"); } catch {}
      }
    } else if (this.ctx.getWebSockets("client").length >= MAX_CLIENTS) {
      return new Response("room full", { status: 429 });
    }

    this.ctx.acceptWebSocket(server, [role]);
    server.serializeAttachment({ role, start: Date.now(), count: 0 });

    if (role === "host") {
      this.broadcast("client", { relay: "host_online" });
    } else {
      const online = this.ctx.getWebSockets("host").length > 0;
      server.send(JSON.stringify({ relay: online ? "host_online" : "host_offline" }));
      this.broadcast("host", { relay: "client_joined" });
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  broadcast(tag, obj, except) {
    const text = typeof obj === "string" ? obj : JSON.stringify(obj);
    for (const ws of this.ctx.getWebSockets(tag)) {
      if (ws === except) continue;
      try { ws.send(text); } catch {}
    }
  }

  webSocketMessage(ws, message) {
    if (typeof message !== "string" || message.length > MAX_FRAME) { ws.close(1009, "too large"); return; }
    const a = ws.deserializeAttachment() || { role: "client", start: Date.now(), count: 0 };
    const now = Date.now();
    if (now - a.start > 60_000) { a.start = now; a.count = 0; }
    if (++a.count > MAX_MSGS_PER_MIN) { ws.close(1008, "rate limit"); return; }
    ws.serializeAttachment(a);
    // Phones talk to the host; the host talks to every phone.
    this.broadcast(a.role === "host" ? "client" : "host", message, ws);
  }

  webSocketClose(ws, code) {
    try { ws.close(code === 1005 || code === 1006 ? 1000 : code, "closed"); } catch {}
    this.afterClose(ws);
  }

  webSocketError(ws) { this.afterClose(ws); }

  afterClose(ws) {
    const a = ws.deserializeAttachment();
    if (a?.role === "host" && this.ctx.getWebSockets("host").filter((w) => w !== ws).length === 0) {
      this.broadcast("client", { relay: "host_offline" });
    }
  }
}
