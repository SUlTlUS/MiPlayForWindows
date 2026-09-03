package com.suitius.dlnacast.miplay

/** One PTS/PCR clock shared by every receiver in the media group. */
internal class MiPlaySharedOutputTimeline {
    private val lock = Any()
    private val retained = LinkedHashMap<Long, MiPlayOutputTimestamp>()
    private var lastAccessUnitIndex = -1L
    private var firstOutputMicroseconds = 0L
    private var previousOutputMicroseconds = 0L
    private var correctionMicroseconds = 0L
    private var correctionCount = 0L
    private var transportAdvance90Khz = 0L
    private var lastTransportRecoveryIndex = -1L
    private var lastTransportCompletionMicroseconds = 0L
    private var lastTransportRecoveryDelta = 0L

    fun getTimestamp(accessUnitIndex: Long, outputMicroseconds: Long): MiPlayOutputTimestamp {
        require(accessUnitIndex >= 0)
        require(outputMicroseconds >= 0)
        synchronized(lock) {
            retained[accessUnitIndex]?.let { return it }
            check(accessUnitIndex == lastAccessUnitIndex + 1) {
                "Shared MiPlay output timestamps must remain ordered"
            }
            if (lastAccessUnitIndex < 0) {
                firstOutputMicroseconds = outputMicroseconds
            } else {
                require(outputMicroseconds >= previousOutputMicroseconds)
            }

            val encoderPts = calculateEncoderPresentationTimeMicroseconds(accessUnitIndex)
            val transportAdvanceMicroseconds = transportAdvance90Khz * 1_000_000L / 90_000L
            var pts = encoderPts + correctionMicroseconds
            val effectivePts = pts + transportAdvanceMicroseconds
            val elapsed = outputMicroseconds - firstOutputMicroseconds
            val correction = (elapsed - effectivePts).coerceAtLeast(0L)
            val corrected = correction > FIX_THRESHOLD_MICROSECONDS
            if (corrected) {
                correctionMicroseconds += correction
                pts += correction
                correctionCount++
            }

            val timestamp = MiPlayOutputTimestamp(
                presentationTimestamp90Khz = pts * 90L / 1_000L,
                programClockReference27Mhz = createTabletProgramClockReference27Mhz(
                    outputMicroseconds,
                ),
                corrected = corrected,
                correctionCount = correctionCount,
                totalCorrectionMicroseconds = correctionMicroseconds,
            )
            previousOutputMicroseconds = outputMicroseconds
            lastAccessUnitIndex = accessUnitIndex
            retained[accessUnitIndex] = timestamp
            while (retained.size > RETAINED_TIMESTAMP_COUNT) {
                retained.remove(retained.keys.first())
            }
            return timestamp
        }
    }

    fun recoverAfterSend(
        completedAccessUnitIndex: Long,
        completedMicroseconds: Long,
    ): Long {
        require(completedAccessUnitIndex >= 0)
        require(completedMicroseconds >= 0)
        synchronized(lock) {
            if (completedAccessUnitIndex == lastTransportRecoveryIndex) {
                check(completedMicroseconds == lastTransportCompletionMicroseconds) {
                    "Receivers disagreed about shared send completion"
                }
                return lastTransportRecoveryDelta
            }
            check(completedAccessUnitIndex == lastAccessUnitIndex) {
                "Transport recovery must match the current shared output AU"
            }
            require(completedMicroseconds >= previousOutputMicroseconds)
            val effectivePts = calculateEncoderPresentationTimeMicroseconds(
                completedAccessUnitIndex,
            ) + correctionMicroseconds +
                transportAdvance90Khz * 1_000_000L / 90_000L
            val elapsed = completedMicroseconds - firstOutputMicroseconds
            val remainingLag = (elapsed - effectivePts).coerceAtLeast(0L)
            val advance90Khz = if (remainingLag > FIX_THRESHOLD_MICROSECONDS) {
                remainingLag * 90L / 1_000L
            } else {
                0L
            }
            transportAdvance90Khz += advance90Khz
            lastTransportRecoveryIndex = completedAccessUnitIndex
            lastTransportCompletionMicroseconds = completedMicroseconds
            lastTransportRecoveryDelta = advance90Khz
            return advance90Khz
        }
    }

    companion object {
        const val FIX_THRESHOLD_MICROSECONDS = 50L
        const val RETAINED_TIMESTAMP_COUNT = 64

        fun calculateEncoderPresentationTimeMicroseconds(accessUnitIndex: Long): Long {
            require(accessUnitIndex >= 0)
            return accessUnitIndex * 1_024L * 1_000_000L / 48_000L
        }

        fun createTabletProgramClockReference27Mhz(nativeNowMicroseconds: Long): Long {
            require(nativeNowMicroseconds >= 0)
            val modulus = (1L shl 33) * 300L
            return (nativeNowMicroseconds * 27L) % modulus
        }
    }
}

internal data class MiPlayOutputTimestamp(
    val presentationTimestamp90Khz: Long,
    val programClockReference27Mhz: Long,
    val corrected: Boolean,
    val correctionCount: Long,
    val totalCorrectionMicroseconds: Long,
)
