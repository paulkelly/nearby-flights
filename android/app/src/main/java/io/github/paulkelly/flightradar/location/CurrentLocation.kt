package io.github.paulkelly.flightradar.location

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationManager
import androidx.core.content.ContextCompat
import androidx.core.location.LocationManagerCompat
import com.google.android.gms.common.ConnectionResult
import com.google.android.gms.common.GoogleApiAvailability
import com.google.android.gms.location.CurrentLocationRequest
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import com.google.android.gms.tasks.CancellationTokenSource
import kotlin.coroutines.resume
import kotlinx.coroutines.suspendCancellableCoroutine

/** A position fix. `accuracyM`: the radius (metres) it's probably within; large when the user allowed only "approximate". */
data class Position(val lat: Double, val lon: Double, val accuracyM: Float?)

/** Why there's no position, in words a user can act on. */
class LocationUnavailableException(message: String) : Exception(message)

/**
 * The device's position, once, like the web app's "Use my location".
 *
 * Asks Google Play services' fused location provider (what Chrome and Maps use), not Android's own LocationManager:
 * on a Pixel indoors the platform's provider waited its full 30 s and gave nothing, while Play services answers in a
 * second or two from nearby Wi-Fi networks, and reuses a recent fix when there is one.
 */
class CurrentLocation(private val context: Context) {

    /** True if the user has allowed precise or approximate location. */
    fun hasPermission(): Boolean =
        PERMISSIONS.any { ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED }

    /** Throws LocationUnavailableException when there's no permission, location is off, or no fix comes. */
    suspend fun get(): Position {
        if (!hasPermission()) throw LocationUnavailableException("Location permission was denied.")
        val manager = context.getSystemService(LocationManager::class.java)
        if (!LocationManagerCompat.isLocationEnabled(manager)) {
            throw LocationUnavailableException("Location is turned off. Turn it on in quick settings and try again.")
        }
        if (GoogleApiAvailability.getInstance().isGooglePlayServicesAvailable(context) != ConnectionResult.SUCCESS) {
            throw LocationUnavailableException("Finding your location needs Google Play services, which isn't available.")
        }

        val request = CurrentLocationRequest.Builder()
            // Wi-Fi and cell towers, GPS only if needed: about 100 m, plenty for a search centre (stored to ~100 m).
            .setPriority(Priority.PRIORITY_BALANCED_POWER_ACCURACY)
            .setMaxUpdateAgeMillis(MAX_AGE_MS) // a fix from the last minute is fine, like the web app
            .setDurationMillis(TIMEOUT_MS)
            .build()

        val location = awaitLocation(request)
            ?: throw LocationUnavailableException("Couldn't find your location. Try again.")
        return Position(location.latitude, location.longitude, if (location.hasAccuracy()) location.accuracy else null)
    }

    /**
     * Play services reports through a `Task` (its callback-style future). suspendCancellableCoroutine turns that into
     * a suspend function, like wrapping a callback API in a TaskCompletionSource; cancelling the coroutine (the dialog
     * closed) cancels the request through the token. (kotlinx-coroutines-play-services has a ready-made `await()`;
     * these few lines save a dependency.)
     */
    @SuppressLint("MissingPermission") // get() checks hasPermission() first; Lint can't follow it into a helper
    private suspend fun awaitLocation(request: CurrentLocationRequest): Location? =
        suspendCancellableCoroutine { continuation ->
            val cancellation = CancellationTokenSource()
            continuation.invokeOnCancellation { cancellation.cancel() }
            LocationServices.getFusedLocationProviderClient(context)
                .getCurrentLocation(request, cancellation.token)
                .addOnSuccessListener { location -> continuation.resume(location) } // null = no fix in time
                .addOnFailureListener { continuation.resume(null) } // e.g. permission withdrawn meanwhile
                .addOnCanceledListener { continuation.resume(null) }
        }

    companion object {
        /** Asked for together: since Android 12 that lets the user choose precise or approximate. */
        val PERMISSIONS = arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)

        private const val MAX_AGE_MS = 60_000L
        private const val TIMEOUT_MS = 20_000L
    }
}
