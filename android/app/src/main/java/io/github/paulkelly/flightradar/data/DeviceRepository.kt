package io.github.paulkelly.flightradar.data

import java.net.HttpURLConnection.HTTP_NOT_FOUND
import java.net.HttpURLConnection.HTTP_UNAUTHORIZED
import retrofit2.HttpException

/**
 * Keeps the server's list of "locations this phone's widgets show" up to date (step 9c), so it watches them around
 * the clock. Sends only when something changed, or once a day to say the phone is still here (the server forgets
 * devices silent for a week).
 *
 * Errors: `IOException` (no connection) and `ApiException` (the server said no) are thrown, and nothing is recorded,
 * so the next sync tries again.
 */
class DeviceRepository(
    private val api: FlightRadarApi,
    private val tokens: OwnerTokenStore,
    private val store: DeviceStore,
    private val nowMs: () -> Long = System::currentTimeMillis,
) {
    suspend fun sync(locationIds: Set<String>) {
        val now = nowMs()
        val pushToken = store.pushToken()
        if (!needsRegistration(locationIds, pushToken, store.lastRegistration(), now)) return

        // No owner, no saved locations of ours: there's nothing the server could watch.
        val token = tokens.read()
        if (token != null) {
            try {
                put(token, store.installationId(), locationIds, pushToken)
            } catch (e: ApiException) {
                when (e.status) {
                    // This ID is registered to another owner: ours, before the database forgot its token and a save
                    // made a new one. Start again as a new device.
                    HTTP_NOT_FOUND -> put(token, store.newInstallationId(), locationIds, pushToken)
                    // The token is dead (database reset), so its locations are gone: nothing to watch.
                    HTTP_UNAUTHORIZED -> tokens.clear()
                    else -> throw e
                }
            }
        }
        store.setLastRegistration(Registration(locationIds, pushToken, now))
    }

    private suspend fun put(token: String, installationId: String, locationIds: Set<String>, pushToken: String?) {
        try {
            api.putDeviceWatches(bearer(token), installationId, DeviceWatchesRequest(locationIds.sorted(), pushToken))
        } catch (e: HttpException) {
            throw e.toApiException()
        }
    }

    companion object {
        /** The server forgets a device after 7 days; renewing daily leaves room for a phone off for a few days. */
        const val RENEW_AFTER_MS = 24 * 60 * 60 * 1000L

        /**
         * Whether to tell the server: the set changed; or there's something to watch and either the push address
         * changed or it's been a day. Never registered and nothing to watch: nothing to say.
         */
        fun needsRegistration(current: Set<String>, pushToken: String?, last: Registration?, nowMs: Long): Boolean =
            when {
                last == null -> current.isNotEmpty()
                current != last.locationIds -> true
                current.isEmpty() -> false
                pushToken != last.pushToken -> true
                else -> nowMs - last.atMs >= RENEW_AFTER_MS
            }
    }
}
