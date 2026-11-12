// Top-level build file: declares the plugins the modules use, with their versions, without applying them here.
plugins {
    alias(libs.plugins.android.application) apply false
    // AGP 9 compiles Kotlin itself ("built-in Kotlin"), with the Kotlin Gradle plugin it was built against unless a
    // newer one is on the build classpath. Declaring it here does that; the app module never applies it.
    alias(libs.plugins.kotlin.android) apply false
    alias(libs.plugins.kotlin.compose) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    alias(libs.plugins.google.services) apply false
}
