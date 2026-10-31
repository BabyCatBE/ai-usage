package dev.usage.monitor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.PixelFormat
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.Drawable
import android.graphics.drawable.StateListDrawable
import android.util.AttributeSet
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.widget.TextView

fun Context.dp(v: Float) = v * resources.displayMetrics.density
fun Context.dpi(v: Float) = (v * resources.displayMetrics.density).toInt()

/** Cached neumorphic surface. [margin] leaves room for the soft shadows inside the view bounds. */
class NeuDrawable(private val pal: Palette, private val radiusDp: Float, private val raised: Boolean, private val marginDp: Float, private val density: Float) : Drawable() {
    private var cache: Bitmap? = null
    override fun onBoundsChange(b: android.graphics.Rect) { cache?.recycle(); cache = null }
    override fun draw(canvas: Canvas) {
        val b = bounds
        if (b.width() <= 0 || b.height() <= 0) return
        val bmp = cache ?: Bitmap.createBitmap(b.width(), b.height(), Bitmap.Config.ARGB_8888).also { bm ->
            val c = Canvas(bm); c.scale(density, density)
            val r = RectF(marginDp, marginDp, b.width() / density - marginDp, b.height() / density - marginDp)
            if (raised) Renderer.raised(c, r, radiusDp, pal, 4f, 9f) else Renderer.inset(c, r, radiusDp, pal, 2.5f)
            cache = bm
        }
        canvas.drawBitmap(bmp, b.left.toFloat(), b.top.toFloat(), null)
    }
    override fun setAlpha(alpha: Int) {}
    override fun setColorFilter(colorFilter: ColorFilter?) {}
    @Deprecated("Deprecated in Java") override fun getOpacity() = PixelFormat.TRANSLUCENT
}

object Neu {
    fun surface(ctx: Context, pal: Palette, radius: Float, raised: Boolean, margin: Float = 10f) = NeuDrawable(pal, radius, raised, margin, ctx.resources.displayMetrics.density)

    fun button(ctx: Context, pal: Palette, label: String, accent: Int? = null, onClick: () -> Unit): TextView = TextView(ctx).apply {
        text = label
        gravity = Gravity.CENTER
        textSize = 14f
        typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
        setTextColor(accent ?: pal.text)
        val m = 10f
        background = StateListDrawable().apply {
            addState(intArrayOf(android.R.attr.state_pressed), surface(ctx, pal, 16f, false, m))
            addState(intArrayOf(), surface(ctx, pal, 16f, true, m))
        }
        setPadding(ctx.dpi(m + 14), ctx.dpi(m + 12), ctx.dpi(m + 14), ctx.dpi(m + 12))
        minHeight = ctx.dpi(48f + m * 2)
        isClickable = true
        setOnClickListener { onClick() }
    }
}

/** Draws the dashboard with the same renderer as the widget, at the natural height for the available width. */
class DashboardView @JvmOverloads constructor(context: Context, attrs: AttributeSet? = null) : View(context, attrs) {
    var state: UiState = UiState(null, false, null, 0, false)
        set(v) { field = v; invalidateCache(); requestLayout() }
    var onRefresh: () -> Unit = {}
    var onPairTap: () -> Unit = {}
    private var cache: Bitmap? = null
    private var cacheKey = ""

    private val pal get() = Store(context).palette(context)
    private val level get() = Level.DETAIL

    fun invalidateCache() { cacheKey = "" ; invalidate() }

    override fun onMeasure(w: Int, h: Int) {
        val width = MeasureSpec.getSize(w)
        setMeasuredDimension(width, (Renderer.naturalHeightDp(state, level) * resources.displayMetrics.density).toInt())
    }

    override fun onDraw(canvas: Canvas) {
        if (width <= 0 || height <= 0) return
        val now = System.currentTimeMillis()
        val key = "$width×$height|${now / 30_000}|${state.hashCode()}|${pal.dark}|${pal.glass}"
        if (key != cacheKey || cache == null) {
            cache?.recycle()
            cache = Renderer.render(state, width, height, resources.displayMetrics.density, pal, now, level, rounded = false)
            cacheKey = key
        }
        canvas.drawBitmap(cache!!, 0f, 0f, null)
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (e.action == MotionEvent.ACTION_UP) {
            val d = resources.displayMetrics.density
            val (cx, cy) = Renderer.refreshCenter(width / d)
            if (Math.hypot((e.x / d - cx).toDouble(), (e.y / d - cy).toDouble()) < 30) { performClick(); onRefresh(); return true }
            if (!state.paired) { performClick(); onPairTap(); return true }
        }
        return e.action == MotionEvent.ACTION_DOWN || super.onTouchEvent(e)
    }

    override fun performClick(): Boolean = super.performClick()
}

/** One selectable glyph: raised when idle, pressed-in with an accent ring when chosen. */
class IconChoice(context: Context, private val pal: Palette, private val iconId: String, private val accent: Int, private val selected: Boolean, private val caption: String, onPick: () -> Unit) : View(context) {
    private val d = context.resources.displayMetrics.density
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = 10f * d; textAlign = Paint.Align.CENTER; color = if (selected) pal.text else pal.dim; typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL) }

    init {
        background = Neu.surface(context, pal, 22f, !selected, 12f)
        isClickable = true
        contentDescription = caption
        setOnClickListener { onPick() }
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cx = width / 2f; val cy = height * 0.42f
        Icons.draw(canvas, iconId, cx, cy, 14f * d, if (selected) accent else pal.dim)
        canvas.drawText(caption, cx, height - 14f * d, textPaint)
    }
}

/** Window backdrop for the glass theme, drawn by the same code as the widget. */
class GlassBackdrop(private val dark: Boolean) : Drawable() {
    override fun draw(canvas: Canvas) { Renderer.glassBackground(canvas, bounds.width().toFloat(), bounds.height().toFloat(), dark) }
    override fun setAlpha(alpha: Int) {}
    override fun setColorFilter(colorFilter: ColorFilter?) {}
    @Deprecated("Deprecated in Java") override fun getOpacity() = PixelFormat.OPAQUE
}
