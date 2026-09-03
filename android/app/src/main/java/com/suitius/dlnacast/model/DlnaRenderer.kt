package com.suitius.dlnacast.model

data class DlnaRenderer(
    val id: String,
    val name: String,
    val descriptionUrl: String,
    val host: String,
    val avTransportControlUrl: String,
    val renderingControlUrl: String?,
    val connectionManagerControlUrl: String?,
)
