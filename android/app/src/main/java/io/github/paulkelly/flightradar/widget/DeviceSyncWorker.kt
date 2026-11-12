package io.github.paulkelly.flightradar.widget

import android.content.Context
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import io.github.paulkelly.flightradar.FlightRadarApp
import io.github.paulkelly.flightradar.data.ApiException
import java.io.IOException
import java.util.concurrent.TimeUnit

/**
 * Tells the server which saved locations this phone's widgets show (step 9c), and which have the lock-screen
 * notification on (step 9e), so it watches them around the clock.
 * A WorkManager job rather than a call on the spot: it's started from places that can't wait for the network (a
 * widget being removed is a broadcast with seconds to live), it waits for a connection, and it retries with backoff
 * if the server can't be reached. It reads the widgets' locations when it runs, so it always sends the latest set.
 */
class DeviceSyncWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val repository = (applicationContext as FlightRadarApp).container.deviceRepository
        return try {
            repository.sync(watchedLocationIds(applicationContext))
            Result.success()
        } catch (_: IOException) {
            Result.retry()
        } catch (e: ApiException) {
            // 409 (a clash, sending again sorts it), 429 and 5xx are worth another go; anything else isn't. WorkManager
            // records the outcome (adb shell dumpsys jobscheduler), so no log of our own.
            if (e.status == 409 || e.status == 429 || e.status >= 500) Result.retry() else Result.failure()
        }
    }

    companion object {
        private const val WORK_NAME = "device-sync"
        private const val PERIODIC_WORK_NAME = "device-sync-daily"

        /**
         * Syncs once there's a network. REPLACE: a newer request supersedes a waiting one (it reads the widgets when it
         * runs anyway), so a burst of changes makes one request.
         */
        fun syncSoon(context: Context) {
            val request = OneTimeWorkRequestBuilder<DeviceSyncWorker>()
                .setConstraints(Constraints(requiredNetworkType = NetworkType.CONNECTED))
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 1, TimeUnit.MINUTES)
                .build()
            WorkManager.getInstance(context).enqueueUniqueWork(WORK_NAME, ExistingWorkPolicy.REPLACE, request)
        }

        /**
         * Once a day, while there's a network: DeviceRepository then tells the server this device is still here (if
         * anything is watched; otherwise nothing is sent). Before 9e this rode on the widgets' 15-minute refresh, which
         * only runs while there are widgets. KEEP: scheduling again (each app start) doesn't restart the day.
         */
        fun schedulePeriodic(context: Context) {
            val request = PeriodicWorkRequestBuilder<DeviceSyncWorker>(1, TimeUnit.DAYS)
                .setConstraints(Constraints(requiredNetworkType = NetworkType.CONNECTED))
                .build()
            WorkManager.getInstance(context)
                .enqueueUniquePeriodicWork(PERIODIC_WORK_NAME, ExistingPeriodicWorkPolicy.KEEP, request)
        }

        /**
         * The saved locations the server should watch for this device: those its home-screen widgets show (widgets
         * not set up yet have none) and those with the lock-screen notification on (step 9e).
         */
        private suspend fun watchedLocationIds(context: Context): Set<String> {
            val widgets = GlanceAppWidgetManager(context).getGlanceIds(FlightWidget::class.java)
                .mapNotNull { FlightWidgetState.read(context, it).location?.id }
            val notifying = (context.applicationContext as FlightRadarApp).container.notifySettings.current().keys
            return widgets.toSet() + notifying
        }
    }
}
