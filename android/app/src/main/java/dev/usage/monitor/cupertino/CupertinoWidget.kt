package dev.usage.monitor.cupertino

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import dev.usage.monitor.Fmt
import dev.usage.monitor.Icons
import dev.usage.monitor.Provider
import dev.usage.monitor.Renderer
import dev.usage.monitor.UiState
import kotlin.math.max
import kotlin.math.min

/**
 * The home-screen widget of the Cupertino look. One UI Home blurs the wallpaper behind the widget (see UsageWidget);
 * this draws the glass on top: the rim catching the light, the sheen along the top edge, a darker lower lip, and the
 * content in vibrant white (clear) or in full colour on a denser tint (tinted), like the two iOS 26 widget styles.
 * Controls on the glass use fills, never a second layer of glass.
 */
object CupertinoWidget {
    /** Tint the launcher lays over its wallpaper blur (alpha 1-254 keeps the blur on). */
    fun launcherTint(dark: Boolean, glass: String, lightWallpaper: Boolean = false): Int = when {
        glass == "clear" -> if (lightWallpaper) 0x66000000 else 0x45000000   // still see-through, dense enough for white text on a busy wallpaper
        dark -> 0x9E1C1C1E.toInt()
        else -> 0xA6F5F5F7.toInt()
    }

    /** The wallpaper asks for dark text (a bright wallpaper): white text on clear glass needs a deeper dim. */
    fun lightWallpaper(context: android.content.Context): Boolean = try {
        if (android.os.Build.VERSION.SDK_INT < 31) false
        else {
            val wc = android.app.WallpaperManager.getInstance(context).getWallpaperColors(android.app.WallpaperManager.FLAG_SYSTEM)
            wc != null && (wc.colorHints and android.app.WallpaperColors.HINT_SUPPORTS_DARK_TEXT) != 0
        }
    } catch (_: Exception) { false }

    private class Look(dark: Boolean, glass: String, val colorRings: Boolean) {
        val clear = glass == "clear"
        val t = Tokens(dark = clear || dark)
        val fg = if (clear || dark) 0xFFFFFFFF.toInt() else 0xFF000000.toInt()
        val fg2 = alpha(fg, if (clear) 0.78f else 0.62f)
        val fg3 = alpha(fg, if (clear) 0.52f else 0.36f)
        val fill = alpha(fg, if (clear) 0.16f else 0.08f)
        val shadow = clear
        /** Clear glass draws white rings unless the user turned on coloured rings (then they take each AI's own colour). */
        fun accent(id: String) = if (clear && !colorRings) 0xFFFFFFFF.toInt() else t.accent(id)
        fun level(p: Double?, id: String): Int {
            val a = accent(id)
            return when { p == null -> a; p >= 90 -> t.red; p >= 75 -> if (clear) t.yellow else t.orange; else -> a }
        }
    }

    /** Call [Type.init] first so the text is set in Inter. */
    fun render(s: UiState, wPx: Int, hPx: Int, density: Float, dark: Boolean, nowMs: Long, glass: String, cornerDp: Float = 24f, colorRings: Boolean = false): Bitmap {
        val bmp = Bitmap.createBitmap(wPx, hPx, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        c.scale(density, density)
        val w = wPx / density; val h = hPx / density
        val ink = Look(dark, glass, colorRings)
        glassBody(c, w, h, cornerDp, ink, dark)
        val ps = Renderer.visible(s)
        refreshButton(c, w, s, ink)
        when {
            ps.isEmpty() -> empty(c, w, h, s, ink)
            h >= 96f + ps.size * 92f -> large(c, w, h, ps, s, ink, nowMs)
            h >= 64f + ps.size * 40f -> medium(c, w, h, ps, s, ink, nowMs)
            else -> small(c, w, h, ps, s, ink)
        }
        return bmp
    }

    // ---- the glass slab -------------------------------------------------------------------------------------------

    private fun glassBody(c: Canvas, w: Float, h: Float, cornerDp: Float, ink: Look, dark: Boolean) {
        val r = RectF(0.5f, 0.5f, w - 0.5f, h - 0.5f)
        val path = Squircle.path(r, cornerDp)
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        c.save(); c.clipPath(path)
        // sheen from the top edge, a darker lower lip: the slab reads as thick glass
        p.shader = LinearGradient(0f, 0f, 0f, h, intArrayOf(withAlpha(-1, if (ink.clear) 0x2A else 0x22), withAlpha(-1, 0x06), 0, withAlpha(0xFF000000.toInt(), if (ink.clear) 0x1C else 0x10)), floatArrayOf(0f, 0.22f, 0.7f, 1f), Shader.TileMode.CLAMP)
        c.drawRect(r, p)
        // broad specular bloom near the lit corner
        p.shader = RadialGradient(w * 0.12f, 0f, max(w, h) * 0.55f, intArrayOf(withAlpha(-1, if (ink.clear) 0x26 else 0x1A), withAlpha(-1, 0)), floatArrayOf(0f, 1f), Shader.TileMode.CLAMP)
        c.drawRect(r, p)
        c.restore()
        // the rim itself: bright where it faces the light (top-left), a fainter catch on the opposite corner
        val rim = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE; strokeWidth = 1.1f
            shader = LinearGradient(0f, 0f, w, h, intArrayOf(withAlpha(-1, if (dark || ink.clear) 0xC8 else 0xFF), withAlpha(-1, 0x30), withAlpha(-1, 0x14), withAlpha(-1, if (dark || ink.clear) 0x50 else 0x60)), floatArrayOf(0f, 0.35f, 0.62f, 1f), Shader.TileMode.CLAMP)
        }
        c.drawPath(path, rim)
    }

    // ---- content ----------------------------------------------------------------------------------------------------

    private const val PAD = 18f

    private fun paint(style: Type.Style, color: Int, ink: Look, weight: Int = style.weight, tabular: Boolean = false, size: Float = style.size) =
        Type.paint(style, color, weight, tabular, size).apply { if (ink.shadow) setShadowLayer(2.4f, 0f, 0.6f, 0x66000000) }

    private fun refreshButton(c: Canvas, w: Float, s: UiState, ink: Look) {
        val (cx, cy) = Renderer.refreshCenter(w)
        c.drawCircle(cx, cy, 16f, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = ink.fill })
        Glyphs.refresh(c, cx, cy, 17f, if (s.busy) ink.t.blue else if (s.paired && s.hostOnline == false) ink.t.orange else ink.fg2)
    }

    private fun header(c: Canvas, w: Float, s: UiState, ink: Look, now: Long): Float {
        val title = paint(Type.Style.Headline, ink.fg, ink)
        c.drawText(Fmt.appTitle, PAD, PAD + 15f, title)
        val sub = when {
            !s.paired -> Fmt.notPaired
            s.busy -> Fmt.updating
            s.hostOnline == false -> Fmt.pcOffline
            s.snapshot == null -> Fmt.waiting
            else -> Fmt.clock(s.snapshot.generatedAtMs) + " · " + Fmt.age(s.snapshot.generatedAtMs, now)
        }
        val sp = paint(Type.Style.Caption1, if (s.paired && s.hostOnline == false) ink.t.orange else ink.fg2, ink, 500)
        c.drawText(Ink.fit(sub, sp, w - PAD * 2 - 44f), PAD, PAD + 32f, sp)
        return PAD + 46f
    }

    private fun tile(c: Canvas, x: Float, cy: Float, size: Float, p: Provider, s: UiState, ink: Look) {
        val coloured = !ink.clear || ink.colorRings
        val accent = if (coloured) ink.t.tile(p.id) else ink.fill
        val path = Squircle.path(RectF(x, cy - size / 2, x + size, cy + size / 2), size * 0.25f)
        c.drawPath(path, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = accent })
        Icons.draw(c, s.icons[p.id] ?: Icons.default(p.id), x + size / 2, cy, size * 0.36f, -1, eye = if (coloured) accent else 0xFF3A3A3C.toInt())
    }

    private fun large(c: Canvas, w: Float, h: Float, ps: List<Provider>, s: UiState, ink: Look, now: Long) {
        var y = header(c, w, s, ink, now) + 4f
        val per = (h - y - PAD) / ps.size
        val gridCols = ps.maxOf { pr -> pr.windows.count { it.percent != null } }.coerceIn(1, 4)
        val name = paint(Type.Style.Subheadline, ink.fg, ink, 600)
        val plan = paint(Type.Style.Caption2, ink.fg2, ink, 600)
        val lab = paint(Type.Style.Caption1, ink.fg, ink, 600).apply { textAlign = Paint.Align.CENTER }
        val cd = paint(Type.Style.Caption2, ink.fg2, ink, 400, tabular = true).apply { textAlign = Paint.Align.CENTER }
        val sep = Paint().apply { color = ink.fill }
        ps.forEachIndexed { i, p ->
            if (i > 0) c.drawRect(PAD, y - 0.5f, w - PAD, y, sep)
            val top = y + 10f
            var x = PAD
            if (s.showIcons) { tile(c, x, top + 11f, 22f, p, s, ink); x += 30f }
            val nm = Ink.fit(p.name, name, w - x - PAD - 60f)
            Ink.centred(c, nm, x, top + 11f, name)
            if (p.plan != null) { val px0 = x + name.measureText(nm) + 7f; Ink.centred(c, Ink.fit(p.plan, plan, w - px0 - PAD), px0, top + 11.5f, plan) }
            if (p.status == "stale") { val st = paint(Type.Style.Caption2, ink.t.orange, ink, 600).apply { textAlign = Paint.Align.RIGHT }; Ink.centred(c, if (Fmt.pt) "Antigo" else "Stale", w - PAD, top + 11f, st) }
            val ws = p.windows.filter { it.percent != null }
            val ringTop = top + 30f
            if (ws.isEmpty()) {
                val e = paint(Type.Style.Footnote, ink.fg2, ink).apply { textAlign = Paint.Align.CENTER }
                Ink.centred(c, Ink.fit(Fmt.error(p.errorCode), e, w - PAD * 2), w / 2, ringTop + (per - 40f) / 2f, e)
            } else {
                // accessory-style cells on one grid shared by every provider: ring left, value / window / countdown right
                val n = min(ws.size, 4)
                val colW = (w - PAD * 2) / max(n, 2)
                val d = (per - 34f).coerceIn(26f, min(40f, colW * 0.36f))
                val stroke = max(4f, d * 0.15f)
                val big = paint(Type.Style.Headline, ink.fg, ink, 600, tabular = true, size = 17f)
                val unit = paint(Type.Style.Caption2, ink.fg2, ink, 600, size = 11f)
                val left = Paint.Align.LEFT
                lab.textAlign = left; cd.textAlign = left
                ws.take(n).forEachIndexed { j, wd ->
                    val x0 = PAD + colW * j
                    val cy = ringTop + d / 2f + 2f
                    val col = ink.level(wd.percent, p.id)
                    Rings.draw(c, x0 + d / 2f, cy, d / 2f - stroke / 2f, stroke, (wd.percent!! / 100.0).toFloat(), col, alpha(col, if (ink.clear) 0.24f else 0.2f))
                    val tx = x0 + d + 8f
                    val tw = colW - d - 12f
                    val num = Fmt.percent(wd.percent).removeSuffix("%")
                    big.textAlign = left; unit.textAlign = left
                    val base = cy - 4f
                    c.drawText(num, tx, base, big)
                    c.drawText("%", tx + big.measureText(num) + 1f, base, unit)
                    c.drawText(Ink.fit(Fmt.windowName(wd.id, wd.label), lab, tw), tx, base + 14f, lab)
                    c.drawText(Ink.fit(Fmt.countdown(wd.resetsAtMs, now), cd, tw), tx, base + 27f, cd)
                }
            }
            y += per
        }
    }

    private fun medium(c: Canvas, w: Float, h: Float, ps: List<Provider>, s: UiState, ink: Look, now: Long) {
        var y = header(c, w, s, ink, now)
        val row = ((h - y - PAD + 6f) / ps.size).coerceIn(36f, 64f)
        y += ((h - y - PAD) - row * ps.size).coerceAtLeast(0f) / 2f
        val name = paint(Type.Style.Subheadline, ink.fg, ink, 600)
        val pct = paint(Type.Style.Subheadline, ink.fg, ink, 600, tabular = true)
        val lab = paint(Type.Style.Caption2, ink.fg2, ink, 500)
        for (p in ps) {
            val cy = y + row / 2f
            var x = PAD
            if (s.showIcons) { tile(c, x, cy, 22f, p, s, ink); x += 30f }
            val ws = p.windows.filter { it.percent != null }
            // right-aligned window gauges: mini ring + value + label
            var right = w - PAD
            val gauges = ArrayList<Float>()
            if (ws.isEmpty()) {
                val e = paint(Type.Style.Caption1, ink.fg2, ink).apply { textAlign = Paint.Align.RIGHT }
                Ink.centred(c, Ink.fit(Fmt.error(p.errorCode), e, w - x - PAD - 90f), right, cy, e)
                right -= e.measureText(Fmt.error(p.errorCode)) + 8f
            } else for (wd in ws.reversed()) {
                val v = Fmt.percent(wd.percent); val l = Fmt.windowName(wd.id, wd.label, true)
                val gw = 20f + 5f + max(pct.measureText(v), lab.measureText(l))
                if (right - gw < x + 70f) break
                val col = ink.level(wd.percent, p.id)
                Rings.draw(c, right - gw + 10f, cy, 8f, 3.6f, (wd.percent!! / 100.0).toFloat(), col, alpha(col, 0.22f))
                c.drawText(v, right - gw + 25f, cy + 1f, pct)
                c.drawText(l, right - gw + 25f, cy + 13f, lab)
                right -= gw + 12f
                gauges += right
            }
            Ink.centred(c, Ink.fit(p.name, name, right - x - 4f), x, cy, name)
            y += row
        }
    }

    private fun small(c: Canvas, w: Float, h: Float, ps: List<Provider>, s: UiState, ink: Look) {
        val row = ((h - 20f) / ps.size).coerceIn(26f, 48f)
        var y = ((h - row * ps.size) / 2f).coerceAtLeast(10f)
        val name = paint(Type.Style.Footnote, ink.fg, ink, 600)
        val pct = paint(Type.Style.Footnote, ink.fg, ink, 600, tabular = true).apply { textAlign = Paint.Align.RIGHT }
        val lab = paint(Type.Style.Caption2, ink.fg2, ink, 500).apply { textAlign = Paint.Align.RIGHT }
        for (p in ps) {
            val cy = y + row / 2f
            var x = PAD
            if (s.showIcons) { tile(c, x, cy, 18f, p, s, ink); x += 25f }
            var right = w - PAD - 44f
            for (wd in p.windows.filter { it.percent != null }.reversed()) {
                val v = Fmt.percent(wd.percent); val l = Fmt.windowName(wd.id, wd.label, true)
                val vw = pct.measureText(v); val lw = lab.measureText(l)
                if (right - vw - lw - 4f < x + 50f) break
                pct.color = ink.level(wd.percent, p.id).let { if (it == ink.accent(p.id)) ink.fg else it }
                Ink.centred(c, v, right, cy, pct)
                Ink.centred(c, l, right - vw - 4f, cy, lab)
                right -= vw + lw + 14f
            }
            Ink.centred(c, Ink.fit(p.name, name, right - x), x, cy, name)
            y += row
        }
    }

    private fun empty(c: Canvas, w: Float, h: Float, s: UiState, ink: Look) {
        val y = header(c, w, s, ink, System.currentTimeMillis())
        val msg = paint(Type.Style.Subheadline, ink.fg2, ink).apply { textAlign = Paint.Align.CENTER }
        val text = if (!s.paired) Fmt.pairHelp else if (s.hostOnline == false) Fmt.pcOffline else Fmt.waiting
        val lines = Ink.wrap(text, msg, w - PAD * 3, 3)
        val top = y + (h - y - PAD - lines.size * 20f) / 2f
        lines.forEachIndexed { i, l -> Ink.centred(c, l, w / 2, top + 10f + i * 20f, msg) }
    }

}
