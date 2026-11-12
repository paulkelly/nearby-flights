package io.github.paulkelly.flightradar.notify

import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import java.io.File
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

/** The real DataStore, on a file in a temporary folder. */
class NotifySettingsTest {
    @get:Rule
    val folder = TemporaryFolder()

    private fun TestScope.dataStore() = PreferenceDataStoreFactory.create(scope = backgroundScope) {
        File(folder.root, "settings.preferences_pb")
    }

    @Test
    fun set_onAndOff() = runTest {
        val settings = DataStoreNotifySettings(dataStore())

        settings.set("1", "Home", enabled = true)
        settings.set("2", "Work", enabled = true)
        settings.set("1", "Home", enabled = false)

        assertEquals(mapOf("2" to "Work"), settings.current())
    }

    @Test
    fun rename_onlyLocationsThatNotify() = runTest {
        val settings = DataStoreNotifySettings(dataStore())
        settings.set("1", "Home", enabled = true)

        settings.rename("1", "Gran's")
        settings.rename("2", "Work") // doesn't notify: stays off

        assertEquals(mapOf("1" to "Gran's"), settings.current())
    }

    @Test
    fun unreadable_startsEmpty() = runTest {
        val store = dataStore()
        store.edit { it[stringPreferencesKey("notify_locations")] = "{not json" }

        assertEquals(emptyMap<String, String>(), DataStoreNotifySettings(store).current())
    }
}
