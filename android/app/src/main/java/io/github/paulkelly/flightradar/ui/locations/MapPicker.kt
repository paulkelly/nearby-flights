package io.github.paulkelly.flightradar.ui.locations

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import io.github.paulkelly.flightradar.geo.LatLon
import io.github.paulkelly.flightradar.geo.circlePolygon
import io.github.paulkelly.flightradar.geo.zoomToShow
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.PropertyFactory
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.Point
import org.maplibre.geojson.Polygon

/** OpenFreeMap's "Liberty" style: free, no key; MapLibre shows its required attribution (the ⓘ button). */
private const val STYLE_URL = "https://tiles.openfreemap.org/styles/liberty"
private const val CIRCLE_SOURCE = "search-circle"

/**
 * A map with a fixed dot in the middle: the user moves the map under it, and the spot under the dot is the location.
 * The search circle around it is redrawn on every camera frame.
 *
 * - [start]: where the map first opens (and reopens after rotation, when the MapView is recreated).
 * - [target]: a move asked for by code ("Use my location"); each new object moves the camera once.
 * - [zoomToFit]: changing it zooms (keeping the centre) so the circle fits, e.g. after the slider is let go.
 * - [onGestureStart]: the user started moving the map. [onCentreChosen]: where it came to rest after the user moved it
 *   (moves made by code aren't reported: the caller already knows where it sent the map).
 *
 * MapLibre is a classic Android View, so it's hosted with AndroidView (like embedding a native plugin's window in an
 * engine UI). Unlike Compose, it doesn't follow the screen's lifecycle by itself: it has to be told about start/stop
 * (to pause rendering and downloads) and destroy (to free its native memory), so we forward those events by hand.
 * The circle is updated straight on the map in the camera listener, not through Compose state, so it moves with the
 * map every frame without recomposing anything: the same trick as the web app's dead-reckoned markers.
 */
@Composable
internal fun MapPicker(
    start: LatLon,
    target: MapTarget?,
    radiusKm: Double,
    zoomToFit: Int,
    onGestureStart: () -> Unit,
    onCentreChosen: (LatLon) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val mapView = remember {
        MapLibre.getInstance(context) // once per process; must come before the first MapView
        MapView(context)
    }
    var map by remember { mutableStateOf<MapLibreMap?>(null) }
    var circle by remember { mutableStateOf<GeoJsonSource?>(null) }

    // Read inside MapLibre's listeners, which were registered once: these keep them seeing the latest values.
    val currentRadius by rememberUpdatedState(radiusKm)
    val currentStart by rememberUpdatedState(start)
    val currentOnGestureStart by rememberUpdatedState(onGestureStart)
    val currentOnCentreChosen by rememberUpdatedState(onCentreChosen)

    val circleColour = MaterialTheme.colorScheme.primary.toArgb()

    ForwardLifecycle(mapView)

    LaunchedEffect(mapView) {
        mapView.getMapAsync { readyMap ->
            readyMap.uiSettings.isRotateGesturesEnabled = false // north stays up, as on the web
            readyMap.uiSettings.isTiltGesturesEnabled = false
            readyMap.moveCamera(
                CameraUpdateFactory.newLatLngZoom(currentStart.toLatLng(), zoomFor(mapView, currentRadius, currentStart.lat)),
            )

            var movedByUser = false
            readyMap.addOnCameraMoveStartedListener { reason ->
                if (reason == MapLibreMap.OnCameraMoveStartedListener.REASON_API_GESTURE) {
                    movedByUser = true
                    currentOnGestureStart()
                }
            }
            readyMap.addOnCameraIdleListener {
                if (movedByUser) {
                    movedByUser = false
                    readyMap.centre()?.let { currentOnCentreChosen(it) }
                }
            }

            readyMap.setStyle(Style.Builder().fromUri(STYLE_URL)) { style ->
                val source = GeoJsonSource(CIRCLE_SOURCE)
                style.addSource(source)
                style.addLayer(
                    FillLayer("search-circle-fill", CIRCLE_SOURCE)
                        .withProperties(PropertyFactory.fillColor(circleColour), PropertyFactory.fillOpacity(0.12f)),
                )
                style.addLayer(
                    LineLayer("search-circle-line", CIRCLE_SOURCE)
                        .withProperties(PropertyFactory.lineColor(circleColour), PropertyFactory.lineWidth(2f)),
                )
                readyMap.addOnCameraMoveListener { readyMap.centre()?.let { source.drawCircle(it, currentRadius) } }
                readyMap.centre()?.let { source.drawCircle(it, currentRadius) }
                circle = source
            }
            map = readyMap
        }
    }

    // The slider moved: redraw at the new size (the camera listener covers map moves).
    LaunchedEffect(circle, radiusKm) {
        val source = circle ?: return@LaunchedEffect
        map?.centre()?.let { source.drawCircle(it, radiusKm) }
    }

    LaunchedEffect(map, target) {
        val readyMap = map ?: return@LaunchedEffect
        target ?: return@LaunchedEffect
        val update = CameraUpdateFactory.newLatLngZoom(
            LatLng(target.lat, target.lon),
            zoomFor(mapView, currentRadius, target.lat),
        )
        readyMap.animateCamera(update, 800)
    }

    LaunchedEffect(map, zoomToFit) {
        val readyMap = map ?: return@LaunchedEffect
        if (zoomToFit == 0) return@LaunchedEffect // nothing asked yet: keep the zoom the map opened with
        val centre = readyMap.centre() ?: return@LaunchedEffect
        readyMap.easeCamera(CameraUpdateFactory.zoomTo(zoomFor(mapView, currentRadius, centre.lat)), 400)
    }

    Box(modifier) {
        AndroidView(factory = { mapView }, modifier = Modifier.matchParentSize())
        CentreDot(Modifier.align(Alignment.Center))
    }
}

/** A position the map should move to, asked for by code. Not a data class: two requests for one spot are two moves. */
class MapTarget(val lat: Double, val lon: Double)

/** The fixed marker in the middle of the map, like the web app's centre dot. */
@Composable
private fun CentreDot(modifier: Modifier = Modifier) {
    val colour = MaterialTheme.colorScheme.primary
    Canvas(modifier.size(18.dp)) {
        val radius = size.minDimension / 2
        drawCircle(Color.White, radius = radius)
        drawCircle(colour, radius = radius - 3.dp.toPx())
        drawCircle(Color.Black.copy(alpha = 0.25f), radius = radius, style = Stroke(1.dp.toPx()))
    }
}

/**
 * Tells [mapView] about the screen's lifecycle: created → started → resumed and back. When the composable leaves the
 * screen first (the Add screen closed while the Activity carries on), it winds the map down itself.
 */
@Composable
private fun ForwardLifecycle(mapView: MapView) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    DisposableEffect(lifecycle, mapView) {
        // Added late, an observer is first sent the events it missed (ON_CREATE, ON_START, ...), so the map catches up.
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_CREATE -> mapView.onCreate(null)
                Lifecycle.Event.ON_START -> mapView.onStart()
                Lifecycle.Event.ON_RESUME -> mapView.onResume()
                Lifecycle.Event.ON_PAUSE -> mapView.onPause()
                Lifecycle.Event.ON_STOP -> mapView.onStop()
                else -> Unit // ON_DESTROY: handled below, once
            }
        }
        lifecycle.addObserver(observer)
        onDispose {
            lifecycle.removeObserver(observer)
            if (lifecycle.currentState.isAtLeast(Lifecycle.State.RESUMED)) mapView.onPause()
            if (lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) mapView.onStop()
            mapView.onDestroy()
        }
    }
}

private fun MapLibreMap.centre(): LatLon? = cameraPosition.target?.let { LatLon(it.latitude, it.longitude) }

private fun LatLon.toLatLng() = LatLng(lat, lon)

private fun GeoJsonSource.drawCircle(centre: LatLon, radiusKm: Double) {
    val ring = circlePolygon(centre, radiusKm).map { Point.fromLngLat(it.lon, it.lat) } // GeoJSON is lon, lat
    setGeoJson(Polygon.fromLngLats(listOf(ring)))
}

/** The zoom that fits the circle in the map view; before the view has a size, assume a phone-sized 360 dp. */
private fun zoomFor(mapView: MapView, radiusKm: Double, lat: Double): Double {
    val density = mapView.resources.displayMetrics.density
    val sideDp = minOf(mapView.width, mapView.height) / density
    return zoomToShow(radiusKm, lat, if (sideDp > 0) sideDp.toDouble() else 360.0)
}
