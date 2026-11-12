# The Android app (step 9)

A small companion to the website, in [`android/`](../android): the places you care about on your home screen and lock
screen.

- **Saved locations**, chosen on a map: move the map under the centre dot, set the radius, name it. Tap one to move,
  resize or rename it.
- **Home-screen widget**: the nearest aircraft to one saved location, like the website's card (airline and logo,
  flight, route, distance, altitude, speed...). Tap it to refresh; tap its **⚙ HOME · 14:05** label to choose another
  location.
- **Lock-screen notification**: turn on a location's **bell** and a quiet notification shows the aircraft overhead
  (`easyJet EZY151G`, `LGW → BHD`, the airline's logo), updated in place as planes come and go, and gone when the sky
  is clear. No sound, no vibration. Aircraft on the ground don't count.

The app is its own anonymous owner of its saved locations (like a browser on the website, no account), so its
locations are separate from your browser's.

## How it stays current

Widgets can't keep a connection open, and Android runs background work at most every 15 minutes. So the server does
the watching:

```
phone ──"my widgets and bells show these locations" (PUT /api/devices/{id})──► API
                                                                              │ polls each one every 60 s
phone ◄──Firebase push: "the aircraft at Home is now …"── FCM ◄───────────────┘ only when it changes
```

- The phone registers the locations its widgets show plus the ones with the bell on, when that changes and once a
  day. The server forgets a phone it hasn't heard from in 7 days.
- The server polls those areas every 60 s, around the clock, and pushes to the phone **only when the aircraft it
  would show changes** (another one becomes nearest by a clear 10%, or none is left, or one arrives). The push carries
  the aircraft, so the widget and notification update without fetching anything.
- Caps, because adsb.lol is a free community service: 3 locations per person and 10 areas overall, oldest first.
  Beyond them a location gets no pushes; its widget still refreshes every 15 minutes and when tapped.
- Pushes are optional: a build without Firebase (below) still works, refreshing every 15 minutes and on tap.

## Building

Open the **`android/` folder** (not the repository root) in [Android Studio](https://developer.android.com/studio). It
downloads Gradle, the Android SDK pieces and the libraries on the first sync.

From a terminal (PowerShell), with Android Studio's own Java (the build needs Java 25):

```powershell
$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
cd android
.\gradlew.bat testDebugUnitTest   # unit tests (JVM, no phone needed)
.\gradlew.bat lintDebug           # Android Lint; report in app/build/reports/
.\gradlew.bat installDebug        # build and install on the connected phone(s)
```

With more than one device connected (say, the phone over USB and over Wi-Fi), pick one with
`$env:ANDROID_SERIAL = "<serial from adb devices>"`. `adb` is in the SDK, usually
`$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe`, and isn't on the PATH by default.

### Which server it talks to

The deployed Azure site, by default. For a local API, run the Compose stack, open a
[Cloudflare quick tunnel](../README.md#try-it-on-your-phone) (`cloudflared tunnel --url http://localhost:8080`) and
build with its address, in `~/.gradle/gradle.properties` (not committed):

```properties
flightradar.apiBaseUrl=https://<something>.trycloudflare.com/
```

It must be HTTPS (the app allows no plain HTTP) and end with `/`.

### Firebase (pushes)

Optional. The Gradle plugin that reads Firebase's config is only applied when `android/app/google-services.json`
exists, and that file is git-ignored (it names our Firebase project; not a secret, but this is a public repo). So a
fresh clone, and CI, build an app **without pushes**.

To build with pushes, download `google-services.json` from the Firebase console (Project settings → Your apps) into
`android/app/`. The server side (a service account key, stored as a GitHub secret) is in
[deploy-azure.md](deploy-azure.md#firebase-pushes-to-the-android-app).

Firebase addresses the phone by its **Firebase Installation ID**: FCM's registration tokens (`getToken()`,
`onNewToken()`) were deprecated in 2026. The manifest turns that on (`firebase_messaging_installation_id_enabled`).

## Checking it on a phone

Debug builds only (`run-as` works for debuggable apps). Never read `settings.preferences_pb`: it holds the owner token,
the only key to the app's saved locations.

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"

# The app's log since it started (WorkManager jobs, widget updates, Firebase):
& $adb logcat -v time --pid=$(& $adb shell pidof io.github.paulkelly.flightradar)

# Has Firebase given it an address, and has it registered with the server? (key names only)
& $adb shell run-as io.github.paulkelly.flightradar ls files/datastore
#   device.preferences_pb holds installation_id, push_token, registered_location_ids, registered_push_token

# The notification as Android sees it (title, text, large icon, timeout):
& $adb shell dumpsys notification --noredact | Select-String -Context 0,40 "pkg=io.github.paulkelly.flightradar"
```

On the server, the Azure guide's [log queries](deploy-azure.md#checking-a-deployment) show registrations
(`api/devices`), the 60 s fetches, and `Widget pushes: 1 locations changed, 1 sent, …`.

## Good to know

- **Backups.** Android's Auto Backup keeps the owner token and the bells, so a restored phone has its saved locations.
  It leaves out the installation ID (`device` DataStore file): a restored phone registers with the server as a new
  device rather than taking over the old one's pushes.
- **The widget has no "configure" screen in its manifest, on purpose.** The Pixel Launcher opens a declared one
  through a proxy; swiping that away in Recents loses its answer, and the launcher then locks the home screen (no
  long-press anywhere) until it restarts. So the widget is placed as "NOT SET UP" and opens its own settings when
  tapped. If a home screen ever gets stuck like that: `adb shell am force-stop com.google.android.apps.nexuslauncher`.
- **Notification permission** (Android 13+) is asked the first time a bell is turned on, not when the app starts. If
  it was refused, allow it in Settings → Apps → Flight Radar → Notifications.
- **The notification's small icon** is a one-colour plane: Android draws small icons from their shape only. The
  airline's logo is the large icon, on the right.
- **Maps** are [OpenFreeMap](https://openfreemap.org) vector tiles drawn by MapLibre: free and keyless, with no service
  guarantee. Keep MapLibre's attribution (ⓘ) button, and don't download tiles in bulk.

## CI and updates

Every pull request runs the **Android** job in [ci.yml](../.github/workflows/ci.yml): unit tests, lint and a debug
build, without Firebase. It always reports, but skips the build in seconds when nothing under `android/` changed.
[Dependabot](../.github/dependabot.yml) proposes library and Gradle updates weekly (minor and patch grouped); majors of
the Android Gradle plugin and Kotlin are left for a deliberate upgrade.
