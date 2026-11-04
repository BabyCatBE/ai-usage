package dev.usage.monitor.cupertino

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.app.Activity
import android.content.ClipboardManager
import android.content.Context
import android.graphics.drawable.ColorDrawable
import android.os.Build
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.animation.LinearInterpolator
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.window.BackEvent
import android.window.OnBackAnimationCallback
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import androidx.annotation.RequiresApi
import dev.usage.monitor.Fmt
import dev.usage.monitor.Icons
import dev.usage.monitor.Pairing
import dev.usage.monitor.Renderer
import dev.usage.monitor.Store
import dev.usage.monitor.UiState

/**
 * The Cupertino look of the app: a dashboard over the aurora, and Settings pushed from the right, as an iOS 26 app
 * would do it. Content is solid (inset-grouped platters); Liquid Glass is reserved for the floating controls.
 */
class CupertinoScreen(private val act: Activity, private val store: Store, private val actions: Actions) {
    class Actions(
        val refresh: () -> Unit,
        val pair: (String) -> Boolean,
        val scan: () -> Unit,
        val unpair: () -> Unit,
        val pinWidget: () -> Unit,
        /** Another look was chosen: the activity rebuilds itself with the other UI. */
        val restyle: () -> Unit,
        /** Light/dark mode changed: rebuild this screen keeping the page stack. */
        val remode: (List<String>) -> Unit,
        /** Something the widget draws changed. */
        val widgetChanged: () -> Unit,
    )

    private val t = Tokens.of(act, store.theme)
    private val reduceMotion = Tokens.reduceMotion(act)
    private val aurora = Aurora(t.dark, reduceMotion)
    val root = FrameLayout(act)
    private val stack = ArrayList<Page>()
    private var state = UiState(null, false, null, 0, false)
    private var insetTop = 0
    private var insetBottom = 0
    private val d = act.resources.displayMetrics.density
    private fun px(v: Float) = (v * d).toInt()
    private val pt get() = Fmt.pt
    private fun tr(p: String, e: String) = if (pt) p else e

    val stackIds: List<String> get() = stack.map { it.id }

    fun show(initial: List<String>, s: UiState) {
        state = s
        Type.init(act)
        systemBars()
        root.setBackgroundColor(t.groupedBg)
        root.setOnApplyWindowInsetsListener { _, ins ->
            @Suppress("DEPRECATION")
            val top = ins.systemWindowInsetTop
            @Suppress("DEPRECATION")
            var bottom = ins.systemWindowInsetBottom
            if (Build.VERSION.SDK_INT >= 30) bottom = maxOf(ins.getInsets(WindowInsets.Type.navigationBars()).bottom, ins.getInsets(WindowInsets.Type.ime()).bottom)
            if (top != insetTop || bottom != insetBottom) { insetTop = top; insetBottom = bottom; stack.forEach { it.applyInsets() } }
            ins
        }
        act.setContentView(root)
        for (id in initial.ifEmpty { listOf("main") }) push(page(id), animate = false)
        update(s)
    }

    private fun page(id: String): Page = when (id) {
        "settings" -> SettingsPage()
        "icons" -> IconsPage()
        else -> MainPage()
    }

    fun update(s: UiState) {
        val pairedChanged = s.paired != state.paired
        state = s
        stack.forEach { it.update(s, pairedChanged) }
    }

    @Suppress("DEPRECATION")
    private fun systemBars() {
        val w = act.window
        w.statusBarColor = 0
        w.navigationBarColor = 0
        if (Build.VERSION.SDK_INT >= 29) { w.isNavigationBarContrastEnforced = false; w.isStatusBarContrastEnforced = false }
        if (Build.VERSION.SDK_INT >= 30) {
            w.setDecorFitsSystemWindows(false)
            w.decorView
            val light = android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or android.view.WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            w.insetsController?.setSystemBarsAppearance(if (t.dark) 0 else light, light)
        } else {
            var f = View.SYSTEM_UI_FLAG_LAYOUT_STABLE or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
            if (!t.dark) f = f or View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR
            w.decorView.systemUiVisibility = f
        }
    }

    // ---- navigation ---------------------------------------------------------------------------------------------

    /** Called before the activity replaces this screen: nothing of it may keep running or keep the back key. */
    fun dispose() {
        if (Build.VERSION.SDK_INT >= 33) (backCallback as? OnBackInvokedCallback)?.let { act.onBackInvokedDispatcher.unregisterOnBackInvokedCallback(it) }
        backCallback = null
        nav.snapTo(1f)
        stack.forEach { it.dispose() }
    }

    private var gesturing = false
    private var navTop: Page? = null
    private var navBelow: Page? = null
    private var popping = false
    private val nav = Spring(1f, reduceMotion) { v -> applyNav(v) }

    private fun applyNav(v: Float) {
        val w = root.width.toFloat().takeIf { it > 0 } ?: act.resources.displayMetrics.widthPixels.toFloat()
        navTop?.host?.translationX = (1f - v) * w
        navBelow?.host?.let { b ->
            b.translationX = -0.3f * w * v
            (b.foreground as? ColorDrawable)?.alpha = (v * 26).toInt()
        }
        if (popping && v <= 0.001f && nav.target == 0f) finishPop()
        if (!popping && !gesturing && v >= 0.999f && nav.target == 1f) navBelow?.host?.visibility = View.INVISIBLE
        else if (v < 0.999f) navBelow?.host?.visibility = View.VISIBLE
    }

    private fun push(p: Page, animate: Boolean = true) {
        if (popping) { nav.snapTo(0f); if (popping) finishPop() }   // a push while a pop is still settling: finish the pop first
        val below = stack.lastOrNull()
        stack += p
        root.addView(p.host, FrameLayout.LayoutParams(-1, -1))
        p.applyInsets()
        syncBack()
        if (below == null) return
        below.host.foreground = ColorDrawable(0xFF000000.toInt()).apply { alpha = 0 }
        navTop = p; navBelow = below; popping = false
        if (!animate) { nav.snapTo(1f); return }
        nav.snapTo(0f)
        nav.animateTo(1f, 0.5f, 1f)
    }

    private fun pop() {
        if (stack.size < 2 || popping) return
        navTop = stack[stack.size - 1]; navBelow = stack[stack.size - 2]
        navBelow!!.host.visibility = View.VISIBLE
        popping = true
        nav.animateTo(0f, 0.45f, 1f)
    }

    private fun finishPop() {
        val top = stack.removeAt(stack.lastIndex)
        root.removeView(top.host)
        popping = false
        navBelow?.host?.apply { translationX = 0f; foreground = null }
        navTop = null; navBelow = null
        syncBack()
        if (stack.size >= 2) { navTop = stack.last(); navBelow = stack[stack.size - 2] }
    }

    /** For Android < 13, where the activity forwards the back key. */
    fun handleBack(): Boolean { if (stack.size < 2) return false; pop(); return true }

    private var backCallback: Any? = null

    private fun syncBack() {
        if (Build.VERSION.SDK_INT < 33) return
        val dispatcher = act.onBackInvokedDispatcher
        if (stack.size > 1 && backCallback == null) {
            val cb: OnBackInvokedCallback = if (Build.VERSION.SDK_INT >= 34) backAnimation() else OnBackInvokedCallback { pop() }
            dispatcher.registerOnBackInvokedCallback(OnBackInvokedDispatcher.PRIORITY_DEFAULT, cb)
            backCallback = cb
        } else if (stack.size <= 1 && backCallback != null) {
            dispatcher.unregisterOnBackInvokedCallback(backCallback as OnBackInvokedCallback)
            backCallback = null
        }
    }

    /** Interactive back: the page follows the edge swipe, like the iOS swipe-to-go-back. */
    @RequiresApi(34)
    private fun backAnimation() = object : OnBackAnimationCallback {
        override fun onBackStarted(e: BackEvent) {
            if (stack.size < 2) return
            gesturing = true
            navTop = stack[stack.size - 1]; navBelow = stack[stack.size - 2]
            navBelow!!.host.visibility = View.VISIBLE
            if (navBelow!!.host.foreground == null) navBelow!!.host.foreground = ColorDrawable(0xFF000000.toInt()).apply { alpha = 26 }
        }
        override fun onBackProgressed(e: BackEvent) { if (!popping) nav.snapTo(1f - e.progress * 0.85f) }
        override fun onBackCancelled() { gesturing = false; nav.animateTo(1f, 0.4f, 1f) }
        override fun onBackInvoked() { gesturing = false; pop() }
    }

    // ---- pages --------------------------------------------------------------------------------------------------

    /** A navigable page: scrolling content, large title that hands over to an inline title, floating glass controls. */
    private abstract inner class Page(val id: String, title: String, withAurora: Boolean, back: Boolean) {
        val host = GlassHost(act, t, if (withAurora) aurora else null)
        val scroll = ScrollView(act).apply { isVerticalScrollBarEnabled = false; clipToPadding = false; isFillViewport = true }
        val list = LinearLayout(act).apply { orientation = LinearLayout.VERTICAL }
        val largeTitle = label(act, title, Type.Style.LargeTitle, t.label)
        private val inline = InlineTitle(act, title, t)
        private val backPane: GlassPane? = if (back) GlassPane(act, t, -1f).apply {
            addView(GlyphView(act, t.label, 20f) { c, x, y, s, col -> Glyphs.back(c, x - s * 0.04f, y, s, col) }, FrameLayout.LayoutParams(-1, -1))
            contentDescription = tr("Voltar", "Back")
            isClickable = true
            setOnClickListener { Haptics.tap(this); pop() }
        } else null

        init {
            scroll.addView(list, FrameLayout.LayoutParams(-1, -2))
            list.addView(largeTitle, LinearLayout.LayoutParams(-2, -2).apply { leftMargin = px(20f); rightMargin = px(20f) })
            host.addView(scroll, FrameLayout.LayoutParams(-1, -1))
            host.addView(inline, FrameLayout.LayoutParams(-1, px(44f), Gravity.TOP))
            backPane?.let { host.addView(it, FrameLayout.LayoutParams(px(44f), px(44f), Gravity.TOP or Gravity.START)) }
            scroll.setOnScrollChangeListener { _, _, y, _, _ ->
                val handover = (largeTitle.bottom - insetTop - px(48f)).toFloat()
                inline.alpha = smoothstep(handover, handover + px(12f), y.toFloat())
                host.invalidate()
            }
        }

        open fun applyInsets() {
            list.setPadding(0, insetTop + px(52f), 0, insetBottom + px(32f))
            (inline.layoutParams as FrameLayout.LayoutParams).topMargin = insetTop + px(4f)
            backPane?.let { (it.layoutParams as FrameLayout.LayoutParams).apply { topMargin = insetTop + px(4f); leftMargin = px(16f) } }
            host.edgeHeight = (insetTop + px(54f)).toFloat()
            host.requestLayout()
        }

        open fun update(s: UiState, pairedChanged: Boolean) {}
        open fun dispose() {}
    }

    private inner class MainPage : Page("main", Fmt.appTitle, withAurora = true, back = false) {
        private val subtitle = label(act, "", Type.Style.Subheadline, t.secondaryLabel)
        private val cards = LinearLayout(act).apply { orientation = LinearLayout.VERTICAL }
        private val footer = label(act, "", Type.Style.Footnote, t.secondaryLabel).apply { gravity = Gravity.CENTER }
        private val byId = HashMap<String, ProviderCard>()
        private var spin = 0f
        private var spinner: ValueAnimator? = null
        private val refreshGlyph = GlyphView(act, t.label, 20f) { c, x, y, s, col -> Glyphs.refresh(c, x, y, s, col, spin) }
        private val tools = GlassPane(act, t, -1f).apply {
            val row = LinearLayout(act).apply { orientation = LinearLayout.HORIZONTAL }
            fun button(v: View, desc: String, onTap: () -> Unit) = FrameLayout(act).apply {
                addView(v, FrameLayout.LayoutParams(-1, -1)); contentDescription = desc; isClickable = true
                setOnClickListener { Haptics.tap(this); onTap() }
            }
            row.addView(button(refreshGlyph, tr("Atualizar", "Refresh")) { actions.refresh() }, LinearLayout.LayoutParams(px(46f), -1))
            row.addView(button(GlyphView(act, t.label, 21f) { c, x, y, s, col -> Glyphs.gear(c, x, y, s, col) }, tr("Ajustes", "Settings")) { push(SettingsPage()) }, LinearLayout.LayoutParams(px(46f), -1))
            addView(row, FrameLayout.LayoutParams(-1, -1))
        }

        init {
            list.addView(subtitle, LinearLayout.LayoutParams(-1, -2).apply { leftMargin = px(20f); rightMargin = px(20f); topMargin = px(2f) })
            list.addView(cards, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(18f) })
            list.addView(footer, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(20f); leftMargin = px(32f); rightMargin = px(32f) })
            host.addView(tools, FrameLayout.LayoutParams(px(92f), px(44f), Gravity.TOP or Gravity.END))
        }

        override fun applyInsets() {
            super.applyInsets()
            (tools.layoutParams as FrameLayout.LayoutParams).apply { topMargin = insetTop + px(4f); rightMargin = px(16f) }
        }

        override fun update(s: UiState, pairedChanged: Boolean) {
            val now = System.currentTimeMillis()
            subtitle.text = when {
                !s.paired -> tr("Não pareado", "Not paired")
                s.busy -> Fmt.updating
                s.hostOnline == false -> Fmt.pcOffline + if (s.receivedAtMs > 0) " · " + Fmt.clock(s.receivedAtMs) else ""
                s.snapshot == null -> Fmt.waiting
                now - s.snapshot.generatedAtMs < 60_000 -> tr("Atualizado agora", "Updated just now")
                else -> tr("Atualizado às ", "Updated at ") + Fmt.clock(s.snapshot.generatedAtMs)
            }
            subtitle.setTextColor(if (s.paired && s.hostOnline == false) t.orange else t.secondaryLabel)
            setSpinning(s.busy)
            val ps = Renderer.visible(s)
            if (!s.paired || ps.isEmpty()) {
                byId.clear(); cards.removeAllViews()
                cards.addView(if (!s.paired) pairCard() else waitingCard(s), cardParams())
            } else {
                if (cards.childCount != ps.size || ps.any { byId[it.id] == null }) { cards.removeAllViews(); byId.keys.retainAll(ps.map { it.id }.toSet()) }
                ps.forEachIndexed { i, p ->
                    val card = byId.getOrPut(p.id) { ProviderCard(act, t) }
                    card.iconId = s.icons[p.id] ?: Icons.default(p.id)
                    card.showIcon = s.showIcons
                    card.nowMs = now
                    card.projector = if (store.projection) { pr, wd ->
                        val readAt = pr.fetchedAtMs ?: s.snapshot?.generatedAtMs ?: now
                        // a reading older than 15 min would project from the past: show "collecting" instead
                        if (System.currentTimeMillis() - readAt > 15 * 60_000L) null
                        else dev.usage.monitor.Projection.project(store.history(pr.id, wd.id), wd.percent ?: 0.0, wd.resetsAtMs, readAt)
                    } else null
                    card.provider = p
                    if (card.parent == null) cards.addView(card, cardParams(i))
                }
            }
            footer.text = store.pairing?.let { tr("Conectado a ", "Connected to ") + it.pcName } ?: ""
        }

        private fun cardParams(i: Int = 0) = LinearLayout.LayoutParams(-1, -2).apply { leftMargin = px(20f); rightMargin = px(20f); if (i > 0) topMargin = px(14f) }

        override fun dispose() { setSpinning(false) }

        private fun setSpinning(on: Boolean) {
            if (on && spinner == null && !reduceMotion) spinner = ValueAnimator.ofFloat(0f, (2 * Math.PI).toFloat()).apply {
                duration = 900; repeatCount = ValueAnimator.INFINITE; interpolator = LinearInterpolator()
                addUpdateListener { spin = it.animatedValue as Float; refreshGlyph.invalidate(); host.invalidate() }
                start()
            } else if (!on && spinner != null) { spinner?.cancel(); spinner = null; spin = 0f; refreshGlyph.invalidate() }
        }

        private fun platter(): LinearLayout = GlassBlockLayout(act, t.material, Metrics.cardRadius).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(20f), px(22f), px(20f), px(20f))
        }

        private fun pairCard(): View = platter().apply {
            addView(label(act, tr("Conecte ao seu PC", "Connect to your PC"), Type.Style.Title2, t.label))
            addView(label(act, Fmt.pairHelp, Type.Style.Body, t.secondaryLabel).apply { setPadding(0, px(8f), 0, px(20f)) })
            addView(ProminentButton(act, t, tr("Escanear código QR", "Scan QR Code")) { actions.scan() }, LinearLayout.LayoutParams(-1, px(50f)))
            addView(label(act, tr("Inserir código", "Enter Code"), Type.Style.Body, t.blue).apply {
                gravity = Gravity.CENTER; setPadding(0, px(14f), 0, px(2f)); isClickable = true
                setOnClickListener { Haptics.tap(this); push(SettingsPage()) }
            }, LinearLayout.LayoutParams(-1, -2))
        }

        private fun waitingCard(s: UiState): View = platter().apply {
            gravity = Gravity.CENTER_HORIZONTAL
            addView(label(act, if (s.hostOnline == false) Fmt.pcOffline else Fmt.waiting, Type.Style.Headline, t.label).apply { gravity = Gravity.CENTER })
            addView(label(act, tr("Abra o app no PC. Os limites aparecem aqui assim que ele responder.", "Open the app on your PC. Your limits show up here as soon as it answers."), Type.Style.Subheadline, t.secondaryLabel).apply { gravity = Gravity.CENTER; setPadding(0, px(6f), 0, 0) })
        }
    }

    private inner class SettingsPage : Page("settings", tr("Ajustes", "Settings"), withAurora = false, back = true) {
        private val body = LinearLayout(act).apply { orientation = LinearLayout.VERTICAL }
        private var codeInput: EditText? = null

        init { list.addView(body, LinearLayout.LayoutParams(-1, -2)); build() }

        override fun update(s: UiState, pairedChanged: Boolean) { if (pairedChanged) build() }

        fun build() {
            body.removeAllViews()
            // Appearance
            val modes = listOf("auto", "light", "dark")
            val seg = SegmentedControl(act, t, listOf(tr("Automático", "Automatic"), tr("Claro", "Light"), tr("Escuro", "Dark")), modes.indexOf(store.theme).coerceAtLeast(0)) { i ->
                if (store.theme != modes[i]) { store.theme = modes[i]; actions.widgetChanged(); root.postDelayed({ actions.remode(stackIds) }, if (reduceMotion) 0 else 380) }
            }
            body.addView(Section(act, t, tr("Aparência", "Appearance")).add(padded(seg)))

            val looks = listOf("cupertino" to "Cupertino", "glass" to "Liquid glass", "neu" to "Neumorphism", "mono" to "Mono", "cyber" to "Cyberpunk", "soft" to tr("Suave", "Soft"))
            val themeSection = Section(act, t, tr("Tema", "Theme"), tr("Cupertino usa vidro com refração em tempo real no app e o desfoque do papel de parede no widget. Gasta mais bateria que os outros temas.",
                "Cupertino renders real-time refracting glass in the app and wallpaper blur in the widget. It uses more battery than the other themes."))
            for ((id, name) in looks) themeSection.add(Row(act, t, name, accessory = Row.Accessory.Check, onTap = {
                if (id != "cupertino") { store.style = id; actions.widgetChanged(); actions.restyle() }
            }).apply { checked = id == "cupertino" })
            body.addView(themeSection)

            // Widget
            val glassModes = listOf("clear", "tinted")
            val wseg = SegmentedControl(act, t, listOf(tr("Transparente", "Clear"), tr("Tingido", "Tinted")), glassModes.indexOf(store.widgetGlass).coerceAtLeast(0)) { i ->
                if (store.widgetGlass != glassModes[i]) { store.widgetGlass = glassModes[i]; actions.widgetChanged() }
            }
            val widget = Section(act, t, "Widget", tr("O One UI desfoca o papel de parede atrás do widget. Transparente deixa as cores dele aparecerem; Tingido aumenta o contraste do texto. Anéis coloridos pintam cada IA com a sua cor, em qualquer estilo.",
                "One UI blurs your wallpaper behind the widget. Clear lets its colours through; Tinted adds contrast for the text. Coloured rings paint each AI in its own colour, in either style."))
            widget.add(padded(wseg))
            widget.add(Row(act, t, tr("Anéis coloridos", "Coloured Rings")).trailing(IosSwitch(act, t, store.widgetColorRings) { on -> store.widgetColorRings = on; actions.widgetChanged() }))
            if (store.pairing != null) widget.add(Row(act, t, tr("Adicionar à tela inicial", "Add to Home Screen"), titleColor = t.blue, onTap = { actions.pinWidget() }))
            body.addView(widget)

            // Icons
            val icons = Section(act, t, tr("Ícones", "Icons"))
            val iconsRow = Row(act, t, tr("Mostrar ícones", "Show Icons"))
            iconsRow.trailing(IosSwitch(act, t, store.showIcons) { on -> store.showIcons = on; actions.widgetChanged(); stack.firstOrNull()?.update(stateWithIcons(), false); build() })
            icons.add(iconsRow)
            if (store.showIcons) icons.add(Row(act, t, tr("Ícones dos provedores", "Provider Icons"), accessory = Row.Accessory.Chevron, onTap = { push(IconsPage()) }))
            body.addView(icons)

            // Beta: pace projection
            val beta = Section(act, t, tr("Laboratório", "Labs"), tr("Beta. Toque num anel do painel para ver em quanto tempo aquele limite acaba se você continuar no ritmo da última hora.",
                "Beta. Tap a ring on the dashboard to see when that limit runs out if you keep the pace of the last hour."))
            beta.add(Row(act, t, tr("Previsão de duração", "Usage Forecast")).trailing(IosSwitch(act, t, store.projection) { on -> store.projection = on; stack.firstOrNull()?.update(state, false) }))
            body.addView(beta)

            // Connection
            val p = store.pairing
            if (p != null) {
                val conn = Section(act, t, tr("Conexão", "Connection"))
                conn.add(Row(act, t, "PC", p.pcName))
                conn.add(Row(act, t, "Relay", p.relayUrl.removePrefix("wss://").removePrefix("ws://")))
                conn.add(Row(act, t, tr("Atualizar agora", "Refresh Now"), titleColor = t.blue, onTap = { actions.refresh() }))
                body.addView(conn)
                body.addView(Section(act, t, null).add(Row(act, t, tr("Desparear este celular", "Unpair This Phone"), titleColor = t.red, onTap = {
                    Alert.show(root, t, tr("Desparear do PC?", "Unpair from your PC?"), tr("Para voltar a ver os limites, gere um novo código no PC.", "To see your limits again, create a new code on the PC."),
                        tr("Cancelar", "Cancel"), tr("Desparear", "Unpair")) { actions.unpair() }
                })))
            } else {
                val conn = Section(act, t, tr("Parear com o PC", "Pair with Your PC"), Fmt.pairHelp)
                val input = EditText(act).apply {
                    hint = tr("Código (XXXXX-XXXXX-…)", "Code (XXXXX-XXXXX-…)")
                    Type.apply(this, Type.Style.Body, t.label); setHintTextColor(t.tertiaryLabel)
                    background = null; isSingleLine = true
                    inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
                    setPadding(px(16f), px(14f), px(16f), px(14f))
                }
                codeInput = input
                conn.add(input)
                conn.add(Row(act, t, tr("Escanear código QR", "Scan QR Code"), titleColor = t.blue, onTap = { actions.scan() }))
                conn.add(Row(act, t, tr("Colar código", "Paste Code"), titleColor = t.blue, onTap = {
                    val clip = (act.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).primaryClip?.getItemAt(0)?.text?.toString().orEmpty()
                    input.setText(clip)
                }))
                conn.add(Row(act, t, tr("Parear", "Pair"), titleColor = t.blue, titleWeight = 600, onTap = {
                    if (Pairing.parse(input.text.toString()) != null && actions.pair(input.text.toString())) { Haptics.confirm(input); pop() }
                }))
                body.addView(conn)
            }
            body.addView(label(act, "AI Usage " + versionName() + " · " + tr("Fonte Inter (OFL)", "Inter typeface (OFL)"), Type.Style.Footnote, t.tertiaryLabel).apply {
                gravity = Gravity.CENTER; setPadding(px(16f), px(28f), px(16f), 0)
            }, LinearLayout.LayoutParams(-1, -2))
        }

        private fun padded(v: View) = FrameLayout(act).apply { setPadding(px(12f), px(10f), px(12f), px(10f)); addView(v, FrameLayout.LayoutParams(-1, -2)) }
    }

    private fun stateWithIcons() = state.copy(icons = store.icons, showIcons = store.showIcons)

    private inner class IconsPage : Page("icons", tr("Ícones", "Icons"), withAurora = false, back = true) {
        private val body = LinearLayout(act).apply { orientation = LinearLayout.VERTICAL }
        init { list.addView(body, LinearLayout.LayoutParams(-1, -2)); build() }

        private fun build() {
            body.removeAllViews()
            for ((provider, name) in Icons.providers) {
                val sec = Section(act, t, name)
                val rows = ArrayList<Pair<String, Row>>()
                for (opt in Icons.options(provider)) {
                    val row = Row(act, t, opt.label, accessory = Row.Accessory.Check, leading = IconTile(act, t.accent(provider), opt.id), onTap = {
                        store.setIcon(provider, opt.id)
                        rows.forEach { (id, r) -> r.checked = id == opt.id }
                        actions.widgetChanged()
                        stack.firstOrNull()?.update(stateWithIcons(), false)
                    }).apply { checked = store.icon(provider) == opt.id }
                    rows += opt.id to row
                    sec.add(row, inset = 60f)
                }
                body.addView(sec)
            }
        }
    }

    private fun versionName(): String = try { act.packageManager.getPackageInfo(act.packageName, 0).versionName ?: "" } catch (_: Exception) { "" }
}

/** Inline navigation title: appears over the scroll-edge effect once the large title has scrolled away. */
@SuppressLint("ViewConstructor")
class InlineTitle(context: Context, text: String, t: Tokens) : TextView(context), Overlay {
    init {
        this.text = text
        Type.apply(this, Type.Style.Headline, t.label)
        gravity = Gravity.CENTER
        alpha = 0f
        maxLines = 1
        setPadding((72 * resources.displayMetrics.density).toInt(), 0, (72 * resources.displayMetrics.density).toInt(), 0)
    }
}

/** iOS 26 alert: dimmed backdrop, rounded platter, capsule buttons; scales in on a spring. */
object Alert {
    fun show(root: ViewGroup, t: Tokens, title: String, message: String, cancel: String, destructive: String, onConfirm: () -> Unit) {
        val ctx = root.context
        val d = ctx.resources.displayMetrics.density
        fun px(v: Float) = (v * d).toInt()
        val reduce = Tokens.reduceMotion(ctx)
        val overlay = FrameLayout(ctx).apply { setBackgroundColor(0); isClickable = true }
        val card = LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL
            background = SquircleDrawable(if (t.dark) 0xF22C2C2E.toInt() else 0xF7FFFFFF.toInt(), 34 * d)
            setPadding(px(22f), px(22f), px(22f), px(16f))
            elevation = 24 * d
            alpha = 0f
        }
        card.addView(label(ctx, title, Type.Style.Headline, t.label).apply { gravity = Gravity.CENTER }, LinearLayout.LayoutParams(-1, -2))
        card.addView(label(ctx, message, Type.Style.Footnote, t.secondaryLabel).apply { gravity = Gravity.CENTER; setPadding(0, px(6f), 0, px(18f)) }, LinearLayout.LayoutParams(-1, -2))
        val row = LinearLayout(ctx).apply { orientation = LinearLayout.HORIZONTAL }
        lateinit var dismiss: (Boolean) -> Unit
        fun btn(text: String, color: Int, weight: Int, onTap: () -> Unit) = FrameLayout(ctx).apply {
            background = SquircleDrawable(t.tertiaryFill, 100 * d)
            addView(label(ctx, text, Type.Style.Body, color, weight).apply { gravity = Gravity.CENTER }, FrameLayout.LayoutParams(-1, -1))
            isClickable = true; setOnClickListener { Haptics.tap(this); onTap() }
        }
        row.addView(btn(cancel, t.label, 500) { dismiss(false) }, LinearLayout.LayoutParams(0, px(48f), 1f))
        row.addView(btn(destructive, t.red, 600) { dismiss(true) }, LinearLayout.LayoutParams(0, px(48f), 1f).apply { leftMargin = px(10f) })
        card.addView(row, LinearLayout.LayoutParams(-1, -2))
        overlay.addView(card, FrameLayout.LayoutParams(minOf(px(300f), root.width - px(48f)), -2, Gravity.CENTER))
        root.addView(overlay, FrameLayout.LayoutParams(-1, -1))
        val dim = if (t.dark) 0x66 else 0x33
        val sp = Spring(0f, reduce) { v ->
            overlay.setBackgroundColor((((dim * v).toInt().coerceIn(0, 255)) shl 24))
            card.alpha = v.coerceIn(0f, 1f); val s = 1.12f - 0.12f * v; card.scaleX = s; card.scaleY = s
            if (v <= 0.001f && overlay.tag == "closing") root.removeView(overlay)
        }
        dismiss = { confirm -> overlay.tag = "closing"; sp.animateTo(0f, 0.25f, 1f); if (reduce) root.removeView(overlay); if (confirm) onConfirm() }
        sp.animateTo(1f, 0.35f, 0.82f)
    }
}
