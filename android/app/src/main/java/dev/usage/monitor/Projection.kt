package dev.usage.monitor

import org.json.JSONArray
import org.json.JSONObject

/**
 * Beta: "at this pace, when does this window run out?". The phone keeps a short history of every window's percentage
 * (one sample per snapshot it receives) and projects the pace of the last hour forward.
 */
object Projection {
    data class Sample(val atMs: Long, val percent: Double, val resetsAtMs: Long?)

    data class Result(
        /** Percentage points per hour over [basisMinutes]. */
        val perHour: Double,
        val basisMinutes: Long,
        /** When the window would reach 100 % at this pace; null when that happens after the reset (or never). */
        val runsOutAtMs: Long?,
        /** Expected percentage when the window resets, if it does not run out first. */
        val atResetPercent: Double?,
    )

    const val KEEP_MS = 8 * 3_600_000L
    private const val TARGET_MS = 60 * 60_000L
    private const val MIN_BASIS_MS = 12 * 60_000L
    private const val MAX_BASIS_MS = 2 * 3_600_000L
    private const val SPACING_MS = 2 * 60_000L

    fun key(provider: String, window: String) = "$provider/$window"

    /**
     * Pace over the last hour (or the longest stretch since the last reset, at least 12 minutes). Samples before a reset
     * (the reset time moved, or the percentage dropped) are ignored. Null when there is not enough history yet.
     */
    fun project(history: List<Sample>, nowPercent: Double, resetsAtMs: Long?, nowMs: Long): Result? {
        val sorted = history.filter { it.atMs <= nowMs }.sortedBy { it.atMs }
        if (sorted.isEmpty()) return null
        // keep only the run since the last reset
        var start = 0
        for (i in 1 until sorted.size) {
            val a = sorted[i - 1]; val b = sorted[i]
            val resetMoved = a.resetsAtMs != null && b.resetsAtMs != null && b.resetsAtMs - a.resetsAtMs > 5 * 60_000L
            if (b.percent + 0.5 < a.percent || resetMoved) start = i
        }
        val run = sorted.subList(start, sorted.size)
        // newest sample at least an hour old, but not older than two hours (a PC asleep for hours would dilute the pace)
        val base = run.lastOrNull { nowMs - it.atMs in TARGET_MS..MAX_BASIS_MS } ?: run.firstOrNull { nowMs - it.atMs <= MAX_BASIS_MS } ?: return null
        val span = nowMs - base.atMs
        if (span < MIN_BASIS_MS) return null
        val perHour = ((nowPercent - base.percent) / (span / 3_600_000.0)).coerceAtLeast(0.0)
        val left = (100.0 - nowPercent).coerceAtLeast(0.0)
        val untilReset = resetsAtMs?.let { it - nowMs }?.takeIf { it > 0 }
        if (perHour < 0.05) return Result(0.0, span / 60_000, null, nowPercent)
        val toEmptyMs = Math.round(left / perHour * 3_600_000.0)
        return if (untilReset != null && toEmptyMs >= untilReset)
            Result(perHour, span / 60_000, null, (nowPercent + perHour * untilReset / 3_600_000.0).coerceAtMost(100.0))
        else Result(perHour, span / 60_000, nowMs + toEmptyMs, null)
    }

    // ---- history persistence (JSON: {"provider/window": [[atMs, percent, resetsAtMs|0], …]}) ------------------

    fun append(json: String?, snapshot: Snapshot, nowMs: Long = System.currentTimeMillis()): String {
        val root = try { if (json.isNullOrEmpty()) JSONObject() else JSONObject(json) } catch (_: Exception) { JSONObject() }
        for (p in snapshot.providers) for (w in p.windows) {
            val pct = w.percent ?: continue
            val at = p.fetchedAtMs ?: snapshot.generatedAtMs
            val k = key(p.id, w.id)
            val arr = root.optJSONArray(k) ?: JSONArray()
            val last = if (arr.length() > 0) arr.optJSONArray(arr.length() - 1) else null
            if (last != null && at - last.optLong(0) < SPACING_MS) continue   // same reading twice, or a burst of live pushes
            arr.put(JSONArray().put(at).put(pct).put(w.resetsAtMs ?: 0L))
            root.put(k, arr)
        }
        // drop old samples and dead keys
        val keys = root.keys().asSequence().toList()
        for (k in keys) {
            val arr = root.optJSONArray(k) ?: continue
            val kept = JSONArray()
            for (i in 0 until arr.length()) { val e = arr.optJSONArray(i) ?: continue; if (nowMs - e.optLong(0) <= KEEP_MS) kept.put(e) }
            if (kept.length() == 0) root.remove(k) else root.put(k, kept)
        }
        return root.toString()
    }

    fun read(json: String?, key: String): List<Sample> {
        val arr = try { if (json.isNullOrEmpty()) return emptyList() else JSONObject(json).optJSONArray(key) } catch (_: Exception) { null } ?: return emptyList()
        return (0 until arr.length()).mapNotNull { i -> arr.optJSONArray(i)?.let { Sample(it.optLong(0), it.optDouble(1), it.optLong(2).takeIf { r -> r > 0 }) } }
    }
}
