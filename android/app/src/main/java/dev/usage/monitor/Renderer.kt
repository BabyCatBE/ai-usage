package dev.usage.monitor

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface

/** What the UI knows about the link, independent of the data itself. */
data class UiState(
    val snapshot: Snapshot?,
    val paired: Boolean,
    val hostOnline: Boolean?,   // null = unknown
    val receivedAtMs: Long,
    val busy: Boolean,
)

enum class Level { DETAIL, NORMAL, COMPACT, MICRO }

class Palette(val dark: Boolean) {
    val base = if (dark) 0xFF2A2E35.toInt() else 0xFFE4E8EE.toInt()
    val light = if (dark) 0xFF383D46.toInt() else 0xFFFFFFFF.toInt()
    val shade = if (dark) 0xFF1B1E23.toInt() else 0xFFA9B4C6.toInt()
    val text = if (dark) 0xFFE9ECF2.toInt() else 0xFF343B48.toInt()
    val dim = if (dark) 0xFF9AA3B2.toInt() else 0xFF7A8496.toInt()
    val warn = 0xFFE6A23C.toInt()
    val crit = 0xFFE5534B.toInt()
    fun accent(id: String) = when (id) {
        "codex" -> 0xFF2FCBA0.toInt()
        "claude" -> 0xFFE0875A.toInt()
        "opencode" -> 0xFF9B87F5.toInt()
        else -> 0xFF5AA9E6.toInt()
    }
    fun level(percent: Double?, accent: Int) = when {
        percent == null -> accent
        percent >= 85 -> crit
        percent >= 60 -> warn
        else -> accent
    }
}

/**
 * Soft neumorphism drawn with Canvas. The same code paints the home-screen widget (into a bitmap)
 * and the app dashboard, so both always look identical.
 */
object Renderer {
    private const val PAD = 16f
    private const val GAP = 12f
    private const val HEADER = 56f

    private val regular = Typeface.create("sans-serif", Typeface.NORMAL)
    private val medium = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    private val bold = Typeface.create("sans-serif", Typeface.BOLD)

    fun visible(s: UiState) = s.snapshot?.providers?.filter { !it.hidden } ?: emptyList()

    // ---- measurements (dp) --------------------------------------------------------------------------------

    private fun modelLines(p: Provider, level: Level) = if (p.models.isEmpty()) 0 else if (level == Level.DETAIL) 2 else 1

    private fun cardHeight(p: Provider, level: Level): Float {
        val rows = p.windows.count { it.percent != null }
        val failed = rows == 0
        return when (level) {
            Level.DETAIL -> 16f + 34f + (if (failed) 40f else rows * 60f) + (if (!failed && p.models.isNotEmpty()) 8f + modelLines(p, level) * 16f else 0f) + 10f
            Level.NORMAL -> 12f + 30f + (if (failed) 30f else rows * 36f) + (if (!failed && p.models.isNotEmpty()) 6f + 16f else 0f) + 8f
            Level.COMPACT -> 12f + 24f + (if (failed) 20f else 38f) + 8f
            Level.MICRO -> 34f
        }
    }

    /** Natural height in dp. */
    fun naturalHeightDp(s: UiState, level: Level): Float {
        val ps = visible(s)
        if (level == Level.MICRO) return 12f + 12f + maxOf(1, ps.size) * 34f
        val body = if (ps.isEmpty()) 110f else ps.sumOf { cardHeight(it, level).toDouble() }.toFloat() + GAP * (ps.size - 1)
        return PAD + HEADER + 6f + body + PAD
    }

    fun pickLevel(s: UiState, heightDp: Float): Level =
        Level.entries.firstOrNull { naturalHeightDp(s, it) <= heightDp * 1.2f } ?: Level.MICRO

    // ---- entry points ------------------------------------------------------------------------------------

    /** Renders to a bitmap of exactly w×h px. The level is chosen to fit unless [forced]. */
    fun render(s: UiState, wPx: Int, hPx: Int, density: Float, dark: Boolean, nowMs: Long, forced: Level? = null, rounded: Boolean = true): Bitmap {
        val bmp = Bitmap.createBitmap(wPx, hPx, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        val level = forced ?: pickLevel(s, hPx / density)
        val natural = naturalHeightDp(s, level)
        val scale = if (forced == null && natural > hPx / density) (hPx / density / natural).coerceIn(0.55f, 1f) else 1f
        c.scale(density * scale, density * scale)
        draw(c, wPx / density / scale, hPx / density / scale, s, level, Palette(dark), nowMs, rounded)
        return bmp
    }

    /** Dashboard for the app: natural height for the given width. */
    fun renderNatural(s: UiState, wPx: Int, density: Float, dark: Boolean, nowMs: Long, level: Level = Level.DETAIL): Bitmap {
        val h = (naturalHeightDp(s, level) * density).toInt().coerceAtLeast(1)
        return render(s, wPx, h, density, dark, nowMs, level, rounded = false)
    }

    /** Centre of the refresh button in dp, for hit-testing. */
    fun refreshCenter(wDp: Float) = (wDp - PAD - 22f) to (PAD + 22f)

    // ---- drawing ------------------------------------------------------------------------------------------

    private fun draw(c: Canvas, w: Float, h: Float, s: UiState, level: Level, pal: Palette, now: Long, rounded: Boolean) {
        val bg = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.base }
        if (rounded) c.drawRoundRect(0f, 0f, w, h, 28f, 28f, bg) else c.drawColor(pal.base)
        val ps = visible(s)
        if (level == Level.MICRO) { micro(c, w, h, ps, s, pal, now); return }
        header(c, w, s, pal, now)
        val extra = if (ps.isEmpty()) 0f else ((h - naturalHeightDp(s, level)) / ps.size).coerceIn(0f, 24f)
        var y = PAD + HEADER + 6f + extra / 2
        if (ps.isEmpty()) { emptyCard(c, w, y, s, pal); return }
        for (p in ps) {
            val ch = cardHeight(p, level)
            card(c, RectF(PAD, y, w - PAD, y + ch), p, level, pal, now)
            y += ch + GAP + extra
        }
    }

    /** One line per provider for very short widgets: dot, name and one pill per window. */
    private fun micro(c: Canvas, w: Float, h: Float, ps: List<Provider>, s: UiState, pal: Palette, now: Long) {
        val (cx, cy) = refreshCenter(w)
        raised(c, RectF(cx - 18, cy - 18, cx + 18, cy + 18), 18f, pal, 3f, 7f)
        refreshGlyph(c, cx, cy, if (s.busy) pal.accent("codex") else if (s.hostOnline == false) pal.warn else pal.dim)
        if (ps.isEmpty()) { text(c, if (!s.paired) Fmt.notPaired else Fmt.waiting, PAD, 40f, 13f, medium, pal.text, maxWidth = w - PAD * 2 - 56); return }
        val right = w - PAD - 52
        val rowH = ((h - 24f) / ps.size).coerceIn(34f, 58f)
        var y = ((h - rowH * ps.size) / 2f).coerceAtLeast(12f)
        for (p in ps) {
            val accent = pal.accent(p.id)
            val cyRow = y + rowH / 2f
            c.drawCircle(PAD + 6, cyRow, 4.5f, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = accent })
            text(c, p.name, PAD + 18, cyRow + 4.5f, 13f, bold, pal.text)
            val ws = p.windows.filter { it.percent != null }
            if (ws.isEmpty()) text(c, Fmt.error(p.errorCode), right, cyRow + 4f, 11f, regular, pal.dim, right = true, maxWidth = right - PAD - 90)
            else {
                var x = right
                for (wd in ws.reversed()) {
                    val pct = Fmt.percent(wd.percent)
                    val col = pal.level(wd.percent, accent)
                    val pw = measure(pct, 13f, bold)
                    text(c, pct, x, cyRow + 4.5f, 13f, bold, col, right = true)
                    val lw = measure(Fmt.windowName(wd.id, wd.label, true), 10.5f, regular)
                    text(c, Fmt.windowName(wd.id, wd.label, true), x - pw - 4, cyRow + 4f, 10.5f, regular, pal.dim, right = true)
                    x -= pw + lw + 16
                }
            }
            y += rowH
        }
    }

    private fun header(c: Canvas, w: Float, s: UiState, pal: Palette, now: Long) {
        text(c, Fmt.appTitle, PAD + 2, PAD + 20, 19f, bold, pal.text)
        val sub = when {
            !s.paired -> Fmt.notPaired
            s.busy -> Fmt.updating
            s.hostOnline == false -> Fmt.pcOffline + if (s.receivedAtMs > 0) " · " + Fmt.clock(s.receivedAtMs) else ""
            s.snapshot == null -> Fmt.waiting
            else -> (if (Fmt.pt) "Atualizado " else "Updated ") + Fmt.clock(s.snapshot.generatedAtMs) + " · " + Fmt.age(s.snapshot.generatedAtMs, now)
        }
        val staleLink = s.paired && s.hostOnline == false
        text(c, sub, PAD + 2, PAD + 38, 11.5f, regular, if (staleLink) pal.warn else pal.dim, maxWidth = w - PAD * 2 - 56)
        val (cx, cy) = Renderer.refreshCenter(w)
        raised(c, RectF(cx - 18, cy - 18, cx + 18, cy + 18), 18f, pal, 3f, 7f)
        refreshGlyph(c, cx, cy, if (s.busy) pal.accent("codex") else pal.dim)
    }

    private fun refreshGlyph(c: Canvas, cx: Float, cy: Float, color: Int) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 2.2f; strokeCap = Paint.Cap.ROUND; this.color = color }
        val r = 7f
        c.drawArc(RectF(cx - r, cy - r, cx + r, cy + r), -50f, 280f, false, p)
        val path = Path().apply { moveTo(cx + r * 0.64f + 3.5f, cy - r * 0.77f - 1.5f); lineTo(cx + r * 0.64f - 1f, cy - r * 0.77f - 1.8f); lineTo(cx + r * 0.64f + 1.3f, cy - r * 0.77f + 3.4f); close() }
        c.drawPath(path, Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL; this.color = color })
    }

    private fun emptyCard(c: Canvas, w: Float, y: Float, s: UiState, pal: Palette) {
        val r = RectF(PAD, y, w - PAD, y + 98f)
        raised(c, r, 20f, pal, 5f, 11f)
        val msg = if (!s.paired) Fmt.notPaired else if (s.hostOnline == false) Fmt.pcOffline else Fmt.waiting
        text(c, msg, r.centerX(), r.top + 42, 14f, medium, pal.text, center = true, maxWidth = r.width() - 24)
        if (!s.paired) text(c, Fmt.pairHelp, r.centerX(), r.top + 64, 11f, regular, pal.dim, center = true, maxWidth = r.width() - 28, maxLines = 2)
    }

    private fun card(c: Canvas, r: RectF, p: Provider, level: Level, pal: Palette, now: Long) {
        raised(c, r, 20f, pal, 5f, 11f)
        val accent = pal.accent(p.id)
        val inner = RectF(r.left + 16, r.top, r.right - 16, r.bottom)
        val topPad = if (level == Level.DETAIL) 16f else 12f
        var y = r.top + topPad
        // header: badge + name + plan + status
        val bh = if (level == Level.DETAIL) 34f else if (level == Level.NORMAL) 30f else 24f
        badge(c, p.id, inner.left + 13, y + bh / 2 - 2, 12f, accent, pal)
        val nameSize = if (level == Level.COMPACT) 14f else 15.5f
        text(c, p.name, inner.left + 32, y + bh / 2 + 3, nameSize, bold, pal.text)
        var nx = inner.left + 32 + measure(p.name, nameSize, bold) + 8
        if (p.plan != null) {
            val pw = measure(p.plan, 10.5f, medium) + 14
            inset(c, RectF(nx, y + bh / 2 - 10, nx + pw, y + bh / 2 + 6), 8f, pal)
            text(c, p.plan, nx + pw / 2, y + bh / 2 + 1.5f, 10.5f, medium, pal.dim, center = true)
            nx += pw + 6
        }
        val statusText = when {
            p.status == "stale" -> (if (Fmt.pt) "Dados antigos" else "Stale") + (p.fetchedAtMs?.let { " · " + Fmt.age(it, now) } ?: "")
            p.status == "error" && p.hasData.not() -> null
            else -> null
        }
        if (statusText != null) {
            dot(c, inner.right - measure(statusText, 10.5f, regular) - 8, y + bh / 2 - 0.5f, pal.warn)
            text(c, statusText, inner.right, y + bh / 2 + 3, 10.5f, regular, pal.warn, right = true)
        }
        y += bh

        val windows = p.windows.filter { it.percent != null }
        if (windows.isEmpty()) {
            text(c, Fmt.error(p.errorCode), inner.centerX(), y + (if (level == Level.DETAIL) 22f else 18f), 12f, regular, pal.dim, center = true, maxWidth = inner.width())
            return
        }
        if (level == Level.COMPACT) {
            val colW = inner.width() / windows.size
            windows.forEachIndexed { i, wdw ->
                val x0 = inner.left + i * colW
                val col = pal.level(wdw.percent, accent)
                text(c, Fmt.windowName(wdw.id, wdw.label, true), x0 + 2, y + 9, 10.5f, regular, pal.dim)
                text(c, Fmt.percent(wdw.percent), x0 + colW - 10, y + 12, 14f, bold, col, right = true)
                bar(c, RectF(x0 + 2, y + 20, x0 + colW - 10, y + 27), wdw.percent!!, col, pal)
            }
            return
        }
        for (wdw in windows) {
            val col = pal.level(wdw.percent, accent)
            if (level == Level.DETAIL) {
                text(c, Fmt.windowName(wdw.id, wdw.label), inner.left, y + 14, 13f, medium, pal.text)
                text(c, Fmt.percent(wdw.percent), inner.right, y + 16, 17f, bold, col, right = true)
                bar(c, RectF(inner.left, y + 24, inner.right, y + 34), wdw.percent!!, col, pal)
                val reset = wdw.resetsAtMs?.let { (if (Fmt.pt) "Reseta em " else "Resets in ") + Fmt.countdown(it, now) + " · " + Fmt.resetAt(it, now) } ?: ""
                text(c, reset, inner.left, y + 49, 11f, regular, pal.dim, maxWidth = inner.width())
                y += 60f
            } else {
                val cd = Fmt.countdown(wdw.resetsAtMs, now)
                text(c, Fmt.windowName(wdw.id, wdw.label, true), inner.left, y + 12, 12.5f, medium, pal.dim)
                text(c, cd, inner.right, y + 12, 11f, regular, pal.dim, right = true)
                text(c, Fmt.percent(wdw.percent), inner.right - measure(cd, 11f, regular) - 10, y + 13, 14f, bold, col, right = true)
                bar(c, RectF(inner.left, y + 19, inner.right, y + 27), wdw.percent!!, col, pal)
                y += 36f
            }
        }
        if (p.models.isNotEmpty()) {
            y += if (level == Level.DETAIL) 8f else 6f
            val items = p.models.map { m -> m.name + " " + (m.spentUsd?.let(Fmt::usd) ?: Fmt.percent(m.percent)) }
            wrapChips(items, inner.width(), 11f, modelLines(p, level)).forEachIndexed { i, line ->
                text(c, line, inner.left, y + 11 + i * 16, 11f, regular, pal.dim)
            }
        }
    }

    // ---- primitives ---------------------------------------------------------------------------------------

    private fun wrapChips(items: List<String>, width: Float, size: Float, maxLines: Int): List<String> {
        val lines = ArrayList<String>(); var cur = ""
        for ((i, it) in items.withIndex()) {
            val next = if (cur.isEmpty()) it else "$cur · $it"
            if (measure(next, size, regular) <= width) cur = next
            else {
                if (cur.isNotEmpty()) lines.add(cur)
                if (lines.size == maxLines) return lines.also { it[maxLines - 1] = ellipsize(it[maxLines - 1] + " …", width, size) }
                cur = it
            }
            if (i == items.lastIndex) lines.add(cur)
        }
        return if (lines.size > maxLines) lines.take(maxLines).toMutableList().also { it[maxLines - 1] = ellipsize(it[maxLines - 1] + " …", width, size) } else lines
    }

    private fun measure(s: String, size: Float, tf: Typeface) = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = size; typeface = tf }.measureText(s)

    private fun ellipsize(s: String, width: Float, size: Float): String {
        if (measure(s, size, regular) <= width) return s
        var t = s
        while (t.length > 1 && measure("$t…", size, regular) > width) t = t.dropLast(1)
        return "$t…"
    }

    private fun text(c: Canvas, s: String, x: Float, y: Float, size: Float, tf: Typeface, color: Int, right: Boolean = false, center: Boolean = false, maxWidth: Float = 0f, maxLines: Int = 1) {
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = size; typeface = tf; this.color = color; textAlign = if (center) Paint.Align.CENTER else if (right) Paint.Align.RIGHT else Paint.Align.LEFT }
        if (maxLines > 1 && maxWidth > 0) {
            val words = s.split(' '); var line = ""; var yy = y; var n = 0
            for (wd in words) {
                val t = if (line.isEmpty()) wd else "$line $wd"
                if (paint.measureText(t) <= maxWidth || line.isEmpty()) line = t
                else { c.drawText(line, x, yy, paint); yy += size + 4; line = wd; if (++n >= maxLines) return }
            }
            c.drawText(line, x, yy, paint); return
        }
        var out = s
        if (maxWidth > 0 && paint.measureText(out) > maxWidth) {
            while (out.length > 1 && paint.measureText("$out…") > maxWidth) out = out.dropLast(1)
            out += "…"
        }
        c.drawText(out, x, y, paint)
    }

    private fun dot(c: Canvas, x: Float, y: Float, color: Int) { c.drawCircle(x, y, 3f, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color }) }

    /** Raised surface: light shadow top-left, dark shadow bottom-right. */
    internal fun raised(c: Canvas, r: RectF, radius: Float, pal: Palette, offset: Float, blur: Float) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.base }
        p.setShadowLayer(blur, offset, offset, pal.shade); c.drawRoundRect(r, radius, radius, p)
        p.setShadowLayer(blur, -offset, -offset, pal.light); c.drawRoundRect(r, radius, radius, p)
        p.clearShadowLayer(); c.drawRoundRect(r, radius, radius, p)
    }

    /** Pressed-in surface: shadows are cast inwards from the edge. */
    internal fun inset(c: Canvas, r: RectF, radius: Float, pal: Palette, depth: Float = 2f) {
        val base = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.base }
        c.drawRoundRect(r, radius, radius, base)
        c.save()
        val clip = Path().apply { addRoundRect(r, radius, radius, Path.Direction.CW) }
        c.clipPath(clip)
        val frame = Path().apply {
            fillType = Path.FillType.EVEN_ODD
            addRect(RectF(r.left - 30, r.top - 30, r.right + 30, r.bottom + 30), Path.Direction.CW)
            addRoundRect(r, radius, radius, Path.Direction.CW)
        }
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.base }
        p.setShadowLayer(depth * 2f, depth, depth, pal.shade); c.drawPath(frame, p)
        p.setShadowLayer(depth * 2f, -depth, -depth, pal.light); c.drawPath(frame, p)
        c.restore()
    }

    private fun bar(c: Canvas, r: RectF, percent: Double, color: Int, pal: Palette) {
        val radius = r.height() / 2
        inset(c, r, radius, pal, 2f)
        val fillW = (r.width() * (percent.coerceIn(0.0, 100.0) / 100.0)).toFloat()
        if (fillW <= 0.5f) return
        val fr = RectF(r.left + 1.2f, r.top + 1.2f, r.left + 1.2f + maxOf(fillW - 2.4f, r.height() - 2.4f), r.bottom - 1.2f)
        val glow = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color; setShadowLayer(5f, 0f, 0f, (color and 0x00FFFFFF) or 0x80000000.toInt()) }
        c.drawRoundRect(fr, radius, radius, glow)
        val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            shader = LinearGradient(fr.left, fr.top, fr.left, fr.bottom, lighten(color, 0.28f), color, Shader.TileMode.CLAMP)
        }
        c.drawRoundRect(fr, radius, radius, fill)
    }

    private fun lighten(color: Int, f: Float): Int {
        fun ch(v: Int) = (v + (255 - v) * f).toInt().coerceIn(0, 255)
        return (color and 0xFF000000.toInt()) or (ch((color shr 16) and 255) shl 16) or (ch((color shr 8) and 255) shl 8) or ch(color and 255)
    }

    private fun badge(c: Canvas, id: String, cx: Float, cy: Float, r: Float, color: Int, pal: Palette) {
        inset(c, RectF(cx - r, cy - r, cx + r, cy + r), r, pal, 2f)
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color; style = Paint.Style.STROKE; strokeWidth = 2f; strokeCap = Paint.Cap.ROUND }
        when (id) {
            "claude" -> for (i in 0 until 4) { // asterisk
                val a = Math.toRadians(i * 45.0 + 0)
                c.drawLine(cx - (r * 0.55f * Math.cos(a)).toFloat(), cy - (r * 0.55f * Math.sin(a)).toFloat(), cx + (r * 0.55f * Math.cos(a)).toFloat(), cy + (r * 0.55f * Math.sin(a)).toFloat(), p)
            }
            "opencode" -> c.drawPath(Path().apply { moveTo(cx, cy - r * 0.55f); lineTo(cx + r * 0.5f, cy); lineTo(cx, cy + r * 0.55f); lineTo(cx - r * 0.5f, cy); close() }, p)
            else -> { // codex / chatgpt: ring with a notch
                c.drawArc(RectF(cx - r * 0.5f, cy - r * 0.5f, cx + r * 0.5f, cy + r * 0.5f), -30f, 300f, false, p)
            }
        }
    }
}
