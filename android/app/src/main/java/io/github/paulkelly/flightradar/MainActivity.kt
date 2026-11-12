package io.github.paulkelly.flightradar

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import io.github.paulkelly.flightradar.ui.locations.LocationsScreen
import io.github.paulkelly.flightradar.ui.theme.FlightRadarTheme

/** The app's one screen: saved locations. (The widget, step 9b, will be how the app is mostly used.) */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge() // draw behind the status and navigation bars; Scaffold pads its content clear of them
        setContent {
            FlightRadarTheme {
                LocationsScreen()
            }
        }
    }
}
