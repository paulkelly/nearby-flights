plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    // Generates JSON (de)serializers for @Serializable classes at compile time: no reflection at run time, like
    // System.Text.Json's source generators.
    alias(libs.plugins.kotlin.serialization)
}

// Where the app finds the API (the site's origin; the app adds /api/...). Defaults to the Azure deployment. Override
// it for a build with a Gradle property, e.g. in ~/.gradle/gradle.properties (not committed):
//   flightradar.apiBaseUrl=https://<something>.trycloudflare.com/
val apiBaseUrl = providers.gradleProperty("flightradar.apiBaseUrl")
    .getOrElse("https://flightradar-web.delightfulforest-82237f6b.northeurope.azurecontainerapps.io/")

android {
    namespace = "io.github.paulkelly.flightradar"
    compileSdk {
        version = release(37)
    }

    defaultConfig {
        applicationId = "io.github.paulkelly.flightradar"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"

        // Becomes a constant in the generated BuildConfig class: BuildConfig.API_BASE_URL.
        buildConfigField("String", "API_BASE_URL", "\"$apiBaseUrl\"")
    }

    buildTypes {
        release {
            optimization {
                enable = true
                packageScope = setOf("androidx.**", "kotlin.**", "kotlinx.**")
            }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildFeatures {
        compose = true
        buildConfig = true // off by default since AGP 8: needed for API_BASE_URL above
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.androidx.glance.appwidget) // the home-screen widget, written like Compose
    implementation(libs.androidx.work.runtime.ktx) // its background refresh
    implementation(libs.retrofit)
    implementation(libs.retrofit.converter.kotlinx.serialization)
    implementation(libs.okhttp)
    implementation(libs.play.services.location)
    implementation(libs.kotlinx.serialization.json)

    testImplementation(libs.junit)
    testImplementation(libs.okhttp.mockwebserver)
    testImplementation(libs.kotlinx.coroutines.test)

    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.junit)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
