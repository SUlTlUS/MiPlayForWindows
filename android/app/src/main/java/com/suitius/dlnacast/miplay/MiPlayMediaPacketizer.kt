package com.suitius.dlnacast.miplay

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.ceil
import kotlin.math.min

internal data class MiPlayPacketizedAccessUnit(
    val wireFrames: List<ByteArray>,
    val containsProgramTables: Boolean,
)

internal data class MiPlayMpegTsMuxerSnapshot(
    val patContinuity: Int = 1,
    val pmtContinuity: Int = 1,
    val audioContinuity: Int = 0,
)

internal data class MiPlayMediaPacketizerSnapshot(
    val accessUnitIndex: Long = 0L,
    val sequence: Int = 0,
    val muxer: MiPlayMpegTsMuxerSnapshot = MiPlayMpegTsMuxerSnapshot(),
)

internal class MiPlayMediaPacketizer(
    snapshot: MiPlayMediaPacketizerSnapshot = MiPlayMediaPacketizerSnapshot(),
) {
    private val muxer = MpegTsMuxer(snapshot.muxer)
    private var sequence = snapshot.sequence
    private var accessUnitIndex = snapshot.accessUnitIndex

    fun snapshot(): MiPlayMediaPacketizerSnapshot = MiPlayMediaPacketizerSnapshot(
        accessUnitIndex = accessUnitIndex,
        sequence = sequence,
        muxer = muxer.snapshot(),
    )

    fun packetize(
        rawAac: ByteArray,
        presentationTimeUs: Long,
        outputTimestamp: MiPlayOutputTimestamp? = null,
    ): MiPlayPacketizedAccessUnit {
        val adts = prependAdts(rawAac)
        val includeTables = accessUnitIndex == 0L ||
            (accessUnitIndex >= 13L && (accessUnitIndex - 13L) % 5L == 0L)
        val timestamp90Khz = outputTimestamp?.presentationTimestamp90Khz
            ?: capturedTimestamp90Khz(accessUnitIndex)
        val pcr27Mhz = if (includeTables) {
            outputTimestamp?.programClockReference27Mhz
                ?: ((presentationTimeUs.coerceAtLeast(0L) * 27L) % ((1L shl 33) * 300L))
        } else {
            null
        }
        val transportStream = muxer.mux(adts, timestamp90Khz, pcr27Mhz)
        val maximumPayload = 7 * 188
        val fragmentCount = ceil(transportStream.size / maximumPayload.toDouble()).toInt()
        val wireFrames = ArrayList<ByteArray>(fragmentCount)
        repeat(fragmentCount) { index ->
            val offset = index * maximumPayload
            val length = min(maximumPayload, transportStream.size - offset)
            val rtp = encodeRtp(
                sequence = sequence,
                timestamp = timestamp90Khz,
                payload = transportStream.copyOfRange(offset, offset + length),
                marker = index == fragmentCount - 1,
            )
            sequence = (sequence + 1) and 0xffff
            wireFrames += encodeDollarFrame(rtp)
        }
        accessUnitIndex++
        return MiPlayPacketizedAccessUnit(wireFrames, includeTables)
    }

    private fun capturedTimestamp90Khz(index: Long): Long {
        val capturedLast = MiPlayStartupPacing.dueMilliseconds.lastIndex.toLong()
        val quantized = { value: Long -> value * 21_333L * 90_000L / 1_000_000L }
        return if (index <= capturedLast) {
            quantized(index)
        } else {
            quantized(capturedLast) + (index - capturedLast) * 1_920L
        }
    }
}

internal object MiPlayStartupPacing {
    val dueMilliseconds = doubleArrayOf(
        0.000, 0.000, 0.812, 0.812, 3.463, 4.230,
        20.360, 22.199, 23.864, 40.903, 42.905, 62.727,
        84.784, 111.306, 128.829, 148.735, 170.391, 202.805,
        211.654,
    )
    const val nominalAccessUnitMilliseconds = 1024.0 / 48_000.0 * 1_000.0

    fun dueAfterMilliseconds(accessUnitsSent: Long): Double =
        if (accessUnitsSent < dueMilliseconds.size) {
            dueMilliseconds[accessUnitsSent.toInt()]
        } else {
            dueMilliseconds.last() +
                (accessUnitsSent - dueMilliseconds.lastIndex) * nominalAccessUnitMilliseconds
        }
}

internal fun prependAdts(rawAac: ByteArray): ByteArray {
    val completeLength = rawAac.size + 7
    require(completeLength in 7..0x1fff) { "AAC access unit does not fit an ADTS frame" }
    val packet = ByteArray(completeLength)
    packet[0] = 0xff.toByte()
    packet[1] = 0xf9.toByte()
    packet[2] = 0x4c
    packet[3] = (0x80 + (completeLength shr 11)).toByte()
    packet[4] = ((completeLength and 0x7ff) shr 3).toByte()
    packet[5] = (((completeLength and 7) shl 5) + 0x1f).toByte()
    packet[6] = 0xfc.toByte()
    rawAac.copyInto(packet, 7)
    return packet
}

private fun encodeRtp(sequence: Int, timestamp: Long, payload: ByteArray, marker: Boolean): ByteArray {
    require(payload.isNotEmpty() && payload.size % 188 == 0 && payload.size <= 7 * 188)
    return ByteBuffer.allocate(12 + payload.size)
        .order(ByteOrder.BIG_ENDIAN)
        .put(0x80.toByte())
        .put(((if (marker) 0x80 else 0) or 33).toByte())
        .putShort(sequence.toShort())
        .putInt(timestamp.toInt())
        .putInt(0xdead_beef.toInt())
        .put(payload)
        .array()
}

private fun encodeDollarFrame(payload: ByteArray): ByteArray {
    require(payload.isNotEmpty() && payload.size <= 0x00ff_ffff)
    val frame = ByteArray(payload.size + 4)
    frame[0] = 0x24
    frame[1] = (payload.size shr 16).toByte()
    frame[2] = (payload.size shr 8).toByte()
    frame[3] = payload.size.toByte()
    payload.copyInto(frame, 4)
    return frame
}

private class MpegTsMuxer(
    initialPatContinuity: Int,
    initialPmtContinuity: Int,
    initialAudioContinuity: Int,
) {
    constructor(snapshot: MiPlayMpegTsMuxerSnapshot) : this(
        snapshot.patContinuity,
        snapshot.pmtContinuity,
        snapshot.audioContinuity,
    )

    private var patContinuity = initialPatContinuity
    private var pmtContinuity = initialPmtContinuity
    private var audioContinuity = initialAudioContinuity

    fun snapshot() = MiPlayMpegTsMuxerSnapshot(
        patContinuity = patContinuity,
        pmtContinuity = pmtContinuity,
        audioContinuity = audioContinuity,
    )

    fun mux(adts: ByteArray, presentationTimestamp90Khz: Long, pcr27Mhz: Long?): ByteArray {
        require(adts.size >= 7 && adts[0] == 0xff.toByte() && (adts[1].toInt() and 0xf0) == 0xf0)
        val packets = mutableListOf<ByteArray>()
        if (pcr27Mhz != null) {
            packets += buildPat()
            packets += buildPmt()
            packets += buildPcr(pcr27Mhz)
        }
        packetizePes(buildPes(adts, presentationTimestamp90Khz), packets)
        return ByteArrayOutputStream(packets.size * 188).use { output ->
            packets.forEach(output::write)
            output.toByteArray()
        }
    }

    private fun buildPat(): ByteArray {
        val section = byteArrayOf(
            0x00, 0xb0.toByte(), 0x0d, 0x00, 0x00, 0xc3.toByte(), 0x00, 0x00,
            0x00, 0x01, 0xe1.toByte(), 0x00,
            0x00, 0x00, 0x00, 0x00,
        )
        writeIntBigEndian(section, 12, mpegCrc32(section, 12).toInt())
        val packet = psiPacket(0x0000, section, patContinuity)
        patContinuity = (patContinuity + 1) and 0x0f
        return packet
    }

    private fun buildPmt(): ByteArray {
        val section = byteArrayOf(
            0x02, 0xb0.toByte(), 0x12, 0x00, 0x01, 0xc3.toByte(), 0x00, 0x00,
            0xf0.toByte(), 0x00, 0xf0.toByte(), 0x00, 0x0f, 0xf1.toByte(), 0x00,
            0xf0.toByte(), 0x00,
            0x00, 0x00, 0x00, 0x00,
        )
        writeIntBigEndian(section, 17, mpegCrc32(section, 17).toInt())
        val packet = psiPacket(0x0100, section, pmtContinuity)
        pmtContinuity = (pmtContinuity + 1) and 0x0f
        return packet
    }

    private fun psiPacket(pid: Int, section: ByteArray, continuity: Int): ByteArray {
        val packet = stuffedPacket()
        writeTsHeader(packet, pid, payloadUnitStart = true, adaptationFieldControl = 1, continuity)
        packet[4] = 0
        section.copyInto(packet, 5)
        return packet
    }

    private fun buildPcr(clock27Mhz: Long): ByteArray {
        val packet = stuffedPacket()
        writeTsHeader(packet, 0x1000, payloadUnitStart = true, adaptationFieldControl = 2, continuity = 0)
        packet[4] = 183.toByte()
        packet[5] = 0x10
        val modulus = (1L shl 33) * 300L
        val pcr = clock27Mhz % modulus
        val base = pcr / 300L
        val extension = pcr % 300L
        packet[6] = (base shr 25).toByte()
        packet[7] = (base shr 17).toByte()
        packet[8] = (base shr 9).toByte()
        packet[9] = (base shr 1).toByte()
        packet[10] = (((base and 1L) shl 7) or 0x7e or (extension shr 8)).toByte()
        packet[11] = extension.toByte()
        return packet
    }

    private fun buildPes(adts: ByteArray, timestamp90Khz: Long): ByteArray {
        val pesPacketLength = 3 + 7 + adts.size
        require(pesPacketLength <= 0xffff)
        val pes = ByteArray(6 + pesPacketLength)
        pes[0] = 0
        pes[1] = 0
        pes[2] = 1
        pes[3] = 0xc0.toByte()
        pes[4] = (pesPacketLength shr 8).toByte()
        pes[5] = pesPacketLength.toByte()
        pes[6] = 0x84.toByte()
        pes[7] = 0x80.toByte()
        pes[8] = 7
        writePts(pes, 9, timestamp90Khz)
        pes[14] = 0xff.toByte()
        pes[15] = 0xff.toByte()
        adts.copyInto(pes, 16)
        return pes
    }

    private fun packetizePes(pes: ByteArray, destination: MutableList<ByteArray>) {
        var offset = 0
        var first = true
        while (offset < pes.size) {
            val remaining = pes.size - offset
            val payloadLength = min(184, remaining)
            val packet = stuffedPacket()
            if (payloadLength == 184) {
                writeTsHeader(packet, 0x1100, first, 1, audioContinuity)
                pes.copyInto(packet, 4, offset, offset + payloadLength)
            } else {
                writeTsHeader(packet, 0x1100, first, 3, audioContinuity)
                val adaptationFieldLength = 183 - payloadLength
                packet[4] = adaptationFieldLength.toByte()
                if (adaptationFieldLength > 0) packet[5] = 0
                val payloadOffset = 5 + adaptationFieldLength
                pes.copyInto(packet, payloadOffset, offset, offset + payloadLength)
            }
            destination += packet
            audioContinuity = (audioContinuity + 1) and 0x0f
            offset += payloadLength
            first = false
        }
    }

    private fun writeTsHeader(
        packet: ByteArray,
        pid: Int,
        payloadUnitStart: Boolean,
        adaptationFieldControl: Int,
        continuity: Int,
    ) {
        packet[0] = 0x47
        packet[1] = ((if (payloadUnitStart) 0x40 else 0) or ((pid shr 8) and 0x1f)).toByte()
        packet[2] = pid.toByte()
        packet[3] = ((adaptationFieldControl shl 4) or continuity).toByte()
    }

    private fun writePts(destination: ByteArray, offset: Int, timestamp90Khz: Long) {
        val pts = timestamp90Khz and 0x1_ffff_ffffL
        destination[offset] = (0x21L or ((pts shr 29) and 0x0e)).toByte()
        destination[offset + 1] = (pts shr 22).toByte()
        destination[offset + 2] = (((pts shr 14) and 0xfe) or 1).toByte()
        destination[offset + 3] = (pts shr 7).toByte()
        destination[offset + 4] = (((pts shl 1) and 0xfe) or 1).toByte()
    }

    private fun stuffedPacket() = ByteArray(188) { 0xff.toByte() }

    private fun mpegCrc32(bytes: ByteArray, length: Int): Long {
        var crc = 0xffff_ffffL
        repeat(length) { index ->
            crc = crc xor ((bytes[index].toLong() and 0xffL) shl 24)
            repeat(8) {
                crc = if ((crc and 0x8000_0000L) != 0L) {
                    ((crc shl 1) xor 0x04c1_1db7L) and 0xffff_ffffL
                } else {
                    (crc shl 1) and 0xffff_ffffL
                }
            }
        }
        return crc
    }

    private fun writeIntBigEndian(destination: ByteArray, offset: Int, value: Int) {
        destination[offset] = (value ushr 24).toByte()
        destination[offset + 1] = (value ushr 16).toByte()
        destination[offset + 2] = (value ushr 8).toByte()
        destination[offset + 3] = value.toByte()
    }
}
