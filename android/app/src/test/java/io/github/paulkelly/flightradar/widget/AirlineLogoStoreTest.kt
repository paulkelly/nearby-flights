package io.github.paulkelly.flightradar.widget

import io.github.paulkelly.flightradar.data.FlightRadarApi
import kotlinx.coroutines.test.runTest
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import okio.Buffer
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

// Downloading and caching only: decoding (BitmapFactory) is Android code that doesn't run in JVM unit tests.
class AirlineLogoStoreTest {
    @get:Rule
    val folder = TemporaryFolder()

    private val server = MockWebServer()
    private lateinit var store: AirlineLogoStore

    private val png = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3)

    @Before
    fun setUp() {
        server.start()
        store = AirlineLogoStore(folder.root, FlightRadarApi.create(server.url("/").toString()))
    }

    @After
    fun tearDown() {
        server.close()
    }

    private fun respond(code: Int, body: ByteArray = ByteArray(0)) {
        server.enqueue(MockResponse.Builder().code(code).body(Buffer().write(body)).build())
    }

    @Test
    fun fetchIfMissing_logo_isDownloadedOnceAndKept() = runTest {
        respond(200, png)

        store.fetchIfMissing("RYR")
        store.fetchIfMissing("RYR") // already on file: no second request

        assertEquals(1, server.requestCount)
        assertEquals("/api/airlines/RYR/logo", server.takeRequest().url.encodedPath)
        assertArrayEquals(png, store.fileFor("RYR")!!.readBytes())
    }

    @Test
    fun fetchIfMissing_noLogo_isRememberedAsAnEmptyFile() = runTest {
        respond(404)

        store.fetchIfMissing("XYZ")
        store.fetchIfMissing("XYZ")

        assertEquals(1, server.requestCount)
        assertEquals(0L, store.fileFor("XYZ")!!.length())
    }

    @Test
    fun fetchIfMissing_serverError_isNotRememberedSoItIsTriedAgain() = runTest {
        respond(503)
        respond(200, png)

        store.fetchIfMissing("EIN")
        assertFalse(store.fileFor("EIN")!!.exists())
        store.fetchIfMissing("EIN")

        assertEquals(2, server.requestCount)
        assertTrue(store.fileFor("EIN")!!.exists())
    }

    @Test
    fun fetchIfMissing_serverUnreachable_doesNotThrow() = runTest {
        server.close()
        store.fetchIfMissing("RYR")
        assertFalse(store.fileFor("RYR")!!.exists())
    }

    @Test
    fun fileFor_codesThatArentAirlineCodes_areRefused() {
        assertNull(store.fileFor("../x"))
        assertNull(store.fileFor("A/B"))
        assertNull(store.fileFor(""))
        assertNull(store.fileFor("TOOLONG"))
        assertEquals("RYR.png", store.fileFor("ryr")!!.name)
    }
}
