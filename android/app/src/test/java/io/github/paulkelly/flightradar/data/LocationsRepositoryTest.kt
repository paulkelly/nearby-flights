package io.github.paulkelly.flightradar.data

import kotlinx.coroutines.test.runTest
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import mockwebserver3.RecordedRequest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test

/**
 * The repository against MockWebServer: a real HTTP server on localhost that answers with queued responses and
 * records what it received. Retrofit, OkHttp and the JSON converter all run for real; only the API is fake.
 */
class LocationsRepositoryTest {
    private val server = MockWebServer()
    private val tokens = InMemoryOwnerTokenStore()
    private lateinit var repository: LocationsRepository

    private val home = SavedLocationRequest("Home", 53.35, -6.26, 10.0)
    private val homeJson = """{"id":"1","name":"Home","lat":53.35,"lon":-6.26,"radiusKm":10}"""

    @Before
    fun setUp() {
        server.start()
        repository = LocationsRepository(FlightRadarApi.create(server.url("/").toString()), tokens)
    }

    @After
    fun tearDown() {
        server.close()
    }

    private fun respond(code: Int, body: String = "") {
        server.enqueue(MockResponse.Builder().code(code).body(body).build())
    }

    private fun RecordedRequest.authorization() = headers["Authorization"]

    @Test
    fun list_noToken_returnsNoneWithoutARequest() = runTest {
        assertEquals(emptyList<SavedLocation>(), repository.list())
        assertEquals(0, server.requestCount)
    }

    @Test
    fun list_withToken_sendsItAndReadsTheList() = runTest {
        tokens.token = "abc"
        // An unknown field ("extra") must not break parsing: the API may add fields before the app knows them.
        respond(200, """[{"id":"1","name":"Home","lat":53.35,"lon":-6.26,"radiusKm":10,"extra":true}]""")

        val locations = repository.list()

        assertEquals(listOf(SavedLocation("1", "Home", 53.35, -6.26, 10.0)), locations)
        val request = server.takeRequest()
        assertEquals("GET", request.method)
        assertEquals("/api/locations", request.url.encodedPath)
        assertEquals("Bearer abc", request.authorization())
    }

    @Test
    fun list_rejectedToken_forgetsIt() = runTest {
        tokens.token = "dead"
        respond(401)

        assertEquals(emptyList<SavedLocation>(), repository.list())
        assertNull(tokens.token)
    }

    @Test
    fun save_noToken_createsAnOwnerFirstAndKeepsItsToken() = runTest {
        respond(201, """{"token":"new"}""")
        respond(201, homeJson)

        val saved = repository.save(home)

        assertEquals("Home", saved.name)
        assertEquals("new", tokens.token)
        val owner = server.takeRequest()
        assertEquals("POST", owner.method)
        assertEquals("/api/owners", owner.url.encodedPath)
        val create = server.takeRequest()
        assertEquals("/api/locations", create.url.encodedPath)
        assertEquals("Bearer new", create.authorization())
        val body = create.body?.utf8().orEmpty()
        assertTrue(body, body.contains(""""name":"Home"""") && body.contains(""""radiusKm":10.0"""))
    }

    @Test
    fun save_rejectedToken_becomesANewOwnerAndRetriesOnce() = runTest {
        tokens.token = "dead"
        respond(401)
        respond(201, """{"token":"new"}""")
        respond(201, homeJson)

        repository.save(home)

        assertEquals("Bearer dead", server.takeRequest().authorization())
        assertEquals("/api/owners", server.takeRequest().url.encodedPath)
        assertEquals("Bearer new", server.takeRequest().authorization())
        assertEquals("new", tokens.token)
    }

    @Test
    fun save_conflict_throwsTheServersMessageAndKeepsTheToken() = runTest {
        tokens.token = "abc"
        respond(409, """{"title":"Conflict","status":409,"detail":"You already have a location called Home."}""")

        try {
            repository.save(home)
            fail("Expected an ApiException")
        } catch (e: ApiException) {
            assertEquals(409, e.status)
            assertEquals("You already have a location called Home.", e.message)
        }
        assertEquals("abc", tokens.token)
    }

    @Test
    fun update_sendsAPutWithTheIdAndToken() = runTest {
        tokens.token = "abc"
        respond(200, homeJson)

        val updated = repository.update("1", home)

        assertEquals(SavedLocation("1", "Home", 53.35, -6.26, 10.0), updated)
        val request = server.takeRequest()
        assertEquals("PUT", request.method)
        assertEquals("/api/locations/1", request.url.encodedPath)
        assertEquals("Bearer abc", request.authorization())
        assertTrue(request.body!!.utf8().contains("\"name\":\"Home\""))
    }

    @Test
    fun update_deletedMeanwhile_saysSo() = runTest {
        tokens.token = "abc"
        respond(404)

        try {
            repository.update("1", home)
            fail("Expected an ApiException")
        } catch (e: ApiException) {
            assertEquals(404, e.status)
            assertEquals("This location no longer exists, so it couldn't be changed.", e.message)
        }
        assertEquals("abc", tokens.token)
    }

    @Test
    fun update_rejectedToken_forgetsItWithoutRetrying() = runTest {
        tokens.token = "dead"
        respond(401)

        try {
            repository.update("1", home)
            fail("Expected an ApiException")
        } catch (e: ApiException) {
            assertEquals(401, e.status)
        }
        assertNull(tokens.token)
        assertEquals(1, server.requestCount)
    }

    @Test
    fun update_conflict_throwsTheServersMessage() = runTest {
        tokens.token = "abc"
        respond(409, """{"title":"Conflict","status":409,"detail":"You already have a location called Home."}""")

        try {
            repository.update("1", home)
            fail("Expected an ApiException")
        } catch (e: ApiException) {
            assertEquals("You already have a location called Home.", e.message)
        }
    }

    @Test
    fun delete_sendsTheIdWithTheToken() = runTest {
        tokens.token = "abc"
        respond(204)

        repository.delete("42")

        val request = server.takeRequest()
        assertEquals("DELETE", request.method)
        assertEquals("/api/locations/42", request.url.encodedPath)
        assertEquals("Bearer abc", request.authorization())
    }

    @Test
    fun delete_alreadyGone_isNotAnError() = runTest {
        tokens.token = "abc"
        respond(404)

        repository.delete("42") // no exception
    }
}

/** An OwnerTokenStore in memory, for tests. */
class InMemoryOwnerTokenStore(var token: String? = null) : OwnerTokenStore {
    override suspend fun read(): String? = token

    override suspend fun write(token: String) {
        this.token = token
    }

    override suspend fun clear() {
        token = null
    }
}
