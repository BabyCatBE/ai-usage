package dev.usage.monitor.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.res.Configuration
import android.os.Bundle
import android.widget.RemoteViews
import dev.usage.monitor.Fmt
import dev.usage.monitor.MainActivity
import dev.usage.monitor.R
import dev.usage.monitor.Renderer
import dev.usage.monitor.Store
import dev.usage.monitor.SyncWorker
import dev.usage.monitor.UiState
import dev.usage.monitor.cupertino.CupertinoWidget
import dev.usage.monitor.cupertino.Type

class UsageWidget : AppWidgetProvider() {
    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        if (intent.action == ACTION_REFRESH) SyncWorker.refreshNow(context)
    }

    override fun onUpdate(context: Context, manager: AppWidgetManager, ids: IntArray) {
        ids.forEach { update(context, manager, it) }
        SyncWorker.schedulePeriodic(context)
    }

    override fun onAppWidgetOptionsChanged(context: Context, manager: AppWidgetManager, id: Int, options: Bundle) = update(context, manager, id)
    override fun onEnabled(context: Context) = SyncWorker.schedulePeriodic(context)
    override fun onDisabled(context: Context) = SyncWorker.cancelPeriodic(context)

    companion object {
        const val ACTION_REFRESH = "dev.usage.monitor.REFRESH"

        fun updateAll(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            manager.getAppWidgetIds(ComponentName(context, UsageWidget::class.java)).forEach { update(context, manager, it) }
        }

        fun stateOf(store: Store) = UiState(store.snapshot, store.pairing != null, store.hostOnline, store.receivedAtMs, store.busy, store.icons, store.showIcons)

        fun update(context: Context, manager: AppWidgetManager, id: Int) {
            val opts = manager.getAppWidgetOptions(id)
            val density = context.resources.displayMetrics.density
            val wDp = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 250).coerceAtLeast(110)
            val hDp = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MAX_HEIGHT, 400).coerceAtLeast(110)
            // Keep the bitmap well under the RemoteViews memory limit.
            val k = minOf(1f, Math.sqrt(1_800_000.0 / (wDp * density * hDp * density)).toFloat())
            val store = Store(context)
            val state = stateOf(store)
            val now = System.currentTimeMillis()
            val pal = store.palette(context)
            val wPx = (wDp * density * k).toInt(); val hPx = (hDp * density * k).toInt()
            val bmp = if (pal.cupertino) {
                Type.init(context)
                val corner = (opts.get("semShapeRadius") as? Number)?.toFloat()?.takeIf { it > 0f } ?: 24f
                // One UI scales widgets down (hsResizeRatio ~0.83): lay out for the size it is really shown at, so text keeps its point size.
                val ratio = (opts.get("hsResizeRatio") as? Number)?.toFloat()?.takeIf { it in 0.5f..1f } ?: 1f
                CupertinoWidget.render(state, wPx, hPx, density * k / ratio, pal.dark, now, store.widgetGlass, corner * ratio)
            } else Renderer.render(state, wPx, hPx, density * k, pal, now)

            val views = RemoteViews(context.packageName, if (pal.cupertino) R.layout.widget_cupertino else R.layout.widget)
            // One UI Home blurs the wallpaper under @android:id/background when its alpha is 1-254.
            if (pal.cupertino) views.setInt(android.R.id.background, "setBackgroundColor", CupertinoWidget.launcherTint(pal.dark, store.widgetGlass, CupertinoWidget.lightWallpaper(context)))
            views.setImageViewBitmap(R.id.image, bmp)
            views.setContentDescription(R.id.image, summary(state))
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            views.setOnClickPendingIntent(R.id.image, PendingIntent.getActivity(context, 0, Intent(context, MainActivity::class.java), flags))
            val refresh = Intent(context, UsageWidget::class.java).setAction(ACTION_REFRESH)
            views.setOnClickPendingIntent(R.id.refresh, if (state.paired) PendingIntent.getBroadcast(context, 1, refresh, flags)
                else PendingIntent.getActivity(context, 0, Intent(context, MainActivity::class.java), flags))
            manager.updateAppWidget(id, views)
        }

        private fun summary(s: UiState) = (s.snapshot?.providers ?: emptyList()).filter { !it.hidden }.joinToString(". ") { p ->
            p.name + ": " + p.windows.filter { it.percent != null }.joinToString(", ") { Fmt.windowName(it.id, it.label) + " " + Fmt.percent(it.percent) }
        }.ifEmpty { Fmt.appTitle }
    }
}
