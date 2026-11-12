package io.github.paulkelly.flightradar.widget

import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import androidx.glance.GlanceModifier
import androidx.glance.GlanceTheme
import androidx.glance.action.Action
import androidx.glance.action.clickable
import androidx.glance.appwidget.cornerRadius
import androidx.glance.background
import androidx.glance.layout.Column
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.padding
import androidx.glance.text.Text
import androidx.glance.text.TextStyle

// =====================================================================================================================
// EXERCISE (step 9b): the widget's layout. Replace the body of FlightWidgetContent below.
//
// What you have:
// - state.location       what it watches (name, radiusKm), or null: not set up yet → show a "choose a location" prompt
//                        that runs onSetUp. (Normally set up when added, so this is rare.)
// - state.snapshot       the latest data, or null before the first refresh:
//     .aircraft          the one to show, or null = none in range. title ("Ryanair"), callsign ("RYR2DQ" or null),
//                        route ("DUB → STN", "From DUB" or null), aircraftType ("B738"), distanceKm, altitudeFt,
//                        onGround
//     .count             all aircraft in range
//     .updatedAtMs       when it was fetched
// - state.error          why the last refresh failed ("No connection"), or null; snapshot is then older data
// - state.refreshing     a refresh is running
// - onRefresh, onSetUp   Actions: GlanceModifier.clickable(onRefresh) on what should refresh when tapped
// - helpers in WidgetModel.kt: formatDistance(km) → "3.4 km", formatAltitude(aircraft) → "36,000 ft"/"On the ground"
//   For a time: java.text.DateFormat.getTimeInstance(DateFormat.SHORT).format(java.util.Date(updatedAtMs)) → "14:05"
//
// Glance's building blocks (all androidx.glance.*, NOT androidx.compose.material3 / foundation):
// - layout: Column, Row, Box, Spacer; modifiers GlanceModifier.fillMaxSize()/fillMaxWidth()/padding(…)/
//   defaultWeight() (Row/Column children: take the remaining space)/size(…)
// - text: Text(text, style = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.Bold, color = …), maxLines = 1)
// - colours: GlanceTheme.colors.widgetBackground / onSurface / onSurfaceVariant / primary / error … (Material You)
// - GlanceModifier.background(colour), .cornerRadius(16.dp) (rounded on Android 12+), .clickable(action)
// - Image(provider = ImageProvider(R.drawable.x), contentDescription = …) if you want an icon
//
// Sizes: a widget can be resized. FlightWidget can set `override val sizeMode = SizeMode.Responsive(setOf(...))` and
// LocalSize.current tells you which size you're drawing; e.g. drop the route line when it's only one cell tall.
// Start simple: one layout that works at the default 3×2 cells.
//
// To try a change: Run the app (or .\gradlew.bat installDebug); the widget redraws on its next update, so tap it.
// =====================================================================================================================

/** The widget's layout: draws [state]. (A stand-in until the exercise replaces it: just the data, as plain text.) */
@Composable
fun FlightWidgetContent(state: FlightWidgetUiState, onRefresh: Action, onSetUp: Action) {
    val location = state.location
    Column(
        GlanceModifier
            .fillMaxSize()
            .background(GlanceTheme.colors.widgetBackground)
            .cornerRadius(16.dp)
            .padding(12.dp)
            .clickable(if (location == null) onSetUp else onRefresh),
    ) {
        val text = TextStyle(color = GlanceTheme.colors.onSurface)
        if (location == null) {
            Text("Tap to choose a location", style = text)
            return@Column
        }
        Text(location.name, style = text)
        val aircraft = state.snapshot?.aircraft
        Text(
            when {
                state.snapshot == null -> "Loading…"
                aircraft == null -> "No aircraft within ${formatDistance(location.radiusKm)}"
                else -> listOfNotNull(aircraft.title, aircraft.route, formatDistance(aircraft.distanceKm), formatAltitude(aircraft))
                    .joinToString(" · ")
            },
            style = text,
        )
        if (state.refreshing) Text("Refreshing…", style = text)
        state.error?.let { Text(it, style = text) }
    }
}
