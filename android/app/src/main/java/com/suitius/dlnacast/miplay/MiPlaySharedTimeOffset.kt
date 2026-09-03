package com.suitius.dlnacast.miplay

/**
 * Group-owned TIME_OFFSET state. A late receiver copies the committed group
 * value instead of deriving a new value from its own connection time.
 */
internal class MiPlaySharedTimeOffsetState {
    private val lock = Any()
    private val runtimeUpdates = LinkedHashMap<MiPlaySharedTimeOffsetUpdateKey, RuntimeUpdate>()
    private var initialized = false
    private var currentValue = 0L
    private var nextOrdinal = 0L
    private var committedOrdinal = 0L

    fun initialize(value: Long): Long {
        require(value >= 0) { "MiPlay TIME_OFFSET cannot be negative" }
        synchronized(lock) {
            if (initialized && currentValue != value) {
                error("Initial MiPlay receivers disagreed about the shared TIME_OFFSET")
            }
            currentValue = value
            initialized = true
            return currentValue
        }
    }

    fun current(): Long = synchronized(lock) {
        check(initialized) { "Shared MiPlay TIME_OFFSET is unavailable before media startup" }
        currentValue
    }

    fun prepareRuntimeUpdate(key: MiPlaySharedTimeOffsetUpdateKey, candidate: Long): Long {
        require(candidate >= 0) { "MiPlay TIME_OFFSET cannot be negative" }
        synchronized(lock) {
            check(initialized) { "Shared MiPlay TIME_OFFSET is unavailable before media startup" }
            return runtimeUpdates[key]?.value ?: run {
                val update = RuntimeUpdate(candidate, ++nextOrdinal)
                runtimeUpdates[key] = update
                while (runtimeUpdates.size > RETAINED_RUNTIME_UPDATE_COUNT) {
                    runtimeUpdates.remove(runtimeUpdates.keys.first())
                }
                update.value
            }
        }
    }

    fun commitRuntimeUpdate(key: MiPlaySharedTimeOffsetUpdateKey, value: Long) {
        synchronized(lock) {
            check(initialized) { "Shared MiPlay TIME_OFFSET is unavailable before media startup" }
            val update = runtimeUpdates[key]
            check(update?.value == value) {
                "MiPlay TIME_OFFSET update was not prepared by this group"
            }
            if (update.ordinal > committedOrdinal) {
                currentValue = value
                committedOrdinal = update.ordinal
            }
        }
    }

    private data class RuntimeUpdate(val value: Long, val ordinal: Long)

    private companion object {
        const val RETAINED_RUNTIME_UPDATE_COUNT = 64
    }
}

internal enum class MiPlaySharedTimeOffsetUpdateKind {
    TransportRecovery,
    Resume,
    GroupResume,
}

internal data class MiPlaySharedTimeOffsetUpdateKey(
    val kind: MiPlaySharedTimeOffsetUpdateKind,
    val mediaPosition: Long,
)
