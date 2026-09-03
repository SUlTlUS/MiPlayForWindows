package com.suitius.dlnacast.miplay

import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Test

class MiPlaySharedSendCompletionCoordinatorTest {
    @Test
    fun waitsForAllMembersThatAreLiveAtTheCurrentBoundary() = runBlocking {
        val coordinator = MiPlaySharedSendCompletionCoordinator()
        coordinator.register(1, 0)
        coordinator.register(2, 4)

        val waiting = async { coordinator.await(0) }
        assertFalse(waiting.isCompleted)
        coordinator.complete(1, 0, 100)

        val completed = waiting.await()
        assertEquals(100L, completed.minimumCompletedMicroseconds)
        assertEquals(100L, completed.maximumCompletedMicroseconds)
    }

    @Test
    fun aRemovedMemberNoLongerBlocksTheCurrentFrame() = runBlocking {
        val coordinator = MiPlaySharedSendCompletionCoordinator()
        coordinator.register(1, 0)
        coordinator.register(2, 0)
        val waiting = async { coordinator.await(0) }

        coordinator.complete(1, 0, 100)
        coordinator.unregister(2)

        assertEquals(100L, waiting.await().maximumCompletedMicroseconds)
    }

    @Test
    fun duplicateCompletionAndUnexpectedFramesAreRejected() {
        val coordinator = MiPlaySharedSendCompletionCoordinator()
        coordinator.register(1, 3)

        coordinator.complete(1, 2, 100)
        assertThrows(IllegalStateException::class.java) {
            coordinator.complete(1, 3, 100)
            coordinator.complete(1, 3, 101)
        }
    }
}
