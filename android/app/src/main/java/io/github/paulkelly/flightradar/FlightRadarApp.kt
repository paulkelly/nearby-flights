package io.github.paulkelly.flightradar

import android.app.Application
import android.content.Context
import androidx.datastore.preferences.preferencesDataStore
import io.github.paulkelly.flightradar.data.DataStoreOwnerTokenStore
import io.github.paulkelly.flightradar.data.FlightRadarApi
import io.github.paulkelly.flightradar.data.LocationsRepository
import io.github.paulkelly.flightradar.location.CurrentLocation

/**
 * The app's process-wide object: Android creates it before any screen (named in AndroidManifest.xml). It owns the
 * [AppContainer], so everything shares one HTTP client and one token store, like singletons registered in .NET DI.
 */
class FlightRadarApp : Application() {
    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }
}

/** The app's settings file. A property delegate: one DataStore per file per process (two would corrupt it). */
private val Context.settings by preferencesDataStore(name = "settings")

/**
 * Builds the app's services by hand: dependency injection without a framework (Hilt/Koin would generate or look up
 * this wiring). Enough for an app this size, and nothing is hidden. `by lazy` creates each on first use.
 */
class AppContainer(context: Context) {
    private val appContext = context.applicationContext

    val api: FlightRadarApi by lazy { FlightRadarApi.create(BuildConfig.API_BASE_URL) }

    val locationsRepository: LocationsRepository by lazy {
        LocationsRepository(api, DataStoreOwnerTokenStore(appContext.settings))
    }

    val currentLocation: CurrentLocation by lazy { CurrentLocation(appContext) }
}
