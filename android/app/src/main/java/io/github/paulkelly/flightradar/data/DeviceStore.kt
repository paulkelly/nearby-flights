package io.github.paulkelly.flightradar.data

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.core.stringSetPreferencesKey
import java.util.UUID
import kotlinx.coroutines.flow.first

/** What this device last told the server: which locations, and when (epoch ms). */
data class Registration(val locationIds: Set<String>, val atMs: Long)

/**
 * This install's identity for the server (step 9c) and what it last registered. An interface so tests can use an
 * in-memory one.
 */
interface DeviceStore {
    /** The installation ID, made (a random UUID) the first time it's asked for. */
    suspend fun installationId(): String

    /** Replaces the installation ID with a new one, and returns it. */
    suspend fun newInstallationId(): String

    suspend fun lastRegistration(): Registration?

    suspend fun setLastRegistration(registration: Registration)
}

/**
 * Kept in its own DataStore file, `device`, which backup_rules.xml and data_extraction_rules.xml leave out of
 * backups: a phone restored from a backup must register as a new device, not take over the old phone's watches (and,
 * in 9d, its pushes). The owner token, in `settings`, is backed up: the restored phone keeps the saved locations.
 */
class DataStoreDeviceStore(private val dataStore: DataStore<Preferences>) : DeviceStore {
    private val installationIdKey = stringPreferencesKey("installation_id")
    private val registeredIdsKey = stringSetPreferencesKey("registered_location_ids")
    private val registeredAtKey = longPreferencesKey("registered_at")

    // edit is atomic and returns what it wrote: two callers at once can't make two different IDs.
    override suspend fun installationId(): String =
        dataStore.data.first()[installationIdKey]
            ?: dataStore.edit {
                if (it[installationIdKey] == null) it[installationIdKey] = UUID.randomUUID().toString()
            }[installationIdKey]!!

    override suspend fun newInstallationId(): String {
        val id = UUID.randomUUID().toString()
        dataStore.edit { it[installationIdKey] = id }
        return id
    }

    override suspend fun lastRegistration(): Registration? {
        val preferences = dataStore.data.first()
        val ids = preferences[registeredIdsKey] ?: return null
        return Registration(ids, preferences[registeredAtKey] ?: 0)
    }

    override suspend fun setLastRegistration(registration: Registration) {
        dataStore.edit {
            it[registeredIdsKey] = registration.locationIds
            it[registeredAtKey] = registration.atMs
        }
    }
}
