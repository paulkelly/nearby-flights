package io.github.paulkelly.flightradar.ui.locations

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider.AndroidViewModelFactory.Companion.APPLICATION_KEY
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import io.github.paulkelly.flightradar.FlightRadarApp
import io.github.paulkelly.flightradar.data.ApiException
import io.github.paulkelly.flightradar.data.LocationsRepository
import io.github.paulkelly.flightradar.data.SavedLocation
import io.github.paulkelly.flightradar.data.SavedLocationRequest
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
    /** The "Add location" dialog is open. */
    val adding: Boolean = false,
    /** The position for the location being added, once found. */
    val position: Position? = null,
    val locating: Boolean = false,
    val saving: Boolean = false,
    /** A problem inside the dialog (it covers the screen, so it's shown there). */
    val addError: String? = null,
    /** A one-off message for the snackbar; cleared by [LocationsViewModel.messageShown]. */
    val message: String? = null,
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
) : ViewModel() {
    private val _state = MutableStateFlow(LocationsUiState())
    val state: StateFlow<LocationsUiState> = _state.asStateFlow()

    /** The position search in progress, so closing the dialog can stop it. */
    private var locating: Job? = null

    init {
        refresh()
    }

    fun refresh() {
        viewModelScope.launch {
            _state.update { it.copy(loading = true) }
            val failure = attempt { _state.update { it.copy(locations = repository.list()) } }
            _state.update { it.copy(loading = false, message = failure ?: it.message) }
        }
    }

    /** Opens the dialog, and looks for the position straight away if location is already allowed. */
    fun startAdding() {
        _state.update { it.copy(adding = true, position = null, addError = null) }
        if (currentLocation.hasPermission()) locate()
    }

    fun cancelAdding() {
        locating?.cancel()
        _state.update { it.copy(adding = false, position = null, locating = false, addError = null) }
    }

    /** Whether asking for the position needs the system's permission question first. */
    fun needsLocationPermission(): Boolean = !currentLocation.hasPermission()

    fun locationPermissionDenied() {
        _state.update {
            it.copy(addError = "Location permission was denied. Allow it in the app's settings to add a location here.")
        }
    }

    fun locate() {
        if (_state.value.locating) return
        locating = viewModelScope.launch {
            _state.update { it.copy(locating = true, addError = null) }
            val failure = attempt {
                val position = currentLocation.get()
                _state.update { it.copy(position = position) }
            }
            _state.update { it.copy(locating = false, addError = failure) }
        }
    }

    fun save(name: String, radiusKm: Double) {
        val position = _state.value.position ?: return
        if (_state.value.saving) return
        viewModelScope.launch {
            _state.update { it.copy(saving = true, addError = null) }
            val failure = attempt {
                repository.save(SavedLocationRequest(name.trim(), position.lat, position.lon, radiusKm))
            }
            if (failure == null) {
                _state.update { it.copy(saving = false, adding = false, position = null) }
                refresh() // the server is the source of truth (it rounds and orders): read the list again
            } else {
                _state.update { it.copy(saving = false, addError = failure) }
            }
        }
    }

    fun delete(location: SavedLocation) {
        viewModelScope.launch {
            val failure = attempt { repository.delete(location.id) }
            if (failure == null) refresh() else _state.update { it.copy(message = failure) }
        }
    }

    fun messageShown() {
        _state.update { it.copy(message = null) }
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
        /** How Android creates this ViewModel: with the app's shared services from [FlightRadarApp]. */
        val Factory = viewModelFactory {
            initializer {
                val container = (this[APPLICATION_KEY] as FlightRadarApp).container
                LocationsViewModel(container.locationsRepository, container.currentLocation)
            }
        }
    }
}
