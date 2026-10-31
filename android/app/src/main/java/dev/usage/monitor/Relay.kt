package dev.usage.monitor

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

sealed class Fetch {
    data class Ok(val snapshot: Snapshot, val raw: String, val fresh: Boolean) : Fetch()
    object HostOffline : Fetch()
    object Unreachable : Fetch()
}

object Relay {
    val http: OkHttpClient = OkHttpClient.Builder()
        .pingInterval(25, TimeUnit.SECONDS)
        .connectTimeout(12, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()

    fun url(p: Pairing) = p.relayUrl.trimEnd('/') + "/r/" + p.keys.roomId + "?role=client"
    private fun request(p: Pairing) = Request.Builder().url(url(p).replaceFirst("wss://", "https://").replaceFirst("ws://", "http://")).build()
    private fun msg(type: String) = JSONObject().put("t", type).put("ts", System.currentTimeMillis())

    /**
     * One-shot, blocking: asks the PC for the current snapshot (or for a fresh collection when [refresh]).
     * Used by the widget worker, so it never keeps a connection open.
     */
    fun fetch(p: Pairing, refresh: Boolean, timeoutMs: Long = if (refresh) 70_000 else 20_000, previousGeneratedAt: Long = 0): Fetch {
        val done = CountDownLatch(1)
        val resultRef = java.util.concurrent.atomic.AtomicReference<Fetch>(Fetch.Unreachable)
        val bestRef = java.util.concurrent.atomic.AtomicReference<Fetch.Ok?>(null)
        val ws = http.newWebSocket(request(p), object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) { webSocket.send(p.seal('c', msg(if (refresh) "refresh" else "get")).toString()) }
            override fun onMessage(webSocket: WebSocket, text: String) {
                val o = try { JSONObject(text) } catch (_: Exception) { return }
                when (o.optString("relay")) {
                    "host_offline" -> { if (bestRef.get() == null) { resultRef.set(Fetch.HostOffline); done.countDown() }; return }
                    "host_online" -> return
                    "" -> {}
                    else -> return
                }
                val m = p.open('h', text) ?: return
                val data = m.optJSONObject("d") ?: return
                val snap = SnapshotParser.parse(data) ?: return
                val fresh = snap.generatedAtMs > previousGeneratedAt && !m.optBoolean("busy")
                val ok = Fetch.Ok(snap, data.toString(), fresh)
                bestRef.set(ok); resultRef.set(ok)
                // For a refresh keep waiting for the new collection; otherwise the first answer is enough.
                if (!refresh || fresh) done.countDown()
            }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) { done.countDown() }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) { done.countDown() }
        })
        done.await(timeoutMs, TimeUnit.MILLISECONDS)
        ws.close(1000, null)
        return bestRef.get() ?: resultRef.get()
    }

    /** Long-lived session for the app screen: pushes every snapshot as the PC publishes it. */
    class Live(private val p: Pairing, private val onSnapshot: (Snapshot, String, Boolean) -> Unit, private val onHost: (Boolean) -> Unit, private val onLink: (Boolean) -> Unit) {
        private var ws: WebSocket? = null
        @Volatile private var closed = false
        private var attempt = 0
        private val backoff = intArrayOf(1, 2, 5, 10, 30, 60)
        private val handler = android.os.Handler(android.os.Looper.getMainLooper())

        fun start() { if (!closed) connect() }
        fun stop() { closed = true; handler.removeCallbacksAndMessages(null); ws?.close(1000, null); ws = null }
        fun refresh() { ws?.send(p.seal('c', msg("refresh")).toString()) }
        fun get() { ws?.send(p.seal('c', msg("get")).toString()) }

        private fun connect() {
            ws = http.newWebSocket(request(p), object : WebSocketListener() {
                override fun onOpen(webSocket: WebSocket, response: Response) { attempt = 0; onLink(true); get() }
                override fun onMessage(webSocket: WebSocket, text: String) {
                    val o = try { JSONObject(text) } catch (_: Exception) { return }
                    when (o.optString("relay")) {
                        "host_online" -> { onHost(true); get(); return }
                        "host_offline" -> { onHost(false); return }
                    }
                    val m = p.open('h', text) ?: return
                    val data = m.optJSONObject("d") ?: return
                    SnapshotParser.parse(data)?.let { onHost(true); onSnapshot(it, data.toString(), m.optBoolean("busy")) }
                }
                override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = reconnect()
                override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = reconnect()
            })
        }

        private fun reconnect() {
            onLink(false)
            if (closed) return
            handler.postDelayed({ if (!closed) connect() }, backoff[minOf(attempt++, backoff.size - 1)] * 1000L)
        }
    }
}
