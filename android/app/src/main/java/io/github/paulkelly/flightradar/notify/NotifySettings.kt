package io.github.paulkelly.flightradar.notify

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import io.github.paulkelly.flightradar.data.FlightRadarApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

/**
 * Which saved locations show the lock-screen notification (step 9e), with their names: a location can notify without
 * a widget, and a push carries only the location's ID. An interface so tests can use an in-memory one.
 */
interface NotifySettings {
    /** Location ID → name, for every location that notifies. Updates as it changes (for the app's list). */
    val locations: Flow<Map<String, String>>

    suspend fun current(): Map<String, String> = locations.first()

    /** Turns a location's notification on or off. */
    suspend fun set(locationId: String, name: String, enabled: Boolean)

    /** Keeps the stored name in step after a rename. Does nothing for locations that don't notify. */
    suspend fun rename(locationId: String, name: String)
}

/**
 * Kept in the app's `settings` DataStore (backed up with the owner token: a restored phone keeps both), as one JSON
 * map: small, and changed only when a bell is tapped.
 */
class DataStoreNotifySettings(private val dataStore: DataStore<Preferences>) : NotifySettings {
    private val key = stringPreferencesKey("notify_locations")
    private val json = FlightRadarApi.json

    override val locations: Flow<Map<String, String>> = dataStore.data.map { decode(it[key]) }

    override suspend fun set(locationId: String, name: String, enabled: Boolean) = edit {
        if (enabled) it + (locationId to name) else it - locationId
    }

    override suspend fun rename(locationId: String, name: String) = edit {
        if (locationId in it) it + (locationId to name) else it
    }

    private suspend fun edit(change: (Map<String, String>) -> Map<String, String>) {
        dataStore.edit { preferences -> preferences[key] = json.encodeToString(change(decode(preferences[key]))) }
    }

    private fun decode(text: String?): Map<String, String> =
        try {
            text?.let { json.decodeFromString<Map<String, String>>(it) } ?: emptyMap()
        } catch (_: IllegalArgumentException) { // unreadable (SerializationException is one): start again
            emptyMap()
        }
}
