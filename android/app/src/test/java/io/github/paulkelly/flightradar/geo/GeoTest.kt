package io.github.paulkelly.flightradar.geo

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.PI
import kotlin.math.pow

class GeoTest {
    private val dublin = LatLon(53.421, -6.27)

    @Test
    fun distanceKm_oneDegreeOfLatitude_isAbout111Km() {
        // 2πR / 360 with R = 6371 km.
        assertEquals(111.195, distanceKm(LatLon(0.0, 0.0), LatLon(1.0, 0.0)), 0.001)
    }

    @Test
    fun destination_dueNorth_onlyChangesLatitude() {
        val end = destination(dublin, 0.0, 111.195)
        assertEquals(dublin.lat + 1, end.lat, 1e-4)
        assertEquals(dublin.lon, end.lon, 1e-9)
    }

    @Test
    fun destination_isTheGivenDistanceAway() {
        for (bearing in listOf(0.0, 45.0, 90.0, 180.0, 270.0, 333.0)) {
            assertEquals(42.0, distanceKm(dublin, destination(dublin, bearing, 42.0)), 1e-6)
        }
    }

    @Test
    fun circlePolygon_everyPointIsTheRadiusAway() {
        val ring = circlePolygon(dublin, 10.0)
        ring.forEach { assertEquals(10.0, distanceKm(dublin, it), 1e-6) }
    }

    @Test
    fun circlePolygon_isClosed_withTheGivenNumberOfCorners() {
        val ring = circlePolygon(dublin, 10.0, points = 32)
        assertEquals(33, ring.size)
        assertEquals(ring.first(), ring.last())
    }

    @Test
    fun circlePolygon_startsDueNorth_andGoesClockwise() {
        val ring = circlePolygon(dublin, 10.0, points = 4)
        assertTrue("north", ring[0].lat > dublin.lat && abs(ring[0].lon - dublin.lon) < 1e-9)
        assertTrue("then east", ring[1].lon > dublin.lon)
        assertTrue("then south", ring[2].lat < dublin.lat)
        assertTrue("then west", ring[3].lon < dublin.lon)
    }

    @Test
    fun circlePolygon_acrossThe180Line_staysContinuous() {
        val centre = LatLon(-17.0, 179.9) // Fiji
        val ring = circlePolygon(centre, 50.0)
        // No jump to -180: every corner stays within a degree of the centre, some past 180.
        ring.forEach { assertTrue("$it", abs(it.lon - centre.lon) < 1.0) }
        assertTrue(ring.any { it.lon > 180.0 })
        ring.forEach { assertEquals(50.0, distanceKm(centre, it), 1e-6) }
    }

    @Test
    fun circlePolygon_farNorth_isStillTheRightDistance() {
        val centre = LatLon(85.0, 20.0)
        circlePolygon(centre, 250.0).forEach { assertEquals(250.0, distanceKm(centre, it), 1e-6) }
    }

    @Test
    fun zoomToShow_circleSpansTheRequestedFraction() {
        val zoom = zoomToShow(radiusKm = 10.0, lat = 53.4, viewSizeDp = 400.0, fraction = 0.75)
        val metresPerDp = 2 * PI * 6_371_000 * cos(53.4 * PI / 180) / 512 / 2.0.pow(zoom)
        assertEquals(400.0 * 0.75, 20_000 / metresPerDp, 1e-6)
    }

    @Test
    fun zoomToShow_biggerCircle_zoomsOut() {
        assertTrue(zoomToShow(250.0, 53.4, 400.0) < zoomToShow(10.0, 53.4, 400.0))
    }
}
