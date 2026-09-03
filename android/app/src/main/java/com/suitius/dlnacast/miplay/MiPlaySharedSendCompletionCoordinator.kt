package com.suitius.dlnacast.miplay

import kotlinx.coroutines.CompletableDeferred

/**
 * Coordinates completion of one shared live AU without serializing socket
 * writes. A late member is registered at the next live index, so its retained
 * replay never holds back the members that are already playing.
 */
internal class MiPlaySharedSendCompletionCoordinator(
    private val capacity: Int = 63,
) {
    private val lock = Any()
    private val participants = LinkedHashMap<Int, Long>()
    private val observations = LinkedHashMap<Int, Long>()
    private var waitingIndex: Long? = null
    private var waitingCompletion: CompletableDeferred<MiPlaySharedSendCompletion>? = null

    init {
        require(capacity in 2..63)
    }

    fun register(participantId: Int, liveStartAccessUnitIndex: Long) {
        require(participantId > 0)
        require(liveStartAccessUnitIndex >= 0)
        synchronized(lock) {
            check(participants.size < capacity) { "MiPlay shared send participant limit reached" }
            check(participantId !in participants) { "MiPlay participant is already registered" }
            participants[participantId] = liveStartAccessUnitIndex
        }
    }

    suspend fun await(accessUnitIndex: Long): MiPlaySharedSendCompletion {
        require(accessUnitIndex >= 0)
        val completion = CompletableDeferred<MiPlaySharedSendCompletion>()
        synchronized(lock) {
            check(waitingIndex == null) { "MiPlay send completion is already awaiting an AU" }
            waitingIndex = accessUnitIndex
            waitingCompletion = completion
            tryCompleteLocked()
        }
        return try {
            completion.await()
        } finally {
            synchronized(lock) {
                if (waitingCompletion === completion) {
                    waitingIndex = null
                    waitingCompletion = null
                    observations.clear()
                }
            }
        }
    }

    fun complete(participantId: Int, accessUnitIndex: Long, completedMicroseconds: Long) {
        require(completedMicroseconds >= 0)
        synchronized(lock) {
            val startIndex = participants[participantId] ?: return
            if (accessUnitIndex < startIndex) return
            check(waitingIndex == null || waitingIndex == accessUnitIndex) {
                "MiPlay receiver completed an unexpected shared AU"
            }
            check(observations.put(participantId, completedMicroseconds) == null) {
                "MiPlay receiver completed the same live AU twice"
            }
            tryCompleteLocked()
        }
    }

    fun unregister(participantId: Int) {
        synchronized(lock) {
            participants.remove(participantId)
            observations.remove(participantId)
            tryCompleteLocked()
        }
    }

    private fun tryCompleteLocked() {
        val index = waitingIndex ?: return
        val completion = waitingCompletion ?: return
        val eligible = participants.filterValues { startIndex -> startIndex <= index }.keys
        if (eligible.isEmpty()) {
            completion.completeExceptionally(
                IllegalStateException("No MiPlay receiver remains for shared AU $index"),
            )
            return
        }
        if (eligible.any { it !in observations }) return
        val completed = eligible.map { observations.getValue(it) }
        completion.complete(
            MiPlaySharedSendCompletion(
                minimumCompletedMicroseconds = completed.minOrNull()!!,
                maximumCompletedMicroseconds = completed.maxOrNull()!!,
            ),
        )
    }
}

internal data class MiPlaySharedSendCompletion(
    val minimumCompletedMicroseconds: Long,
    val maximumCompletedMicroseconds: Long,
) {
    val skewMicroseconds: Long
        get() = maximumCompletedMicroseconds - minimumCompletedMicroseconds
}
