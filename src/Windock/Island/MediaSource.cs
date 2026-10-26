using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windock.Core.Island;
using Windock.Core.Logging;

namespace Windock.Island;

/// The media session Windows shows in its volume flyout (Spotify, browsers, Groove, …), read through
/// GlobalSystemMediaTransportControlsSessionManager. Session events arrive on worker threads and are
/// forwarded to the UI thread; nothing is polled. Created only while the island's media activity is on.
internal sealed class MediaSource : IDisposable
{
    private const int CoverPixels = 112;

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshVersion;
    private string? _coverKey;
    private bool _disposed;

    public MediaSource(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
        _ = StartAsync();
    }

    /// Raised on the UI thread when the track, playback state or position changed.
    public event Action? Changed;

    public MediaState? State { get; private set; }
    public ImageSource? Cover { get; private set; }
    public string? AppId => _session?.SourceAppUserModelId;

    /// Position at TimelineAt; Duration zero when the player does not report one.
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; private set; }
    public DateTimeOffset TimelineAt { get; private set; }

    /// Position now, moved on by the time since the player last reported it.
    public TimeSpan CurrentPosition()
    {
        if (State is not { Playing: true } || Duration <= TimeSpan.Zero) return Position;
        var moved = Position + (DateTimeOffset.Now - TimelineAt);
        return moved < TimeSpan.Zero ? TimeSpan.Zero : moved > Duration ? Duration : moved;
    }

    public void TogglePlayPause() => Run(s => s.TryTogglePlayPauseAsync().AsTask());
    public void Next() => Run(s => s.TrySkipNextAsync().AsTask());
    public void Previous() => Run(s => s.TrySkipPreviousAsync().AsTask());

    private async void Run(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (_session is not { } session) return;
        try
        {
            await command(session);
        }
        catch (Exception ex)
        {
            _log.Error("media command failed", ex);
        }
    }

    private async Task StartAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = manager;
            manager.CurrentSessionChanged += OnSessionChanged;
            AttachSession();
        }
        catch (Exception ex)
        {
            _log.Error("media sessions unavailable", ex);
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ui.BeginInvoke(AttachSession);

    private void AttachSession()
    {
        if (_disposed || _manager is null) return;
        var session = _manager.GetCurrentSession();
        if (!ReferenceEquals(session, _session))
        {
            DetachSession();
            _session = session;
            if (session is not null)
            {
                session.MediaPropertiesChanged += OnMediaChanged;
                session.PlaybackInfoChanged += OnPlaybackChanged;
                session.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }
        Refresh();
    }

    private void DetachSession()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(Refresh);
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(Refresh);
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(ReadTimeline);

    private async void Refresh()
    {
        var version = ++_refreshVersion;
        var session = _session;
        try
        {
            if (session is null)
            {
                Publish(null, null);
                return;
            }
            var status = session.GetPlaybackInfo()?.PlaybackStatus;
            if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused))
            {
                Publish(null, null);
                return;
            }
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _refreshVersion || _disposed) return;
            if (props is null || string.IsNullOrWhiteSpace(props.Title))
            {
                Publish(null, null);
                return;
            }
            var state = new MediaState(props.Title.Trim(), (props.Artist ?? "").Trim(), MediaAppName.From(session.SourceAppUserModelId),
                status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, (props.AlbumTitle ?? "").Trim());
            var coverKey = $"{session.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.AlbumTitle}";
            var cover = Cover;
            if (coverKey != _coverKey)
            {
                cover = props.Thumbnail is { } thumb ? await LoadCoverAsync(thumb) : null;
                if (version != _refreshVersion || _disposed) return;
                _coverKey = coverKey;
            }
            ReadTimelineQuiet(session);
            Publish(state, cover);
        }
        catch (Exception ex)
        {
            _log.Error("media session read failed", ex);
            if (version == _refreshVersion) Publish(null, null);
        }
    }

    private void Publish(MediaState? state, ImageSource? cover)
    {
        if (state is null) _coverKey = null;
        if (state == State && ReferenceEquals(cover, Cover)) return;
        State = state;
        Cover = cover;
        Changed?.Invoke();
    }

    private void ReadTimeline()
    {
        if (_session is null) return;
        ReadTimelineQuiet(_session);
        Changed?.Invoke();
    }

    private void ReadTimelineQuiet(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var t = session.GetTimelineProperties();
            Duration = t.EndTime - t.StartTime;
            Position = t.Position - t.StartTime;
            TimelineAt = t.LastUpdatedTime.Year > 2000 ? t.LastUpdatedTime : DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            _log.Error("media timeline read failed", ex);
            Duration = TimeSpan.Zero;
        }
    }

    /// Decoded small (the island shows it at 22–56 px) and frozen, so the stream is not kept.
    private async Task<ImageSource?> LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            memory.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = CoverPixels;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            _log.Error("media cover load failed", ex);
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        DetachSession();
        if (_manager is not null) _manager.CurrentSessionChanged -= OnSessionChanged;
        _manager = null;
        State = null;
        Cover = null;
    }
}
