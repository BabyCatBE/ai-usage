package dev.usage.monitor

import org.json.JSONArray
import org.json.JSONObject

data class Window(val id: String, val label: String, val percent: Double?, val resetsAtMs: Long?, val durationMin: Int?)
data class ModelUse(val name: String, val percent: Double?, val spentUsd: Double?)
data class Provider(
    val id: String, val name: String, val status: String, val fetchedAtMs: Long?, val errorCode: String?,
    val plan: String?, val windows: List<Window>, val models: List<ModelUse>, val details: List<Pair<String, String>>,
) {
    val hasData get() = windows.any { it.percent != null }
    /** Providers whose CLI is not installed are not shown. */
    val hidden get() = status == "error" && errorCode == "CLI_NOT_FOUND" && !hasData
}
data class Snapshot(val generatedAtMs: Long, val providers: List<Provider>)

object SnapshotParser {
    private fun JSONObject.str(k: String): String? = if (isNull(k)) null else optString(k).ifEmpty { null }
    private fun JSONObject.num(k: String): Double? = if (isNull(k) || !has(k)) null else optDouble(k).takeIf { !it.isNaN() }
    private fun iso(s: String?): Long? = s?.let { try { java.time.OffsetDateTime.parse(it).toInstant().toEpochMilli() } catch (_: Exception) { null } }

    fun parse(json: JSONObject): Snapshot? {
        if (json.optInt("schemaVersion") != 2) return null
        val generated = iso(json.str("generatedAt")) ?: return null
        val list = json.optJSONArray("providers") ?: JSONArray()
        val providers = (0 until list.length()).mapNotNull { i -> list.optJSONObject(i)?.let(::provider) }
        return Snapshot(generated, providers)
    }

    fun parse(text: String): Snapshot? = try { parse(JSONObject(text)) } catch (_: Exception) { null }

    private fun provider(o: JSONObject): Provider? {
        val id = o.str("id") ?: return null
        val ws = o.optJSONArray("windows") ?: JSONArray()
        val ms = o.optJSONArray("models") ?: JSONArray()
        val ds = o.optJSONArray("details") ?: JSONArray()
        return Provider(
            id, o.str("name") ?: id, (o.str("status") ?: "error").lowercase(), iso(o.str("fetchedAt")), o.str("errorCode"), o.str("plan"),
            (0 until ws.length()).mapNotNull { ws.optJSONObject(it) }.map { w ->
                Window(w.str("id") ?: "?", w.str("label") ?: "", w.num("usedPercent"), iso(w.str("resetsAt")), if (w.isNull("durationMinutes")) null else w.optInt("durationMinutes"))
            },
            (0 until ms.length()).mapNotNull { ms.optJSONObject(it) }.map { ModelUse(it.str("name") ?: "?", it.num("usedPercent"), it.num("spentUsd")) },
            (0 until ds.length()).mapNotNull { ds.optJSONObject(it) }.map { (it.str("label") ?: "") to (it.str("value") ?: "") },
        )
    }
}
