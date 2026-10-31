package dev.usage.monitor

import android.app.Activity
import android.appwidget.AppWidgetManager
import android.content.ClipboardManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.res.Configuration
import android.graphics.Typeface
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import dev.usage.monitor.widget.UsageWidget

class MainActivity : Activity() {
    private lateinit var store: Store
    private lateinit var pal: Palette
    private lateinit var root: LinearLayout
    private lateinit var dashboard: DashboardView
    private var live: Relay.Live? = null
    private var linkUp = false
    private val ui = android.os.Handler(android.os.Looper.getMainLooper())
    private var busyUntil = 0L

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = Store(this)
        val dark = (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
        pal = Palette(dark)
        window.statusBarColor = pal.base
        window.navigationBarColor = pal.base
        window.decorView // installs the decor view; insetsController is null before it exists
        if (!dark && android.os.Build.VERSION.SDK_INT >= 30)
            window.insetsController?.setSystemBarsAppearance(
                android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or android.view.WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS,
                android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or android.view.WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS)
        handleIntent(intent)
        build()
    }

    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); handleIntent(intent); build(); restartLive() }

    override fun onStart() { super.onStart(); restartLive(); refreshState() }
    override fun onStop() { super.onStop(); live?.stop(); live = null }

    // ---- pairing ---------------------------------------------------------------------------------------------

    private fun handleIntent(i: Intent?) {
        val data = i?.dataString ?: return
        if (data.startsWith("usage://", true)) pair(data)
    }

    private fun pair(text: String): Boolean {
        val p = Pairing.parse(text)
        if (p == null) { Toast.makeText(this, if (Fmt.pt) "Código inválido" else "Invalid code", Toast.LENGTH_LONG).show(); return false }
        store.pairing = p
        Toast.makeText(this, (if (Fmt.pt) "Pareado com " else "Paired with ") + p.pcName, Toast.LENGTH_SHORT).show()
        SyncWorker.schedulePeriodic(this)
        return true
    }

    private fun unpair() {
        live?.stop(); live = null
        store.pairing = null
        SyncWorker.cancelPeriodic(this)
        UsageWidget.updateAll(this)
        build()
    }

    // ---- live session ---------------------------------------------------------------------------------------

    private fun restartLive() {
        live?.stop(); live = null
        val p = store.pairing ?: return
        live = Relay.Live(p,
            onSnapshot = { s, raw, busy -> runOnUiThread { store.save(raw); store.hostOnline = true; if (!busy) busyUntil = 0; refreshState(); UsageWidget.updateAll(this) } },
            onHost = { online -> runOnUiThread { store.hostOnline = online; refreshState() } },
            onLink = { up -> runOnUiThread { linkUp = up; refreshState() } },
        ).also { it.start() }
    }

    private fun currentState() = UiState(store.snapshot, store.pairing != null, store.hostOnline, store.receivedAtMs, System.currentTimeMillis() < busyUntil, store.icons, store.showIcons)

    private fun refreshState() { if (::dashboard.isInitialized) { dashboard.state = currentState() } }

    private fun refresh() {
        val l = live
        busyUntil = System.currentTimeMillis() + 60_000
        refreshState()
        ui.postDelayed({ refreshState() }, 61_000)
        if (l != null && linkUp) l.refresh() else SyncWorker.refreshNow(this)
    }

    // ---- layout -----------------------------------------------------------------------------------------------

    private fun build() {
        val scroll = ScrollView(this).apply {
            setBackgroundColor(pal.base); isFillViewport = true; overScrollMode = View.OVER_SCROLL_NEVER
            setOnApplyWindowInsetsListener { v, insets ->
                v.setPadding(0, insets.systemWindowInsetTop, 0, insets.systemWindowInsetBottom); insets
            }
        }
        root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(0, 0, 0, dpi(24f)) }
        scroll.addView(root, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        dashboard = DashboardView(this).apply { onRefresh = ::refresh; onPairTap = { scroll.post { scroll.fullScroll(View.FOCUS_DOWN) } } }
        dashboard.state = currentState()
        root.addView(dashboard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        val pairing = store.pairing
        appearancePanel()
        if (pairing == null) pairPanel() else connectedPanel(pairing)
        setContentView(scroll)
    }

    private fun label(text: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        this.text = text; textSize = size; setTextColor(color)
        typeface = Typeface.create(if (bold) "sans-serif-medium" else "sans-serif", Typeface.NORMAL)
    }

    private fun panel(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = Neu.surface(this@MainActivity, pal, 20f, true, 12f)
        val p = dpi(12f + 16f); setPadding(p, p, p, p)
        root.addView(this, LinearLayout.LayoutParams(-1, -2).apply { setMargins(dpi(16f - 12f), dpi(4f), dpi(16f - 12f), dpi(4f)) })
    }

    private fun row(parent: LinearLayout, vararg v: View) {
        val r = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        v.forEachIndexed { i, view -> r.addView(view, LinearLayout.LayoutParams(0, -2, 1f).apply { if (i > 0) leftMargin = dpi(4f) }) }
        parent.addView(r, LinearLayout.LayoutParams(-1, -2).apply { topMargin = dpi(4f) })
    }

    // ---- appearance: icons on/off and which glyph each provider uses ---------------------------------------

    private fun appearancePanel() {
        val p = panel()
        fun fill() {
            p.removeAllViews()
            p.addView(label(if (Fmt.pt) "Aparência" else "Appearance", 17f, pal.text, true))
            val on = store.showIcons
            row(p, Neu.button(this, pal, (if (Fmt.pt) "Ícones: " else "Icons: ") + (if (on) (if (Fmt.pt) "ligados" else "on") else (if (Fmt.pt) "desligados" else "off")), if (on) pal.accent("codex") else pal.dim) {
                store.showIcons = !store.showIcons; applyAppearance(); fill()
            })
            if (on) for ((id, name) in Icons.providers) {
                p.addView(label(name, 13f, pal.dim).apply { setPadding(dpi(4f), dpi(8f), 0, dpi(2f)) })
                val strip = android.widget.HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; overScrollMode = View.OVER_SCROLL_NEVER }
                val inner = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
                for (opt in Icons.options(id)) {
                    inner.addView(IconChoice(this, pal, opt.id, pal.accent(id), store.icon(id) == opt.id, opt.label) {
                        store.setIcon(id, opt.id); applyAppearance(); fill()
                    }, LinearLayout.LayoutParams(dpi(72f), dpi(84f)))
                }
                strip.addView(inner)
                p.addView(strip, LinearLayout.LayoutParams(-1, -2))
            }
        }
        fill()
    }

    private fun applyAppearance() { refreshState(); UsageWidget.updateAll(this) }

    private fun pairPanel() {
        val p = panel()
        p.addView(label(if (Fmt.pt) "Parear com o PC" else "Pair with your PC", 17f, pal.text, true))
        p.addView(label(Fmt.pairHelp, 13f, pal.dim).apply { setPadding(0, dpi(6f), 0, dpi(6f)) })
        val input = EditText(this).apply {
            hint = if (Fmt.pt) "Código (XXXXX-XXXXX-…)" else "Code (XXXXX-XXXXX-…)"
            setHintTextColor(pal.dim); setTextColor(pal.text); textSize = 15f
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            background = Neu.surface(this@MainActivity, pal, 16f, false, 8f)
            val pad = dpi(8f + 14f); setPadding(pad, pad, pad, pad)
            isSingleLine = true
        }
        p.addView(input, LinearLayout.LayoutParams(-1, -2).apply { topMargin = dpi(2f) })
        val accent = pal.accent("codex")
        row(p,
            Neu.button(this, pal, if (Fmt.pt) "Escanear QR" else "Scan QR", accent) { scan() },
            Neu.button(this, pal, if (Fmt.pt) "Colar" else "Paste") {
                val clip = (getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).primaryClip?.getItemAt(0)?.text?.toString().orEmpty()
                input.setText(clip)
            })
        row(p, Neu.button(this, pal, if (Fmt.pt) "Parear" else "Pair", accent) { if (pair(input.text.toString())) { build(); restartLive() } })
    }

    private fun scan() {
        val options = GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
        GmsBarcodeScanning.getClient(this, options).startScan()
            .addOnSuccessListener { code -> code.rawValue?.let { if (pair(it)) { build(); restartLive() } } }
            .addOnFailureListener { Toast.makeText(this, if (Fmt.pt) "Não foi possível abrir o leitor de QR. Cole o código." else "QR scanner unavailable. Paste the code.", Toast.LENGTH_LONG).show() }
    }

    private fun connectedPanel(p: Pairing) {
        val panel = panel()
        panel.addView(label((if (Fmt.pt) "Conectado a " else "Connected to ") + p.pcName, 16f, pal.text, true))
        panel.addView(label(p.relayUrl.removePrefix("wss://").removePrefix("ws://"), 12f, pal.dim).apply { setPadding(0, dpi(2f), 0, dpi(8f)) })
        val accent = pal.accent("codex")
        row(panel,
            Neu.button(this, pal, if (Fmt.pt) "Atualizar agora" else "Refresh now", accent) { refresh() },
            Neu.button(this, pal, if (Fmt.pt) "Adicionar widget" else "Add widget") { pinWidget() })
        row(panel, Neu.button(this, pal, if (Fmt.pt) "Desparear" else "Unpair", pal.crit) { unpair() })
    }

    private fun pinWidget() {
        val m = getSystemService(AppWidgetManager::class.java)
        if (m.isRequestPinAppWidgetSupported) m.requestPinAppWidget(ComponentName(this, UsageWidget::class.java), null, null)
        else Toast.makeText(this, if (Fmt.pt) "Adicione pelo menu de widgets da tela inicial" else "Add it from the home screen widget picker", Toast.LENGTH_LONG).show()
    }
}
