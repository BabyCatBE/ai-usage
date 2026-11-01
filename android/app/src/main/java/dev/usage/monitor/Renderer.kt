package dev.usage.monitor

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.RadialGradient
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
    val icons: Map<String, String> = emptyMap(),
    val showIcons: Boolean = true,
)

enum class Level { DETAIL, NORMAL, COMPACT, MICRO }

class Palette(val dark: Boolean, val glass: Boolean = false, val variant: CyberVariant? = null) {
    val soft = variant?.soft == true
    val cyber = variant != null && !soft
    /** Liquid glass, the neon look and the soft look share the backdrop drawing path. */
    val frosted = glass || variant != null
    val base = variant?.base ?: if (glass) (if (dark) 0xFF0B1024.toInt() else 0xFFE9EEFA.toInt()) else if (dark) 0xFF2A2E35.toInt() else 0xFFE4E8EE.toInt()
    val light = if (soft) mixWhite(variant!!.base, if (variant.dark) 0.06f else 0.55f) else variant?.primary ?: if (glass) (if (dark) 0xFF6E80B0.toInt() else 0xFFFFFFFF.toInt()) else if (dark) 0xFF383D46.toInt() else 0xFFFFFFFF.toInt()
    val shade = variant?.ink ?: if (glass) (if (dark) 0xFF050818.toInt() else 0xFF8EA0C4.toInt()) else if (dark) 0xFF1B1E23.toInt() else 0xFFA9B4C6.toInt()
    val text = variant?.text ?: if (glass) (if (dark) 0xFFF2F6FF.toInt() else 0xFF1B253D.toInt()) else if (dark) 0xFFE9ECF2.toInt() else 0xFF343B48.toInt()
    val dim = variant?.dim ?: if (glass) (if (dark) 0xFFB8C6E4.toInt() else 0xFF4F5F80.toInt()) else if (dark) 0xFF9AA3B2.toInt() else 0xFF7A8496.toInt()
    val warn = variant?.warn ?: 0xFFE6A23C.toInt()
    val crit = variant?.crit ?: 0xFFE5534B.toInt()
    fun accent(id: String) = if (variant != null) when (id) {
        "codex" -> variant.codex
        "claude" -> variant.claude
        "opencode" -> variant.open
        else -> variant.primary
    } else when (id) {
        "codex" -> 0xFF2FCBA0.toInt()
        "claude" -> 0xFFE0875A.toInt()
        "opencode" -> 0xFF9B87F5.toInt()
        else -> 0xFF5AA9E6.toInt()
    }
    private fun mixWhite(c: Int, f: Float): Int {
        fun ch(v: Int): Int = (v + (255 - v) * f).toInt().coerceIn(0, 255)
        return 0xFF000000.toInt() or (ch(c shr 16 and 255) shl 16) or (ch(c shr 8 and 255) shl 8) or ch(c and 255)
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
    fun render(s: UiState, wPx: Int, hPx: Int, density: Float, pal: Palette, nowMs: Long, forced: Level? = null, rounded: Boolean = true): Bitmap {
        val bmp = Bitmap.createBitmap(wPx, hPx, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        val level = forced ?: pickLevel(s, hPx / density)
        val natural = naturalHeightDp(s, level)
        val scale = if (forced == null && natural > hPx / density) (hPx / density / natural).coerceIn(0.55f, 1f) else 1f
        c.scale(density * scale, density * scale)
        draw(c, wPx / density / scale, hPx / density / scale, s, level, pal, nowMs, rounded)
        return bmp
    }

    /** Dashboard for the app: natural height for the given width. */
    fun renderNatural(s: UiState, wPx: Int, density: Float, pal: Palette, nowMs: Long, level: Level = Level.DETAIL): Bitmap {
        val h = (naturalHeightDp(s, level) * density).toInt().coerceAtLeast(1)
        return render(s, wPx, h, density, pal, nowMs, level, rounded = false)
    }

    /** Centre of the refresh button in dp, for hit-testing. */
    fun refreshCenter(wDp: Float) = (wDp - PAD - 22f) to (PAD + 22f)

    // ---- drawing ------------------------------------------------------------------------------------------

    private fun draw(c: Canvas, w: Float, h: Float, s: UiState, level: Level, pal: Palette, now: Long, rounded: Boolean) {
        val bg = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.base }
        if (pal.frosted) {
            if (rounded) { c.save(); c.clipPath(Path().apply { addRoundRect(RectF(0f, 0f, w, h), 28f, 28f, Path.Direction.CW) }) }
            if (rounded) background(c, w, h, pal) // in the app the window backdrop shows through
            if (rounded) c.restore()
        } else if (rounded) c.drawRoundRect(0f, 0f, w, h, 28f, 28f, bg) else c.drawColor(pal.base)
        val ps = visible(s)
        if (level == Level.MICRO) { micro(c, w, h, ps, s, pal, now); return }
        header(c, w, s, pal, now)
        val extra = if (ps.isEmpty()) 0f else ((h - naturalHeightDp(s, level)) / ps.size).coerceIn(0f, 24f)
        var y = PAD + HEADER + 6f + extra / 2
        if (ps.isEmpty()) { emptyCard(c, w, y, s, pal); return }
        for (p in ps) {
            val ch = cardHeight(p, level)
            card(c, RectF(PAD, y, w - PAD, y + ch), p, level, pal, now, s.icons[p.id] ?: Icons.default(p.id), s.showIcons)
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
            if (s.showIcons) Icons.draw(c, s.icons[p.id] ?: Icons.default(p.id), PAD + 9, cyRow, 9f, accent)
            text(c, p.name, if (s.showIcons) PAD + 24 else PAD + 2, cyRow + 4.5f, 13f, bold, pal.text)
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

    private fun card(c: Canvas, r: RectF, p: Provider, level: Level, pal: Palette, now: Long, icon: String, showIcon: Boolean) {
        raised(c, r, 20f, pal, 5f, 11f)
        val accent = pal.accent(p.id)
        val inner = RectF(r.left + 16, r.top, r.right - 16, r.bottom)
        val topPad = if (level == Level.DETAIL) 16f else 12f
        var y = r.top + topPad
        // header: badge + name + plan + status
        val bh = if (level == Level.DETAIL) 34f else if (level == Level.NORMAL) 30f else 24f
        val off = if (showIcon) 32f else 2f
        if (showIcon) badge(c, icon, inner.left + 13, y + bh / 2 - 2, 12f, accent, pal)
        val nameSize = if (level == Level.COMPACT) 14f else 15.5f
        text(c, p.name, inner.left + off, y + bh / 2 + 3, nameSize, bold, pal.text)
        var nx = inner.left + off + measure(p.name, nameSize, bold) + 8
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

    private fun a(color: Int, alpha: Int) = (alpha shl 24) or (color and 0x00FFFFFF)
    private fun mix(x: Int, y: Int): Int = ((((x shr 16 and 255) + (y shr 16 and 255)) / 2) shl 16) or ((((x shr 8 and 255) + (y shr 8 and 255)) / 2) shl 8) or (((x and 255) + (y and 255)) / 2) or 0xFF000000.toInt()

    internal fun background(c: Canvas, w: Float, h: Float, pal: Palette) { val v = pal.variant; if (v != null && v.soft) softBackground(c, w, h, v) else if (v != null) cyberBackground(c, w, h, v) else glassBackground(c, w, h, pal.dark) }

    /** Soft theme backdrop: a barely-there three-stop gradient, nothing else. */
    internal fun softBackground(c: Canvas, w: Float, h: Float, v: CyberVariant) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.shader = LinearGradient(0f, 0f, w, h, intArrayOf(v.bgA, v.bgB, v.bgC), floatArrayOf(0f, 0.55f, 1f), Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, w, h, p)
    }

    /** Night-city backdrop of the cyberpunk theme: ink-violet gradient, faint cyan grid, magenta and cyan glows. */
    internal fun cyberBackground(c: Canvas, w: Float, h: Float, v: CyberVariant) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.shader = LinearGradient(0f, 0f, w, h, intArrayOf(v.bgA, v.bgB, v.bgC), floatArrayOf(0f, 0.55f, 1f), Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, w, h, p)
        fun glow(cx: Float, cy: Float, rad: Float, rgb: Int, a: Int) {
            p.shader = RadialGradient(cx, cy, rad, (a shl 24) or rgb, rgb, Shader.TileMode.CLAMP)
            c.drawCircle(cx, cy, rad, p)
        }
        val m = maxOf(w, h)
        glow(w * 0.92f, h * 0.06f, m * 0.55f, v.glow1 and 0xFFFFFF, if (v.dark) 0x70 else 0x60)
        glow(w * 0.08f, h * 0.94f, m * 0.6f, v.glow2 and 0xFFFFFF, if (v.dark) 0x60 else 0x55)
        val grid = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1f; color = a(v.primary, if (v.dark) 0x16 else 0x1C) }
        val step = m / 12f
        var x = step; while (x < w) { c.drawLine(x, 0f, x, h, grid); x += step }
        var y = step; while (y < h) { c.drawLine(0f, y, w, y, grid); y += step }
    }

    /** Deep night gradient with soft violet, cyan and pink glows (the "glass" theme backdrop). */
    internal fun glassBackground(c: Canvas, w: Float, h: Float, dark: Boolean) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.shader = LinearGradient(0f, 0f, w, h, if (dark) intArrayOf(0xFF0A0E22.toInt(), 0xFF171245.toInt(), 0xFF082035.toInt()) else intArrayOf(0xFFDDE7FB.toInt(), 0xFFEAE0FB.toInt(), 0xFFD6F0F6.toInt()), floatArrayOf(0f, 0.55f, 1f), Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, w, h, p)
        fun glow(cx: Float, cy: Float, rad: Float, rgb: Int, a: Int) {
            p.shader = RadialGradient(cx, cy, rad, (a shl 24) or rgb, rgb, Shader.TileMode.CLAMP)
            c.drawCircle(cx, cy, rad, p)
        }
        val m = maxOf(w, h)
        glow(w * 0.15f, h * 0.08f, m * 0.55f, 0x8B5CF6, if (dark) 0x90 else 0x55)
        glow(w * 0.9f, h * 0.95f, m * 0.55f, 0x22D3EE, if (dark) 0x70 else 0x50)
        glow(w * 0.95f, h * 0.05f, m * 0.35f, 0xF472B6, 0x40)
    }

    /** Raised surface: light shadow top-left, dark shadow bottom-right (glass: frosted pane with a bright rim). */
    internal fun raised(c: Canvas, r: RectF, radius: Float, pal: Palette, offset: Float, blur: Float) {
        pal.variant?.let { v ->
            if (v.soft) {   // flat surface, hairline border, no shadow or glow
                val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.light }
                c.drawRoundRect(r, radius * 0.8f, radius * 0.8f, fill)
                val rim = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1f; color = a(v.dim, if (v.dark) 0x30 else 0x38) }
                c.drawRoundRect(r, radius * 0.8f, radius * 0.8f, rim)
                return
            }
            val rad = radius * 0.4f
            val fa = if (v.dark) 0x26 else 0x1C
            val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = a(v.base, if (v.dark) 0xF0 else 0xEC); setShadowLayer(blur * 1.2f, 0f, 0f, a(v.primary, 0x66)) }
            c.drawRoundRect(r, rad, rad, p)
            p.clearShadowLayer()
            p.shader = LinearGradient(r.left, r.top, r.right, r.bottom, intArrayOf(a(v.primary, fa), a(mix(v.primary, v.secondary), fa / 2), a(v.secondary, fa * 9 / 10)), floatArrayOf(0f, 0.55f, 1f), Shader.TileMode.CLAMP)
            c.drawRoundRect(r, rad, rad, p)
            val rim = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE; strokeWidth = 1.8f
                shader = LinearGradient(r.left, r.top, r.right, r.bottom, intArrayOf(v.primary, a(mix(v.primary, v.secondary), 0x70), v.secondary), floatArrayOf(0f, 0.5f, 1f), Shader.TileMode.CLAMP)
            }
            c.drawRoundRect(r, rad, rad, rim)
            return
        }
        if (pal.glass) {
            val d = pal.dark
            val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = if (d) 0xE80B1024.toInt() else 0xEEF4F7FF.toInt(); setShadowLayer(blur * 1.6f, 0f, offset * 0.8f, if (d) 0x88000000.toInt() else 0x555B6FA8) }
            c.drawRoundRect(r, radius, radius, p)
            p.clearShadowLayer()
            p.shader = LinearGradient(r.left, r.top, r.right, r.bottom, if (d) 0x38FFFFFF else 0x99FFFFFF.toInt(), if (d) 0x12C8DCFF else 0x33C8D7FF, Shader.TileMode.CLAMP)
            c.drawRoundRect(r, radius, radius, p)
            val rim = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE; strokeWidth = 1.2f
                shader = LinearGradient(r.left, r.top, r.right, r.bottom, if (d) intArrayOf(0xA0FFFFFF.toInt(), 0x1CA0BEFF, 0x55FFFFFF) else intArrayOf(0xFFFFFFFF.toInt(), 0x7096AADC, 0xC0FFFFFF.toInt()), floatArrayOf(0f, 0.5f, 1f), Shader.TileMode.CLAMP)
            }
            c.drawRoundRect(r, radius, radius, rim)
            return
        }
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = pal.base }
        p.setShadowLayer(blur, offset, offset, pal.shade); c.drawRoundRect(r, radius, radius, p)
        p.setShadowLayer(blur, -offset, -offset, pal.light); c.drawRoundRect(r, radius, radius, p)
        p.clearShadowLayer(); c.drawRoundRect(r, radius, radius, p)
    }

    /** Pressed-in surface: shadows are cast inwards from the edge. */
    internal fun inset(c: Canvas, r: RectF, radius: Float, pal: Palette, depth: Float = 2f) {
        pal.variant?.let { v ->
            if (v.soft) { c.drawRoundRect(r, radius, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = a(v.ink, if (v.dark) 0xB0 else 0xA0) }); return }
            val rad = minOf(radius, 4f)
            c.drawRoundRect(r, rad, rad, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = a(v.ink, 0xD7) })
            c.drawRoundRect(r, rad, rad, Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1f; color = a(v.primary, 0x50) })
            return
        }
        if (pal.glass) {
            c.drawRoundRect(r, radius, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = if (pal.dark) 0x66000000 else 0x24284878 })
            c.drawRoundRect(r, radius, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1f; color = if (pal.dark) 0x2EFFFFFF else 0x80FFFFFF.toInt() })
            return
        }
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
        val glow = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color; setShadowLayer(if (pal.cyber) 9f else 5f, 0f, 0f, (color and 0x00FFFFFF) or 0x80000000.toInt()) }
        if (!pal.soft) c.drawRoundRect(fr, radius, radius, glow)
        val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            shader = if (pal.cyber) LinearGradient(fr.left, 0f, fr.right, 0f, (color and 0x00FFFFFF) or 0xBB000000.toInt(), lighten(color, 0.45f), Shader.TileMode.CLAMP)
            else LinearGradient(fr.left, fr.top, fr.left, fr.bottom, lighten(color, 0.28f), color, Shader.TileMode.CLAMP)
        }
        c.drawRoundRect(fr, radius, radius, fill)
    }

    private fun lighten(color: Int, f: Float): Int {
        fun ch(v: Int) = (v + (255 - v) * f).toInt().coerceIn(0, 255)
        return (color and 0xFF000000.toInt()) or (ch((color shr 16) and 255) shl 16) or (ch((color shr 8) and 255) shl 8) or ch(color and 255)
    }

    private fun badge(c: Canvas, iconId: String, cx: Float, cy: Float, r: Float, color: Int, pal: Palette) {
        inset(c, RectF(cx - r, cy - r, cx + r, cy + r), r, pal, 2f)
        Icons.draw(c, iconId, cx, cy, r, color)
    }
}
