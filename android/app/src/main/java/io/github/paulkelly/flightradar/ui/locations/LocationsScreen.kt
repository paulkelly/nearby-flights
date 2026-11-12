package io.github.paulkelly.flightradar.ui.locations

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableDoubleStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.location.CurrentLocation
import io.github.paulkelly.flightradar.location.Position
import io.github.paulkelly.flightradar.ui.theme.FlightRadarTheme

/** Longest name the API accepts (`SavedLocationRules.MaxNameLength`). */
private const val MAX_NAME_LENGTH = 50

/**
 * The stateful screen: connects the ViewModel and the system's permission question to [LocationsContent], which only
 * draws what it's given. Splitting them like this (like a React container and a presentational component) is what
 * lets the preview at the bottom render without a ViewModel, a network or a phone.
 */
@Composable
fun LocationsScreen(viewModel: LocationsViewModel = viewModel(factory = LocationsViewModel.Factory)) {
    // A StateFlow read as Compose state: the UI recomposes when it changes, and stops listening while the app is in
    // the background (the "WithLifecycle" part).
    val state by viewModel.state.collectAsStateWithLifecycle()
    val adding = rememberAddLocationActions(viewModel)

    LocationsContent(
        state = state,
        onAdd = adding.start,
        onLocate = adding.locate,
        onSave = viewModel::save,
        onCancelAdding = viewModel::cancelAdding,
        onDelete = viewModel::delete,
        onMessageShown = viewModel::messageShown,
    )
}

/** "Add location" and "Use my location", with Android's permission question first when it's needed. */
internal class AddLocationActions(val start: () -> Unit, val locate: () -> Unit)

/**
 * The add-a-location actions for [viewModel]. Shared by the app's screen and the widget's "choose a location" screen.
 */
@Composable
internal fun rememberAddLocationActions(viewModel: LocationsViewModel): AddLocationActions {
    // Asks Android's permission question; the answer arrives in the callback. Registered up front, like an event
    // handler, because the Activity may be recreated while the question is on screen.
    val askLocationPermission = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions(),
    ) { grants ->
        if (grants.values.any { it }) viewModel.locate() else viewModel.locationPermissionDenied()
    }
    return remember(viewModel, askLocationPermission) {
        AddLocationActions(
            start = {
                viewModel.startAdding()
                if (viewModel.needsLocationPermission()) askLocationPermission.launch(CurrentLocation.PERMISSIONS)
            },
            locate = {
                if (viewModel.needsLocationPermission()) {
                    askLocationPermission.launch(CurrentLocation.PERMISSIONS)
                } else {
                    viewModel.locate()
                }
            },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LocationsContent(
    state: LocationsUiState,
    onAdd: () -> Unit,
    onLocate: () -> Unit,
    onSave: (name: String, radiusKm: Double) -> Unit,
    onCancelAdding: () -> Unit,
    onDelete: (SavedLocation) -> Unit,
    onMessageShown: () -> Unit,
) {
    val snackbar = remember { SnackbarHostState() }
    // Runs when the message changes: shows it, waits until the snackbar goes, then marks it shown.
    LaunchedEffect(state.message) {
        state.message?.let {
            snackbar.showSnackbar(it)
            onMessageShown()
        }
    }

    // The location waiting for "Delete?" to be confirmed. Plain UI state, so it lives here, not in the ViewModel.
    var confirmingDelete by remember { mutableStateOf<SavedLocation?>(null) }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Flight Radar") }) },
        floatingActionButton = {
            ExtendedFloatingActionButton(onClick = onAdd) { Text("Add location") }
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

                state.locations.isEmpty() ->
                    Text(
                        "No saved locations yet.\nAdd the places where you'd like to know what's flying overhead.",
                        textAlign = TextAlign.Center,
                        modifier = Modifier
                            .align(Alignment.Center)
                            .padding(32.dp),
                    )

                // LazyColumn only composes the rows on screen, like a virtualised list.
                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(state.locations, key = { it.id }) { location ->
                        ListItem(
                            headlineContent = { Text(location.name) },
                            supportingContent = {
                                Text("${formatKm(location.radiusKm)} around ${formatLatLon(location.lat, location.lon)}")
                            },
                            trailingContent = {
                                TextButton(onClick = { confirmingDelete = location }) { Text("Delete") }
                            },
                        )
                        HorizontalDivider()
                    }
                }
            }
        }
    }

    if (state.adding) {
        AddLocationDialog(state = state, onLocate = onLocate, onSave = onSave, onDismiss = onCancelAdding)
    }

    confirmingDelete?.let { location ->
        AlertDialog(
            onDismissRequest = { confirmingDelete = null },
            title = { Text("Delete “${location.name}”?") },
            confirmButton = {
                TextButton(onClick = {
                    onDelete(location)
                    confirmingDelete = null
                }) { Text("Delete") }
            },
            dismissButton = { TextButton(onClick = { confirmingDelete = null }) { Text("Cancel") } },
        )
    }
}

/** The "Add a location" dialog: name, radius and position. Internal: the widget's set-up screen uses it too. */
@Composable
internal fun AddLocationDialog(
    state: LocationsUiState,
    onLocate: () -> Unit,
    onSave: (name: String, radiusKm: Double) -> Unit,
    onDismiss: () -> Unit,
) {
    // rememberSaveable: kept across rotation too (plain remember would lose what was typed).
    var name by rememberSaveable { mutableStateOf("") }
    var radiusKm by rememberSaveable { mutableDoubleStateOf(RadiusScale.DEFAULT_KM) }
    val canSave = name.isNotBlank() && state.position != null && !state.saving

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Add a location") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { if (it.length <= MAX_NAME_LENGTH) name = it },
                    label = { Text("Name") },
                    placeholder = { Text("e.g. Home") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )

                Column {
                    Text("Radius: ${formatKm(radiusKm)}")
                    Slider(
                        value = RadiusScale.fractionFor(radiusKm),
                        onValueChange = { radiusKm = RadiusScale.kmAt(it) },
                    )
                }

                when {
                    state.locating -> Row(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                        Text("Finding your location…")
                    }

                    state.position != null -> Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(describePosition(state.position), Modifier.weight(1f))
                        TextButton(onClick = onLocate) { Text("Update") }
                    }

                    else -> OutlinedButton(onClick = onLocate) { Text("Use my location") }
                }

                state.addError?.let {
                    Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onSave(name, radiusKm) }, enabled = canSave) {
                Text(if (state.saving) "Saving…" else "Save")
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

/** "10 km"; tidy radii are whole numbers, but the API allows fractions (e.g. one saved from the web app). */
private fun formatKm(km: Double): String =
    if (km % 1.0 == 0.0) "${km.toLong()} km" else "%.1f km".format(km)

private fun formatLatLon(lat: Double, lon: Double): String = "%.3f, %.3f".format(lat, lon)

/** Where the location will be saved, and roughly how exactly. "Approximate" fixes can be kilometres out. */
private fun describePosition(position: Position): String {
    val where = "Here: ${formatLatLon(position.lat, position.lon)}"
    val accuracy = position.accuracyM ?: return where
    return when {
        accuracy >= 1000 -> "$where (approximate, within ${"%.0f".format(accuracy / 1000)} km)"
        else -> "$where (within ${"%.0f".format(accuracy)} m)"
    }
}

@Preview(showBackground = true)
@Composable
private fun LocationsPreview() {
    FlightRadarTheme {
        LocationsContent(
            state = LocationsUiState(
                loading = false,
                locations = listOf(
                    SavedLocation("1", "Home", 53.35, -6.26, 10.0),
                    SavedLocation("2", "Dublin Airport", 53.421, -6.27, 5.0),
                ),
            ),
            onAdd = {},
            onLocate = {},
            onSave = { _, _ -> },
            onCancelAdding = {},
            onDelete = {},
            onMessageShown = {},
        )
    }
}
