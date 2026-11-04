package dev.usage.monitor

import android.content.Context

/** Pairing and the last snapshot, in app-private storage (backup disabled in the manifest). */
class Store(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("usage", Context.MODE_PRIVATE)

    var pairing: Pairing?
        get() = prefs.getString("code", null)?.let { Pairing.create(it, prefs.getString("relay", null), prefs.getString("pc", null)) }
        set(v) {
            prefs.edit().apply {
                if (v == null) { remove("code"); remove("relay"); remove("pc"); remove("snapshot"); remove("receivedAt"); remove("hostOnline"); remove("history") }
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

    /** Colour mode: auto | light | dark. */
    var theme: String
        get() = (prefs.getString("theme", "auto") ?: "auto").takeIf { it != "glass" } ?: "auto"
        set(v) { prefs.edit().putString("theme", v).apply() }

    /** Look: neu (soft neumorphism) | glass (liquid glass) | cyber (neon cyberpunk) | soft (muted) | cupertino (Apple design, full Liquid Glass). A legacy theme value "glass" counts as the glass style. */
    var style: String
        get() = prefs.getString("style", null) ?: if (prefs.getString("theme", "") == "glass") "glass" else "neu"
        set(v) { prefs.edit().putString("style", v).apply() }

    /** Cupertino widget glass: clear (wallpaper shows through, white content) | tinted (more contrast). */
    var widgetGlass: String
        get() = prefs.getString("widgetGlass", null)?.takeIf { it == "clear" || it == "tinted" } ?: "clear"
        set(v) { prefs.edit().putString("widgetGlass", v).apply() }

    /** Soft sub-theme id (see [CyberVariant.getSoft]). */
    var softVariant: String
        get() = prefs.getString("softVariant", null) ?: CyberVariant.getSoft(null).id
        set(v) { prefs.edit().putString("softVariant", v).apply() }

    /** Cyberpunk sub-theme id (see [CyberVariant]). */
    var cyberVariant: String
        get() = prefs.getString("cyberVariant", null) ?: CyberVariant.all[0].id
        set(v) { prefs.edit().putString("cyberVariant", v).apply() }

    fun palette(context: Context): Palette {
        val night = (context.resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES
        val dark = when (theme) { "dark" -> true; "light" -> false; else -> night }
        val v = if (style == "cyber") CyberVariant.get(cyberVariant) else if (style == "soft") CyberVariant.getSoft(softVariant) else null   // each cyber sub-theme is either dark or light
        // Cupertino draws with RenderNode (API 29+); older phones get the neumorphic look instead.
        val cupertino = style == "cupertino" && android.os.Build.VERSION.SDK_INT >= 29
        return Palette(v?.dark ?: dark, style == "glass", v, cupertino = cupertino, mono = style == "mono")
    }

    var showIcons: Boolean
        get() = prefs.getBoolean("showIcons", true)
        set(v) { prefs.edit().putBoolean("showIcons", v).apply() }

    fun icon(provider: String): String = prefs.getString("icon_$provider", null)?.takeIf { id -> Icons.options(provider).any { it.id == id } } ?: Icons.default(provider)
    fun setIcon(provider: String, id: String) { prefs.edit().putString("icon_$provider", id).apply() }
    val icons: Map<String, String> get() = Icons.providers.associate { it.first to icon(it.first) }

    /** Cupertino widget: draw the rings and icon tiles in each AI's colour, whatever the glass style (clear glass keeps its transparency). */
    var widgetColorRings: Boolean
        get() = prefs.getBoolean("widgetColorRings", false)
        set(v) { prefs.edit().putBoolean("widgetColorRings", v).apply() }

    /** The UI thread (live link) and SyncWorker both save: serialise the read-modify-write of the history. */
    fun save(raw: String) = synchronized(Store::class.java) {
        val e = prefs.edit().putString("snapshot", raw).putLong("receivedAt", System.currentTimeMillis())
        SnapshotParser.parse(raw)?.let { e.putString("history", Projection.append(prefs.getString("history", null), it)) }
        e.apply()
    }

    /** Beta: pace-based "runs out in…" projection, shown when a ring is tapped (Cupertino look). */
    var projection: Boolean
        get() = prefs.getBoolean("projection", true)
        set(v) { prefs.edit().putBoolean("projection", v).apply() }

    fun history(provider: String, window: String): List<Projection.Sample> = Projection.read(prefs.getString("history", null), Projection.key(provider, window))
}
