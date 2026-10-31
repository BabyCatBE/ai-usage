package dev.usage.monitor

import android.content.Context

/** Pairing and the last snapshot, in app-private storage (backup disabled in the manifest). */
class Store(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("usage", Context.MODE_PRIVATE)

    var pairing: Pairing?
        get() = prefs.getString("code", null)?.let { Pairing.create(it, prefs.getString("relay", null), prefs.getString("pc", null)) }
        set(v) {
            prefs.edit().apply {
                if (v == null) { remove("code"); remove("relay"); remove("pc"); remove("snapshot"); remove("receivedAt"); remove("hostOnline") }
                else { putString("code", v.code); putString("relay", v.relayUrl); putString("pc", v.pcName) }
            }.apply()
        }

    val snapshot: Snapshot? get() = prefs.getString("snapshot", null)?.let(SnapshotParser::parse)
    val receivedAtMs: Long get() = prefs.getLong("receivedAt", 0)

    /** null = unknown, true/false = last known state of the PC. */
    var hostOnline: Boolean?
        get() = if (prefs.contains("hostOnline")) prefs.getBoolean("hostOnline", false) else null
        set(v) { prefs.edit().apply { if (v == null) remove("hostOnline") else putBoolean("hostOnline", v) }.apply() }

    var busy: Boolean
        get() = prefs.getBoolean("busy", false) && System.currentTimeMillis() - prefs.getLong("busyAt", 0) < 90_000
        set(v) { prefs.edit().putBoolean("busy", v).putLong("busyAt", System.currentTimeMillis()).apply() }

    fun save(raw: String) { prefs.edit().putString("snapshot", raw).putLong("receivedAt", System.currentTimeMillis()).apply() }
}
