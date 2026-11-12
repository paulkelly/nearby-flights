package io.github.paulkelly.flightradar.ui.locations

import org.junit.Assert.assertEquals
import org.junit.Test

// The same cases as the web app's radiusScale.test.ts, so both sliders behave alike.
class RadiusScaleTest {
    @Test
    fun kmAt_ends_areTheLimits() {
        assertEquals(RadiusScale.MIN_KM, RadiusScale.kmAt(0f), 0.0)
        assertEquals(RadiusScale.MAX_KM, RadiusScale.kmAt(1f), 0.0)
    }

    @Test
    fun kmAt_middle_isTheGeometricMeanTidied() {
        // √(1 × 250) ≈ 15.8 → 16 (1 km steps below 20 km)
        assertEquals(16.0, RadiusScale.kmAt(0.5f), 0.0)
    }

    @Test
    fun kmAt_outOfRange_isClamped() {
        assertEquals(RadiusScale.MIN_KM, RadiusScale.kmAt(-0.5f), 0.0)
        assertEquals(RadiusScale.MAX_KM, RadiusScale.kmAt(1.5f), 0.0)
    }

    @Test
    fun round_usesTheStepForEachBand() {
        assertEquals(12.0, RadiusScale.round(12.4), 0.0)
        assertEquals(45.0, RadiusScale.round(44.7), 0.0)
        assertEquals(130.0, RadiusScale.round(127.0), 0.0)
    }

    @Test
    fun round_halves_roundUp() {
        assertEquals(13.0, RadiusScale.round(12.5), 0.0)
    }

    @Test
    fun fractionFor_isTheInverseOfKmAt() {
        for (km in listOf(1.0, 5.0, 10.0, 16.0, 45.0, 120.0, 250.0)) {
            assertEquals(km, RadiusScale.kmAt(RadiusScale.fractionFor(km)), 0.0)
        }
    }
}
