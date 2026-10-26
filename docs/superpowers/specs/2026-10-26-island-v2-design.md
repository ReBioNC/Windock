# Dynamic Island v2 — design

Agreed with the user on 2026-10-26 after looking at a TikTok video of a macOS notch app together (layout and panel ideas
only; nothing is copied: the mascot is our own design). Builds on
[2026-10-25-dynamic-island-design.md](2026-10-25-dynamic-island-design.md).

## Layout (step 1)

The open card (about 560 x 196) has three bands:

- **Header:** activity glyph and the time at the left (bolt while charging); at the right a waveform while music
  plays and **Wado**, the mascot.
- **Content:** the selected panel. Panels slide sideways as before.
- **Tab bar:** one icon per enabled panel; the selected one sits on a dark pill. Click an icon to jump to its panel.
  Two-finger swipe and the mouse wheel still turn pages. The dots and the invisible side click areas are gone.

Weather shows the next hours as rounded tiles and a rain note ("Rain until 00:00", "Rain from 18:00").

## Wado the whale (step 1)

A small blue whale drawn with vector shapes, at the right of the header and of the compact pill.

| Mood | When | Looks |
|---|---|---|
| Idle | default | blinks every few seconds (a 150 ms animation; nothing moves in between) |
| Music | media playing | sways (20 fps, like the music bars) |
| Happy | charger plugged in, device connected | ^ ^ eyes for 3 s |
| Focus | focus timer running (step 3) | determined eyes |
| Sleep | 23:00–06:00 with nothing playing | eyes closed, "z" |

Setting: show Wado (on by default).

## Later steps (each installed on its own)

2. **Synced lyrics** on the Music page: LRCLIB (`lrclib.net/api/get`, no key; sends title, artist, album, length),
   cached per track, current line highlighted from the media position; a button shows or hides them.
3. **Focus timer** panel: focus / short break / long break (25 / 5 / 15 min, long break after 4), Start, Reset; the
   pill shows the countdown; a pop-up when a session ends. Durations in Settings.
4. **Tasks and notes** panels: a Today list ("Call mom 14:30" adds a reminder pop-up at 14:30) and several quick
   notes, saved to `%AppData%\Windock`. Typing takes keyboard focus; the island stays open until a click elsewhere
   or Esc.
5. **Clipboard and shelf** panels: the last 20 copied texts (memory only; skips clipboard content marked as
   excluded from history, as password managers do) and a shelf of dropped files to drag out or open later.

Every panel can be turned off in Settings › Dynamic Island.
