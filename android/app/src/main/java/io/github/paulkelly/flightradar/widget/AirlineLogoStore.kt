package io.github.paulkelly.flightradar.widget

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import io.github.paulkelly.flightradar.data.FlightRadarApi
import java.io.File
import java.io.IOException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Airline logos for the widget, kept as files. A widget can't load an image from a URL (the launcher draws it in
 * another process, from data we hand over), so the refresh downloads the logo first and the widget draws the file.
 *
 * One file per airline in [directory] (the app's cache folder: Android may empty it when space runs low, and then
 * logos are simply downloaded again). "No logo" (404) is remembered as an empty file, so it isn't asked again. Logos
 * hardly ever change, so a file is kept for good.
 */
class AirlineLogoStore(private val directory: File, private val api: FlightRadarApi) {

    /** Makes sure the logo for [code] is on file, downloading it if needed. Never throws: a logo is optional. */
    suspend fun fetchIfMissing(code: String) {
        val file = fileFor(code) ?: return
        if (file.exists()) return
        try {
            val response = api.airlineLogo(code)
            response.errorBody()?.close() // not needed; closing frees the connection for reuse
            // Dispatchers.IO: reading the body and writing the file block a thread (like sync I/O in .NET), so they
            // run on the pool meant for that rather than the CPU-sized default one.
            withContext(Dispatchers.IO) {
                val bytes = response.body().use { body ->
                    when {
                        response.isSuccessful && body != null -> body.bytes()
                        response.code() == 404 -> ByteArray(0) // no logo: remember that
                        else -> return@withContext // a server problem: try again next refresh
                    }
                }
                directory.mkdirs()
                // Write to a temporary name, then rename: a half-written file is never taken for a logo.
                val partial = File(directory, "${file.name}.partial")
                partial.writeBytes(bytes)
                partial.renameTo(file)
            }
        } catch (e: CancellationException) {
            throw e
        } catch (_: IOException) {
            // Offline or the write failed: no logo this time.
        }
    }

    /** The logo, or null if there's none (yet). Decoding a 90×90 PNG is quick; still, call it off the main thread. */
    fun load(code: String): Bitmap? {
        val file = fileFor(code)?.takeIf { it.length() > 0 } ?: return null
        return BitmapFactory.decodeFile(file.path) // null if it isn't a readable image
    }

    /**
     * The file for an airline, or null for a code that isn't one. The code becomes a file name, so it must never
     * contain anything like "../": only accept what ICAO codes look like (the API sends 3 letters).
     */
    internal fun fileFor(code: String): File? =
        if (VALID_CODE.matches(code)) File(directory, "${code.uppercase()}.png") else null

    private companion object {
        val VALID_CODE = Regex("^[A-Za-z0-9]{2,4}$")
    }
}
