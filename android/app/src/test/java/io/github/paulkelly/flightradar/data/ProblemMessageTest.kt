package io.github.paulkelly.flightradar.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ProblemMessageTest {
    @Test
    fun problemMessage_validationErrors_firstErrorWins() {
        val body = """{"title":"One or more validation errors occurred.","status":400,
            "errors":{"name":["name must be at most 50 characters."],"lat":["lat must be between -90 and 90."]}}"""
        assertEquals("name must be at most 50 characters.", problemMessage(body))
    }

    @Test
    fun problemMessage_detail_preferredToTitle() {
        val body = """{"title":"Conflict","status":409,"detail":"You already have a location called Home."}"""
        assertEquals("You already have a location called Home.", problemMessage(body))
    }

    @Test
    fun problemMessage_titleOnly_isTheTitle() {
        assertEquals("Service Unavailable", problemMessage("""{"title":"Service Unavailable","status":503}"""))
    }

    @Test
    fun problemMessage_notProblemDetails_isNull() {
        assertNull(problemMessage(null))
        assertNull(problemMessage(""))
        assertNull(problemMessage("<html>Bad gateway</html>"))
        assertNull(problemMessage("[1,2]"))
        assertNull(problemMessage("""{"status":500}"""))
    }
}
