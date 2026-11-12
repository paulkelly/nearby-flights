package io.github.paulkelly.flightradar.push

import android.annotation.SuppressLint
import android.content.Context
import androidx.glance.appwidget.GlanceAppWidgetManager
import com.google.firebase.FirebaseApp
import com.google.firebase.messaging.FirebaseMessaging
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import io.github.paulkelly.flightradar.FlightRadarApp
import io.github.paulkelly.flightradar.widget.DeviceSyncWorker
import io.github.paulkelly.flightradar.widget.FlightWidget
import io.github.paulkelly.flightradar.widget.FlightWidgetState
import io.github.paulkelly.flightradar.widget.FlightWidgetWorker
import kotlinx.coroutines.runBlocking

/**
 * Where FCM delivers (step 9d). Android starts this service when a push arrives, even with the app closed, and gives
 * it a few seconds; like a server's webhook handler, it should hand longer work to a job.
 *
 * Step 9d only refreshes the widgets showing the location the push is about (a fetch, through WorkManager). Step 9e
 * uses the aircraft in the push directly and adds the notification.
 *
 * No onNewToken, despite lint's MissingFirebaseInstanceTokenRefresh (a check from before installation IDs): with
 * firebase_messaging_installation_id_enabled the address is the installation ID, and its changes arrive in
 * [onRegistered]. onNewToken belongs to the deprecated token API.
 */
@SuppressLint("MissingFirebaseInstanceTokenRefresh")
class FlightRadarMessagingService : FirebaseMessagingService() {

    /**
     * Firebase gave this device its address (Firebase Installation ID): first time, or a new one (the SDK notices
     * changes and calls this again). The server needs it to push, so register again when it changed.
     */
    override fun onRegistered(installationId: String) {
        val store = (application as FlightRadarApp).container.deviceStore
        // onMessageReceived/onRegistered run on a background thread, so blocking briefly for DataStore is fine.
        val changed = runBlocking {
            if (store.pushToken() == installationId) false else {
                store.setPushToken(installationId)
                true
            }
        }
        if (changed) DeviceSyncWorker.syncSoon(this)
    }

    override fun onMessageReceived(message: RemoteMessage) {
        if (message.data["type"] != TYPE_NEAREST) return
        val locationId = message.data["locationId"] ?: return
        runBlocking { refreshWidgetsShowing(this@FlightRadarMessagingService, locationId) }
    }

    companion object {
        private const val TYPE_NEAREST = "nearest"

        /**
         * Asks Firebase for this device's address; the answer arrives in [onRegistered]. Called when the app starts.
         * Without google-services.json the build has no Firebase project (FirebaseApp never starts), so no pushes.
         */
        fun register(context: Context) {
            if (FirebaseApp.getApps(context).isEmpty()) return
            FirebaseMessaging.getInstance().register()
        }

        private suspend fun refreshWidgetsShowing(context: Context, locationId: String) {
            val manager = GlanceAppWidgetManager(context)
            for (glanceId in manager.getGlanceIds(FlightWidget::class.java)) {
                if (FlightWidgetState.read(context, glanceId).location?.id != locationId) continue
                FlightWidgetWorker.refreshNow(context, manager.getAppWidgetId(glanceId))
            }
        }
    }
}
