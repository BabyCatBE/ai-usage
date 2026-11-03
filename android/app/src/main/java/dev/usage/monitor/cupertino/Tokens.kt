package dev.usage.monitor.cupertino

import android.content.Context
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.provider.Settings
import kotlin.math.exp
import kotlin.math.min

/**
 * Design tokens of the Cupertino look, taken from Apple's semantic system colours (iOS 26) and type ramp.
 * Every surface of the theme (app, settings, widget) reads its colours from here, so light and dark stay in step.
 */
class Tokens(val dark: Boolean) {
    // backgrounds (grouped style)
    val groupedBg = if (dark) 0xFF000000.toInt() else 0xFFF2F2F7.toInt()
    val card = if (dark) 0xFF1C1C1E.toInt() else 0xFFFFFFFF.toInt()
    val cardRaised = if (dark) 0xFF2C2C2E.toInt() else 0xFFFFFFFF.toInt()
    /** Content platters over the aurora: the "thick material" look (blur of a smooth gradient is the gradient itself). */
    val material = if (dark) 0xD91C1C1E.toInt() else 0xD9FFFFFF.toInt()

    // labels
    val label = if (dark) 0xFFFFFFFF.toInt() else 0xFF000000.toInt()
    val secondaryLabel = if (dark) 0x99EBEBF5.toInt() else 0x993C3C43.toInt()
    val tertiaryLabel = if (dark) 0x4DEBEBF5 else 0x4D3C3C43
    val separator = if (dark) 0x99545458.toInt() else 0x4A3C3C43

    // fills (controls sitting on content)
    val fill = if (dark) 0x5C787880 else 0x33787880
    val secondaryFill = if (dark) 0x52787880 else 0x29787880
    val tertiaryFill = if (dark) 0x3D767680 else 0x1F767680
    val quaternaryFill = if (dark) 0x2E767680 else 0x14747480

    // system colours
    val blue = if (dark) 0xFF0A84FF.toInt() else 0xFF007AFF.toInt()
    val green = if (dark) 0xFF30D158.toInt() else 0xFF34C759.toInt()
    val mint = if (dark) 0xFF63E6E2.toInt() else 0xFF00C7BE.toInt()
    val teal = if (dark) 0xFF40C8E0.toInt() else 0xFF30B0C7.toInt()
    val orange = if (dark) 0xFFFF9F0A.toInt() else 0xFFFF9500.toInt()
    val yellow = if (dark) 0xFFFFD60A.toInt() else 0xFFFFCC00.toInt()
    val red = if (dark) 0xFFFF453A.toInt() else 0xFFFF3B30.toInt()
    val indigo = if (dark) 0xFF5E5CE6.toInt() else 0xFF5856D6.toInt()
    val purple = if (dark) 0xFFBF5AF2.toInt() else 0xFFAF52DE.toInt()
    val gray = 0xFF8E8E93.toInt()

    /** Segmented-control thumb at rest. */
    val thumb = if (dark) 0xFF636366.toInt() else 0xFFFFFFFF.toInt()

    fun accent(provider: String) = when (provider) {
        "codex" -> mint
        "claude" -> orange
        "opencode" -> indigo
        else -> blue
    }

    /** Settings-style icon tile behind a white glyph: deep enough for the glyph to read (light mint would not). */
    fun tile(provider: String) = when (provider) {
        "codex" -> 0xFF00A39E.toInt()
        "claude" -> 0xFFF08A00.toInt()
        "opencode" -> 0xFF5856D6.toInt()
        else -> 0xFF007AFF.toInt()
    }

    /** Gauge colour: the accent until the window gets tight, then the system warning colours. */
    fun level(percent: Double?, accent: Int) = when {
        percent == null -> accent
        percent >= 90 -> red
        percent >= 75 -> if (accent == orange) red else orange
        else -> accent
    }

    companion object {
        fun of(context: Context, theme: String): Tokens {
            val night = (context.resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES
            return Tokens(when (theme) { "dark" -> true; "light" -> false; else -> night })
        }

        /** "Remove animations" in the system settings: no springs, no drifting aurora, no gyroscope highlights. */
        fun reduceMotion(context: Context): Boolean = try {
            Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f) == 0f
        } catch (_: Exception) { false }

        /**
         * "Reduce transparency" (Samsung exposes it as accessibility_reduce_transparency; some builds use the AOSP secure key).
         * Glass then turns frosted and opaque. Window cross-blur support is irrelevant here: the glass is drawn by the app.
         */
        fun reduceTransparency(context: Context): Boolean {
            if (android.os.Build.VERSION.SDK_INT < 31) return true
            val r = context.contentResolver
            for (key in listOf("accessibility_reduce_transparency")) {
                try { if (Settings.System.getInt(r, key, 0) == 1) return true } catch (_: Exception) {}
                try { if (Settings.Secure.getInt(r, key, 0) == 1) return true } catch (_: Exception) {}
                try { if (Settings.Global.getInt(r, key, 0) == 1) return true } catch (_: Exception) {}
            }
            return false
        }
    }
}

fun alpha(color: Int, a: Float): Int = ((((color ushr 24) * a).toInt().coerceIn(0, 255)) shl 24) or (color and 0x00FFFFFF)
fun withAlpha(color: Int, a: Int): Int = (a.coerceIn(0, 255) shl 24) or (color and 0x00FFFFFF)

/**
 * Apple's text styles with Inter standing in for SF Pro (SF is licensed for Apple platforms only).
 * Inter's optical-size axis and its dynamic tracking formula give the same text/display behaviour SF gets from the system.
 */
object Type {
    enum class Style(val size: Float, val weight: Int, val leading: Float) {
        LargeTitle(34f, 700, 41f), Title1(28f, 700, 34f), Title2(22f, 700, 28f), Title3(20f, 600, 25f),
        Headline(17f, 600, 22f), Body(17f, 400, 22f), Callout(16f, 400, 21f), Subheadline(15f, 400, 20f),
        Footnote(13f, 400, 18f), Caption1(12f, 400, 16f), Caption2(11f, 400, 13f),
    }

    private var assets: android.content.res.AssetManager? = null
    private val cache = HashMap<Long, Typeface>()

    fun init(context: Context) { if (assets == null) assets = context.applicationContext.assets }

    /** Inter at [weight] (100-900), optical size picked from the point size (Inter Text below 14, Display at 32 and up). */
    fun face(weight: Int, size: Float): Typeface {
        val opsz = size.coerceIn(14f, 32f).toInt()
        val key = weight.toLong() * 1000 + opsz
        cache[key]?.let { return it }
        val a = assets ?: return fallback(weight)
        val tf = try {
            Typeface.Builder(a, "fonts/InterVariable.ttf").setFontVariationSettings("'wght' $weight, 'opsz' $opsz").build()
        } catch (_: Exception) { fallback(weight) }
        cache[key] = tf
        return tf
    }

    private fun fallback(weight: Int): Typeface =
        if (android.os.Build.VERSION.SDK_INT >= 28) Typeface.create(Typeface.SANS_SERIF, weight, false) else if (weight >= 600) Typeface.DEFAULT_BOLD else Typeface.SANS_SERIF

    /** Inter's dynamic metrics: tracking in em for a size in points (tight for display, slightly open for small text). */
    fun tracking(size: Float): Float = -0.0223f + 0.185f * exp(-0.1745f * size).toFloat()

    /** [scale] = display density for views that draw in pixels; the optical size still follows the point size. */
    fun paint(style: Style, color: Int, weight: Int = style.weight, tabular: Boolean = false, size: Float = style.size, scale: Float = 1f): Paint =
        Paint(Paint.ANTI_ALIAS_FLAG or Paint.SUBPIXEL_TEXT_FLAG).apply {
            typeface = face(weight, size)
            textSize = size * scale
            letterSpacing = tracking(size)
            this.color = color
            if (tabular) fontFeatureSettings = "'tnum' 1"
        }

    fun apply(tv: android.widget.TextView, style: Style, color: Int, weight: Int = style.weight) {
        tv.typeface = face(weight, style.size)
        tv.textSize = style.size
        tv.letterSpacing = tracking(style.size)
        tv.setTextColor(color)
        tv.includeFontPadding = false
        tv.setLineSpacing(0f, 1f)
        if (android.os.Build.VERSION.SDK_INT >= 28) tv.lineHeight = android.util.TypedValue.applyDimension(android.util.TypedValue.COMPLEX_UNIT_SP, style.leading, tv.resources.displayMetrics).toInt()
    }
}

/** Spacing and radii on Apple's 8-point grid (iOS 26 inset-grouped lists use 26pt card corners). */
object Metrics {
    const val margin = 16f
    const val cardRadius = 26f
    const val rowHeight = 52f
    const val controlHeight = 44f
    const val iconTile = 30f
    const val iconTileRadius = 7.5f
}

/**
 * Continuous ("squircle") corners: the curvature eases in over ~1.53 r along each edge instead of jumping at a tangent,
 * the way UIKit draws every rounded rect. Coefficients from the reverse-engineered UIBezierPath(roundedRect:cornerRadius:).
 * A radius at or above half the short side gives a capsule, which Apple draws as a plain stadium.
 */
object Squircle {
    private const val K = 1.52866483f
    private val pts = floatArrayOf(
        1.08849323f, 0f, 0.86840689f, 0.02052542f, 0.66993427f, 0.06549600f,
        0.37282392f, 0.15070260f, 0.15464700f, 0.36849710f, 0.07491100f, 0.63149399f,
        0.02052542f, 0.86840689f, 0f, 1.08849323f, 0f, K,
    )

    fun path(r: RectF, radius: Float, out: Path = Path()): Path {
        out.reset()
        val half = min(r.width(), r.height()) / 2f
        if (radius <= 0f) { out.addRect(r, Path.Direction.CW); return out }
        if (radius >= half * 0.999f) { out.addRoundRect(r, half, half, Path.Direction.CW); return out }
        val k = min(radius, half / K)
        // corners clockwise: (corner, incoming edge direction u, outgoing direction v)
        val corners = arrayOf(
            floatArrayOf(r.right, r.top, 1f, 0f, 0f, 1f),
            floatArrayOf(r.right, r.bottom, 0f, 1f, -1f, 0f),
            floatArrayOf(r.left, r.bottom, -1f, 0f, 0f, -1f),
            floatArrayOf(r.left, r.top, 0f, -1f, 1f, 0f),
        )
        fun px(c: FloatArray, a: Float, b: Float) = c[0] - a * k * c[2] + b * k * c[4]
        fun py(c: FloatArray, a: Float, b: Float) = c[1] - a * k * c[3] + b * k * c[5]
        val first = corners[0]
        out.moveTo(px(first, K, 0f), py(first, K, 0f))
        for (c in corners) {
            out.lineTo(px(c, K, 0f), py(c, K, 0f))
            var i = 0
            while (i < pts.size) {
                out.cubicTo(px(c, pts[i], pts[i + 1]), py(c, pts[i], pts[i + 1]), px(c, pts[i + 2], pts[i + 3]), py(c, pts[i + 2], pts[i + 3]), px(c, pts[i + 4], pts[i + 5]), py(c, pts[i + 4], pts[i + 5]))
                i += 6
            }
        }
        out.close()
        return out
    }
}
