using System.Collections.Concurrent;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedPlaybackControlCoordinatorTests
{
    [Fact]
    public async Task PauseAndResumeAreBroadcastWithOneGenerationPerGroupCommand()
    {
        var coordinator = new MiPlaySharedPlaybackControlCoordinator();
        var received = new ConcurrentQueue<(int Participant, MiPlaySharedPlaybackControlRequest Request)>();
        for (var participant = 1; participant <= 3; participant++)
        {
            var capturedParticipant = participant;
            coordinator.Register(
                participant,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    received.Enqueue((capturedParticipant, request));
                    return Task.CompletedTask;
                });
        }

        await coordinator.BroadcastAsync(
            MiPlaySharedPlaybackControlCommand.Pause,
            CancellationToken.None);
        var pause = received.ToArray();
        Assert.Equal(3, pause.Length);
        Assert.All(pause, item => Assert.Equal(MiPlaySharedPlaybackControlCommand.Pause, item.Request.Command));
        Assert.Single(pause.Select(item => item.Request.Generation).Distinct());

        while (received.TryDequeue(out _))
        {
        }
        await coordinator.BroadcastAsync(
            MiPlaySharedPlaybackControlCommand.Resume,
            CancellationToken.None);
        var resume = received.ToArray();
        Assert.Equal(3, resume.Length);
        Assert.All(resume, item => Assert.Equal(MiPlaySharedPlaybackControlCommand.Resume, item.Request.Command));
        Assert.Single(resume.Select(item => item.Request.Generation).Distinct());
        Assert.True(resume[0].Request.Generation > pause[0].Request.Generation);
    }

    [Fact]
    public async Task RemovedReceiverIsNotIncludedInTheNextGroupCommand()
    {
        var coordinator = new MiPlaySharedPlaybackControlCoordinator();
        var participants = new ConcurrentQueue<int>();
        for (var participant = 1; participant <= 3; participant++)
        {
            var capturedParticipant = participant;
            coordinator.Register(
                participant,
                (_, _) =>
                {
                    participants.Enqueue(capturedParticipant);
                    return Task.CompletedTask;
                });
        }

        coordinator.Unregister(2);
        await coordinator.BroadcastAsync(
            MiPlaySharedPlaybackControlCommand.Resume,
            CancellationToken.None);

        Assert.Equal([1, 3], participants.Order().ToArray());
    }

    [Fact]
    public async Task ReceiverOnlyBroadcastPreservesTheSharedMediaTimelineFlag()
    {
        var coordinator = new MiPlaySharedPlaybackControlCoordinator();
        MiPlaySharedPlaybackControlRequest received = default;
        coordinator.Register(1, (request, _) =>
        {
            received = request;
            return Task.CompletedTask;
        });

        await coordinator.BroadcastAsync(
            MiPlaySharedPlaybackControlCommand.Pause,
            CancellationToken.None,
            preserveMediaTimeline: true);

        Assert.Equal(MiPlaySharedPlaybackControlCommand.Pause, received.Command);
        Assert.True(received.PreserveMediaTimeline);
    }

    [Fact]
    public async Task RejectsDuplicateOrEmptyControlGroups()
    {
        var coordinator = new MiPlaySharedPlaybackControlCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BroadcastAsync(
            MiPlaySharedPlaybackControlCommand.Pause,
            CancellationToken.None));
        coordinator.Register(1, (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.Register(1, (_, _) => Task.CompletedTask));
    }
}
