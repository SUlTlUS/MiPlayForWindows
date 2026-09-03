package com.suitius.dlnacast.miplay

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class MiPlaySharedTimeOffsetTest {
    @Test
    fun lateReceiverReadsTheCommittedGroupValue() {
        val state = MiPlaySharedTimeOffsetState()

        assertEquals(1_000L, state.initialize(1_000L))
        assertEquals(1_000L, state.current())
        assertEquals(1_000L, state.initialize(1_000L))
    }

    @Test
    fun initialReceiversCannotDisagreeAboutTheGroupValue() {
        val state = MiPlaySharedTimeOffsetState()
        state.initialize(1_000L)

        assertThrows(IllegalStateException::class.java) {
            state.initialize(1_001L)
        }
    }

    @Test
    fun anOlderRuntimeCompletionCannotRollBackTheGroup() {
        val state = MiPlaySharedTimeOffsetState()
        state.initialize(1_000L)
        val older = MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.Resume,
            10L,
        )
        val newer = MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.TransportRecovery,
            20L,
        )

        state.prepareRuntimeUpdate(older, 2_000L)
        state.prepareRuntimeUpdate(newer, 3_000L)
        state.commitRuntimeUpdate(newer, 3_000L)
        state.commitRuntimeUpdate(older, 2_000L)

        assertEquals(3_000L, state.current())
    }

    @Test
    fun unknownOrMismatchedRuntimeUpdatesAreRejected() {
        val state = MiPlaySharedTimeOffsetState()
        val key = MiPlaySharedTimeOffsetUpdateKey(
            MiPlaySharedTimeOffsetUpdateKind.GroupResume,
            30L,
        )

        assertThrows(IllegalStateException::class.java) {
            state.current()
        }
        state.initialize(1_000L)
        state.prepareRuntimeUpdate(key, 4_000L)
        assertThrows(IllegalStateException::class.java) {
            state.commitRuntimeUpdate(key, 4_001L)
        }
    }
}
