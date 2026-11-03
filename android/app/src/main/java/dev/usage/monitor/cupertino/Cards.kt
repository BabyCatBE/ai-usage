package dev.usage.monitor.cupertino

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.text.TextPaint
import android.text.TextUtils
import android.view.View
import dev.usage.monitor.Fmt
import dev.usage.monitor.Icons
import dev.usage.monitor.Projection
import dev.usage.monitor.Provider
import kotlin.math.max
import kotlin.math.min

internal fun hostOf(v: View): GlassHost? {
    var p = v.parent
    while (p != null) { if (p is GlassHost) return p; p = p.parent }
    return null
}

/** LinearLayout whose background is a glass block (the pairing and waiting platters on the dashboard). */
class GlassBlockLayout(context: Context, private val fallback: Int, override val glassCornerDp: Float) : android.widget.LinearLayout(context), GlassBlock {
    override var hostDrawsGlass = false
        set(v) { if (field != v) { field = v; background = if (v) null else SquircleDrawable(fallback, glassCornerDp * resources.displayMetrics.density) } }
    init { background = SquircleDrawable(fallback, glassCornerDp * resources.displayMetrics.density) }
    override fun onAttachedToWindow() { super.onAttachedToWindow(); hostOf(this)?.register(this) }
    override fun onDetachedFromWindow() { hostOf(this)?.unregister(this); super.onDetachedFromWindow() }
}

/** Text helpers shared by the cards and the widget renderer. */
internal object Ink {
    fun fit(s: String, p: Paint, width: Float): String =
        if (width <= 0f) "" else TextUtils.ellipsize(s, if (p is TextPaint) p else TextPaint(p), width, TextUtils.TruncateAt.END).toString()

    /** Draws [s] with its baseline placed so the text is optically centred on [cy]. */
    fun centred(c: Canvas, s: String, x: Float, cy: Float, p: Paint) = c.drawText(s, x, cy - (p.descent() + p.ascent()) / 2f, p)

    fun wrap(s: String, p: Paint, width: Float, maxLines: Int): List<String> {
        val words = s.split(' ')
        val out = ArrayList<String>(); var cur = ""
        for (w in words) {
            val next = if (cur.isEmpty()) w else "$cur $w"
            if (p.measureText(next) <= width || cur.isEmpty()) cur = next
            else { out += cur; cur = w; if (out.size == maxLines) break }
        }
        if (out.size < maxLines && cur.isNotEmpty()) out += cur
        if (out.size == maxLines && (out.joinToString(" ").length < s.length)) out[maxLines - 1] = fit(out[maxLines - 1] + " …", p, width)
        return out
    }

    /** Percentage as "37" + small "%" (Apple sets units smaller than the figure). */
    fun percent(c: Canvas, value: Double?, cx: Float, cy: Float, big: Paint, unit: Paint) {
        if (value == null) { centred(c, "—", cx, cy, big.apply { textAlign = Paint.Align.CENTER }); return }
        val num = Fmt.percent(value).removeSuffix("%").trim()
        big.textAlign = Paint.Align.LEFT; unit.textAlign = Paint.Align.LEFT
        val wn = big.measureText(num); val wu = unit.measureText("%") + unit.textSize * 0.06f
        val x0 = cx - (wn + wu) / 2f
        val base = cy - (big.descent() + big.ascent()) / 2f
        c.drawText(num, x0, base, big)
        c.drawText("%", x0 + wn + unit.textSize * 0.06f, base, unit)
    }
}

/**
 * One provider on the dashboard: a content-layer platter (thick material over the aurora, never glass) with a
 * Settings-style icon tile, the plan, and one Activity ring per usage window. Rings fill in on a spring.
 */
@SuppressLint("ViewConstructor")
class ProviderCard(context: Context, private val t: Tokens) : View(context), GlassBlock {
    override val glassCornerDp = Metrics.cardRadius
    override var hostDrawsGlass = false
        set(v) { if (field != v) { field = v; invalidate() } }

    override fun onAttachedToWindow() { super.onAttachedToWindow(); hostOf(this)?.register(this) }
    override fun onDetachedFromWindow() { hostOf(this)?.unregister(this); super.onDetachedFromWindow() }
    // the host draws this card's glass: it must re-record when the card moves or resizes
    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) { super.onLayout(changed, l, t, r, b); if (changed) hostOf(this)?.invalidate() }

    var provider: Provider? = null
        set(v) {
            val old = field
            field = v
            if (old == null || old.windows.map { it.percent } != v?.windows?.map { it.percent }) { from = shown.copyOf(); grow.snapTo(0f); grow.animateTo(1f, 0.9f, 0.86f) }
            if (selected >= 0) projection = computeProjection()
            contentDescription = v?.let { p -> p.name + ": " + p.windows.filter { it.percent != null }.joinToString(", ") { Fmt.windowName(it.id, it.label) + " " + Fmt.percent(it.percent) } }
            requestLayout(); invalidate()
        }
    /** Beta pace projection for one window; null hides the feature (no tapping on rings). */
    var projector: ((Provider, dev.usage.monitor.Window) -> Projection.Result?)? = null
    private var selected = -1
    private var expand = 0f
    private var projection: Projection.Result? = null
    private val ringHits = ArrayList<FloatArray>()
    private val panelH = 116f
    private val panelPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val panelPath = android.graphics.Path()
    var iconId: String = "generic"
    var showIcon = true
    var nowMs = System.currentTimeMillis()

    private val reduceMotion = Tokens.reduceMotion(context)
    private var progress = if (reduceMotion) 1f else 0f
    private val grow = Spring(progress, reduceMotion) { progress = it; invalidate() }
    private var collapsing = false
    private val expandSpring = Spring(0f, reduceMotion) { expand = it; if (it <= 0.001f && collapsing) selected = -1; requestLayout(); invalidate(); hostOf(this)?.invalidate() }

    private fun computeProjection(): Projection.Result? {
        val p = provider ?: return null
        val wd = windows().getOrNull(selected) ?: return null
        return projector?.invoke(p, wd)
    }
    private var from = FloatArray(0)
    private var shown = FloatArray(0)
    private val bg = SquircleDrawable(t.material, Metrics.cardRadius)
    private val tileBg = SquircleDrawable(0, Metrics.iconTileRadius)
    private val sep = Paint().apply { color = t.separator }

    private val name = Type.paint(Type.Style.Headline, t.label)
    private val plan = Type.paint(Type.Style.Caption1, t.secondaryLabel, 600)
    private val chip = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = t.quaternaryFill }
    private val status = Type.paint(Type.Style.Caption1, t.orange, 500).apply { textAlign = Paint.Align.RIGHT }
    private val ringLabel = Type.paint(Type.Style.Footnote, t.label, 600).apply { textAlign = Paint.Align.CENTER }
    private val reset = Type.paint(Type.Style.Caption1, t.secondaryLabel, 400, tabular = true).apply { textAlign = Paint.Align.CENTER }
    private val resetAt = Type.paint(Type.Style.Caption1, t.secondaryLabel, 400, tabular = true).apply { textAlign = Paint.Align.CENTER }
    private val models = Type.paint(Type.Style.Footnote, t.secondaryLabel)
    private val error = Type.paint(Type.Style.Subheadline, t.secondaryLabel).apply { textAlign = Paint.Align.CENTER }
    private val dot = Paint(Paint.ANTI_ALIAS_FLAG)

    // Drawn on a canvas scaled to dp, so every measurement below is in dp like the type ramp.
    private val dens = resources.displayMetrics.density
    private fun dp(v: Float) = v
    private val pad get() = 16f

    private fun windows() = provider?.windows?.filter { it.percent != null } ?: emptyList()

    private fun columns(w: Float, n: Int): Int {
        if (n <= 0) return 1
        val inner = w - pad * 2
        var cols = min(n, 4)
        while (cols > 1 && inner / cols < dp(78f)) cols--
        return cols
    }

    private fun ringSize(w: Float, cols: Int) = min((w - pad * 2) / cols * 0.62f, 80f)

    private fun modelLines(w: Float): List<String> {
        val p = provider ?: return emptyList()
        if (p.models.isEmpty()) return emptyList()
        val s = p.models.joinToString(" · ") { m -> m.name + " " + (m.spentUsd?.let(Fmt::usd) ?: Fmt.percent(m.percent)) }
        return Ink.wrap(s, models, w - pad * 2, 2)
    }

    override fun onMeasure(wSpec: Int, hSpec: Int) {
        val w = MeasureSpec.getSize(wSpec) / dens
        val ws = windows()
        var h = pad + dp(30f) + dp(14f)
        if (ws.isEmpty()) h += dp(44f)
        else {
            val cols = columns(w, ws.size); val rows = (ws.size + cols - 1) / cols
            h += rows * (ringSize(w, cols) + dp(8f) + dp(18f) + dp(16f) + dp(14f)) + (rows - 1) * dp(14f)
            h += expand * (panelH + 14f)
            val ml = modelLines(w)
            if (ml.isNotEmpty()) h += dp(14f) + ml.size * dp(18f)
        }
        h += pad
        setMeasuredDimension(MeasureSpec.getSize(wSpec), (h * dens).toInt())
    }

    override fun onDraw(c: Canvas) {
        val p = provider ?: return
        c.save(); c.scale(dens, dens)
        try { drawCard(c, p) } finally { c.restore() }
    }

    private fun drawCard(c: Canvas, p: Provider) {
        val w = width / dens
        if (!hostDrawsGlass) { bg.setBounds(0, 0, kotlin.math.ceil(w).toInt(), kotlin.math.ceil(height / dens).toInt()); bg.draw(c) }
        val accent = t.accent(p.id)
        // header
        var x = pad
        val hy = pad + dp(15f)
        if (showIcon) {
            val tile = tileBg.also { it.color = t.tile(p.id) }
            val s = dp(Metrics.iconTile)
            tile.setBounds(x.toInt(), (hy - s / 2).toInt(), (x + s).toInt(), (hy + s / 2).toInt()); tile.draw(c)
            Icons.draw(c, iconId, x + s / 2, hy, s * 0.36f, -1, eye = t.tile(p.id))
            x += s + dp(12f)
        }
        val stale = p.status == "stale"
        val statusText = if (stale) (if (Fmt.pt) "Desatualizado" else "Stale") + (p.fetchedAtMs?.let { " · " + Fmt.age(it, nowMs) } ?: "") else null
        val statusW = statusText?.let { status.measureText(it) + dp(14f) } ?: 0f
        val nameStr = Ink.fit(p.name, name, w - pad - x - statusW - dp(60f))
        Ink.centred(c, nameStr, x, hy, name)
        x += name.measureText(nameStr) + dp(8f)
        if (p.plan != null) {
            val pw = plan.measureText(p.plan) + dp(14f)
            if (x + pw < w - pad - statusW) {
                c.drawRoundRect(RectF(x, hy - dp(10f), x + pw, hy + dp(10f)), dp(10f), dp(10f), chip)
                plan.textAlign = Paint.Align.CENTER
                Ink.centred(c, p.plan, x + pw / 2, hy, plan)
            }
        }
        if (statusText != null) {
            Ink.centred(c, statusText, w - pad, hy, status)
            dot.color = t.orange
            c.drawCircle(w - pad - status.measureText(statusText) - dp(7f), hy, dp(3f), dot)
        }

        val ws = windows()
        var y = pad + dp(30f) + dp(14f)
        if (ws.isEmpty()) {
            Ink.centred(c, Ink.fit(Fmt.error(p.errorCode), error, w - pad * 2), w / 2, y + dp(20f), error)
            return
        }
        if (shown.size != ws.size) { shown = FloatArray(ws.size); if (from.size != ws.size) from = FloatArray(ws.size) }
        val cols = columns(w, ws.size)
        val d = ringSize(w, cols)
        val colW = (w - pad * 2) / cols
        ringHits.clear()
        val stroke = max(dp(7f), d * 0.12f)
        val big = Type.paint(if (d >= dp(76f)) Type.Style.Title3 else Type.Style.Headline, t.label, 600, tabular = true)
        val unit = Type.paint(Type.Style.Caption1, t.secondaryLabel, 600)
        ws.forEachIndexed { i, wd ->
            val row = i / cols; val col = i % cols
            val inRow = min(cols, ws.size - row * cols)
            val offset = (cols - inRow) * colW / 2f
            val cx = pad + offset + colW * col + colW / 2f
            val top = y + row * (d + dp(8f) + dp(18f) + dp(16f) + dp(14f) + dp(14f))
            val cy = top + d / 2f
            val target = (wd.percent!! / 100.0).toFloat()
            val stagger = ((progress * 1.25f) - i * 0.08f).coerceIn(0f, 1f)
            val f = (from.getOrElse(i) { 0f }) + (target - from.getOrElse(i) { 0f }) * stagger
            shown[i] = f
            val col2 = t.level(wd.percent, accent)
            // with the forecast open, the other rings step back so the selected one is the subject
            val dim = expand > 0.01f && selected >= 0 && i != selected && !collapsing
            if (dim) c.saveLayerAlpha(cx - d, top - 4f, cx + d, top + d + 60f, (255 * (1f - 0.6f * expand)).toInt())
            Rings.draw(c, cx, cy, d / 2f - stroke / 2f, stroke, f, col2, alpha(col2, if (t.dark) 0.24f else 0.18f))
            Ink.percent(c, wd.percent, cx, cy, big, unit)
            ringHits += floatArrayOf(cx, cy, d / 2f + 10f, i.toFloat())
            var ty = top + d + dp(8f) + dp(9f)
            Ink.centred(c, Ink.fit(Fmt.windowName(wd.id, wd.label), ringLabel, colW - dp(6f)), cx, ty, ringLabel)
            ty += dp(17f)
            val cd = wd.resetsAtMs?.let { (if (Fmt.pt) "em " else "in ") + Fmt.countdown(it, nowMs) } ?: (if (Fmt.pt) "não iniciada" else "not started")
            Ink.centred(c, Ink.fit(cd, reset, colW - dp(6f)), cx, ty, reset)
            ty += dp(15f)
            Ink.centred(c, Ink.fit(Fmt.resetAt(wd.resetsAtMs, nowMs), resetAt, colW - dp(6f)), cx, ty, resetAt)
            if (dim) c.restore()
        }
        val rows = (ws.size + cols - 1) / cols
        y += rows * (d + dp(8f) + dp(18f) + dp(16f) + dp(14f)) + (rows - 1) * dp(14f)
        if (expand > 0.01f && selected in ws.indices) { drawPanel(c, w, y + 14f, ws[selected]); y += expand * (panelH + 14f) }
        val ml = modelLines(w)
        if (ml.isNotEmpty()) {
            y += dp(14f)
            c.drawRect(pad, y - dp(7f), w - pad, y - dp(7f) + max(1f, dp(0.33f)), sep)
            models.textAlign = Paint.Align.CENTER
            ml.forEachIndexed { i, line -> Ink.centred(c, line, w / 2f, y + dp(9f) + i * dp(18f), models) }
        }
    }

    /**
     * Beta, "the detail of the detail": tap a ring to see where the window is heading at the pace of the last hour.
     */
    private fun drawPanel(c: Canvas, w: Float, top: Float, wd: dev.usage.monitor.Window) {
        val r = RectF(pad, top, w - pad, top + panelH)
        c.save()
        c.clipRect(0f, top - 2f, w, top + expand * (panelH + 2f))
        val a = (expand.coerceIn(0f, 1f) * 255).toInt()
        c.saveLayerAlpha(r.left - 2f, r.top - 2f, r.right + 2f, r.bottom + 2f, a)
        panelPaint.color = alpha(t.label, if (t.dark) 0.07f else 0.05f)
        c.drawPath(Squircle.path(r, 12f, panelPath), panelPaint)
        val pt = Fmt.pt
        val head = Type.paint(Type.Style.Caption2, t.secondaryLabel, 600)
        Ink.centred(c, Ink.fit(((if (pt) "Previsão · " else "Forecast · ") + Fmt.windowName(wd.id, wd.label)).uppercase(), head, r.width() - 80f), r.left + 14f, r.top + 18f, head)
        // BETA capsule
        val beta = Type.paint(Type.Style.Caption2, t.blue, 700).apply { textAlign = Paint.Align.CENTER }
        val bw = beta.measureText("BETA") + 14f
        panelPaint.color = alpha(t.blue, 0.16f)
        c.drawRoundRect(RectF(r.right - 14f - bw, r.top + 10f, r.right - 14f, r.top + 26f), 8f, 8f, panelPaint)
        Ink.centred(c, "BETA", r.right - 14f - bw / 2f, r.top + 18f, beta)

        val res = projection
        val main = Type.paint(Type.Style.Title3, t.label, 600, tabular = true)
        val side = Type.paint(Type.Style.Footnote, t.secondaryLabel, 500, tabular = true).apply { textAlign = Paint.Align.RIGHT }
        val body = Type.paint(Type.Style.Footnote, t.secondaryLabel)
        val rate = res?.let { "+" + (if (it.perHour >= 10) "%.0f" else "%.1f").format(java.util.Locale.getDefault(), it.perHour) + "%/h" } ?: ""
        val headline: String; val note: String; var sideText = ""
        when {
            res == null -> {
                main.textSize = Type.Style.Subheadline.size; main.typeface = Type.face(500, 15f)
                main.color = t.secondaryLabel
                headline = if (pt) "Coletando dados…" else "Collecting data…"
                note = if (pt) "A previsão usa o ritmo da última hora e aparece depois de uns 15 min de leituras." else "The forecast uses the pace of the last hour and shows up after about 15 min of readings."
            }
            res.perHour <= 0.0 -> {
                main.color = t.green
                headline = if (pt) "Sem uso recente" else "No recent use"
                note = if (pt) "Nada foi gasto nos últimos ${res.basisMinutes} min. Nesse ritmo, sobra tudo até o reset." else "Nothing was used in the last ${res.basisMinutes} min. At this pace it lasts until the reset."
            }
            res.runsOutAtMs != null -> {
                main.color = t.level(90.0, t.orange)
                headline = "~" + Fmt.countdown(res.runsOutAtMs, nowMs)
                sideText = (if (pt) "acaba às " else "runs out at ") + Fmt.clock(res.runsOutAtMs)
                val before = wd.resetsAtMs?.let { Fmt.countdown(it, res.runsOutAtMs) }
                note = if (pt) "No ritmo da última hora ($rate), o limite acaba" + (before?.let { " $it antes do reset." } ?: ".")
                       else "At the pace of the last hour ($rate) the limit runs out" + (before?.let { " $it before the reset." } ?: ".")
            }
            else -> {
                val left = (100.0 - (res.atResetPercent ?: 0.0)).coerceAtLeast(0.0)
                main.color = t.green
                headline = (if (pt) "Sobra ~" else "~") + "%.0f".format(left) + "%" + (if (pt) "" else " left")
                sideText = if (pt) "no reset" else "at the reset"
                note = if (pt) "No ritmo da última hora ($rate), chega a ~${"%.0f".format(res.atResetPercent ?: 0.0)}% quando resetar." else "At the pace of the last hour ($rate) it reaches ~${"%.0f".format(res.atResetPercent ?: 0.0)}% by the reset."
            }
        }
        Ink.centred(c, Ink.fit(headline, main, r.width() - 28f - side.measureText(sideText) - 8f), r.left + 14f, r.top + 46f, main)
        if (sideText.isNotEmpty()) Ink.centred(c, sideText, r.right - 14f, r.top + 47f, side)
        Ink.wrap(note, body, r.width() - 28f, 3).forEachIndexed { i, line -> Ink.centred(c, line, r.left + 14f, r.top + 72f + i * 17f, body) }
        c.restore()
        c.restore()
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: android.view.MotionEvent): Boolean {
        if (projector == null) return super.onTouchEvent(e)
        val x = e.x / dens; val y = e.y / dens
        val hit = ringHits.firstOrNull { (x - it[0]) * (x - it[0]) + (y - it[1]) * (y - it[1]) <= it[2] * it[2] }
        when (e.actionMasked) {
            android.view.MotionEvent.ACTION_DOWN -> return hit != null
            android.view.MotionEvent.ACTION_UP -> if (hit != null) {
                val i = hit[3].toInt()
                Haptics.tick(this)
                if (selected == i && !collapsing) { collapsing = true; expandSpring.animateTo(0f, 0.4f, 1f) }
                else { collapsing = false; selected = i; projection = computeProjection(); expandSpring.animateTo(1f, 0.45f, 0.82f) }
                performClick()
                return true
            }
        }
        return hit != null
    }

    override fun performClick(): Boolean = super.performClick()
}
