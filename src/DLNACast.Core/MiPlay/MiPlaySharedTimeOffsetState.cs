namespace DLNACast.Core.MiPlay;

/// <summary>
/// Mirrors MultiMirrorDisplay's group-level timeOffset field. Initial members
/// publish one synchronized value, runtime resume/recovery replaces it, and a
/// newly added sender receives the stored value instead of deriving another
/// offset from the first retained AAC access unit.
/// </summary>
internal sealed class MiPlaySharedTimeOffsetState
{
    internal const int RetainedRuntimeUpdateCount = 64;
    private readonly Lock gate = new();
    private readonly Dictionary<MiPlaySharedTimeOffsetUpdateKey, RuntimeUpdate> runtimeUpdates = [];
    private readonly Queue<MiPlaySharedTimeOffsetUpdateKey> runtimeUpdateOrder =
        new(RetainedRuntimeUpdateCount);
    private bool initialized;
    private ulong current;
    private long nextRuntimeUpdateOrdinal;
    private long committedRuntimeUpdateOrdinal;

    public ulong Initialize(ulong value)
    {
        lock (gate)
        {
            if (initialized)
            {
                if (current != value)
                {
                    throw new InvalidOperationException(
                        "Initial MiPlay receivers disagreed about the shared TIME_OFFSET.");
                }
                return current;
            }

            current = value;
            initialized = true;
            return current;
        }
    }

    public ulong GetCurrent()
    {
        lock (gate)
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The shared MiPlay TIME_OFFSET is not available before media startup.");
            }
            return current;
        }
    }

    public ulong PrepareRuntimeUpdate(
        MiPlaySharedTimeOffsetUpdateKey key,
        ulong candidate)
    {
        lock (gate)
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The shared MiPlay TIME_OFFSET cannot be prepared before initialization.");
            }

            if (runtimeUpdates.TryGetValue(key, out var existing))
            {
                return existing.Value;
            }

            var update = new RuntimeUpdate(candidate, checked(++nextRuntimeUpdateOrdinal));
            runtimeUpdates.Add(key, update);
            runtimeUpdateOrder.Enqueue(key);
            while (runtimeUpdateOrder.Count > RetainedRuntimeUpdateCount)
            {
                runtimeUpdates.Remove(runtimeUpdateOrder.Dequeue());
            }
            return update.Value;
        }
    }

    public void CommitRuntimeUpdate(
        MiPlaySharedTimeOffsetUpdateKey key,
        ulong value)
    {
        lock (gate)
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "The shared MiPlay TIME_OFFSET cannot be committed before initialization.");
            }
            if (!runtimeUpdates.TryGetValue(key, out var update) || update.Value != value)
            {
                throw new InvalidOperationException(
                    "The shared MiPlay TIME_OFFSET update was not prepared by this group.");
            }

            // A slower sender can finish an older RTSP write after another
            // group update has already completed. It must not roll the value
            // copied by the next addMirror operation backwards.
            if (update.Ordinal > committedRuntimeUpdateOrdinal)
            {
                current = value;
                committedRuntimeUpdateOrdinal = update.Ordinal;
            }
        }
    }

    private readonly record struct RuntimeUpdate(ulong Value, long Ordinal);
}

internal enum MiPlaySharedTimeOffsetUpdateKind
{
    TransportRecovery,
    Resume,
    GroupResume,
}

internal readonly record struct MiPlaySharedTimeOffsetUpdateKey(
    MiPlaySharedTimeOffsetUpdateKind Kind,
    ulong MediaPosition);
