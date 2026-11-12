package io.github.paulkelly.flightradar.widget

import android.appwidget.AppWidgetManager
import android.content.Context
import android.content.Intent
import androidx.compose.runtime.remember
import androidx.datastore.preferences.core.Preferences
import androidx.glance.GlanceId
import androidx.glance.GlanceTheme
import androidx.glance.action.ActionParameters
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.glance.appwidget.GlanceAppWidgetReceiver
import androidx.glance.appwidget.action.ActionCallback
import androidx.glance.appwidget.action.actionRunCallback
import androidx.glance.appwidget.action.actionStartActivity
import androidx.glance.appwidget.provideContent
import androidx.glance.currentState
import io.github.paulkelly.flightradar.FlightRadarApp

/**
 * The home-screen widget. Glance lets us write it like Compose, but it isn't Compose: a widget is drawn by the
 * launcher, in another process, from a description (RemoteViews) our code produces. So only Glance's own composables
 * work inside (`androidx.glance.*`, not `androidx.compose.material3.*`), there's no animation or scrolling state, and
 * our code runs only when the widget is updated, not continuously. Like baking a frame and handing it over, rather
 * than rendering every frame.
 */
class FlightWidget : GlanceAppWidget() {

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val appWidgetId = GlanceAppWidgetManager(context).getAppWidgetId(id)
        val logos = (context.applicationContext as FlightRadarApp).container.airlineLogos
        provideContent {
            // The widget's stored state; Glance redraws when it changes and we call update().
            val stored = FlightWidgetState.uiState(currentState<Preferences>(), System.currentTimeMillis())
            // The logo file, decoded once per airline rather than on every redraw.
            val airlineCode = stored.snapshot?.aircraft?.airlineCode
            val logo = remember(airlineCode) { airlineCode?.let(logos::load) }
            val state = stored.copy(logo = logo)
            // GlanceTheme: Material You colours from the wallpaper (Android 12+), sensible defaults before.
            GlanceTheme {
                FlightWidgetContent(
                    state = state,
                    onRefresh = actionRunCallback<RefreshAction>(),
                    onSetUp = actionStartActivity(configureIntent(context, appWidgetId)),
                )
            }
        }
    }

    /** Opens the "choose a location" screen for this widget (as when it was added). */
    private fun configureIntent(context: Context, appWidgetId: Int) =
        Intent(context, FlightWidgetConfigureActivity::class.java)
            .setAction(AppWidgetManager.ACTION_APPWIDGET_CONFIGURE)
            .putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
}

/** Tapping the widget: show "refreshing" straight away, then fetch in the background. */
class RefreshAction : ActionCallback {
    override suspend fun onAction(context: Context, glanceId: GlanceId, parameters: ActionParameters) {
        FlightWidgetState.markRefreshing(context, glanceId)
        FlightWidget().update(context, glanceId)
        FlightWidgetWorker.refreshNow(context, GlanceAppWidgetManager(context).getAppWidgetId(glanceId))
    }
}

/**
 * What Android talks to (named in AndroidManifest.xml): a BroadcastReceiver that hears "widget added / resized /
 * removed". Glance's base class does the drawing; we only start and stop the 15-minute refresh with the first widget
 * added and the last one removed.
 */
class FlightWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = FlightWidget()

    override fun onEnabled(context: Context) {
        super.onEnabled(context)
        FlightWidgetWorker.schedulePeriodic(context)
    }

    override fun onDisabled(context: Context) {
        super.onDisabled(context)
        FlightWidgetWorker.cancelPeriodic(context)
    }
}
