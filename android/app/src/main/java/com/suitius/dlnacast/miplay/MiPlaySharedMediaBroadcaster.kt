package com.suitius.dlnacast.miplay

import com.suitius.dlnacast.audio.AudioChannelRoute
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Owns the group access-unit timeline. Receivers consume independently, while
 * a late receiver starts with the retained history and then naturally reaches
 * the next live boundary without delaying existing receivers.
 */
internal class MiPlaySharedMediaBroadcaster(
    private val capacity: Int = MAX_RECEIVERS,
) {
    private val lock = Any()
    private val members = LinkedHashMap<Int, Member>()
    private val history = ArrayDeque<MiPlaySharedMediaFrame>(RETAINED_ACCESS_UNITS)
    private var nextParticipantId = 0
    private var nextAccessUnitIndex = 0L

    init {
        require(capacity in 2..MAX_RECEIVERS)
    }

    fun register(route: AudioChannelRoute): MiPlaySharedMediaSubscription {
        synchronized(lock) {
            check(members.size < capacity) { "MiPlay shared media receiver limit reached" }
            val participantId = ++nextParticipantId
            val liveStartAccessUnitIndex = nextAccessUnitIndex
            val queue = LinkedBlockingQueue<MiPlaySharedMediaFrame>(QUEUE_CAPACITY)
            val retained = history.toList()
            val packetizerSnapshot = retained.firstOrNull()
                ?.packetizerSnapshotsBefore
                ?.get(route)
                ?: MiPlayMediaPacketizerSnapshot()
            check(retained.all { route in it.routeAccessUnits }) {
                "MiPlay history does not contain the requested audio route"
            }
            queue.addAll(retained)
            val subscription = MiPlaySharedMediaSubscription(
                owner = this,
                participantId = participantId,
                route = route,
                liveStartAccessUnitIndex = liveStartAccessUnitIndex,
                initialPacketizerSnapshot = packetizerSnapshot,
                queue = queue,
            )
            members[participantId] = Member(route, queue)
            return subscription
        }
    }

    fun publish(
        routeAccessUnits: Map<AudioChannelRoute, EncodedAccessUnit>,
        packetizerSnapshotsBefore: Map<AudioChannelRoute, MiPlayMediaPacketizerSnapshot>,
        outputTimestamp: MiPlayOutputTimestamp,
    ): MiPlaySharedMediaFrame {
        require(routeAccessUnits.isNotEmpty())
        synchronized(lock) {
            val missingRoute = members.values
                .map { it.route }
                .firstOrNull { it !in routeAccessUnits }
            check(missingRoute == null) { "MiPlay frame is missing route $missingRoute" }
            val frame = MiPlaySharedMediaFrame(
                accessUnitIndex = nextAccessUnitIndex++,
                routeAccessUnits = routeAccessUnits.toMap(),
                packetizerSnapshotsBefore = packetizerSnapshotsBefore.toMap(),
                outputTimestamp = outputTimestamp,
            )
            history.addLast(frame)
            while (history.size > RETAINED_ACCESS_UNITS) history.removeFirst()
            members.values.forEach { member ->
                check(member.queue.offer(frame)) {
                    "MiPlay receiver queue is not keeping up with the shared media timeline"
                }
            }
            return frame
        }
    }

    internal fun unregister(subscription: MiPlaySharedMediaSubscription) {
        synchronized(lock) {
            members.remove(subscription.participantId)?.queue?.clear()
        }
    }

    private data class Member(
        val route: AudioChannelRoute,
        val queue: LinkedBlockingQueue<MiPlaySharedMediaFrame>,
    )

    private companion object {
        const val MAX_RECEIVERS = 63
        const val RETAINED_ACCESS_UNITS = 47
        const val QUEUE_CAPACITY = RETAINED_ACCESS_UNITS + 32
    }
}

internal data class MiPlaySharedMediaFrame(
    val accessUnitIndex: Long,
    val routeAccessUnits: Map<AudioChannelRoute, EncodedAccessUnit>,
    val packetizerSnapshotsBefore: Map<AudioChannelRoute, MiPlayMediaPacketizerSnapshot>,
    val outputTimestamp: MiPlayOutputTimestamp,
)

internal class MiPlaySharedMediaSubscription internal constructor(
    private val owner: MiPlaySharedMediaBroadcaster,
    internal val participantId: Int,
    internal val route: AudioChannelRoute,
    internal val liveStartAccessUnitIndex: Long,
    internal val initialPacketizerSnapshot: MiPlayMediaPacketizerSnapshot,
    private val queue: LinkedBlockingQueue<MiPlaySharedMediaFrame>,
) : AutoCloseable {
    private val active = AtomicBoolean(true)

    fun take(): MiPlaySharedMediaFrame? {
        while (active.get()) {
            queue.poll(250, TimeUnit.MILLISECONDS)?.let { return it }
        }
        return null
    }

    override fun close() {
        if (!active.compareAndSet(true, false)) return
        owner.unregister(this)
    }
}
