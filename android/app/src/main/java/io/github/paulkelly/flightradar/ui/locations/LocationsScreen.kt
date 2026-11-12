package io.github.paulkelly.flightradar.ui.locations

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconToggleButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import io.github.paulkelly.flightradar.R
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.geo.LatLon
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
    val setNotifying = rememberNotifyingAction(viewModel, state.locations)

    // Two screens without a navigation library: the Add screen replaces the list while it's open.
    if (state.adding) {
        AddLocationScreen(
            state = state,
            onLocate = adding.locate,
            onSave = viewModel::save,
            onDismiss = viewModel::cancelAdding,
            onMapMoveStarted = viewModel::mapMoveStarted,
            onMapMoved = viewModel::mapMoved,
        )
    } else {
        LocationsContent(
            state = state,
            onAdd = adding.start,
            onEdit = viewModel::startEditing,
            onDelete = viewModel::delete,
            onNotify = setNotifying,
            onMessageShown = viewModel::messageShown,
        )
    }
}

/**
 * The bell's action, with Android 13's notification permission asked the first time one is turned on (not when the
 * app starts: asked in context, people know what they're saying yes to). Refused: the bell stays off, and the
 * snackbar says where to allow it. Android only shows the question twice; after that the answer comes back "no" at
 * once, which ends up at the same message.
 */
@Composable
private fun rememberNotifyingAction(
    viewModel: LocationsViewModel,
    locations: List<SavedLocation>,
): (SavedLocation, Boolean) -> Unit {
    val context = LocalContext.current
    // The location waiting for the answer, by ID: saveable, as the Activity may be recreated while the question shows.
    var waitingId by rememberSaveable { mutableStateOf<String?>(null) }
    val askPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        val location = locations.firstOrNull { it.id == waitingId }
        waitingId = null
        when {
            !granted -> viewModel.notificationPermissionDenied()
            location != null -> viewModel.setNotifying(location, true)
        }
    }
    return { location, enabled ->
        when {
            !enabled -> viewModel.setNotifying(location, false)
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
                ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED -> {
                waitingId = location.id
                askPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
            }
            // Older Androids have no question, but notifications can be switched off for the app in its settings.
            !NotificationManagerCompat.from(context).areNotificationsEnabled() -> viewModel.notificationPermissionDenied()
            else -> viewModel.setNotifying(location, true)
        }
    }
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
    onEdit: (SavedLocation) -> Unit,
    onDelete: (SavedLocation) -> Unit,
    onNotify: (SavedLocation, Boolean) -> Unit,
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
                            // A plain icon, not a button: it marks the whole row as "tap to change", and sits away
                            // from Delete so it doesn't read as a second button. No description of its own: the
                            // row's click label says it (TalkBack: "double-tap to edit").
                            leadingContent = {
                                Icon(painterResource(R.drawable.ic_edit), contentDescription = null)
                            },
                            trailingContent = {
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    NotifyToggle(location, location.id in state.notifying, onNotify)
                                    TextButton(onClick = { confirmingDelete = location }) { Text("Delete") }
                                }
                            },
                            modifier = Modifier.clickable(onClickLabel = "edit") { onEdit(location) },
                        )
                        HorizontalDivider()
                    }
                }
            }
        }
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

/**
 * The bell: the lock-screen notification for this location (step 9e). A real toggle button, unlike the row's pencil:
 * filled when on, an outline when off. TalkBack reads it as a switch, "on" or "off".
 */
@Composable
private fun NotifyToggle(location: SavedLocation, on: Boolean, onNotify: (SavedLocation, Boolean) -> Unit) {
    IconToggleButton(checked = on, onCheckedChange = { onNotify(location, it) }) {
        Icon(
            painterResource(if (on) R.drawable.ic_notifications else R.drawable.ic_notifications_none),
            contentDescription = "Lock-screen notification for ${location.name}",
        )
    }
}

/**
 * The full-screen "Add a location" (or "Edit location" when [LocationsUiState.editing] is set): a map with a fixed
 * dot in the middle (move the map to put it on the spot) and the search circle, then name, radius and "Use my
 * location". Internal: the widget's set-up screen uses it too.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun AddLocationScreen(
    state: LocationsUiState,
    onLocate: () -> Unit,
    onSave: (name: String, radiusKm: Double) -> Unit,
    onDismiss: () -> Unit,
    onMapMoveStarted: () -> Unit,
    onMapMoved: (LatLon) -> Unit,
) {
    // rememberSaveable: kept across rotation too (plain remember would lose what was typed). Editing starts from the
    // location's own values.
    var name by rememberSaveable { mutableStateOf(state.editing?.name ?: "") }
    var radiusKm by rememberSaveable { mutableDoubleStateOf(state.editing?.radiusKm ?: RadiusScale.DEFAULT_KM) }
    // Counts slider releases: each one asks the map to zoom so the whole circle fits.
    var zoomToFit by remember { mutableIntStateOf(0) }
    val canSave = name.isNotBlank() && state.position != null && !state.saving

    BackHandler(onBack = onDismiss) // the system Back gesture closes this screen, not the whole Activity

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (state.editing == null) "Add a location" else "Edit location") },
                navigationIcon = { TextButton(onClick = onDismiss) { Text("Cancel") } },
            )
        },
    ) { innerPadding ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(innerPadding)
                // Scaffold's padding includes the navigation bar, which the keyboard covers: don't count it twice.
                .consumeWindowInsets(innerPadding)
                .imePadding(),
        ) {
            MapPicker(
                start = state.position?.let { LatLon(it.lat, it.lon) } ?: LatLon(0.0, 0.0),
                target = state.mapTarget,
                radiusKm = radiusKm,
                zoomToFit = zoomToFit,
                onGestureStart = onMapMoveStarted,
                onCentreChosen = onMapMoved,
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth(),
            )

            Column(
                Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
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
                        onValueChangeFinished = { zoomToFit++ },
                    )
                }

                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        state.position?.let(::describePosition) ?: "",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f),
                    )
                    if (state.locating) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    } else {
                        TextButton(onClick = onLocate) { Text("Use my location") }
                    }
                }

                state.addError?.let {
                    Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
                }

                Button(
                    onClick = { onSave(name, radiusKm) },
                    enabled = canSave,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text(if (state.saving) "Saving…" else "Save")
                }
            }
        }
    }
}

/** "10 km"; tidy radii are whole numbers, but the API allows fractions (e.g. one saved from the web app). */
private fun formatKm(km: Double): String =
    if (km % 1.0 == 0.0) "${km.toLong()} km" else "%.1f km".format(km)

private fun formatLatLon(lat: Double, lon: Double): String = "%.3f, %.3f".format(lat, lon)

/**
 * Where the location will be saved. Straight after "Use my location" also how exactly: "approximate" fixes can be
 * kilometres out, which is worth knowing before saving (move the map to correct it).
 */
private fun describePosition(position: Position): String {
    val accuracy = position.accuracyM ?: return formatLatLon(position.lat, position.lon)
    return when {
        accuracy >= 1000 -> "Your location (approximate, within ${"%.0f".format(accuracy / 1000)} km)"
        else -> "Your location (within ${"%.0f".format(accuracy)} m)"
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
            onEdit = {},
            onDelete = {},
            onNotify = { _, _ -> },
            onMessageShown = {},
        )
    }
}
