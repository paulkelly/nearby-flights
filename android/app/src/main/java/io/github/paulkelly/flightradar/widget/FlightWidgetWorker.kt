package io.github.paulkelly.flightradar.widget

import android.appwidget.AppWidgetManager
import android.content.Context
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import io.github.paulkelly.flightradar.FlightRadarApp
import java.util.concurrent.TimeUnit

/**
 * Background refresh for the widgets, through WorkManager: Android's scheduler for work that must happen even if the
 * app isn't running. It persists across reboots and runs jobs when their conditions are met (here: a network).
 * Like a server's background job queue, but the OS decides the exact moment, to save battery. Periodic work runs at
 * most every 15 minutes: the reason pushes (9c–9e) are needed for "a plane is overhead now".
 */
class FlightWidgetWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val updater = (applicationContext as FlightRadarApp).container.widgetUpdater
        val appWidgetId = inputData.getInt(KEY_APP_WIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID)
        if (appWidgetId == AppWidgetManager.INVALID_APPWIDGET_ID) {
            updater.refreshAll()
            // The periodic run also renews the server's watch, at most daily (DeviceRepository decides).
            DeviceSyncWorker.syncSoon(applicationContext)
        } else {
            updater.refresh(GlanceAppWidgetManager(applicationContext).getGlanceIdBy(appWidgetId))
        }
        // Failures are shown on the widget; retrying sooner than the next period wouldn't help much.
        return Result.success()
    }

    companion object {
        private const val KEY_APP_WIDGET_ID = "appWidgetId"
        private const val PERIODIC_WORK = "flight-widget-refresh"

        /** Refresh every widget every 15 minutes (WorkManager's minimum), while there's a network. Idempotent. */
        fun schedulePeriodic(context: Context) {
            val request = PeriodicWorkRequestBuilder<FlightWidgetWorker>(15, TimeUnit.MINUTES)
                .setConstraints(Constraints(requiredNetworkType = NetworkType.CONNECTED))
                .build()
            // KEEP: already scheduled → leave it (UPDATE would restart the 15 minutes each time).
            WorkManager.getInstance(context)
                .enqueueUniquePeriodicWork(PERIODIC_WORK, ExistingPeriodicWorkPolicy.KEEP, request)
        }

        fun cancelPeriodic(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(PERIODIC_WORK)
        }

        /**
         * Refresh one widget now (tapped, or just set up). No network constraint: offline, it fails at once and the
         * widget says "No connection" instead of waiting silently. KEEP: tapping again while one runs doesn't start
         * a second.
         */
        fun refreshNow(context: Context, appWidgetId: Int) {
            val request = OneTimeWorkRequestBuilder<FlightWidgetWorker>()
                .setInputData(workDataOf(KEY_APP_WIDGET_ID to appWidgetId))
                .build()
            WorkManager.getInstance(context)
                .enqueueUniqueWork("flight-widget-refresh-$appWidgetId", ExistingWorkPolicy.KEEP, request)
        }
    }
}
