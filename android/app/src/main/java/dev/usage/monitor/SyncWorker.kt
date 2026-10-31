package dev.usage.monitor

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.ForegroundInfo
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.OutOfQuotaPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import dev.usage.monitor.widget.UsageWidget
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.concurrent.TimeUnit

/** Fetches the latest snapshot from the PC through the relay and redraws the widgets. */
class SyncWorker(private val ctx: Context, params: WorkerParameters) : CoroutineWorker(ctx, params) {
    override suspend fun doWork(): Result {
        val store = Store(ctx)
        val pairing = store.pairing ?: return Result.success()
        val refresh = inputData.getBoolean("refresh", false)
        if (refresh) { store.busy = true; UsageWidget.updateAll(ctx) }
        try {
            val previous = store.snapshot?.generatedAtMs ?: 0
            val result = withContext(Dispatchers.IO) { Relay.fetch(pairing, refresh, previousGeneratedAt = previous) }
            when (result) {
                is Fetch.Ok -> { store.save(result.raw); store.hostOnline = true }
                Fetch.HostOffline -> store.hostOnline = false
                Fetch.Unreachable -> {} // keep the last known state; the widget shows the cached data
            }
        } finally {
            store.busy = false
            UsageWidget.updateAll(ctx)
        }
        return Result.success()
    }

    override suspend fun getForegroundInfo(): ForegroundInfo {
        val nm = ctx.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel("sync", Fmt.appTitle, NotificationManager.IMPORTANCE_MIN))
        val n = android.app.Notification.Builder(ctx, "sync").setSmallIcon(R.drawable.ic_stat).setContentTitle(Fmt.updating).build()
        return ForegroundInfo(1, n)
    }

    companion object {
        private val net = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()

        fun schedulePeriodic(ctx: Context) {
            val req = PeriodicWorkRequestBuilder<SyncWorker>(15, TimeUnit.MINUTES).setConstraints(net).build()
            WorkManager.getInstance(ctx).enqueueUniquePeriodicWork("periodic", ExistingPeriodicWorkPolicy.KEEP, req)
        }

        fun cancelPeriodic(ctx: Context) { WorkManager.getInstance(ctx).cancelUniqueWork("periodic") }

        fun refreshNow(ctx: Context) {
            val store = Store(ctx)
            if (store.pairing == null) return
            store.busy = true
            UsageWidget.updateAll(ctx)
            val req = OneTimeWorkRequestBuilder<SyncWorker>().setConstraints(net).setInputData(workDataOf("refresh" to true))
                .setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST).build()
            WorkManager.getInstance(ctx).enqueueUniqueWork("refresh", ExistingWorkPolicy.REPLACE, req)
        }
    }
}
