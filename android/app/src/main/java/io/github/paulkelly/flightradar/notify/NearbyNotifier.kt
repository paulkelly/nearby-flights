package io.github.paulkelly.flightradar.notify

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import io.github.paulkelly.flightradar.MainActivity
import io.github.paulkelly.flightradar.R
import io.github.paulkelly.flightradar.widget.AirlineLogoStore
import io.github.paulkelly.flightradar.widget.WidgetSnapshot
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * The lock-screen view (step 9e): one quiet, ongoing notification per location that has it turned on, updated in
 * place as the nearest aircraft changes and removed when nothing is overhead. Fed by pushes and by the widget's own
 * refreshes, through [show], so the widget and the notification always show the same data.
 *
 * Quiet: its own channel at low importance (no sound, no vibration, no pop-up); the user can change that in the
 * system's settings for the channel. Each notification is tagged with its location's ID, so updates replace it.
 */
class NearbyNotifier(
    private val context: Context,
    private val logos: AirlineLogoStore,
    private val settings: NotifySettings,
) {
    private val manager = NotificationManagerCompat.from(context)

    /** Creates the channel (Android keeps it; doing this again changes nothing). Called when the app starts. */
    fun createChannel() {
        val channel = NotificationChannel(CHANNEL_ID, "Nearby aircraft", NotificationManager.IMPORTANCE_LOW).apply {
            description = "The aircraft nearest your saved locations, updated as they change"
            setShowBadge(false)
        }
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    /**
     * Shows [snapshot] for a location: posts or updates its notification, or removes it when nothing is overhead.
     * Does nothing for a location without notifications on, or without the permission, or when the notification
     * already shows newer data (a push and a refresh can arrive in either order).
     */
    suspend fun show(locationId: String, snapshot: WidgetSnapshot) {
        val name = settings.current()[locationId]
        if (name == null) {
            cancel(locationId) // turned off (or never on): make sure none is left
            return
        }
        if (!hasPermission()) return
        if (shownAtMs(locationId)?.let { snapshot.updatedAtMs < it } == true) return

        val content = nearbyNotificationContent(name, snapshot)
        if (content == null) {
            cancel(locationId)
            return
        }
        val logo = snapshot.aircraft?.airlineCode?.let { code ->
            withTimeoutOrNull(LOGO_WAIT_MS) { logos.fetchIfMissing(code) }
            withContext(Dispatchers.IO) { logos.load(code) }
        }

        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_flight)
            .setColor(APP_BLUE) // the circle behind the small icon, where Android draws one: the app icon's blue
            .setLargeIcon(logo)
            .setSubText(content.header)
            .setContentTitle(content.title)
            .setContentText(content.text)
            .setWhen(snapshot.updatedAtMs) // when the data is from; also how newer and older updates are told apart
            .setShowWhen(true)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC) // all of it on the lock screen (decided in 9e)
            // A safety net: a "clear skies" push can be lost (2-minute TTL while offline), and a stale aircraft must
            // not stay up for ever. Every update starts the 20 minutes again.
            .setTimeoutAfter(TIMEOUT_MS)
            .setContentIntent(openApp())
            .build()
        try {
            manager.notify(locationId, NOTIFICATION_ID, notification)
        } catch (_: SecurityException) {
            // The permission was withdrawn between the check and now: nothing to show.
        }
    }

    /** Removes a location's notification (turned off, deleted, or nothing overhead). */
    fun cancel(locationId: String) = manager.cancel(locationId, NOTIFICATION_ID)

    /** When the data in a location's notification is from, or null if none is showing. */
    private fun shownAtMs(locationId: String): Long? =
        context.getSystemService(NotificationManager::class.java).activeNotifications
            .firstOrNull { it.tag == locationId && it.id == NOTIFICATION_ID }
            ?.notification?.`when`

    /** Android 13+ asks the user; older versions allow notifications unless they're switched off in settings. */
    private fun hasPermission(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED

    private fun openApp(): PendingIntent = PendingIntent.getActivity(
        context,
        0,
        Intent(context, MainActivity::class.java),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private companion object {
        const val CHANNEL_ID = "nearby-aircraft"

        /** The same for every location: the tag (the location's ID) tells them apart. */
        const val NOTIFICATION_ID = 1
        const val TIMEOUT_MS = 20 * 60 * 1000L
        const val LOGO_WAIT_MS = 3_000L

        /** The app icon's blue (ic_launcher_background, web/public/logo.svg). */
        const val APP_BLUE = 0xFF1565C0.toInt()
    }
}
