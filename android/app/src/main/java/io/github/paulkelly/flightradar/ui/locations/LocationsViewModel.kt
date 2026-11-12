package io.github.paulkelly.flightradar.ui.locations

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider.AndroidViewModelFactory.Companion.APPLICATION_KEY
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import io.github.paulkelly.flightradar.FlightRadarApp
import io.github.paulkelly.flightradar.LocationEffects
import io.github.paulkelly.flightradar.data.ApiException
import io.github.paulkelly.flightradar.data.LocationsRepository
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.data.SavedLocationRequest
import io.github.paulkelly.flightradar.geo.LatLon
import io.github.paulkelly.flightradar.location.CurrentLocation
import io.github.paulkelly.flightradar.location.LocationUnavailableException
import io.github.paulkelly.flightradar.location.Position
import java.io.IOException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.serialization.SerializationException

/** Everything the locations screen shows. Immutable: each change is a new copy, so Compose can tell what changed. */
data class LocationsUiState(
    val locations: List<SavedLocation> = emptyList(),
    val loading: Boolean = true,
    /** The Add/Edit screen is open. */
    val adding: Boolean = false,
    /** The location being edited, or null when adding a new one. */
    val editing: SavedLocation? = null,
    /**
     * Where the location being added will be: the spot under the map's centre dot. Starts at the first saved location
     * (or Dublin Airport), then follows the device's position when found, and the map when the user moves it.
     */
    val position: Position? = null,
    /** A move of the map asked for by code (the device's position was found); the map follows it once. */
    val mapTarget: MapTarget? = null,
    val locating: Boolean = false,
    val saving: Boolean = false,
    /** A problem on the Add screen (it covers the list, so it's shown there). */
    val addError: String? = null,
    /** A one-off message for the snackbar; cleared by [LocationsViewModel.messageShown]. */
    val message: String? = null,
    /**
     * The location just added, until [LocationsViewModel.savedHandled]: the widget's set-up screen picks it for the
     * widget. A value in the state rather than a callback, so it isn't lost if the screen is recreated meanwhile.
     */
    val justSaved: SavedLocation? = null,
    /** IDs of the locations with the lock-screen notification on (step 9e): their bell is filled. */
    val notifying: Set<String> = emptySet(),
)

/**
 * The locations screen's state and actions. A ViewModel outlives the screen's UI: rotating the phone recreates the
 * Activity and its composables, but this (and a request it started) carries on. Like a game's model surviving a
 * change of view; the UI just renders [state] and calls the functions below.
 *
 * `viewModelScope` is a coroutine scope cancelled when the ViewModel is cleared (the screen is closed for good), so
 * no request outlives the screen that wanted it.
 */
class LocationsViewModel(
    private val repository: LocationsRepository,
    private val currentLocation: CurrentLocation,
    /**
     * The knock-on effects of edits and deletes (widgets keep a copy, notifications a name, the server what this
     * device registered), and the bells. Null in previews.
     */
    private val effects: LocationEffects? = null,
) : ViewModel() {
    private val _state = MutableStateFlow(LocationsUiState())
    val state: StateFlow<LocationsUiState> = _state.asStateFlow()

    /** The position search in progress, so closing the screen or moving the map can stop it. */
    private var locating: Job? = null

    init {
        refresh()
        effects?.let { effects ->
            viewModelScope.launch { effects.notifying.collect { ids -> _state.update { it.copy(notifying = ids) } } }
        }
    }

    /** The bell: the lock-screen notification for [location] on or off (the permission is the screen's business). */
    fun setNotifying(location: SavedLocation, enabled: Boolean) {
        viewModelScope.launch { effects?.setNotifying(location, enabled) }
    }

    /** The notification permission was refused: the bell stays off, and the snackbar says where to allow it. */
    fun notificationPermissionDenied() {
        _state.update {
            it.copy(message = "Notifications are off for Flight Radar: allow them in Settings to see aircraft on the lock screen.")
        }
    }

    fun refresh() {
        viewModelScope.launch {
            _state.update { it.copy(loading = true) }
            val failure = attempt { _state.update { it.copy(locations = repository.list()) } }
            _state.update { it.copy(loading = false, message = failure ?: it.message) }
        }
    }

    /**
     * Opens the Add screen at the first saved location (else Dublin Airport, like the web app), and looks for the
     * device's position straight away if location is already allowed: the map then flies there.
     */
    fun startAdding() {
        val start = _state.value.locations.firstOrNull()?.let { Position(it.lat, it.lon, null) } ?: DUBLIN_AIRPORT
        _state.update { it.copy(adding = true, editing = null, position = start, mapTarget = null, addError = null) }
        if (currentLocation.hasPermission()) locate()
    }

    /** Opens the same screen on an existing location: its own place, no position search (it's somewhere chosen). */
    fun startEditing(location: SavedLocation) {
        _state.update {
            it.copy(
                adding = true,
                editing = location,
                position = Position(location.lat, location.lon, null),
                mapTarget = null,
                addError = null,
            )
        }
    }

    fun cancelAdding() {
        locating?.cancel()
        _state.update {
            it.copy(adding = false, editing = null, position = null, mapTarget = null, locating = false, addError = null)
        }
    }

    /**
     * The user started moving the map: they're choosing the spot themselves, so a position search still running
     * (e.g. the one started on opening) mustn't yank the map away when it finishes. Like the web app's start-up.
     */
    fun mapMoveStarted() {
        if (locating?.isActive == true) {
            locating?.cancel()
            _state.update { it.copy(locating = false) }
        }
    }

    /** The map came to rest after the user moved it: the spot under the dot is the position now. */
    fun mapMoved(centre: LatLon) {
        _state.update { it.copy(position = Position(centre.lat, centre.lon, null), mapTarget = null) }
    }

    /** Whether asking for the position needs the system's permission question first. */
    fun needsLocationPermission(): Boolean = !currentLocation.hasPermission()

    fun locationPermissionDenied() {
        _state.update {
            it.copy(addError = "Location permission was denied: move the map to put the dot on the spot instead.")
        }
    }

    fun locate() {
        if (_state.value.locating) return
        locating = viewModelScope.launch {
            _state.update { it.copy(locating = true, addError = null) }
            val failure = attempt {
                val position = currentLocation.get()
                _state.update { it.copy(position = position, mapTarget = MapTarget(position.lat, position.lon)) }
            }
            _state.update { it.copy(locating = false, addError = failure) }
        }
    }

    fun save(name: String, radiusKm: Double) {
        val position = _state.value.position ?: return
        if (_state.value.saving) return
        viewModelScope.launch {
            _state.update { it.copy(saving = true, addError = null) }
            val editing = _state.value.editing
            val request = SavedLocationRequest(name.trim(), position.lat, position.lon, radiusKm)
            var saved: SavedLocation? = null
            val failure = attempt {
                if (editing == null) {
                    saved = repository.save(request)
                } else {
                    repository.update(editing.id, request).also { effects?.edited(it) }
                }
            }
            if (failure == null) {
                // justSaved only for a new location: it's what the widget's set-up screen picks.
                _state.update {
                    it.copy(
                        saving = false,
                        adding = false,
                        editing = null,
                        position = null,
                        mapTarget = null,
                        justSaved = saved,
                    )
                }
                refresh() // the server is the source of truth (it rounds and orders): read the list again
            } else {
                _state.update { it.copy(saving = false, addError = failure) }
            }
        }
    }

    fun delete(location: SavedLocation) {
        viewModelScope.launch {
            val failure = attempt {
                repository.delete(location.id)
                effects?.deleted(location)
            }
            if (failure == null) refresh() else _state.update { it.copy(message = failure) }
        }
    }

    fun messageShown() {
        _state.update { it.copy(message = null) }
    }

    fun savedHandled() {
        _state.update { it.copy(justSaved = null) }
    }

    /**
     * Runs [block]; returns null if it worked, else a sentence for the user. Only expected failures are caught:
     * anything else is a bug and should crash loudly. CancellationException must never be swallowed: it's how
     * coroutines stop (catching it would keep a cancelled request "running").
     */
    private suspend fun attempt(block: suspend () -> Unit): String? =
        try {
            block()
            null
        } catch (e: CancellationException) {
            throw e
        } catch (e: ApiException) {
            e.message
        } catch (_: IOException) {
            "Couldn't reach the server. Check your connection."
        } catch (_: SerializationException) {
            "The server's answer didn't make sense. Is the app up to date?"
        } catch (e: LocationUnavailableException) {
            e.message
        }

    companion object {
        /** Where the Add screen opens with no saved locations: Dublin Airport, the web app's default too. */
        private val DUBLIN_AIRPORT = Position(53.4213, -6.2701, null)

        /** How Android creates this ViewModel: with the app's shared services from [FlightRadarApp]. */
        val Factory = viewModelFactory {
            initializer {
                val container = (this[APPLICATION_KEY] as FlightRadarApp).container
                LocationsViewModel(container.locationsRepository, container.currentLocation, container.locationEffects)
            }
        }
    }
}
