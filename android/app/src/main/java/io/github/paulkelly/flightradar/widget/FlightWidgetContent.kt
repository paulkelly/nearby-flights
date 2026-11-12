package io.github.paulkelly.flightradar.widget

import android.graphics.Bitmap
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.glance.ColorFilter
import androidx.glance.GlanceModifier
import androidx.glance.GlanceTheme
import androidx.glance.Image
import androidx.glance.ImageProvider
import androidx.glance.LocalSize
import androidx.glance.action.Action
import androidx.glance.action.clickable
import androidx.glance.appwidget.cornerRadius
import androidx.glance.background
import androidx.glance.layout.Alignment
import androidx.glance.layout.Box
import androidx.glance.layout.Column
import androidx.glance.layout.ColumnScope
import androidx.glance.layout.ContentScale
import androidx.glance.layout.Row
import androidx.glance.layout.Spacer
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.fillMaxWidth
import androidx.glance.layout.height
import androidx.glance.layout.padding
import androidx.glance.layout.size
import androidx.glance.layout.width
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextAlign
import androidx.glance.text.TextStyle
import androidx.glance.unit.ColorProvider
import io.github.paulkelly.flightradar.R
import java.text.DateFormat
import java.util.Date

/**
 * The sizes the widget is drawn at (see FlightWidget.sizeMode). The launcher shows the largest that fits, so each is
 * the smallest space its layout needs.
 */
object WidgetSizes {
    /** About one cell tall: the header and who it is. */
    val COMPACT = DpSize(180.dp, 80.dp)

    /** Plus the details (altitude, speed …), two to a row. */
    val FULL = DpSize(180.dp, 190.dp)

    /** Wide enough for the long route ("Dublin (DUB) → London (STN)"). */
    val WIDE = DpSize(320.dp, 170.dp)

    /** Wide enough for all four details in one row, with room for "↑ 1,500 ft/min" in the last. */
    val WIDEST = DpSize(390.dp, 170.dp)
}

/**
 * Widths of the detail columns before the last, which takes the rest. Glance can't weight columns unequally, so the
 * short values (altitude, speed, track) get a fixed width that fits them and the long one (vertical speed) the
 * remainder, instead of every column getting an equal share and the long one ending in "…".
 */
private val TWO_COLUMNS = listOf(84.dp)
private val FOUR_COLUMNS = listOf(84.dp, 66.dp, 52.dp)

/**
 * The widget's layout: the web app's "closest aircraft" card (NearestFlightCard + FlightInfo), in Glance.
 *
 *   HOME · 14:05                            4.2 km
 *   [logo]  Ryanair                           B738
 *           RYR2DQ
 *           Dublin (DUB) → London (STN)
 *   ───────────────────────────────────────────────
 *   ALTITUDE    SPEED     TRACK    VERTICAL SPEED
 *   8,000 ft    280 kt    95°      ↑ 1,500 ft/min
 *
 * The header's label carries the status (when it was updated, refreshing, or what went wrong), so it shows at every
 * size: a separate footer didn't fit under two rows of details at the default 3×2. Smaller sizes leave out the
 * details. Tapping anywhere refreshes (or, before a location is chosen, opens "choose a location").
 */
@Composable
fun FlightWidgetContent(state: FlightWidgetUiState, onRefresh: Action, onSetUp: Action) {
    val size = LocalSize.current
    val wide = size.width >= WidgetSizes.WIDE.width
    val oneDetailRow = size.width >= WidgetSizes.WIDEST.width
    // All details on one row needs less height than two rows.
    val showDetails = size.height >= (if (oneDetailRow) WidgetSizes.WIDEST.height else WidgetSizes.FULL.height)
    val location = state.location

    Column(
        GlanceModifier
            .fillMaxSize()
            .background(GlanceTheme.colors.widgetBackground)
            .cornerRadius(16.dp)
            .padding(horizontal = 14.dp, vertical = 12.dp)
            .clickable(if (location == null) onSetUp else onRefresh),
    ) {
        if (location == null) {
            Text("Tap to choose a location", style = bodyStyle(14.sp))
            return@Column
        }

        val snapshot = state.snapshot
        val aircraft = snapshot?.aircraft
        Header(location.name, state, aircraft?.distanceKm)
        Spacer(GlanceModifier.height(6.dp))

        if (aircraft == null) {
            // Nothing to show (none in range, or not loaded yet): a message in the middle of the free space.
            Message(
                title = when {
                    snapshot != null -> "Clear skies"
                    state.error != null -> "Couldn't load" // the first refresh failed; the header says why
                    else -> "Loading…"
                },
                subtitle = if (snapshot != null) "Nothing within ${formatRadius(location.radiusKm)} right now" else null,
            )
        } else {
            Identity(aircraft, state.logo, wide)
            if (showDetails) {
                Spacer(GlanceModifier.height(8.dp))
                Divider()
                Spacer(GlanceModifier.height(6.dp))
                Details(aircraft, columnWidths = if (oneDetailRow) FOUR_COLUMNS else TWO_COLUMNS)
            }
        }
    }
}

/**
 * "HOME · 14:05" on the left, the distance on the right (the card's label and distance). The label also says
 * "Refreshing…", or what went wrong ("HOME · 14:05 · No connection", in the error colour: the data is older).
 */
@Composable
private fun Header(locationName: String, state: FlightWidgetUiState, distanceKm: Double?) {
    val updated = state.snapshot?.let { DateFormat.getTimeInstance(DateFormat.SHORT).format(Date(it.updatedAtMs)) }
    val status = when {
        state.refreshing -> "Refreshing…"
        state.error != null -> listOfNotNull(updated, state.error).joinToString(" · ")
        else -> updated
    }
    Row(GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(
            listOfNotNull(locationName.uppercase(), status).joinToString(" · "),
            style = mutedStyle(11.sp).let { if (state.error != null && !state.refreshing) it.copy(color = GlanceTheme.colors.error) else it },
            maxLines = 1,
            modifier = GlanceModifier.defaultWeight(),
        )
        if (distanceKm != null) {
            Text(formatDistance(distanceKm), style = boldStyle(14.sp))
        }
    }
}

/**
 * The aircraft icon beside a title and subtitle, centred in the space under the header: "Clear skies / Nothing
 * within 10 km right now". A ColumnScope extension so it can take the free height (defaultWeight).
 */
@Composable
private fun ColumnScope.Message(title: String, subtitle: String?) {
    Box(GlanceModifier.fillMaxWidth().defaultWeight(), contentAlignment = Alignment.Center) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Image(
                provider = ImageProvider(R.drawable.ic_flight),
                contentDescription = null, // decoration: the text says it all
                colorFilter = ColorFilter.tint(GlanceTheme.colors.onSurfaceVariant),
                modifier = GlanceModifier.size(36.dp),
            )
            Spacer(GlanceModifier.width(12.dp))
            Column {
                Text(title, style = boldStyle(18.sp), maxLines = 1)
                subtitle?.let { Text(it, style = mutedStyle(13.sp), maxLines = 1) }
            }
        }
    }
}

/** The logo beside the airline, the flight's name and the route; the aircraft type on the right (FlightIdentity). */
@Composable
private fun Identity(aircraft: WidgetAircraft, logo: Bitmap?, wide: Boolean) {
    // Centred vertically: the logo sits level with the middle of the two or three lines beside it.
    Row(GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        aircraft.airlineCode?.let { code ->
            AirlineLogo(code, logo)
            Spacer(GlanceModifier.width(10.dp))
        }
        Column(GlanceModifier.defaultWeight()) {
            // The type shares the heading's line (as on the website), so centring the row doesn't move it down.
            Row(GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(aircraft.title, style = boldStyle(18.sp), maxLines = 1, modifier = GlanceModifier.defaultWeight())
                aircraft.aircraftType?.let {
                    Spacer(GlanceModifier.width(8.dp))
                    Text(it, style = mutedStyle(13.sp).copy(textAlign = TextAlign.End))
                }
            }
            aircraft.name?.let { Text(it, style = mutedStyle(13.sp), maxLines = 1) }
            val route = if (wide) aircraft.routeLong ?: aircraft.route else aircraft.route
            if (route != null) {
                Text(route, style = bodyStyle(13.sp).copy(fontWeight = FontWeight.Medium), maxLines = 1)
            } else {
                Text("Unknown route", style = mutedStyle(13.sp), maxLines = 1) // a placeholder: quieter
            }
        }
    }
}

/** The airline's logo, or (no logo, or not downloaded yet) its code on a coloured badge, as on the website. */
@Composable
private fun AirlineLogo(code: String, logo: Bitmap?) {
    val modifier = GlanceModifier.size(LOGO_SIZE).cornerRadius(4.dp)
    if (logo != null) {
        Image(
            provider = ImageProvider(logo),
            contentDescription = null, // the airline's name is right beside it
            contentScale = ContentScale.Fit,
            modifier = modifier,
        )
    } else {
        Box(modifier.background(badgeColour(code)), contentAlignment = Alignment.Center) {
            Text(code, style = TextStyle(color = ColorProvider(Color.White), fontSize = 15.sp, fontWeight = FontWeight.Bold))
        }
    }
}

/**
 * Labelled values (FlightDetails): the ones the card shows, in rows (Glance rows can't wrap). Every column but the
 * last has the width in [columnWidths] (so the columns line up from row to row); the last takes what's left.
 */
@Composable
private fun Details(aircraft: WidgetAircraft, columnWidths: List<Dp>) {
    val perRow = columnWidths.size + 1
    val details = buildList {
        add("Altitude" to formatAltitude(aircraft))
        aircraft.groundSpeedKt?.let { add("Speed" to formatSpeed(it)) }
        aircraft.trackDeg?.let { add("Track" to formatTrack(it)) }
        // "Vertical speed": the pilot's term. Not on the ground, like the web's card.
        if (!aircraft.onGround && aircraft.verticalRateFpm != null) {
            add("Vertical speed" to formatVerticalRate(aircraft.verticalRateFpm))
        }
    }
    Column(GlanceModifier.fillMaxWidth()) {
        details.chunked(perRow).forEachIndexed { index, row ->
            if (index > 0) Spacer(GlanceModifier.height(4.dp))
            Row(GlanceModifier.fillMaxWidth()) {
                row.forEachIndexed { column, (label, value) ->
                    val width = columnWidths.getOrNull(column)
                    // A short last row (e.g. on the ground: no vertical speed) still takes the rest of the width.
                    val modifier = if (width != null && column < row.lastIndex) {
                        GlanceModifier.width(width)
                    } else {
                        GlanceModifier.defaultWeight()
                    }
                    Detail(label, value, modifier)
                }
            }
        }
    }
}

/** One labelled value. */
@Composable
private fun Detail(label: String, value: String, modifier: GlanceModifier) {
    Column(modifier) {
        Text(label.uppercase(), style = mutedStyle(10.sp), maxLines = 1)
        Text(value, style = boldStyle(14.sp), maxLines = 1)
    }
}

/** A thin line across, like the card's border above its details. */
@Composable
private fun Divider() {
    Box(GlanceModifier.fillMaxWidth().height(1.dp).background(GlanceTheme.colors.surfaceVariant)) {}
}

/** The web's badge colour, `hsl(hue 45% 40%)`: dark enough for white text, the same hue for the same airline. */
private fun badgeColour(code: String) =
    ColorProvider(Color.hsl(badgeHue(code).toFloat(), saturation = 0.45f, lightness = 0.40f))

private val LOGO_SIZE = 52.dp

// Text styles. Glance has no opacity, so "quieter" text uses the theme's variant colour instead.
@Composable
private fun bodyStyle(fontSize: TextUnit) = TextStyle(color = GlanceTheme.colors.onSurface, fontSize = fontSize)

@Composable
private fun boldStyle(fontSize: TextUnit) = bodyStyle(fontSize).copy(fontWeight = FontWeight.Bold)

@Composable
private fun mutedStyle(fontSize: TextUnit) = TextStyle(color = GlanceTheme.colors.onSurfaceVariant, fontSize = fontSize)
