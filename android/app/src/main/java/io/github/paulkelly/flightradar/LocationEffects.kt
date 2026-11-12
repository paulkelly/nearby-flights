package io.github.paulkelly.flightradar

import android.content.Context
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.notify.NearbyNotifier
import io.github.paulkelly.flightradar.notify.NotifySettings
import io.github.paulkelly.flightradar.widget.DeviceSyncWorker
import io.github.paulkelly.flightradar.widget.FlightWidgetUpdater
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

/**
 * What else has to happen when the user changes a saved location in the app: widgets keep their own copy, the
 * notification keeps the name, and the server watches what this device registered. One place for those knock-on
 * effects, so the screen's ViewModel only says what the user did. An interface so it can be left out (a preview,
 * tests).
 */
interface LocationEffects {
    /** IDs of the locations with the lock-screen notification on; updates as it changes. */
    val notifying: Flow<Set<String>>

    suspend fun edited(location: SavedLocation)

    suspend fun deleted(location: SavedLocation)

    suspend fun setNotifying(location: SavedLocation, enabled: Boolean)
}

class AppLocationEffects(
    private val context: Context,
    private val settings: NotifySettings,
    private val notifier: NearbyNotifier,
    private val widgets: FlightWidgetUpdater,
) : LocationEffects {
    override val notifying: Flow<Set<String>> = settings.locations.map { it.keys }

    /** Renamed, moved or resized: widgets take the new copy; the notification its new name and a fresh look. */
    override suspend fun edited(location: SavedLocation) {
        widgets.locationChanged(location)
        settings.rename(location.id, location.name)
        if (location.id in settings.current()) widgets.refreshNotification(location)
    }

    /** Gone: no notification for it, and the server can stop watching it (if no widget still shows it). */
    override suspend fun deleted(location: SavedLocation) {
        if (location.id in settings.current()) {
            settings.set(location.id, location.name, enabled = false)
            notifier.cancel(location.id)
            DeviceSyncWorker.syncSoon(context)
        }
    }

    /**
     * The bell. On: the server starts watching it (pushes from now on), and a fetch shows the notification at once if
     * something is overhead, rather than at the next change. Off: the notification goes.
     */
    override suspend fun setNotifying(location: SavedLocation, enabled: Boolean) {
        settings.set(location.id, location.name, enabled)
        DeviceSyncWorker.syncSoon(context)
        if (enabled) widgets.refreshNotification(location) else notifier.cancel(location.id)
    }
}
