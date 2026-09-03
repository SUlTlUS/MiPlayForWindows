package com.suitius.dlnacast.systemui

import com.suitius.dlnacast.service.CastStatus
import com.suitius.dlnacast.service.ReceiverControlState
import org.junit.Assert.assertEquals
import org.junit.Test

class SystemUiBridgeStateTest {
    @Test
    fun preparingOnlyAppliesToRequestedSpeaker() {
        val status = CastStatus.Preparing("connecting")
        val activeIds = status.activeRendererIds(listOf("7503"))
        assertEquals("preparing", status.bridgeStateForRenderer("7503", activeIds))
        assertEquals("idle", status.bridgeStateForRenderer("6333", activeIds))
    }

    @Test
    fun terminalStatesClearSavedSelectionWithoutDiscovery() {
        listOf(CastStatus.Idle, CastStatus.Error("connection failed")).forEach { status ->
            val activeIds = status.activeRendererIds(listOf("7503", "6333"))
            assertEquals(emptySet<String>(), activeIds)
            assertEquals("idle", status.bridgeStateForRenderer("7503", activeIds))
        }
    }

    @Test
    fun streamingClearsPreparingForBothActiveSpeakers() {
        val status = streaming("7503", "6333")
        val activeIds = status.activeRendererIds(listOf("7503", "6333"))
        assertEquals("streaming", status.bridgeStateForRenderer("7503", activeIds))
        assertEquals("streaming", status.bridgeStateForRenderer("6333", activeIds))
    }

    @Test
    fun pausedRemainsSelectedWithoutPreparing() {
        val status = streaming("7503").copy(receiverPaused = true)
        val activeIds = status.activeRendererIds(listOf("7503"))
        assertEquals(setOf("7503"), activeIds)
        assertEquals("paused", status.bridgeStateForRenderer("7503", activeIds))
    }

    @Test
    fun runningConnectionStaysSelectedWhenSavedSelectionIsEmpty() {
        val status = streaming("7503")
        val activeIds = status.activeRendererIds(emptyList())
        assertEquals(setOf("7503"), activeIds)
        assertEquals("streaming", status.bridgeStateForRenderer("7503", activeIds))
    }

    @Test
    fun savedButNotConnectedSpeakerIsNotChecked() {
        val status = streaming("7503")
        val activeIds = status.activeRendererIds(listOf("7503", "6333"))
        assertEquals("idle", status.bridgeStateForRenderer("6333", activeIds))
    }

    @Test
    fun dlnaAndMdnsIdsForSameHostMatchTheActualConnection() {
        val status = CastStatus.Streaming(
            "speaker", "streaming",
            receiverControls = listOf(
                ReceiverControlState("speaker", false, 40, "uuid:7503", "192.168.10.5"),
            ),
        )
        val activeIds = status.activeRendererIds(emptyList())
        assertEquals("streaming", status.bridgeStateForRenderer(
            "miplay://7503._mi-connect._udp.local", activeIds, "192.168.10.5",
        ))
        assertEquals("idle", status.bridgeStateForRenderer(
            "miplay://6333._mi-connect._udp.local", activeIds, "192.168.10.6",
        ))
    }

    @Test
    fun selectionOrderComesFromConnectionsNotSavedPreferences() {
        val status = streaming("6333", "7503")
        assertEquals(listOf("6333", "7503"),
            status.activeRendererIds(listOf("7503", "6333")).toList())
    }

    private fun streaming(vararg rendererIds: String) = CastStatus.Streaming(
        "speakers", "streaming",
        receiverControls = rendererIds.map { ReceiverControlState(it, false, 40, it) },
    )
}
