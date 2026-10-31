package dev.usage.monitor

import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface

/**
 * Provider glyphs, drawn from simple shapes (no bitmaps, no trademarked artwork).
 * Each provider has a default and a few alternatives the user can pick in the app.
 */
object Icons {
    data class Opt(val id: String, val label: String)

    fun options(provider: String): List<Opt> = when (provider) {
        "codex" -> listOf(Opt("chatgpt", "ChatGPT"), Opt("codex", "Codex"))
        "claude" -> listOf(Opt("claude", "Claude"), Opt("claude-code", "Claude Code"))
        "opencode" -> listOf(Opt("opencode", "OpenCode"), Opt("kimi", "Kimi"), Opt("glm", "GLM"), Opt("qwen", "Qwen"), Opt("minimax", "MiniMax"), Opt("deepseek", "DeepSeek"))
        else -> listOf(Opt("generic", provider))
    }

    fun default(provider: String) = options(provider).first().id
    val providers = listOf("codex" to "ChatGPT", "claude" to "Claude", "opencode" to "OpenCode")

    private val letters = mapOf("kimi" to "K", "glm" to "Z", "qwen" to "Q", "minimax" to "M", "deepseek" to "D")

    // Claude Code mascot, 11x6 cells: X body, e eye.
    private val mascot = listOf(
        "..XXXXXXX..",
        ".XXXXXXXXX.",
        "XXXeXXXeXXX",
        "XXXXXXXXXXX",
        ".XXXXXXXXX.",
        "..X.X.X.X..",
    )

    /** Draws [id] centred at (cx, cy) inside a circle of radius [r]. */
    fun draw(c: Canvas, id: String, cx: Float, cy: Float, r: Float, color: Int, eye: Int = 0xFF2A2E35.toInt()) {
        val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color; style = Paint.Style.STROKE; strokeWidth = maxOf(1.6f, r * 0.17f); strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND }
        val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color; style = Paint.Style.FILL }
        when (id) {
            "chatgpt" -> for (i in 0 until 3) {
                c.save(); c.rotate(i * 60f, cx, cy)
                c.drawRoundRect(RectF(cx - r * 0.56f, cy - r * 0.2f, cx + r * 0.56f, cy + r * 0.2f), r * 0.2f, r * 0.2f, stroke)
                c.restore()
            }
            "codex" -> {
                c.drawRoundRect(RectF(cx - r * 0.64f, cy - r * 0.5f, cx + r * 0.64f, cy + r * 0.5f), r * 0.3f, r * 0.3f, stroke)
                c.drawPath(Path().apply { moveTo(cx - r * 0.3f, cy - r * 0.2f); lineTo(cx - r * 0.06f, cy); lineTo(cx - r * 0.3f, cy + r * 0.2f) }, stroke)
                c.drawLine(cx + r * 0.06f, cy + r * 0.22f, cx + r * 0.32f, cy + r * 0.22f, stroke)
            }
            "claude" -> for (i in 0 until 8) {
                val a = Math.toRadians(i * 45.0 + 22.5)
                val inner = r * 0.12f; val outer = if (i % 2 == 0) r * 0.66f else r * 0.5f
                c.drawLine(cx + (inner * Math.cos(a)).toFloat(), cy + (inner * Math.sin(a)).toFloat(), cx + (outer * Math.cos(a)).toFloat(), cy + (outer * Math.sin(a)).toFloat(), stroke)
            }
            "claude-code" -> {
                val cell = r * 1.7f / 11f
                val x0 = cx - cell * 5.5f; val y0 = cy - cell * 3f
                val e = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = eye }
                mascot.forEachIndexed { row, line ->
                    line.forEachIndexed { col, ch ->
                        if (ch != '.') c.drawRect(x0 + col * cell, y0 + row * cell, x0 + (col + 1) * cell + 0.3f, y0 + (row + 1) * cell + 0.3f, if (ch == 'e') e else fill)
                    }
                }
            }
            "opencode" -> {
                c.drawRoundRect(RectF(cx - r * 0.46f, cy - r * 0.58f, cx + r * 0.46f, cy + r * 0.58f), r * 0.12f, r * 0.12f, stroke)
                c.drawRect(cx - r * 0.2f, cy - r * 0.28f, cx + r * 0.2f, cy + r * 0.28f, fill)
            }
            in letters -> {
                val t = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    this.color = color; textAlign = Paint.Align.CENTER; textSize = r * 1.25f; typeface = Typeface.create("sans-serif", Typeface.BOLD)
                }
                c.drawText(letters.getValue(id), cx, cy + r * 0.44f, t)
            }
            else -> c.drawCircle(cx, cy, r * 0.32f, fill)
        }
    }
}
