package io.github.paulkelly.flightradar.widget

import android.content.Context
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.glance.GlanceId
import androidx.glance.appwidget.state.getAppWidgetState
import androidx.glance.appwidget.state.updateAppWidgetState
import androidx.glance.state.PreferencesGlanceStateDefinition
import io.github.paulkelly.flightradar.data.FlightRadarApi

/** Everything [FlightWidgetContent] draws. Built from the widget's stored state each time it renders. */
data class FlightWidgetUiState(
    /** What the widget watches; null until it's set up (choose a location). */
    val location: WidgetLocation?,
    /** The latest data; null before the first refresh finishes. */
    val snapshot: WidgetSnapshot?,
    /** Why the latest refresh failed ("No connection"), or null. `snapshot` is then older data, still worth showing. */
    val error: String?,
    /** A refresh was asked for and hasn't finished. */
    val refreshing: Boolean,
)

/**
 * Each widget's state: a small Preferences file per widget that Glance keeps for us (its "state definition"), like
 * component state that survives the process being killed. A widget isn't a running program: Android draws a
 * snapshot of it and our code only runs to update that snapshot. So everything it shows must be stored here.
 */
object FlightWidgetState {
    private val LOCATION = stringPreferencesKey("location")
    private val SNAPSHOT = stringPreferencesKey("snapshot")
    private val ERROR = stringPreferencesKey("error")
    private val REFRESH_REQUESTED_AT = longPreferencesKey("refresh_requested_at")

    /** A refresh still running after this long has died (process killed): stop showing it as running. */
    private const val REFRESH_GIVE_UP_MS = 60_000L

    private val json = FlightRadarApi.json

    fun uiState(preferences: Preferences, nowMs: Long): FlightWidgetUiState {
        val requestedAt = preferences[REFRESH_REQUESTED_AT]
        return FlightWidgetUiState(
            location = preferences[LOCATION]?.let { decodeOrNull<WidgetLocation>(it) },
            snapshot = preferences[SNAPSHOT]?.let { decodeOrNull<WidgetSnapshot>(it) },
            error = preferences[ERROR],
            refreshing = requestedAt != null && nowMs - requestedAt < REFRESH_GIVE_UP_MS,
        )
    }

    suspend fun read(context: Context, glanceId: GlanceId): FlightWidgetUiState =
        uiState(getAppWidgetState(context, PreferencesGlanceStateDefinition, glanceId), System.currentTimeMillis())

    /** Watch a (new) location: forget the old one's data and mark a refresh as started. */
    suspend fun setLocation(context: Context, glanceId: GlanceId, location: WidgetLocation) = edit(context, glanceId) {
        it[LOCATION] = json.encodeToString(location)
        it.remove(SNAPSHOT)
        it.remove(ERROR)
        it[REFRESH_REQUESTED_AT] = System.currentTimeMillis()
    }

    suspend fun markRefreshing(context: Context, glanceId: GlanceId) = edit(context, glanceId) {
        it[REFRESH_REQUESTED_AT] = System.currentTimeMillis()
    }

    suspend fun setSnapshot(context: Context, glanceId: GlanceId, snapshot: WidgetSnapshot) = edit(context, glanceId) {
        it[SNAPSHOT] = json.encodeToString(snapshot)
        it.remove(ERROR)
        it.remove(REFRESH_REQUESTED_AT)
    }

    /** The refresh failed: keep the last snapshot (older data beats none) and say why. */
    suspend fun setError(context: Context, glanceId: GlanceId, error: String) = edit(context, glanceId) {
        it[ERROR] = error
        it.remove(REFRESH_REQUESTED_AT)
    }

    private suspend fun edit(context: Context, glanceId: GlanceId, change: (MutablePreferences) -> Unit) {
        updateAppWidgetState(context, glanceId) { change(it) }
    }

    /** Stored by an older version of the app in a shape this one can't read: treat it as missing. */
    private inline fun <reified T> decodeOrNull(text: String): T? =
        try {
            json.decodeFromString<T>(text)
        } catch (_: IllegalArgumentException) { // SerializationException is one
            null
        }
}
