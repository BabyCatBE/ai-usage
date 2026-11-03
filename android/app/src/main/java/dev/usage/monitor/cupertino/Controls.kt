package dev.usage.monitor.cupertino

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PixelFormat
import android.graphics.RectF
import android.graphics.RenderEffect
import android.graphics.RenderNode
import android.graphics.RuntimeShader
import android.graphics.SweepGradient
import android.graphics.drawable.Drawable
import android.os.Build
import android.text.TextUtils
import android.view.Gravity
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import kotlin.math.abs
import kotlin.math.roundToInt

fun View.dp(v: Float) = v * resources.displayMetrics.density
fun View.dpi(v: Float) = (v * resources.displayMetrics.density).roundToInt()

object Haptics {
    fun tick(v: View) { v.performHapticFeedback(if (Build.VERSION.SDK_INT >= 34) HapticFeedbackConstants.SEGMENT_TICK else HapticFeedbackConstants.CLOCK_TICK) }
    fun toggle(v: View, on: Boolean) { v.performHapticFeedback(if (Build.VERSION.SDK_INT >= 34) (if (on) HapticFeedbackConstants.TOGGLE_ON else HapticFeedbackConstants.TOGGLE_OFF) else HapticFeedbackConstants.CLOCK_TICK) }
    fun confirm(v: View) { v.performHapticFeedback(if (Build.VERSION.SDK_INT >= 30) HapticFeedbackConstants.CONFIRM else HapticFeedbackConstants.VIRTUAL_KEY) }
    fun tap(v: View) { v.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY) }
}

/** Squircle-filled background (cards, grouped sections, buttons). */
class SquircleDrawable(var color: Int, private val radiusPx: Float, private val stroke: Int = 0, private val strokePx: Float = 0f) : Drawable() {
    private val path = Path()
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val rim = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    override fun draw(canvas: Canvas) {
        val b = RectF(bounds)
        Squircle.path(b, radiusPx, path)
        fill.color = color
        canvas.drawPath(path, fill)
        if (stroke != 0 && strokePx > 0f) { rim.color = stroke; rim.strokeWidth = strokePx; canvas.drawPath(path, rim) }
    }
    override fun setAlpha(alpha: Int) {}
    override fun setColorFilter(colorFilter: ColorFilter?) {}
    @Deprecated("Deprecated in Java") override fun getOpacity() = PixelFormat.TRANSLUCENT
}

fun label(ctx: Context, text: CharSequence, style: Type.Style, color: Int, weight: Int = style.weight): TextView = TextView(ctx).apply {
    this.text = text
    Type.apply(this, style, color, weight)
}

/**
 * iOS segmented control (capsule track, sliding thumb). While the thumb travels it turns into a Liquid Glass lens that
 * magnifies the labels it passes over, then settles back into the solid thumb, the way iOS 26 does.
 */
@SuppressLint("ViewConstructor")
class SegmentedControl(context: Context, private val t: Tokens, private val items: List<String>, selected: Int, private val onSelect: (Int) -> Unit) : View(context) {
    private var index = selected.coerceIn(0, items.lastIndex)
    private var pos = index.toFloat()
    private var dragging = false
    private var downX = 0f
    private val reduceMotion = Tokens.reduceMotion(context)
    private val spring = Spring(pos, reduceMotion) { v -> pos = v; invalidate() }
    private val track = Paint(Paint.ANTI_ALIAS_FLAG)
    private val thumbPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val text = Type.paint(Type.Style.Footnote, t.label, 500, scale = resources.displayMetrics.density).apply { textAlign = Paint.Align.CENTER }
    private val textSel = Type.paint(Type.Style.Footnote, t.label, 600, scale = resources.displayMetrics.density).apply { textAlign = Paint.Align.CENTER }
    private val labelsNode: Any? = if (Build.VERSION.SDK_INT >= 33) RenderNode("seg-labels") else null
    private val lensNode: Any? = if (Build.VERSION.SDK_INT >= 33) RenderNode("seg-lens") else null
    private val lens: Any? = if (Build.VERSION.SDK_INT >= 33) RuntimeShader(GlassShaders.LENS) else null
    private val noGlass = Tokens.reduceTransparency(context)

    init {
        isClickable = true; isFocusable = true
        contentDescription = items.getOrNull(index)
    }

    override fun onMeasure(w: Int, h: Int) = setMeasuredDimension(MeasureSpec.getSize(w), dpi(34f))

    private fun segW() = (width - dp(4f)) / items.size

    private fun thumbRect(p: Float, grow: Float): RectF {
        val sw = segW()
        val l = dp(2f) + p * sw; val r = l + sw
        val cy = height / 2f; val hh = (height - dp(4f)) / 2f
        val gx = sw * 0.10f * grow; val gy = hh * 0.22f * grow
        return RectF(l - gx, cy - hh - gy, r + gx, cy + hh + gy)
    }

    private fun drawLabels(c: Canvas) {
        val sw = segW()
        val base = height / 2f - (text.descent() + text.ascent()) / 2f
        items.forEachIndexed { i, s ->
            val p = if (abs(i - pos) < 0.5f) textSel else text
            c.drawText(TextUtils.ellipsize(s, android.text.TextPaint(p), sw - dp(8f), TextUtils.TruncateAt.END).toString(), dp(2f) + sw * i + sw / 2f, base, p)
        }
    }

    override fun onDraw(c: Canvas) {
        val w = width.toFloat(); val h = height.toFloat()
        track.color = t.tertiaryFill
        c.drawRoundRect(0f, 0f, w, h, h / 2f, h / 2f, track)
        val moving = dragging || abs(pos - spring.target) > 0.02f
        val grow = if (moving) 1f else 0f
        if (moving && !noGlass && Build.VERSION.SDK_INT >= 33 && c.isHardwareAccelerated) { drawLens(c, grow); return }
        val r = thumbRect(pos, 0f)
        thumbPaint.color = t.thumb
        thumbPaint.setShadowLayer(dp(4f), 0f, dp(1.5f), if (t.dark) 0x66000000 else 0x26000000)
        c.drawRoundRect(r, r.height() / 2f, r.height() / 2f, thumbPaint)
        thumbPaint.clearShadowLayer()
        drawLabels(c)
    }

    @androidx.annotation.RequiresApi(33)
    private fun drawLens(c: Canvas, grow: Float) {
        val ln = labelsNode as RenderNode; val node = lensNode as RenderNode; val s = lens as RuntimeShader
        ln.setPosition(0, 0, width, height)
        val rc = ln.beginRecording()
        rc.drawRoundRect(0f, 0f, width.toFloat(), height.toFloat(), height / 2f, height / 2f, track.apply { color = t.tertiaryFill })
        drawLabels(rc)
        ln.endRecording()
        drawLabels(c)
        val r = thumbRect(pos, grow)
        val m = dp(10f).toInt()
        val l = r.left.toInt() - m; val tp = r.top.toInt() - m
        node.setPosition(l, tp, r.right.toInt() + m, r.bottom.toInt() + m)
        val nc = node.beginRecording()
        nc.translate(-l.toFloat(), -tp.toFloat())
        nc.drawColor(t.card)
        nc.drawRenderNode(ln)
        node.endRecording()
        s.setFloatUniform("box", r.left - l, r.top - tp, r.right - l, r.bottom - tp)
        s.setFloatUniform("radius", r.height() / 2f)
        s.setFloatUniform("bezel", r.height() * 0.45f)
        s.setFloatUniform("refraction", r.height() * 0.30f)
        s.setFloatUniform("dispersion", 1f)
        s.setFloatUniform("lightDir", GlassLight.x, GlassLight.y)
        s.setFloatUniform("tint", 1f, 1f, 1f, if (t.dark) 0.06f else 0.12f)
        s.setFloatUniform("dark", if (t.dark) 1f else 0f)
        s.setFloatUniform("press", 0.6f)
        s.setFloatUniform("shadow", if (t.dark) 0.4f else 0.18f)
        s.setFloatUniform("frost", 0f)
        node.setRenderEffect(RenderEffect.createRuntimeShaderEffect(s, "content"))
        c.drawRenderNode(node)
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        val sw = segW()
        fun at(x: Float) = ((x - dp(2f)) / sw).toInt().coerceIn(0, items.lastIndex)
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> { downX = e.x; dragging = at(e.x) == index; parent?.requestDisallowInterceptTouchEvent(dragging); if (dragging) invalidate() }
            MotionEvent.ACTION_MOVE -> if (dragging) {
                val p = ((e.x - dp(2f)) / sw - 0.5f).coerceIn(0f, items.lastIndex.toFloat())
                spring.snapTo(p)
                val near = p.roundToInt()
                if (near != index) { index = near; Haptics.tick(this) }
            }
            MotionEvent.ACTION_UP -> {
                val target = if (dragging && abs(e.x - downX) > dp(6f)) index else at(e.x)
                dragging = false
                select(target)
                performClick()
            }
            MotionEvent.ACTION_CANCEL -> { dragging = false; select(index) }
        }
        return true
    }

    override fun performClick(): Boolean = super.performClick()

    private fun select(i: Int) {
        val changed = i != spring.target.roundToInt() || i != index
        index = i
        spring.animateTo(i.toFloat(), 0.42f, 0.78f)
        contentDescription = items[i]
        if (changed) Haptics.tick(this)
        onSelect(i)
    }
}

/** iOS switch: 51×31 track, knob that stretches while pressed, green when on. */
@SuppressLint("ViewConstructor")
class IosSwitch(context: Context, private val t: Tokens, checked: Boolean, private val onChange: (Boolean) -> Unit) : View(context) {
    var checked = checked; private set
    private var progress = if (checked) 1f else 0f
    private var stretch = 0f
    private val reduceMotion = Tokens.reduceMotion(context)
    private val sp = Spring(progress, reduceMotion) { progress = it; invalidate() }
    private val st = Spring(0f, reduceMotion) { stretch = it; invalidate() }
    private val p = Paint(Paint.ANTI_ALIAS_FLAG)
    private val offTrack = if (t.dark) 0xFF39393D.toInt() else 0xFFE9E9EA.toInt()

    init { isClickable = true; contentDescription = null }

    override fun onMeasure(w: Int, h: Int) = setMeasuredDimension(dpi(51f), dpi(31f))

    override fun onDraw(c: Canvas) {
        val w = width.toFloat(); val h = height.toFloat()
        p.color = mixColor(offTrack, t.green, progress)
        c.drawRoundRect(0f, 0f, w, h, h / 2, h / 2, p)
        val k = dp(27f); val pad = dp(2f)
        val kw = k + dp(7f) * stretch
        val x0 = pad + (w - pad * 2 - kw) * progress
        p.color = -1
        p.setShadowLayer(dp(3f), 0f, dp(1.5f), 0x33000000)
        c.drawRoundRect(x0, pad, x0 + kw, pad + k, k / 2, k / 2, p)
        p.clearShadowLayer()
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> st.animateTo(1f, 0.25f, 0.8f)
            MotionEvent.ACTION_UP -> { st.animateTo(0f, 0.3f, 0.8f); toggle(); performClick() }
            MotionEvent.ACTION_CANCEL -> st.animateTo(0f, 0.3f, 0.8f)
        }
        return true
    }

    override fun performClick(): Boolean = super.performClick()

    fun toggle() {
        checked = !checked
        sp.animateTo(if (checked) 1f else 0f, 0.36f, 0.72f)
        Haptics.toggle(this, checked)
        onChange(checked)
    }

    override fun onInitializeAccessibilityNodeInfo(info: android.view.accessibility.AccessibilityNodeInfo) {
        super.onInitializeAccessibilityNodeInfo(info)
        info.className = android.widget.Switch::class.java.name
        info.isCheckable = true; info.isChecked = checked
    }
}

fun mixColor(a: Int, b: Int, f: Float): Int {
    fun ch(s: Int) = (((a shr s) and 255) + (((b shr s) and 255) - ((a shr s) and 255)) * f).roundToInt().coerceIn(0, 255)
    return (ch(24) shl 24) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
}

/** Trailing glyphs of a list row. */
object Glyphs {
    fun chevron(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = s * 0.19f; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND; this.color = color }
        c.drawPath(Path().apply { moveTo(cx - s * 0.22f, cy - s * 0.45f); lineTo(cx + s * 0.25f, cy); lineTo(cx - s * 0.22f, cy + s * 0.45f) }, p)
    }

    fun back(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = s * 0.16f; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND; this.color = color }
        c.drawPath(Path().apply { moveTo(cx + s * 0.2f, cy - s * 0.5f); lineTo(cx - s * 0.3f, cy); lineTo(cx + s * 0.2f, cy + s * 0.5f) }, p)
    }

    fun check(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = s * 0.15f; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND; this.color = color }
        c.drawPath(Path().apply { moveTo(cx - s * 0.42f, cy + s * 0.02f); lineTo(cx - s * 0.12f, cy + s * 0.34f); lineTo(cx + s * 0.44f, cy - s * 0.38f) }, p)
    }

    /** Circular arrow ("arrow.clockwise"). [spin] rotates it (radians) while a refresh runs. */
    fun refresh(c: Canvas, cx: Float, cy: Float, s: Float, color: Int, spin: Float = 0f) {
        c.save(); c.rotate(Math.toDegrees(spin.toDouble()).toFloat(), cx, cy)
        val r = s * 0.40f
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = s * 0.12f; strokeCap = Paint.Cap.ROUND; this.color = color }
        c.drawArc(RectF(cx - r, cy - r, cx + r, cy + r), -60f, 300f, false, p)
        val a = Math.toRadians(-60.0)
        val tx = cx + r * Math.cos(a).toFloat(); val ty = cy + r * Math.sin(a).toFloat()
        val f = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color }
        val k = s * 0.20f
        c.drawPath(Path().apply { moveTo(tx + k * 0.95f, ty - k * 0.25f); lineTo(tx - k * 0.55f, ty - k * 0.75f); lineTo(tx - k * 0.05f, ty + k * 0.85f); close() }, f)
        c.restore()
    }

    /** Gear ("gearshape"): eight rounded teeth around a ring. */
    fun gear(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color }
        val outer = s * 0.46f; val inner = s * 0.34f
        val path = Path()
        val n = 8
        for (i in 0 until n * 2) {
            val ang = Math.PI * i / n - Math.PI / (2 * n)
            val rr = if (i % 2 == 0) outer else inner
            val a1 = ang - Math.PI / (n * 2.6); val a2 = ang + Math.PI / (n * 2.6)
            val x1 = cx + rr * Math.cos(a1).toFloat(); val y1 = cy + rr * Math.sin(a1).toFloat()
            val x2 = cx + rr * Math.cos(a2).toFloat(); val y2 = cy + rr * Math.sin(a2).toFloat()
            if (i == 0) path.moveTo(x1, y1) else path.lineTo(x1, y1)
            path.lineTo(x2, y2)
        }
        path.close()
        path.addCircle(cx, cy, s * 0.15f, Path.Direction.CCW)
        path.fillType = Path.FillType.EVEN_ODD
        p.pathEffect = android.graphics.CornerPathEffect(s * 0.05f)
        c.drawPath(path, p)
    }
}

/** A glyph drawn by a lambda, sized to its view (used inside glass buttons). */
@SuppressLint("ViewConstructor")
class GlyphView(context: Context, var color: Int, private val sizeDp: Float, private val drawer: (Canvas, Float, Float, Float, Int) -> Unit) : View(context) {
    override fun onDraw(canvas: Canvas) = drawer(canvas, width / 2f, height / 2f, dp(sizeDp), color)
}

/** Inset-grouped section: optional header, a squircle card of rows separated by hairlines, optional footer. */
class Section(context: Context, private val t: Tokens, header: String?, footer: String? = null) : LinearLayout(context) {
    val card = LinearLayout(context).apply { orientation = VERTICAL; background = SquircleDrawable(t.card, dp(Metrics.cardRadius)); clipToOutline = false }
    private var rows = 0

    init {
        orientation = VERTICAL
        val m = dpi(Metrics.margin)
        if (header != null) addView(label(context, header.uppercase(), Type.Style.Footnote, t.secondaryLabel).apply { setPadding(m + dpi(16f), dpi(18f), m, dpi(7f)) })
        else addView(View(context), LayoutParams(-1, dpi(18f)))
        addView(card, LayoutParams(-1, -2).apply { setMargins(m, 0, m, 0) })
        if (footer != null) addView(label(context, footer, Type.Style.Footnote, t.secondaryLabel).apply { setPadding(m + dpi(16f), dpi(7f), m + dpi(16f), 0) })
    }

    fun add(row: View, inset: Float = 16f): Section {
        if (rows > 0) card.addView(Hairline(context, t, inset), LayoutParams(-1, maxOf(1, dpi(0.33f))))
        card.addView(row, LayoutParams(-1, -2))
        rows++
        return this
    }
}

@SuppressLint("ViewConstructor")
class Hairline(context: Context, private val t: Tokens, private val insetDp: Float) : View(context) {
    private val p = Paint().apply { color = t.separator }
    override fun onDraw(canvas: Canvas) { canvas.drawRect(dp(insetDp), 0f, width.toFloat(), height.toFloat(), p) }
}

/**
 * One list row: optional leading tile, title, optional value, and a trailing accessory. Highlights while pressed
 * (the grey fill iOS uses) and fades out on release.
 */
@SuppressLint("ViewConstructor")
class Row(context: Context, private val t: Tokens, title: String, value: String? = null, titleColor: Int = t.label,
          private val accessory: Accessory = Accessory.None, leading: View? = null, titleWeight: Int = 400,
          private val onTap: (() -> Unit)? = null) : LinearLayout(context) {
    enum class Accessory { None, Chevron, Check }
    private var highlight = 0f
    private val hl = Spring(0f, Tokens.reduceMotion(context)) { highlight = it; invalidate() }
    private val hp = Paint()
    val titleView: TextView
    val valueView: TextView?
    var checked = false
        set(v) { field = v; invalidate() }

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        minimumHeight = dpi(if (leading != null) 52f else 48f)
        setPadding(dpi(16f), dpi(10f), dpi(16f), dpi(10f))
        setWillNotDraw(false)
        if (leading != null) addView(leading, LayoutParams(dpi(Metrics.iconTile), dpi(Metrics.iconTile)).apply { rightMargin = dpi(14f) })
        titleView = label(context, title, Type.Style.Body, titleColor, titleWeight).apply { maxLines = 2; ellipsize = TextUtils.TruncateAt.END }
        addView(titleView, LayoutParams(0, -2, 1f))
        valueView = value?.let { v -> label(context, v, Type.Style.Body, t.secondaryLabel).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.MIDDLE; gravity = Gravity.END } }
        valueView?.let { addView(it, LayoutParams(-2, -2).apply { leftMargin = dpi(12f) }) }
        if (accessory != Accessory.None) addView(View(context), LayoutParams(dpi(if (accessory == Accessory.Chevron) 22f else 26f), dpi(20f)))
        if (onTap != null) { isClickable = true; setOnClickListener { Haptics.tap(this); onTap.invoke() } }
    }

    fun trailing(v: View): Row { addView(v, LayoutParams(-2, -2).apply { leftMargin = dpi(12f) }); return this }

    override fun onDraw(canvas: Canvas) {
        if (highlight > 0.01f) { hp.color = alpha(if (t.dark) 0xFF3A3A3C.toInt() else 0xFFD1D1D6.toInt(), highlight); canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), hp) }
        super.onDraw(canvas)
    }

    override fun dispatchDraw(canvas: Canvas) {
        super.dispatchDraw(canvas)
        val cx = width - paddingRight - dp(8f); val cy = height / 2f
        when (accessory) {
            Accessory.Chevron -> Glyphs.chevron(canvas, cx, cy, dp(13f), t.tertiaryLabel)
            Accessory.Check -> if (checked) Glyphs.check(canvas, cx - dp(2f), cy, dp(17f), t.blue)
            Accessory.None -> {}
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (onTap != null) when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> hl.snapTo(1f)
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> hl.animateTo(0f, 0.45f, 1f)
        }
        return super.onTouchEvent(e)
    }
}

/** Settings-style icon tile: white glyph on a coloured squircle. */
@SuppressLint("ViewConstructor")
class IconTile(context: Context, private val color: Int, private val iconId: String) : View(context) {
    private val bg = SquircleDrawable(color, dp(Metrics.iconTileRadius))
    override fun onDraw(c: Canvas) {
        bg.setBounds(0, 0, width, height); bg.draw(c)
        dev.usage.monitor.Icons.draw(c, iconId, width / 2f, height / 2f, width * 0.36f, -1, eye = color)
    }
}

/** Filled capsule button for primary actions in the content layer (`.borderedProminent`). */
@SuppressLint("ViewConstructor")
class ProminentButton(context: Context, t: Tokens, text: String, color: Int = t.blue, onTap: () -> Unit) : FrameLayout(context) {
    private val reduce = Tokens.reduceMotion(context)
    private val s = Spring(1f, reduce) { v -> scaleX = v; scaleY = v; alpha = 0.75f + 0.25f * ((v - 0.96f) / 0.04f).coerceIn(0f, 1f) }
    init {
        background = SquircleDrawable(color, dp(100f))
        addView(label(context, text, Type.Style.Headline, -1).apply { gravity = Gravity.CENTER }, LayoutParams(-1, -1))
        minimumHeight = dpi(50f)
        isClickable = true
        setOnClickListener { Haptics.tap(this); onTap() }
    }
    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> s.animateTo(0.96f, 0.2f, 1f)
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> s.animateTo(1f, 0.35f, 0.7f)
        }
        return super.onTouchEvent(e)
    }
}

/** Activity-style ring: tinted track, gradient arc with round caps, overlap shading past 100 %. */
object Rings {
    private val track = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND }
    private val arc = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND }
    private val tip = Paint(Paint.ANTI_ALIAS_FLAG)

    fun draw(c: Canvas, cx: Float, cy: Float, radius: Float, stroke: Float, fraction: Float, color: Int, trackColor: Int) {
        val r = RectF(cx - radius, cy - radius, cx + radius, cy + radius)
        track.strokeWidth = stroke; track.color = trackColor
        c.drawCircle(cx, cy, radius, track)
        val f = fraction.coerceAtLeast(0f)
        if (f <= 0.001f) return
        val sweep = minOf(f, 1f) * 360f
        arc.strokeWidth = stroke
        val light = mixColor(color, -1, 0.22f)
        arc.shader = SweepGradient(cx, cy, intArrayOf(mixColor(color, 0xFF000000.toInt(), 0.08f), light, light), floatArrayOf(0f, (sweep / 360f).coerceAtLeast(0.01f), 1f)).apply {
            setLocalMatrix(android.graphics.Matrix().apply { setRotate(-90f, cx, cy) })
        }
        c.drawArc(r, -90f, sweep.coerceAtMost(359.9f), false, arc)
        if (f > 1f) {
            val ang = Math.toRadians((-90.0 + 360.0 * (f % 1f)))
            val tx = cx + radius * Math.cos(ang).toFloat(); val ty = cy + radius * Math.sin(ang).toFloat()
            tip.color = light
            tip.setShadowLayer(stroke * 0.45f, 0f, 0f, 0x88000000.toInt())
            c.drawCircle(tx, ty, stroke / 2f, tip)
            tip.clearShadowLayer()
        }
        arc.shader = null
    }
}

