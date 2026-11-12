package io.github.paulkelly.flightradar.widget

import android.appwidget.AppWidgetManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ListItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.viewmodel.compose.viewModel
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.ui.locations.AddLocationScreen
import io.github.paulkelly.flightradar.ui.locations.LocationsViewModel
import io.github.paulkelly.flightradar.ui.locations.rememberAddLocationActions
import io.github.paulkelly.flightradar.ui.theme.FlightRadarTheme
import kotlinx.coroutines.launch

/**
 * "Which location should this widget watch?" Opened by the widget itself (tapping "NOT SET UP", or its "⚙ HOME" label),
 * with the widget's ID in the intent. Not a launcher-run configuration screen: flight_widget_info.xml declares none,
 * so there's no answer to send back and nothing for the launcher to wait on.
 *
 * A new location can be added right here (the same dialog as the app's), and is then chosen for the widget: setting
 * up a widget never needs a trip into the app.
 */
class FlightWidgetConfigureActivity : ComponentActivity() {

    private val appWidgetId by lazy {
        intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (appWidgetId == AppWidgetManager.INVALID_APPWIDGET_ID) {
            finish()
            return
        }

        enableEdgeToEdge()
        setContent {
            FlightRadarTheme {
                ChooseLocationScreen(onChoose = ::choose)
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
            DeviceSyncWorker.syncSoon(this@FlightWidgetConfigureActivity) // the server watches it from now on
            finish()
        }
    }
}

/**
 * The saved locations to choose from, plus "Add location". Uses the app's LocationsViewModel (one ViewModel per
 * screen, so this one is separate from the app's, but the same code: loading, saving, errors).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ChooseLocationScreen(
    onChoose: (SavedLocation) -> Unit,
    viewModel: LocationsViewModel = viewModel(factory = LocationsViewModel.Factory),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val adding = rememberAddLocationActions(viewModel)

    // A location added here is the one this widget is for: choose it straight away.
    LaunchedEffect(state.justSaved) {
        state.justSaved?.let {
            viewModel.savedHandled()
            onChoose(it)
        }
    }

    // The Add screen replaces the list while it's open, as in the app.
    if (state.adding) {
        AddLocationScreen(
            state = state,
            onLocate = adding.locate,
            onSave = viewModel::save,
            onDismiss = viewModel::cancelAdding,
            onMapMoveStarted = viewModel::mapMoveStarted,
            onMapMoved = viewModel::mapMoved,
        )
        return
    }

    val snackbar = remember { SnackbarHostState() }
    LaunchedEffect(state.message) {
        state.message?.let {
            snackbar.showSnackbar(it)
            viewModel.messageShown()
        }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Choose a location") }) },
        floatingActionButton = {
            ExtendedFloatingActionButton(onClick = adding.start) { Text("Add location") }
        },
        snackbarHost = { SnackbarHost(snackbar) },
    ) { innerPadding ->
        Box(
            Modifier
                .fillMaxSize()
                .padding(innerPadding),
        ) {
            when {
                state.loading && state.locations.isEmpty() ->
                    CircularProgressIndicator(Modifier.align(Alignment.Center))

                state.locations.isEmpty() -> Text(
                    "No saved locations yet.\nAdd one for this widget to watch.",
                    textAlign = TextAlign.Center,
                    modifier = Modifier
                        .align(Alignment.Center)
                        .padding(32.dp),
                )

                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(state.locations, key = { it.id }) { location ->
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
