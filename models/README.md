# Speech models

The app downloads voice packs itself: **Settings › Language** lists every
supported language and installs its pack into
`%LOCALAPPDATA%\TalkPrompter\models`. The languages, download URLs and exact
sizes live in `src/Teleprompter.Core/Languages/VoiceLanguageCatalog.cs`.

For development you can also drop a model folder in here. Without any model,
the app runs in **simulation mode** (it reads the script to itself so you can
try the scrolling without a microphone).

The app works out a folder's language from its name and uses the pack for the
language chosen in Settings:

| Engine | Folder looks like | Language |
|---|---|---|
| **Vosk** | `am/`, `conf/`, `graph/` (or the older flat layout with `final.mdl` + `mfcc.conf`) | from the name `vosk-model-[small-]<code>-…`, e.g. `vosk-model-small-pl-0.22` is Polish |
| **sherpa-onnx** | `tokens.txt` + `encoder*.onnx` … | the catalog's Indonesian pack by name; any other sherpa-onnx model counts as English |

For one language with several Vosk models, the catalog pack wins, then "small"
models, which support the script lock (the recognizer is limited to your
script's words).

## Quick install

From the repository root:

```powershell
# Vosk English (the default voice pack)
./scripts/Get-VoskModel.ps1

# or another Vosk language
./scripts/Get-VoskModel.ps1 -Model vosk-model-small-de-0.15

# sherpa-onnx streaming zipformer (English, open vocabulary)
./scripts/Get-SherpaModel.ps1
```

Restart the app after installing — it detects models on startup and again
whenever the language picker opens.

You can also point the app at any model folder explicitly with the
`VOSK_MODEL_PATH` environment variable (works for either engine and applies
to every language).

## Adding a language

1. Find an offline model that streams words: a small Vosk model (check that
   its zip has `graph/HCLr.fst` + `graph/Gr.fst`, or `HCLr.fst` + `Gr.fst` in
   the flat layout) or a sherpa-onnx online transducer.
2. Add it to `VoiceLanguageCatalog` with its exact download size.
3. Test it with a native speaker reading a known script:
   `tools/MicCheck` with `--wav <reading.wav> --script-file <script.txt> --lang <code>`
   and `VOSK_MODEL_PATH` pointing at the model. It prints how far it tracked.

Models are not committed to source control.
