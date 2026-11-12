package io.github.paulkelly.flightradar.widget

import android.appwidget.AppWidgetManager
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ListItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.lifecycleScope
import io.github.paulkelly.flightradar.FlightRadarApp
import io.github.paulkelly.flightradar.MainActivity
import io.github.paulkelly.flightradar.data.ApiException
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.ui.theme.FlightRadarTheme
import java.io.IOException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch
import kotlinx.serialization.SerializationException

/**
 * "Which location should this widget watch?" Android opens it when the widget is added (and, on Android 12+, from the
 * widget's long-press menu: "reconfigurable" in flight_widget_info.xml). The protocol: answer RESULT_OK with the
 * widget's ID to keep the widget, anything else (backing out) and the launcher removes it again.
 */
class FlightWidgetConfigureActivity : ComponentActivity() {

    private val appWidgetId by lazy {
        intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Until a location is picked, the answer is "cancelled": leaving now removes the half-added widget.
        setResult(RESULT_CANCELED, resultIntent())
        if (appWidgetId == AppWidgetManager.INVALID_APPWIDGET_ID) {
            finish()
            return
        }

        enableEdgeToEdge()
        setContent {
            FlightRadarTheme {
                ChooseLocationScreen(
                    onChoose = ::choose,
                    onOpenApp = { startActivity(Intent(this, MainActivity::class.java)) },
                )
            }
        }
    }

    private fun choose(location: SavedLocation) {
        lifecycleScope.launch {
            val glanceId = GlanceAppWidgetManager(this@FlightWidgetConfigureActivity).getGlanceIdBy(appWidgetId)
            FlightWidgetState.setLocation(this@FlightWidgetConfigureActivity, glanceId, WidgetLocation.from(location))
            FlightWidget().update(this@FlightWidgetConfigureActivity, glanceId)
            FlightWidgetWorker.schedulePeriodic(this@FlightWidgetConfigureActivity)
            FlightWidgetWorker.refreshNow(this@FlightWidgetConfigureActivity, appWidgetId)
            setResult(RESULT_OK, resultIntent())
            finish()
        }
    }

    private fun resultIntent() = Intent().putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
}

/** The list of saved locations to choose from; reloaded on return from the app (where one may have been added). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ChooseLocationScreen(onChoose: (SavedLocation) -> Unit, onOpenApp: () -> Unit) {
    val repository = (LocalContext.current.applicationContext as FlightRadarApp)
        .container.locationsRepository
    var locations by remember { mutableStateOf<List<SavedLocation>?>(null) } // null = loading
    var error by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    // Runs each time the screen comes (back) to the front, like onResume.
    LifecycleResumeEffect(Unit) {
        val job = scope.launch {
            error = null
            locations = try {
                repository.list()
            } catch (e: CancellationException) {
                throw e
            } catch (_: IOException) {
                error = "Couldn't reach the server. Check your connection."
                emptyList()
            } catch (e: ApiException) {
                error = e.message
                emptyList()
            } catch (_: SerializationException) {
                error = "The server's answer didn't make sense. Is the app up to date?"
                emptyList()
            }
        }
        onPauseOrDispose { job.cancel() }
    }

    Scaffold(topBar = { TopAppBar(title = { Text("Choose a location") }) }) { innerPadding ->
        Box(
            Modifier
                .fillMaxSize()
                .padding(innerPadding),
        ) {
            val list = locations
            when {
                list == null -> CircularProgressIndicator(Modifier.align(Alignment.Center))

                list.isEmpty() -> Column(
                    Modifier
                        .align(Alignment.Center)
                        .padding(32.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text(
                        error ?: "No saved locations yet. Add one in Flight Radar, then come back here.",
                        textAlign = TextAlign.Center,
                    )
                    Button(onClick = onOpenApp) { Text("Open Flight Radar") }
                }

                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(list, key = { it.id }) { location ->
                        ListItem(
                            headlineContent = { Text(location.name) },
                            supportingContent = { Text("Aircraft within ${formatRadius(location.radiusKm)}") },
                            modifier = Modifier.clickable { onChoose(location) },
                        )
                        HorizontalDivider()
                    }
                }
            }
        }
    }
}
