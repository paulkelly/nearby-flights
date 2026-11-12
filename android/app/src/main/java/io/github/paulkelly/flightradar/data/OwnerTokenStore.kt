package io.github.paulkelly.flightradar.data

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.first

/**
 * Where the app keeps its owner token: the key to its saved locations, like the web app's localStorage entry. An
 * interface so tests can use an in-memory one.
 */
interface OwnerTokenStore {
    suspend fun read(): String?
    suspend fun write(token: String)
    suspend fun clear()
}

/**
 * The token in DataStore: a small file in the app's private storage, which other apps can't read. DataStore reads and
 * writes off the main thread (the older SharedPreferences could block it). Android's Auto Backup includes it, so a
 * phone restored from a backup keeps its saved locations.
 */
class DataStoreOwnerTokenStore(private val dataStore: DataStore<Preferences>) : OwnerTokenStore {
    private val key = stringPreferencesKey("owner_token")

    override suspend fun read(): String? = dataStore.data.first()[key]

    override suspend fun write(token: String) {
        dataStore.edit { it[key] = token }
    }

    override suspend fun clear() {
        dataStore.edit { it.remove(key) }
    }
}
