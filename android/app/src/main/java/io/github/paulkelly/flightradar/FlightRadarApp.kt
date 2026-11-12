package io.github.paulkelly.flightradar

import android.app.Application
import android.content.Context
import androidx.datastore.preferences.preferencesDataStore
import io.github.paulkelly.flightradar.data.DataStoreDeviceStore
import io.github.paulkelly.flightradar.data.DataStoreOwnerTokenStore
import io.github.paulkelly.flightradar.data.DeviceRepository
import io.github.paulkelly.flightradar.data.DeviceStore
import io.github.paulkelly.flightradar.data.FlightRadarApi
import io.github.paulkelly.flightradar.data.LocationsRepository
import io.github.paulkelly.flightradar.location.CurrentLocation
import io.github.paulkelly.flightradar.notify.DataStoreNotifySettings
import io.github.paulkelly.flightradar.notify.NearbyNotifier
import io.github.paulkelly.flightradar.notify.NotifySettings
import io.github.paulkelly.flightradar.push.FlightRadarMessagingService
import io.github.paulkelly.flightradar.widget.AirlineLogoStore
import io.github.paulkelly.flightradar.widget.FlightWidgetUpdater
import java.io.File

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
        container.nearbyNotifier.createChannel()
        // Firebase's address for pushes (step 9d): cheap when it already has one, and it may have changed.
        FlightRadarMessagingService.register(this)
    }
}

/** The app's settings file. A property delegate: one DataStore per file per process (two would corrupt it). */
private val Context.settings by preferencesDataStore(name = "settings")

/** This install's identity for the server. Its own file because backups leave it out (see DataStoreDeviceStore). */
private val Context.device by preferencesDataStore(name = "device")

/**
 * Builds the app's services by hand: dependency injection without a framework (Hilt/Koin would generate or look up
 * this wiring). Enough for an app this size, and nothing is hidden. `by lazy` creates each on first use.
 */
class AppContainer(context: Context) {
    private val appContext = context.applicationContext

    val api: FlightRadarApi by lazy { FlightRadarApi.create(BuildConfig.API_BASE_URL) }

    private val ownerTokens by lazy { DataStoreOwnerTokenStore(appContext.settings) }

    val locationsRepository: LocationsRepository by lazy { LocationsRepository(api, ownerTokens) }

    val deviceStore: DeviceStore by lazy { DataStoreDeviceStore(appContext.device) }

    val deviceRepository: DeviceRepository by lazy { DeviceRepository(api, ownerTokens, deviceStore) }

    val currentLocation: CurrentLocation by lazy { CurrentLocation(appContext) }

    val airlineLogos: AirlineLogoStore by lazy { AirlineLogoStore(File(appContext.cacheDir, "airline-logos"), api) }

    val notifySettings: NotifySettings by lazy { DataStoreNotifySettings(appContext.settings) }

    val nearbyNotifier: NearbyNotifier by lazy { NearbyNotifier(appContext, airlineLogos, notifySettings) }

    val widgetUpdater: FlightWidgetUpdater by lazy {
        FlightWidgetUpdater(appContext, api, airlineLogos, nearbyNotifier)
    }
}
