# TalkPrompter (formerly AI Teleprompter) — Research & Implementation Plan

**Goal:** A local Windows desktop teleprompter that scrolls automatically while the user reads the script aloud, pauses when they stop or go off-script, and resumes when they continue. Fully offline — no cloud speech APIs. Not a throwaway Python script; a polished, distributable desktop app.

**Date:** 2026-07-23

---

## 1. Feasibility — yes, and the pattern is well-established

The key insight: this is **not** a general transcription problem. The script is *known in advance*, so the app only needs to answer "where in the script is the speaker right now?" That is much easier than accurate open-vocabulary ASR:

1. A **local streaming speech engine** emits partial word hypotheses with ~100–300 ms latency.
2. A **matcher** fuzzy-aligns those words against a small window of the script just ahead of the last known position (the "anchor").
3. Good match → advance the anchor → scroll. No match (pause or ad-lib) → anchor holds → scroll stops. Matches reappear near the anchor → scrolling resumes automatically.

Pause/resume and off-script detection **fall out of the algorithm for free** — no special-case logic needed.

This is exactly how PromptSmart's patented "VoiceTrack" works (on-device, since ~2014), and several open-source projects implement the same loop.

---

## 2. Prior art (research summary)

### Commercial
| Product | Engine | Local? | Notes |
|---|---|---|---|
| **PromptSmart** (VoiceTrack) | Proprietary on-device ASR | Yes | Market leader; patented (US 9,953,646, expires 2036) — cosine similarity over bag-of-words vs next ~20 script words, n-gram tie-breaking, "no match = hold" |
| **Speakflow** (Flow mode) | Web Speech API | No (Chrome → Google) | Browser subscription app |
| **Teleprompter.com** (VoiceGlide) | Proprietary "hybrid" | Unclear | Continuous baseline scroll whose *speed* adapts to pace — smoother than hard stop/start |

### Open source (most useful references)
| Repo | Stack | Engine | Why it matters |
|---|---|---|---|
| [jlecomte/voice-activated-teleprompter](https://github.com/jlecomte/voice-activated-teleprompter) (MIT) | React/TS, browser | Web Speech API | **Cleanest matching algorithm** (`src/lib/speech-matcher.ts`): Levenshtein over an expanding window anchored at last matched position. Small, portable to C#. |
| [reverentgeek/electron-teleprompter](https://github.com/reverentgeek/electron-teleprompter) | Electron | Deepgram (cloud) | `auto-scroll.js`: bag-of-words sliding window + **critically-damped spring (SmoothDamp) scrolling**. Companion blog post explains every design decision: <https://reverentgeek.com/teaching-my-teleprompter-to-listen/> |
| [ferric-gravity/ghostprompter](https://github.com/ferric-gravity/ghostprompter) | PyQt6 + Vosk | Vosk (local) | Whole architecture matches this app (always-on-top overlay, local STT, fuzzy matching). Unlicensed — reference only, don't copy code. |
| [f/textream](https://github.com/f/textream) (MIT, ~3.5k★) | Swift/macOS | Apple on-device | Best-known OSS voice prompter; macOS-only, borrow UX ideas. |
| [antrys/daves-prompter](https://github.com/antrys/daves-prompter) (MIT) | Python + browser UI | Vosk | Second reference matcher (`word_matcher.py`). |
| QPrompt, Imaginary Teleprompter | Qt / Electron | — | Mature OSS prompters but **no voice tracking** — good UI/feature reference (mirror mode, remote, countdown). |

**Patent note:** PromptSmart's US 9,953,646 is live until 2036. The generic fuzzy-window approach is widely and independently implemented in OSS; avoid replicating their specific claimed pipeline (hypothesis refinement → cosine similarity → n-gram tie-break) verbatim, especially if this ever becomes a commercial product. The Levenshtein-window approach (jlecomte) is a distinct, safe method.

---

## 3. Speech engine choice (local, streaming, Windows)

| Engine | Streaming partials | Word timestamps | Script biasing | C# bindings | German | Verdict |
|---|---|---|---|---|---|---|
| **sherpa-onnx** | Yes, <300 ms | Yes | **Hotwords with boost scores** — feed it the upcoming script words | Official NuGet `org.k2fsa.sherpa.onnx`, actively maintained | Weak (no streaming DE model yet) | **Primary** |
| **Vosk** | Yes, near-zero | Yes (+ partial words) | **Runtime grammar/vocab list** | Official NuGet (2022, stable) | **Good** (use large `vosk-model-de-0.21`; small DE model has an umlaut bug in grammar mode) | **Fallback / German** |
| Whisper (whisper.cpp / Whisper.net) | Pseudo (0.5–2 s lag) | Only via post-alignment | `initial_prompt` only — can *hallucinate* script-following, dangerous here | Whisper.net (excellent) | Good offline | Rejected for live tracking; optional post-take transcript feature |
| Windows built-in (System.Speech / WinRT) | Yes | Limited | SRGS — but **offline SRGS broken since a 2023 Win11 update**, never fixed | Native | Language packs | Rejected |
| Moonshine v2, Kyutai, April-ASR, Riva | various | — | — | immature / server-only | mostly no | Watch list only |

**Decision:** abstract the engine behind an `ISpeechEngine` interface. Ship **sherpa-onnx** (streaming zipformer, ~40–300 MB models, real-time on 1–2 CPU cores, Apache 2.0) as primary for English; **Vosk** as the pluggable alternative and for German until sherpa-onnx has a streaming DE model. Both are Apache 2.0, both run fully offline, both emit the partial word stream the matcher needs.

---

## 4. Tech stack recommendation

**App framework: C# / .NET 8+ with WPF** (WinUI 3 acceptable, but WPF is more battle-tested for always-on-top/transparent/multi-monitor windows and you already have a working WPF + MVVM toolchain from the Blaster project).

Why not the alternatives:
- **Python (PyQt/Tkinter):** explicitly ruled out — packaging, perf, and polish all worse.
- **Electron:** 150+ MB runtime, higher RAM; native mic + ONNX interop is clumsier than a NuGet reference.
- **Tauri + Rust:** viable (sherpa-onnx has Rust crates) and lightest-weight, but slower to build UI-wise and no existing codebase familiarity. Reasonable Plan B if a web-tech UI is ever wanted.

**Stack:**
- .NET 8 (or 10) + WPF, MVVM (CommunityToolkit.Mvvm)
- `org.k2fsa.sherpa.onnx` NuGet — streaming ASR (primary)
- `Vosk` NuGet — alternative engine (German)
- **NAudio** — microphone capture (WASAPI), 16 kHz mono PCM resampling
- `Fastenshtein` or hand-rolled Levenshtein — the matcher is ~150 lines, no heavy deps
- Packaging: Velopack or Inno Setup; models downloaded on first run or bundled (~100 MB)

---

## 5. Architecture

```
┌─────────────────────────────────────────────────────────┐
│ WPF UI (MVVM)                                           │
│  PrompterWindow: script view, current-word highlight,   │
│  smooth scroll, mirror mode, always-on-top overlay      │
│  EditorWindow: script editing, settings, mic picker     │
└──────────────▲──────────────────────────────────────────┘
               │ anchor position + confidence (UI thread)
┌──────────────┴──────────────┐
│ ScrollController            │  critically-damped spring:
│ target = anchor line offset │  smooth motion, no jumps
└──────────────▲──────────────┘
┌──────────────┴──────────────┐
│ ScriptMatcher (pure C#)     │  tokenized script +
│ anchor + sliding window +   │  Levenshtein prefix match
│ Levenshtein / no-match hold │  (unit-testable, no audio)
└──────────────▲──────────────┘
               │ partial word hypotheses
┌──────────────┴──────────────┐
│ ISpeechEngine               │
│  ├ SherpaOnnxEngine (EN)    │  hotwords ← next N script
│  └ VoskEngine (DE)          │  words, refreshed as the
└──────────────▲──────────────┘  anchor advances
┌──────────────┴──────────────┐
│ AudioCapture (NAudio WASAPI)│  16 kHz mono PCM chunks
└─────────────────────────────┘
```

### Core algorithm (ScriptMatcher)
1. **Load script** → normalize (lowercase, strip punctuation, expand numbers) → token list; keep a map from token index → character offset in the displayed text.
2. On each **partial hypothesis** from the engine, take its last ~5–10 words.
3. Search a **window** of script tokens starting at the current anchor, sized `hypothesisLen * 2 + 10` (jlecomte's heuristic).
4. Compute Levenshtein distance between hypothesis and progressively longer prefixes of the window; the minimum-distance end index is the **candidate anchor**.
5. **Gate:** accept only if normalized similarity ≥ threshold (e.g. 0.6) — otherwise hold the anchor. This *is* the pause/off-script behavior.
6. **Monotonic + bounded:** anchor never moves backward, never jumps more than ~15 words per update (prevents matching a repeated phrase later in the script).
7. **Re-acquisition:** if no match for ~10 s, widen the search window forward (speaker may have skipped a paragraph); always allow click-a-word manual re-anchor as escape hatch.
8. **Biasing feedback loop:** feed the next ~20–40 script words to the engine as hotwords (sherpa-onnx) or grammar vocabulary (Vosk), refreshed as the anchor advances — dramatically improves recognition of names/jargon.

### Scrolling (ScrollController)
Never jump the viewport to the anchor. Use SmoothDamp (critically-damped spring) toward the target offset each frame (`CompositionTarget.Rendering`). Manual scroll input suspends auto-follow for ~5 s. Optional later: "hybrid" mode à la VoiceGlide — maintain a baseline speed from measured words-per-minute and use the anchor only for corrections, which smooths over micro-pauses.

---

## 6. Implementation roadmap

**Phase 0 — Spike (validate riskiest part first):** console app: NAudio mic → sherpa-onnx streaming → print partials + latency. Then port jlecomte's matcher to C# with unit tests (feed scripted hypothesis sequences: clean read, pause, ad-lib, skip, restart-of-sentence). *Go/no-go before any UI work.*

**Phase 1 — MVP:** WPF prompter window (script display, font/speed settings, current-position highlight), matcher wired to live audio, spring scrolling, pause/resume working end-to-end, mic device picker, start/stop.

**Phase 2 — Robustness & UX:** number/abbreviation normalization ("25" ↔ "twenty-five"), re-acquisition + click-to-re-anchor, confidence indicator (listening / tracking / lost), mirror mode, adjustable margins & countdown, settings persistence.

**Phase 3 — Polish & distribution:** model download manager, German support via Vosk engine option, remote-control hotkeys (presenter clicker), Velopack installer + auto-update, optional post-take Whisper transcript export.

### Key risks
| Risk | Mitigation |
|---|---|
| ASR misses fast/accented speech | Hotword biasing from script; threshold tuned low — matcher tolerates ~40% word errors |
| Repeated phrases cause jumps | Monotonic anchor + max-jump bound |
| Stop-and-go scroll feel | Spring damping; later hybrid speed mode |
| German streaming quality | Vosk large DE model behind `ISpeechEngine` |
| Patent (commercial use) | Use Levenshtein-window method, not PromptSmart's claimed cosine/n-gram pipeline |
