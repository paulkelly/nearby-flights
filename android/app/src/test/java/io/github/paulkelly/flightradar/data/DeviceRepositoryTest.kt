package io.github.paulkelly.flightradar.data

import io.github.paulkelly.flightradar.data.DeviceRepository.Companion.RENEW_AFTER_MS
import io.github.paulkelly.flightradar.data.DeviceRepository.Companion.needsRegistration
import java.io.IOException
import kotlinx.coroutines.test.runTest
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test

class DeviceRepositoryTest {
    private val server = MockWebServer()
    private val tokens = InMemoryOwnerTokenStore("abc")
    private val store = InMemoryDeviceStore()
    private var now = 1_000_000L
    private lateinit var repository: DeviceRepository

    @Before
    fun setUp() {
        server.start()
        repository = DeviceRepository(FlightRadarApi.create(server.url("/").toString()), tokens, store) { now }
    }

    @After
    fun tearDown() {
        server.close()
    }

    private fun respond(code: Int, body: String = """{"locationIds":[]}""") {
        server.enqueue(MockResponse.Builder().code(code).body(body).build())
    }

    // ---- needsRegistration (pure) ----

    @Test
    fun needsRegistration_neverRegisteredNothingToWatch_no() =
        assertFalse(needsRegistration(emptySet(), null, now))

    @Test
    fun needsRegistration_neverRegisteredSomethingToWatch_yes() =
        assertTrue(needsRegistration(setOf("1"), null, now))

    @Test
    fun needsRegistration_sameSetWithinADay_no() =
        assertFalse(needsRegistration(setOf("1"), Registration(setOf("1"), now - RENEW_AFTER_MS + 1), now))

    @Test
    fun needsRegistration_sameSetADayLater_yes() =
        assertTrue(needsRegistration(setOf("1"), Registration(setOf("1"), now - RENEW_AFTER_MS), now))

    @Test
    fun needsRegistration_setChanged_yesAtOnce() {
        assertTrue(needsRegistration(setOf("1", "2"), Registration(setOf("1"), now), now))
        assertTrue(needsRegistration(emptySet(), Registration(setOf("1"), now), now)) // last widget removed
    }

    @Test
    fun needsRegistration_stillNothingADayLater_no() =
        assertFalse(needsRegistration(emptySet(), Registration(emptySet(), 0), now))

    // ---- sync ----

    @Test
    fun sync_sendsThePutWithTokenAndInstallationId_andRemembersIt() = runTest {
        respond(200)

        repository.sync(setOf("b", "a"))

        val request = server.takeRequest()
        assertEquals("PUT", request.method)
        assertEquals("/api/devices/${store.id}", request.url.encodedPath)
        assertEquals("Bearer abc", request.headers["Authorization"])
        assertEquals("""{"locationIds":["a","b"]}""", request.body!!.utf8())
        assertEquals(Registration(setOf("a", "b"), now), store.last)
    }

    @Test
    fun sync_nothingChanged_sendsNothing() = runTest {
        store.last = Registration(setOf("a"), now)

        repository.sync(setOf("a"))

        assertEquals(0, server.requestCount)
    }

    @Test
    fun sync_noToken_sendsNothingButRecordsIt() = runTest {
        tokens.token = null

        repository.sync(setOf("a"))

        assertEquals(0, server.requestCount)
        assertEquals(Registration(setOf("a"), now), store.last) // or it would try on every run
    }

    @Test
    fun sync_installationIdTakenByAnotherOwner_startsAgainAsANewDevice() = runTest {
        val oldId = store.id
        respond(404)
        respond(200)

        repository.sync(setOf("a"))

        assertEquals("/api/devices/$oldId", server.takeRequest().url.encodedPath)
        assertNotEquals(oldId, store.id)
        assertEquals("/api/devices/${store.id}", server.takeRequest().url.encodedPath)
        assertEquals(setOf("a"), store.last?.locationIds)
    }

    @Test
    fun sync_rejectedToken_forgetsIt() = runTest {
        respond(401)

        repository.sync(setOf("a"))

        assertNull(tokens.token)
        assertEquals(1, server.requestCount)
    }

    @Test
    fun sync_serverError_throwsAndRecordsNothing() = runTest {
        respond(503, "")

        try {
            repository.sync(setOf("a"))
            fail("Expected an ApiException")
        } catch (e: ApiException) {
            assertEquals(503, e.status)
        }
        assertNull(store.last) // so the next sync tries again
    }

    @Test
    fun sync_noConnection_throwsIOException() = runTest {
        server.close()

        try {
            repository.sync(setOf("a"))
            fail("Expected an IOException")
        } catch (_: IOException) {
        }
        assertNull(store.last)
    }
}

class InMemoryDeviceStore(var id: String = "11111111-1111-1111-1111-111111111111") : DeviceStore {
    var last: Registration? = null

    override suspend fun installationId() = id

    override suspend fun newInstallationId(): String {
        id = java.util.UUID.randomUUID().toString()
        return id
    }

    override suspend fun lastRegistration() = last

    override suspend fun setLastRegistration(registration: Registration) {
        last = registration
    }
}
