using DLNACast.Core.Abstractions;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;

namespace DLNACast.App;

internal sealed class SharedLocalOutputManager(ILocalOutputManager inner) : ISwitchableLocalOutputManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CaptureSelection? _activeSelection;
    private ILocalOutputLease? _innerLease;
    private int _leaseCount;

    public async ValueTask<ILocalOutputLease> RouteForCastAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_leaseCount == 0)
            {
                _innerLease = await inner.RouteForCastAsync(selection, cancellationToken);
                _activeSelection = selection;
            }
            else if (!Equals(_activeSelection, selection))
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "并行投送必须使用同一个音频来源。",
                    "Parallel casts must use the same audio source."));
            }

            _leaseCount++;
            var captureSelection = _innerLease?.CaptureSelection ??
                throw new InvalidOperationException("The shared local-output lease is unavailable.");
            return new SharedLease(this, captureSelection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<CaptureSelection> SwitchActiveRouteAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_leaseCount == 0 || _innerLease is null || _activeSelection is null)
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "当前没有可切换的仅音箱播放路由。",
                    "There is no active speaker-only route to switch."));
            }
            if (Equals(_activeSelection, selection)) return _innerLease.CaptureSelection;

            var previousSelection = _activeSelection;
            var previousLease = _innerLease;
            await previousLease.DisposeAsync();
            try
            {
                var replacement = await inner.RouteForCastAsync(selection, cancellationToken);
                _innerLease = replacement;
                _activeSelection = selection;
                return replacement.CaptureSelection;
            }
            catch (Exception switchFailure)
            {
                try
                {
                    _innerLease = await inner.RouteForCastAsync(previousSelection, CancellationToken.None);
                    _activeSelection = previousSelection;
                }
                catch (Exception rollbackFailure)
                {
                    _innerLease = null;
                    _activeSelection = null;
                    throw new AggregateException(switchFailure, rollbackFailure);
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask ReleaseAsync()
    {
        IAsyncDisposable? lease = null;
        await _gate.WaitAsync();
        try
        {
            if (_leaseCount == 0) return;
            _leaseCount--;
            if (_leaseCount == 0)
            {
                lease = _innerLease;
                _innerLease = null;
                _activeSelection = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (lease is not null) await lease.DisposeAsync();
    }

    private sealed class SharedLease(
        SharedLocalOutputManager owner,
        CaptureSelection captureSelection) : ILocalOutputLease
    {
        private int _released;
        public CaptureSelection CaptureSelection { get; } = captureSelection;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _released, 1) == 0
                ? owner.ReleaseAsync()
                : ValueTask.CompletedTask;
    }
}
