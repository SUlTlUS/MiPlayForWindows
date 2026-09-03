package com.suitius.dlnacast.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val LightColors = lightColorScheme(
    primary = Color(0xFF1D4ED8),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFDBEAFE),
    onPrimaryContainer = Color(0xFF172554),
    secondary = Color(0xFFC2410C),
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFFFEDD5),
    onSecondaryContainer = Color(0xFF431407),
    background = Color(0xFFF8FAFC),
    onBackground = Color(0xFF0F172A),
    surface = Color(0xFFFFFFFF),
    onSurface = Color(0xFF0F172A),
    surfaceVariant = Color(0xFFE2E8F0),
    onSurfaceVariant = Color(0xFF334155),
    error = Color(0xFFB91C1C),
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFF8AB4FF),
    onPrimary = Color(0xFF082F73),
    primaryContainer = Color(0xFF173E80),
    onPrimaryContainer = Color(0xFFD9E7FF),
    secondary = Color(0xFFFFB68B),
    onSecondary = Color(0xFF5B1A00),
    secondaryContainer = Color(0xFF7B2907),
    onSecondaryContainer = Color(0xFFFFDBCA),
    background = Color(0xFF09111F),
    onBackground = Color(0xFFE5EAF2),
    surface = Color(0xFF111B2B),
    onSurface = Color(0xFFE5EAF2),
    surfaceVariant = Color(0xFF243246),
    onSurfaceVariant = Color(0xFFC0CAD8),
    error = Color(0xFFFFB4AB),
)

@Composable
fun DLNACastTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (isSystemInDarkTheme()) DarkColors else LightColors,
        content = content,
    )
}
