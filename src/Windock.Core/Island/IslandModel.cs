using Windock.Core.Abstractions;

namespace Windock.Core.Island;

public enum IslandEventKind { Volume, Brightness, Battery, Device }

/// A short pop-up in the island. Level (0..1) draws a bar (volume, brightness); Detail is right-aligned text.
public sealed record IslandEvent(IslandEventKind Kind, string Glyph, string Text, double? Level = null, string? Detail = null)
{
    /// Volume and brightness follow a key or wheel: each new value replaces the last one at once.
    public bool IsOsd => Kind is IslandEventKind.Volume or IslandEventKind.Brightness;
}

/// What the media session reports. The cover image stays in the app layer.
public sealed record MediaState(string Title, string Artist, string App, bool Playing);

public enum IslandMode { Idle, Media, Event }

/// The island's state: idle, media or a pop-up event, and whether the card is expanded under the pointer.
/// Events show one at a time for the event duration; volume/brightness replace whatever is showing,
/// other events wait in a short queue. All timing goes through the scheduler (tests drive it by hand).
public sealed class IslandModel(IDelayScheduler scheduler, Func<TimeSpan> eventDuration)
{
    public const int MaxQueued = 3;
    public static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(300);
    /// A paused player keeps the media card this long, then the island goes back to idle.
    public static readonly TimeSpan PausedLinger = TimeSpan.FromSeconds(30);

    private readonly LinkedList<IslandEvent> _queue = new();
    private IDisposable? _eventTimer, _collapseTimer, _pausedTimer;
    private bool _pausedExpired;

    public MediaState? Media { get; private set; }
    public IslandEvent? CurrentEvent { get; private set; }
    public bool Expanded { get; private set; }
    public bool PointerOver { get; private set; }

    /// The open card's page (0 = home or media); back to 0 each time the card closes.
    public int Page { get; private set; }

    public bool MediaActive => Media is not null && (Media.Playing || !_pausedExpired);

    public IslandMode Mode => CurrentEvent is not null ? IslandMode.Event : MediaActive ? IslandMode.Media : IslandMode.Idle;

    public IReadOnlyCollection<IslandEvent> Queued => _queue;

    public event Action? Changed;

    public void SetMedia(MediaState? media)
    {
        if (media == Media) return;
        var wasPlaying = Media?.Playing == true;
        Media = media;
        if (media is null || media.Playing)
        {
            _pausedTimer?.Dispose();
            _pausedTimer = null;
            _pausedExpired = false;
        }
        else if (wasPlaying || _pausedTimer is null && !_pausedExpired)
        {
            _pausedTimer?.Dispose();
            _pausedExpired = false;
            _pausedTimer = scheduler.Schedule(PausedLinger, () =>
            {
                _pausedTimer = null;
                _pausedExpired = true;
                if (Expanded && Mode == IslandMode.Idle && !PointerOver)
                {
                    Expanded = false;
                    Page = 0;
                }
                Changed?.Invoke();
            });
        }
        Changed?.Invoke();
    }

    public void Show(IslandEvent e)
    {
        if (Expanded)
        {
            // The card under the pointer stays put; a stale volume value is not worth showing afterwards.
            if (!e.IsOsd) Enqueue(e);
            return;
        }
        if (CurrentEvent is null)
        {
            Start(e);
            return;
        }
        if (e.IsOsd)
        {
            if (!CurrentEvent.IsOsd) _queue.AddFirst(CurrentEvent);
            Start(e);
            return;
        }
        Enqueue(e);
    }

    /// expandAllowed: the "expand on hover" setting.
    public void PointerEntered(bool expandAllowed)
    {
        PointerOver = true;
        _collapseTimer?.Dispose();
        _collapseTimer = null;
        if (!expandAllowed || Expanded || CurrentEvent is not null) return;
        Expanded = true;
        Changed?.Invoke();
    }

    public void PointerLeft()
    {
        PointerOver = false;
        if (!Expanded) return;
        _collapseTimer?.Dispose();
        _collapseTimer = scheduler.Schedule(CollapseDelay, () =>
        {
            _collapseTimer = null;
            Collapse();
        });
    }

    /// Click on a side of the open card, or the mouse wheel: the next/previous page, wrapping around.
    public void TurnPage(int delta, int pageCount)
    {
        if (!Expanded || pageCount < 2 || delta == 0) return;
        Page = ((Page + delta) % pageCount + pageCount) % pageCount;
        Changed?.Invoke();
    }

    /// A tab in the open card's tab bar was clicked.
    public void GoToPage(int page, int pageCount)
    {
        if (!Expanded || page < 0 || page >= pageCount || page == Page) return;
        Page = page;
        Changed?.Invoke();
    }

    /// Settings turned expanding off, or the island was hidden.
    public void Collapse()
    {
        if (!Expanded) return;
        Expanded = false;
        Page = 0;
        Changed?.Invoke();
        if (CurrentEvent is null && _queue.Count > 0) Next();
    }

    /// Drops the pop-up and anything waiting (island turned off, or that kind of event turned off).
    public void ClearEvents(Func<IslandEvent, bool>? which = null)
    {
        which ??= _ => true;
        for (var node = _queue.First; node is not null;)
        {
            var next = node.Next;
            if (which(node.Value)) _queue.Remove(node);
            node = next;
        }
        if (CurrentEvent is not null && which(CurrentEvent)) Next();
    }

    private void Enqueue(IslandEvent e)
    {
        if (_queue.Count >= MaxQueued) return;
        _queue.AddLast(e);
    }

    private void Start(IslandEvent e)
    {
        _eventTimer?.Dispose();
        CurrentEvent = e;
        _eventTimer = scheduler.Schedule(eventDuration(), Next);
        Changed?.Invoke();
    }

    private void Next()
    {
        _eventTimer?.Dispose();
        _eventTimer = null;
        if (!Expanded && _queue.First is { } first)
        {
            _queue.RemoveFirst();
            Start(first.Value);
            return;
        }
        if (CurrentEvent is null) return;
        CurrentEvent = null;
        Changed?.Invoke();
    }
}
