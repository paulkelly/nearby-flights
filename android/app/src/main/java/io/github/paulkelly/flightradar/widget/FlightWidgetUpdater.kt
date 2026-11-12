package io.github.paulkelly.flightradar.widget

import android.content.Context
import androidx.glance.GlanceId
import androidx.glance.appwidget.GlanceAppWidgetManager
import io.github.paulkelly.flightradar.data.FlightRadarApi
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.notify.NearbyNotifier
import io.github.paulkelly.flightradar.push.NearestPush
import java.io.IOException
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.SerializationException
import retrofit2.HttpException

/**
 * Fetches the aircraft for a widget's location and redraws it. Called by [FlightWidgetWorker] (every 15 minutes, and
 * when tapped). Never throws for expected failures: they're stored as the widget's `error` and shown on it.
 */
private const val LOGO_WAIT_MS = 3_000L

class FlightWidgetUpdater(
    private val context: Context,
    private val api: FlightRadarApi,
    private val logos: AirlineLogoStore,
    private val notifier: NearbyNotifier,
) {

    suspend fun refreshAll() {
        for (glanceId in GlanceAppWidgetManager(context).getGlanceIds(FlightWidget::class.java)) {
            refresh(glanceId)
        }
    }

    /**
     * A saved location was edited: widgets watching it (they keep their own copy) take the new name, place and
     * radius, and refresh at once rather than at the next 15-minute run.
     */
    suspend fun locationChanged(location: SavedLocation) {
        val manager = GlanceAppWidgetManager(context)
        for (glanceId in manager.getGlanceIds(FlightWidget::class.java)) {
            if (FlightWidgetState.read(context, glanceId).location?.id != location.id) continue
            FlightWidgetState.setLocation(context, glanceId, WidgetLocation.from(location))
            FlightWidget().update(context, glanceId)
            FlightWidgetWorker.refreshNow(context, manager.getAppWidgetId(glanceId))
        }
    }

    /**
     * A "nearest" push arrived (step 9e): widgets showing its location take the pushed aircraft as their new snapshot,
     * with no fetch, unless they already have newer data. The logo is downloaded first if it's new to us, but only
     * briefly: a push gets a few seconds of CPU, and the badge is fine until the next refresh brings it.
     */
    suspend fun applyPush(push: NearestPush) {
        val snapshot = push.toSnapshot()
        val manager = GlanceAppWidgetManager(context)
        for (glanceId in manager.getGlanceIds(FlightWidget::class.java)) {
            val state = FlightWidgetState.read(context, glanceId)
            if (state.location?.id != push.locationId || !NearestPush.isNewer(push, state.snapshot)) continue
            snapshot.aircraft?.airlineCode?.let { code -> withTimeoutOrNull(LOGO_WAIT_MS) { logos.fetchIfMissing(code) } }
            FlightWidgetState.setSnapshot(context, glanceId, snapshot)
            FlightWidget().update(context, glanceId)
        }
    }

    /**
     * Fetches a location once for its lock-screen notification (its bell was just turned on, or it moved), whether
     * or not a widget shows it. Failures are ignored: the next push or refresh brings it.
     */
    suspend fun refreshNotification(location: SavedLocation) {
        try {
            val response = api.flights(location.lat, location.lon, location.radiusKm)
            notifier.show(location.id, snapshotOf(response, System.currentTimeMillis()))
        } catch (_: IOException) {
        } catch (_: HttpException) {
        } catch (_: SerializationException) {
        }
    }

    suspend fun refresh(glanceId: GlanceId) {
        val location = FlightWidgetState.read(context, glanceId).location ?: return // not set up yet
        val failure = try {
            val response = api.flights(location.lat, location.lon, location.radiusKm)
            val snapshot = snapshotOf(response, System.currentTimeMillis())
            // Before storing the snapshot, so the widget's next drawing already has the logo.
            snapshot.aircraft?.airlineCode?.let { logos.fetchIfMissing(it) }
            FlightWidgetState.setSnapshot(context, glanceId, snapshot)
            notifier.show(location.id, snapshot) // the lock-screen view, if this location has it on
            null
        } catch (_: IOException) {
            "No connection"
        } catch (e: HttpException) {
            when (e.code()) {
                429 -> "Too many requests"
                502, 503, 504 -> "Flight data unavailable"
                else -> "Couldn't refresh (${e.code()})"
            }
        } catch (_: SerializationException) {
            "Couldn't read the answer"
        }
        if (failure != null) FlightWidgetState.setError(context, glanceId, failure)
        FlightWidget().update(context, glanceId)
    }
}
