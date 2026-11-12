package io.github.paulkelly.flightradar.widget

import android.content.Context
import androidx.glance.GlanceId
import androidx.glance.appwidget.GlanceAppWidgetManager
import io.github.paulkelly.flightradar.data.FlightRadarApi
import java.io.IOException
import kotlinx.serialization.SerializationException
import retrofit2.HttpException

/**
 * Fetches the aircraft for a widget's location and redraws it. Called by [FlightWidgetWorker] (every 15 minutes, and
 * when tapped). Never throws for expected failures: they're stored as the widget's `error` and shown on it.
 */
class FlightWidgetUpdater(
    private val context: Context,
    private val api: FlightRadarApi,
    private val logos: AirlineLogoStore,
) {

    suspend fun refreshAll() {
        for (glanceId in GlanceAppWidgetManager(context).getGlanceIds(FlightWidget::class.java)) {
            refresh(glanceId)
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
