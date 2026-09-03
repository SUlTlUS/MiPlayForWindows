package com.suitius.dlnacast.systemui

import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.service.CastStatus
import com.suitius.dlnacast.service.ReceiverControlState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SystemUiCastConfigTest {
    @Test
    fun defaultsPreserveExistingSystemPanelPlayback() {
        val config = SystemUiCastConfig()
        assertTrue(config.enabled)
        assertEquals(1_000_000, config.playbackDelayMicroseconds)
        assertEquals(listOf(AudioChannelRoute.Stereo), config.channelRoutes(1))
    }

    @Test
    fun delayIsClampedBeforeConversionToMicroseconds() {
        assertEquals(0, SystemUiCastConfig(playbackDelayMilliseconds = Int.MIN_VALUE).playbackDelayMicroseconds)
        assertEquals(250_000, SystemUiCastConfig(playbackDelayMilliseconds = 250).playbackDelayMicroseconds)
        assertEquals(2_000_000, SystemUiCastConfig(playbackDelayMilliseconds = Int.MAX_VALUE).playbackDelayMicroseconds)
    }

    @Test
    fun selectedChannelAppliesToEachReceiver() {
        AudioChannelRoute.entries.forEach { route ->
            val config = SystemUiCastConfig(channelRoute = route)
            assertEquals(listOf(route), config.channelRoutes(1))
            assertEquals(listOf(route, route), config.channelRoutes(2))
        }
    }

    @Test(expected = IllegalArgumentException::class)
    fun rejectEmptySelection() {
        SystemUiCastConfig().channelRoutes(0)
    }

    @Test(expected = IllegalArgumentException::class)
    fun rejectTooManySpeakers() {
        SystemUiCastConfig().channelRoutes(3)
    }

    @Test
    fun disabledAndIdleHidesAllDiscoveredSpeakers() {
        assertTrue(systemUiRenderers(listOf(speaker), CastStatus.Idle, emptySet(), false).isEmpty())
    }

    @Test
    fun disablingKeepsLiveReceiverAvailableToControlWithoutDiscovery() {
        val status = CastStatus.Streaming("speaker", "playing", receiverControls = listOf(
            ReceiverControlState("speaker", false, 20, "live-id", "192.168.1.2"),
        ))
        val visible = systemUiRenderers(emptyList(), status, status.activeRendererIds(emptyList()), false)
        assertEquals(listOf("live-id"), visible.map { it.id })
    }

    @Test
    fun activeReceiverAliasDoesNotCreateDuplicateRow() {
        val status = CastStatus.Streaming("speaker", "playing", receiverControls = listOf(
            ReceiverControlState("speaker", false, 20, "live-id", speaker.host),
        ))
        val visible = systemUiRenderers(listOf(speaker), status, status.activeRendererIds(emptyList()), false)
        assertEquals(listOf(speaker), visible)
    }

    @Test
    fun disabledPanelKeepsExistingInitializationVisible() {
        val status = CastStatus.Preparing("connecting")
        assertEquals(listOf(speaker), systemUiRenderers(listOf(speaker), status, setOf(speaker.id), false))
    }

    @Test
    fun enabledPanelShowsIdleSpeakersAgain() {
        assertEquals(listOf(speaker), systemUiRenderers(listOf(speaker), CastStatus.Idle, emptySet(), true))
    }

    private val speaker = DlnaRenderer("mdns-id", "speaker", "miplay://192.168.1.2",
        "192.168.1.2", "", null, null)
}
