package dev.usage.monitor.cupertino

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.RenderEffect
import android.graphics.RenderNode
import android.graphics.RuntimeShader
import android.graphics.Shader
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Build
import android.view.Choreographer
import android.view.MotionEvent
import android.view.View
import android.widget.FrameLayout
import androidx.annotation.RequiresApi
import kotlin.math.abs
import kotlin.math.hypot

/**
 * Liquid Glass, drawn on the GPU with AGSL.
 *
 * The material bends what is behind it: inside a rounded "bezel" along the rim the backdrop is sampled from a displaced
 * position (a convex rim magnifies towards the edge), with a hint of chromatic dispersion, a light vibrancy lift,
 * a specular highlight where the rim faces the light (and a fainter one on the opposite corner), and a soft shadow.
 */
@RequiresApi(33)
object GlassShaders {
    const val LENS = """
uniform shader content;
uniform float4 box;
uniform float radius;
uniform float bezel;
uniform float refraction;
uniform float dispersion;
uniform float2 lightDir;
uniform float4 tint;
uniform float dark;
uniform float press;
uniform float shadow;
uniform float frost;

float sdBox(float2 p, float2 b, float r) {
    float2 q = abs(p) - b + float2(r, r);
    return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
}

half4 main(float2 xy) {
    float2 c = (box.xy + box.zw) * 0.5;
    float2 hb = (box.zw - box.xy) * 0.5;
    float r = min(radius, min(hb.x, hb.y));
    float2 p = xy - c;
    float d = sdBox(p, hb, r);
    if (d > 0.5) {
        float ds = sdBox(p - float2(0.0, 3.0 + hb.y * 0.06), hb, r);
        float s = shadow * (1.0 - smoothstep(-3.0, 16.0 + hb.y * 0.15, ds));
        return half4(0.0, 0.0, 0.0, half(s * s));
    }
    float e = 0.75;
    float2 n = float2(sdBox(p + float2(e, 0.0), hb, r) - sdBox(p - float2(e, 0.0), hb, r),
                      sdBox(p + float2(0.0, e), hb, r) - sdBox(p - float2(0.0, e), hb, r));
    n = n / max(length(n), 0.0001);
    float depth = max(-d, 0.0);
    float t = clamp(depth / bezel, 0.0, 1.0);
    float u = 1.0 - t;
    float slope = 1.0 - sqrt(max(1.0 - u * u, 0.0));
    float disp = refraction * slope * (1.0 + press * 0.25);
    float2 sp = xy - n * disp;
    float ca = disp * 0.14 * dispersion;
    float4 base = float4(content.eval(sp));
    float rr = float(content.eval(sp - n * ca).r);
    float bb = float(content.eval(sp + n * ca).b);
    float3 col = float3(rr, base.g, bb);
    float l = dot(col, float3(0.2126, 0.7152, 0.0722));
    col = mix(float3(l, l, l), col, 1.16);
    col = mix(col, tint.rgb, tint.a);
    col = mix(col, tint.rgb, frost * 0.82);
    float facing = max(dot(n, lightDir), 0.0);
    float back = max(dot(n, -lightDir), 0.0);
    float rim = 1.0 - smoothstep(0.0, 1.8, depth);
    float spec = rim * (0.08 + 0.80 * pow(facing, 3.0) + 0.38 * pow(back, 3.0));
    float band = u * u * u * (0.05 + 0.22 * pow(facing, 2.0) + 0.08 * pow(back, 2.0));
    float lift = dark > 0.5 ? 0.6 : 1.0;
    col += float3(1.0, 1.0, 1.0) * ((spec + band) * lift + press * 0.07);
    float a = clamp(0.5 - d, 0.0, 1.0);
    return half4(half3(col * a), half(a));
}
"""

    /** Scroll-edge effect: the blurred backdrop under the status bar fades out downwards, tinted to the page background. */
    const val EDGE = """
uniform shader content;
uniform float height;
uniform float4 bg;

half4 main(float2 xy) {
    float k = 1.0 - smoothstep(height * 0.35, height, xy.y);
    float4 c = float4(content.eval(xy));
    c.rgb = mix(c.rgb, bg.rgb * c.a, bg.a * (0.55 + 0.45 * k));
    return half4(c * k);
}
"""
}

/** Light direction for the specular highlights. It leans with the phone (game rotation vector) while a glass screen is visible. */
object GlassLight : SensorEventListener {
    var x = -0.55f; private set
    var y = -0.83f; private set
    private val listeners = LinkedHashSet<() -> Unit>()
    private var sm: SensorManager? = null
    private val rot = FloatArray(9); private val ori = FloatArray(3)

    fun attach(context: Context, onChange: () -> Unit) {
        listeners += onChange
        if (sm != null || Tokens.reduceMotion(context)) return
        val m = context.getSystemService(SensorManager::class.java) ?: return
        val s = m.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR) ?: return
        if (m.registerListener(this, s, SensorManager.SENSOR_DELAY_UI)) sm = m
    }

    fun detach(onChange: () -> Unit) {
        listeners -= onChange
        if (listeners.isEmpty()) { sm?.unregisterListener(this); sm = null }
    }

    override fun onSensorChanged(e: SensorEvent) {
        SensorManager.getRotationMatrixFromVector(rot, e.values)
        SensorManager.getOrientation(rot, ori)
        val roll = ori[2].coerceIn(-0.8f, 0.8f)   // tilt left/right
        val pitch = (ori[1] + 0.6f).coerceIn(-0.8f, 0.8f) // phone held upright ~ -0.6 rad
        val nx = -0.55f - roll * 0.9f
        val ny = -0.83f + pitch * 0.6f
        val len = hypot(nx, ny).coerceAtLeast(0.001f)
        val fx = nx / len; val fy = ny / len
        if (abs(fx - x) + abs(fy - y) < 0.012f) return
        x = fx; y = fy
        listeners.toList().forEach { it() }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}
}

/**
 * Critically-tuned spring in Apple's terms (response = period in seconds, dampingFraction = damping ratio).
 * Integrated per display frame; with "remove animations" it jumps straight to the target.
 */
class Spring(initial: Float, private val reduceMotion: Boolean = false, private val onUpdate: (Float) -> Unit) : Choreographer.FrameCallback {
    var value = initial; private set
    var target = initial; private set
    private var velocity = 0f
    private var stiffness = 0f
    private var damping = 0f
    private var lastNs = 0L
    private var running = false

    fun animateTo(t: Float, response: Float = 0.38f, dampingFraction: Float = 0.86f, initialVelocity: Float? = null) {
        target = t
        if (reduceMotion) { snapTo(t); return }
        val w = (2 * Math.PI / response).toFloat()
        stiffness = w * w
        damping = 2f * dampingFraction * w
        if (initialVelocity != null) velocity = initialVelocity
        if (!running) { running = true; lastNs = 0L; Choreographer.getInstance().postFrameCallback(this) }
    }

    fun snapTo(t: Float) {
        if (running) { Choreographer.getInstance().removeFrameCallback(this); running = false }
        target = t; value = t; velocity = 0f; onUpdate(t)
    }

    override fun doFrame(ns: Long) {
        val dt = if (lastNs == 0L) 1f / 120f else ((ns - lastNs) / 1e9f).coerceIn(0f, 0.05f)
        lastNs = ns
        var left = dt
        while (left > 0f) {
            val h = minOf(left, 1f / 240f)
            val a = -stiffness * (value - target) - damping * velocity
            velocity += a * h
            value += velocity * h
            left -= h
        }
        if (abs(velocity) < 0.0005f && abs(value - target) < 0.0005f) { value = target; velocity = 0f; running = false; onUpdate(value); return }
        onUpdate(value)
        Choreographer.getInstance().postFrameCallback(this)
    }
}

/**
 * A screen whose floating controls are Liquid Glass. Every non-glass child (the scrolling content) is recorded into
 * one RenderNode with the backdrop; each [GlassPane] child then redraws its patch of that node through the lens shader,
 * so the glass really refracts the cards scrolling underneath. Without API 33, hardware rendering or system blur the
 * panes fall back to a frosted, opaque look.
 */
class GlassHost(context: Context, val tokens: Tokens, private val backdrop: Aurora?) : FrameLayout(context) {
    private val content = RenderNode("glass-content")
    private val backdropNode = RenderNode("glass-backdrop")
    private val blocks = LinkedHashSet<View>()
    private val blockLenses = HashMap<View, Any>()
    private val tmp = android.graphics.Rect()

    fun register(block: View) { blocks += block; invalidate() }
    fun unregister(block: View) { blocks -= block; blockLenses.remove(block) }
    private val edgeNode = RenderNode("glass-edge")
    private val edgeShader: Any? = if (Build.VERSION.SDK_INT >= 33) RuntimeShader(GlassShaders.EDGE) else null
    val reduceTransparency = Tokens.reduceTransparency(context)
    private val lightListener: () -> Unit = { invalidate() }
    /** Height of the soft scroll-edge effect under the status bar (px); 0 = none. */
    var edgeHeight = 0f
        set(v) { field = v; invalidate() }

    init {
        setWillNotDraw(false)
        clipChildren = false
    }

    private var live = false

    /** Sensor and aurora run only while the screen is actually visible (the views stay attached while the activity is stopped). */
    private fun setLive(on: Boolean) {
        if (on == live) return
        live = on
        if (on) { GlassLight.attach(context, lightListener); backdrop?.start(this) }
        else { GlassLight.detach(lightListener); backdrop?.stop(this) }
    }

    override fun onWindowVisibilityChanged(visibility: Int) { super.onWindowVisibilityChanged(visibility); setLive(visibility == View.VISIBLE && isAttachedToWindow) }
    override fun onAttachedToWindow() { super.onAttachedToWindow(); setLive(windowVisibility == View.VISIBLE) }
    override fun onDetachedFromWindow() { setLive(false); super.onDetachedFromWindow() }

    fun glassOn(canvas: Canvas) = Build.VERSION.SDK_INT >= 33 && canvas.isHardwareAccelerated && !reduceTransparency

    override fun dispatchDraw(canvas: Canvas) {
        val gpu = glassOn(canvas)
        if (!gpu) {
            backdrop?.draw(canvas, width.toFloat(), height.toFloat()) ?: canvas.drawColor(tokens.groupedBg)
            for (i in 0 until childCount) { val c = getChildAt(i); if (c is GlassPane) c.hostDraws = false }
            for (b in blocks) (b as? GlassBlock)?.hostDrawsGlass = false
            super.dispatchDraw(canvas)
            return
        }
        drawGpu(canvas)
    }

    @RequiresApi(33)
    private fun drawGpu(canvas: Canvas) {
        backdropNode.setPosition(0, 0, width, height)
        val bc = backdropNode.beginRecording()
        backdrop?.draw(bc, width.toFloat(), height.toFloat()) ?: bc.drawColor(tokens.groupedBg)
        backdropNode.endRecording()
        content.setPosition(0, 0, width, height)
        val rc = content.beginRecording()
        rc.drawRenderNode(backdropNode)
        val dd = resources.displayMetrics.density
        for (b in blocks) {
            val gb = b as? GlassBlock ?: continue
            if (!b.isAttachedToWindow || b.visibility != View.VISIBLE || b.width == 0) continue
            tmp.set(0, 0, b.width, b.height)
            try { offsetDescendantRectToMyCoords(b, tmp) } catch (_: IllegalArgumentException) { continue }
            gb.hostDrawsGlass = true
            if (tmp.bottom < 0 || tmp.top > height) continue
            val lens = blockLenses.getOrPut(b) { Lens(dd) } as Lens
            lens.draw(rc, backdropNode, tmp.exactCenterX(), tmp.exactCenterY(), tmp.width() / 2f, tmp.height() / 2f, gb.glassCornerDp * dd, tokens,
                if (tokens.dark) 0xFF000000.toInt() else 0xFFFFFFFF.toInt(), if (tokens.dark) 0.40f else 0.42f, 9f, alpha = b.alpha, shadow = if (tokens.dark) 0.5f else 0.18f, bezelCapDp = 8f, refractionCapDp = 6f)
        }
        val time = drawingTime
        for (i in 0 until childCount) {
            val c = getChildAt(i)
            if (c !is GlassPane && c !is Overlay && c.visibility == View.VISIBLE) drawChild(rc, c, time)
        }
        content.endRecording()
        canvas.drawRenderNode(content)

        if (edgeHeight > 0f) {
            val h = edgeHeight.toInt().coerceAtMost(height)
            edgeNode.setPosition(0, 0, width, h)
            val ec = edgeNode.beginRecording()
            ec.drawRenderNode(content)
            edgeNode.endRecording()
            val s = edgeShader as RuntimeShader
            s.setFloatUniform("height", h.toFloat())
            val bg = tokens.groupedBg
            s.setFloatUniform("bg", (bg shr 16 and 255) / 255f, (bg shr 8 and 255) / 255f, (bg and 255) / 255f, if (backdrop != null) 0.25f else 0.6f)
            val blur = 14f * resources.displayMetrics.density
            edgeNode.setRenderEffect(RenderEffect.createChainEffect(RenderEffect.createRuntimeShaderEffect(s, "content"), RenderEffect.createBlurEffect(blur, blur, Shader.TileMode.CLAMP)))
            canvas.drawRenderNode(edgeNode)
        }

        for (i in 0 until childCount) {
            val c = getChildAt(i)
            if (c is Overlay && c.visibility == View.VISIBLE) drawChild(canvas, c, time)
        }
        for (i in 0 until childCount) {
            val c = getChildAt(i)
            if (c is GlassPane && c.visibility == View.VISIBLE) {
                c.hostDraws = true
                if (c.alpha > 0f) c.drawGlass(canvas, content, tokens)
                drawChild(canvas, c, time)
            }
        }
    }
}

/**
 * Draws one piece of Liquid Glass: [source] (a RenderNode in host coordinates) is redrawn through the lens shader
 * over the box centred at (cx, cy). Big panes cap the bezel so only the rim bends, like the real material.
 */
@RequiresApi(33)
class Lens(private val d: Float) {
    private val node = RenderNode("lens")
    private val shader = RuntimeShader(GlassShaders.LENS)

    fun draw(canvas: Canvas, source: RenderNode, cx: Float, cy: Float, hw: Float, hh: Float, radius: Float, t: Tokens,
             tint: Int, tintAmount: Float, blurDp: Float, press: Float = 0f, alpha: Float = 1f, shadow: Float = -1f, bezelCapDp: Float = 24f, refractionCapDp: Float = 15f) {
        val m = (18f * d).toInt()
        val l = (cx - hw).toInt() - m; val tp = (cy - hh).toInt() - m
        val r = (cx + hw).toInt() + m; val b = (cy + hh).toInt() + m
        node.setPosition(l, tp, r, b)
        val rc = node.beginRecording()
        rc.translate(-l.toFloat(), -tp.toFloat())
        rc.drawRenderNode(source)
        node.endRecording()
        val boxL = cx - hw - l; val boxT = cy - hh - tp
        val s = shader
        s.setFloatUniform("box", boxL, boxT, boxL + hw * 2, boxT + hh * 2)
        s.setFloatUniform("radius", radius)
        val k = minOf(hw, hh)
        s.setFloatUniform("bezel", minOf(k * 0.62f, bezelCapDp * d) + 4f * d)
        s.setFloatUniform("refraction", minOf(k * 0.42f, refractionCapDp * d) + 3f * d)
        s.setFloatUniform("dispersion", 1f)
        s.setFloatUniform("lightDir", GlassLight.x, GlassLight.y)
        s.setFloatUniform("tint", (tint shr 16 and 255) / 255f, (tint shr 8 and 255) / 255f, (tint and 255) / 255f, tintAmount)
        s.setFloatUniform("dark", if (t.dark) 1f else 0f)
        s.setFloatUniform("press", press)
        s.setFloatUniform("shadow", if (shadow >= 0f) shadow else if (t.dark) 0.55f else 0.30f)
        s.setFloatUniform("frost", 0f)
        val blur = blurDp * d
        node.setRenderEffect(RenderEffect.createChainEffect(RenderEffect.createRuntimeShaderEffect(s, "content"), RenderEffect.createBlurEffect(blur, blur, Shader.TileMode.CLAMP)))
        node.alpha = alpha
        canvas.drawRenderNode(node)
    }
}

/**
 * A content block made of glass (each provider on the dashboard). The [GlassHost] draws the glass behind it, sampling only
 * the backdrop, so the block's own text and rings sit on top; without GPU glass the block paints its frosted fallback.
 */
interface GlassBlock {
    val glassCornerDp: Float
    var hostDrawsGlass: Boolean
}

/** Marker for views that float above the scroll-edge effect without being glass (e.g. the inline navigation title). */
interface Overlay

/**
 * A floating Liquid Glass control (capsule or rounded rect). Its children (icons, labels) sit on the glass.
 * Touch makes it "interactive" like `.glassEffect(.regular.interactive())`: it swells slightly and brightens on a spring.
 */
@SuppressLint("ViewConstructor")
open class GlassPane(context: Context, private val tokens: Tokens, var cornerDp: Float = -1f, var tintColor: Int = 0, var tintAmount: Float = -1f) : FrameLayout(context) {
    private val d = resources.displayMetrics.density
    private var lensRef: Any? = null
    internal var hostDraws = false
        set(v) { if (field != v) { field = v; invalidate() } }
    private var press = 0f
    private val reduceMotion = Tokens.reduceMotion(context)
    private val pressSpring = Spring(0f, reduceMotion) { v -> press = v; scaleX = 1f + 0.06f * v; scaleY = 1f + 0.06f * v; (parent as? View)?.invalidate() }
    var interactive = true
    private val fallbackPath = Path()
    private val fallbackFill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val fallbackRim = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val shadowPaint = Paint(Paint.ANTI_ALIAS_FLAG)

    init { setWillNotDraw(false); clipChildren = false }

    private fun radiusPx(w: Float, h: Float) = if (cornerDp < 0f) minOf(w, h) / 2f else cornerDp * d

    @RequiresApi(33)
    fun drawGlass(canvas: Canvas, content: RenderNode, t: Tokens) {
        val lens = (lensRef ?: Lens(d).also { lensRef = it }) as Lens
        val cx = left + translationX + width / 2f
        val cy = top + translationY + height / 2f
        val hw = width * scaleX / 2f; val hh = height * scaleY / 2f
        // same recipe as the content blocks (black in dark, white in light), lighter so the controls read as a separate layer
        val tc = if (tintAmount >= 0f) tintColor else if (t.dark) 0xFF000000.toInt() else 0xFFFFFFFF.toInt()
        val ta = if (tintAmount >= 0f) tintAmount else if (t.dark) 0.22f else 0.30f
        lens.draw(canvas, content, cx, cy, hw, hh, radiusPx(hw * 2, hh * 2), t, tc, ta, 2.2f, press, alpha)
    }

    override fun draw(canvas: Canvas) {
        if (!hostDraws) drawFallback(canvas)
        super.draw(canvas)
    }

    /** Frosted, nearly opaque version for "reduce transparency" or devices without AGSL. */
    private fun drawFallback(c: Canvas) {
        val w = width.toFloat(); val h = height.toFloat()
        if (w <= 0f || h <= 0f) return
        val rect = RectF(0f, 0f, w, h)
        Squircle.path(rect, radiusPx(w, h), fallbackPath)
        shadowPaint.color = 0
        shadowPaint.setShadowLayer(10f * d, 0f, 3f * d, if (tokens.dark) 0x80000000.toInt() else 0x33000000)
        c.drawPath(fallbackPath, shadowPaint)
        val base = if (tintAmount >= 0f) tintColor else if (tokens.dark) 0xFF2C2C2E.toInt() else 0xFFF9F9FB.toInt()
        fallbackFill.color = if (tintAmount >= 0f) base else withAlpha(base, 0xF5)
        c.drawPath(fallbackPath, fallbackFill)
        fallbackRim.strokeWidth = 1f * d
        fallbackRim.shader = LinearGradient(0f, 0f, w, h, intArrayOf(withAlpha(-1, if (tokens.dark) 0x66 else 0xFF), withAlpha(-1, 0x10), withAlpha(-1, if (tokens.dark) 0x30 else 0x90)), floatArrayOf(0f, 0.5f, 1f), Shader.TileMode.CLAMP)
        c.drawPath(fallbackPath, fallbackRim)
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (interactive && isEnabled) when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> pressSpring.animateTo(1f, 0.28f, 0.7f)
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> pressSpring.animateTo(0f, 0.42f, 0.62f)
        }
        return super.onTouchEvent(e)
    }
}

/** iOS 26-style wallpaper behind the Cupertino screens: soft colour fields that drift very slowly while visible. */
class Aurora(private val dark: Boolean, private val reduceMotion: Boolean) {
    private val p = Paint(Paint.ANTI_ALIAS_FLAG or Paint.DITHER_FLAG)
    private var phase = 0.17f
    private var animator: android.animation.ValueAnimator? = null
    private var lastFrame = 0L
    private val hosts = LinkedHashSet<View>()

    private val baseTop = if (dark) 0xFF06070D.toInt() else 0xFFF5F2FA.toInt()
    private val baseBottom = if (dark) 0xFF0A0C17.toInt() else 0xFFE9F1FB.toInt()
    private val blobs: IntArray = if (dark) intArrayOf(0xE01E3A8A.toInt(), 0xB06D28D9.toInt(), 0x70BE185D, 0x900E7490.toInt())
        else intArrayOf(0x998EC5FF.toInt(), 0x80C9A7FF.toInt(), 0x66FFB3C7, 0x7A9DF0E0)

    fun start(v: View) {
        hosts += v
        if (reduceMotion || animator != null) return
        animator = android.animation.ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 90_000; repeatCount = android.animation.ValueAnimator.INFINITE; interpolator = android.view.animation.LinearInterpolator()
            // the drift is slow: ~15 redraws a second are indistinguishable from 120 and spare the GPU
            addUpdateListener { val now = android.os.SystemClock.uptimeMillis(); if (now - lastFrame >= 66) { lastFrame = now; phase = it.animatedValue as Float; hosts.forEach(View::invalidate) } }
            start()
        }
    }

    fun stop(v: View) {
        hosts -= v
        if (hosts.isEmpty()) { animator?.cancel(); animator = null }
    }

    fun draw(c: Canvas, w: Float, h: Float) {
        p.shader = LinearGradient(0f, 0f, 0f, h, baseTop, baseBottom, Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, w, h, p)
        val m = maxOf(w, h)
        val tau = (2 * Math.PI).toFloat()
        fun blob(i: Int, bx: Float, by: Float, ax: Float, ay: Float, rad: Float, sp: Float) {
            val a = phase * tau * sp + i * 1.7f
            val cx = w * (bx + ax * kotlin.math.sin(a))
            val cy = h * (by + ay * kotlin.math.cos(a * 0.8f))
            val col = blobs[i]
            p.shader = android.graphics.RadialGradient(cx, cy, m * rad, intArrayOf(col, withAlpha(col, (col ushr 24) / 3), withAlpha(col, 0)), floatArrayOf(0f, 0.55f, 1f), Shader.TileMode.CLAMP)
            c.drawRect(0f, 0f, w, h, p)
        }
        blob(0, 0.18f, 0.12f, 0.10f, 0.05f, 0.62f, 1f)
        blob(1, 0.92f, 0.30f, 0.08f, 0.08f, 0.55f, 2f)
        blob(2, 0.70f, 0.78f, 0.12f, 0.06f, 0.50f, 1f)
        blob(3, 0.10f, 0.70f, 0.07f, 0.09f, 0.58f, 3f)
        p.shader = null
    }
}

fun smoothstep(e0: Float, e1: Float, x: Float): Float { val t = ((x - e0) / (e1 - e0)).coerceIn(0f, 1f); return t * t * (3 - 2 * t) }
