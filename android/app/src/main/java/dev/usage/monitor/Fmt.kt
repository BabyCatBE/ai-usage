package dev.usage.monitor

import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

/** Text and formatting. Portuguese when the system language is Portuguese, English otherwise. */
object Fmt {
    val pt get() = Locale.getDefault().language == "pt"
    private fun t(p: String, e: String) = if (pt) p else e

    val appTitle get() = t("Uso de IA", "AI Usage")
    val updating get() = t("Atualizando…", "Updating…")
    val notPaired get() = t("Toque para parear com o PC", "Tap to pair with your PC")
    val pairHelp get() = t("Abra o app no PC, escolha “Parear celular” e escaneie o QR ou cole o código.", "Open the PC app, choose “Pair phone” and scan the QR or paste the code.")
    val pcOffline get() = t("PC desligado ou app fechado", "PC is off or the app is closed")
    val noNetwork get() = t("Sem conexão", "No connection")
    val waiting get() = t("Aguardando dados do PC…", "Waiting for data from your PC…")

    fun windowName(id: String, label: String, short: Boolean = false) = when (id) {
        "five_hour" -> if (short) "5h" else t("5 horas", "5 hours")
        "seven_day" -> if (short) t("Sem", "7d") else t("Semana", "Week")
        "monthly" -> if (short) t("Mês", "30d") else t("Mês", "Month")
        else -> label.ifEmpty { id }
    }

    fun age(fromMs: Long, nowMs: Long): String {
        val m = ((nowMs - fromMs) / 60000).coerceAtLeast(0)
        return when {
            m < 1 -> t("agora", "now")
            m < 60 -> t("há ${m}min", "${m}m ago")
            m < 1440 -> t("há ${m / 60}h", "${m / 60}h ago")
            else -> t("há ${m / 1440}d", "${m / 1440}d ago")
        }
    }

    fun clock(ms: Long): String = SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(ms))

    /** "5h 18min", "3d 7h", "42min". */
    fun countdown(resetMs: Long?, nowMs: Long): String {
        if (resetMs == null) return "—"
        val mins = (resetMs - nowMs + 59_999) / 60_000
        if (mins <= 0) return t("agora", "now")
        val d = mins / 1440; val h = mins % 1440 / 60; val m = mins % 60
        return when { d > 0 -> if (h > 0) "${d}d ${h}h" else "${d}d"; h > 0 -> if (m > 0) "${h}h ${m}min" else "${h}h"; else -> "${m}min" }
    }

    /** "03:00" today, "ter 09:00" within a week, "12/11 09:00" beyond. */
    fun resetAt(resetMs: Long?, nowMs: Long): String {
        if (resetMs == null) return ""
        val a = Calendar.getInstance().apply { timeInMillis = nowMs }
        val b = Calendar.getInstance().apply { timeInMillis = resetMs }
        val sameDay = a.get(Calendar.YEAR) == b.get(Calendar.YEAR) && a.get(Calendar.DAY_OF_YEAR) == b.get(Calendar.DAY_OF_YEAR)
        val days = (resetMs - nowMs) / 86_400_000
        return when {
            sameDay -> t("hoje ", "today ") + clock(resetMs)
            days < 6 -> SimpleDateFormat("EEE HH:mm", Locale.getDefault()).format(Date(resetMs))
            else -> SimpleDateFormat("dd/MM HH:mm", Locale.getDefault()).format(Date(resetMs))
        }
    }

    fun percent(v: Double?): String = when {
        v == null -> "—"
        v % 1.0 == 0.0 -> "%.0f%%".format(Locale.US, v)
        else -> "%.1f%%".format(Locale.US, v)
    }

    fun usd(v: Double) = "$" + "%.2f".format(Locale.US, v)

    fun error(code: String?): String = when (code) {
        "AUTH_REQUIRED" -> t("Faça login no CLI deste serviço", "Sign in to this service's CLI")
        "CONSOLE_LOGIN_REQUIRED" -> t("Conecte a conta do OpenCode Console", "Connect your OpenCode Console account")
        "CLI_NOT_FOUND" -> t("CLI não encontrado no PC", "CLI not found on the PC")
        "CLI_TIMEOUT" -> t("Sem resposta do CLI", "CLI did not answer")
        "USAGE_UNAVAILABLE" -> t("Limites indisponíveis", "Limits unavailable")
        else -> t("Falha na leitura", "Could not read")
    }

    fun detailLabel(label: String) = when (label) {
        "credits" -> t("Créditos", "Credits")
        "extra_usage" -> t("Uso extra", "Extra usage")
        else -> label
    }
}
