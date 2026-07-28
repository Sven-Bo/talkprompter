# Speech models

Drop a speech model folder in here to enable real voice tracking. Without one,
the app runs in **simulation mode** (it reads the script to itself so you can
try the scrolling without a microphone).

Two engines are supported; the app picks the best model it finds automatically:

| Priority | Engine | Folder looks like | Notes |
|---|---|---|---|
| 1 | **sherpa-onnx** (recommended) | `tokens.txt` + `encoder*.onnx` … | Modern streaming zipformer; best accuracy, low latency |
| 2 | **Vosk** (large) | `am/`, `conf/`, `graph/` … | Runs in grammar mode, locked to your script's words |
| 3 | **Vosk** (small) | same, name contains "small" | Fastest download, weakest accuracy |

## Quick install

From the repository root:

```powershell
# sherpa-onnx streaming zipformer (English, recommended)
./scripts/Get-SherpaModel.ps1

# or the smaller/fastest sherpa model
./scripts/Get-SherpaModel.ps1 -Model sherpa-onnx-streaming-zipformer-en-20M-2023-02-17

# or a Vosk model (e.g. for German)
./scripts/Get-VoskModel.ps1 -Model vosk-model-small-de-0.15
```

Restart the app after installing — it detects models on startup.

You can also point the app at any model folder explicitly with the
`VOSK_MODEL_PATH` environment variable (works for either engine).

Models are not committed to source control.
