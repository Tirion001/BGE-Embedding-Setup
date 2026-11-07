# GitHub publishing kit

## Repository name (pick one)

- `bge-local-embedding` ← recommended
- `bge-embedding-tray`
- `astrbot-bge-embedding`

## About / description (max 350 chars)

```
Tiny CPU-only OpenAI-compatible embedding server for AstrBot. BGE-small-zh-v1.5, 512-dim, ~4 ms/text, 0 MB VRAM, ~210 MB RAM. One-click Windows installer, tray app, built-in WebUI, optional autostart.
```

Shorter alternative (for social posts):

```
Local 512-dim embeddings for AstrBot that never touch your GPU. One-click Windows installer, tray icon, 0 MB VRAM — 4 ms per sentence on CPU.
```

## Topics (tags)

```
embeddings  onnxruntime  bge  astrbot  rag  knowledge-base  windows  tray-app
cpu-only  openai-api  python  csharp  no-cuda
```

## Release notes — v1.1.0 (first public release)

```markdown
## BGE Local Embedding · Tray v1.1.0

First public release.

- OpenAI-compatible embedding server (`/v1/embeddings`) running BGE-small-zh-v1.5 on CPU
- **0 MB VRAM** — ONNX Runtime CPU provider, CUDA is never initialized
- 512-dim, CLS pooling + L2 normalization, ~4 ms per text, ~210 MB RAM
- One-click Windows installer (per-user, no admin, no Python required)
- Tray app: status, restart, logs, autostart toggle, clean exit
- Built-in WebUI with live stats and an embedding playground
- Full-width first-run guide explaining the AstrBot setup

**Install:** download `BGE-Embedding-Setup.exe` below and run it.
**Uninstall:** Apps & features → BGE 本地嵌入服务.
```

## Assets to upload

| File | Where |
|---|---|
| `README.md` / `README.zh-CN.md` | repo root |
| `LICENSE` | repo root |
| `images/guide.png` | `images/` |
| `images/webui.png` | `images/` |
| `BGE-Embedding-Setup.exe` | **Releases** (not the repo — it is 92 MB) |

## Suggested repo structure

```
.
├── README.md
├── README.zh-CN.md
├── LICENSE
├── images/
│   ├── guide.png
│   └── webui.png
├── src/
│   ├── TrayApp.cs
│   ├── GuideForm.cs
│   ├── Setup.cs
│   ├── server.py
│   └── export_onnx.py
└── build/
    ├── build-tray.ps1
    ├── build-installer.ps1
    └── payload/            (git-ignored: runtime, model, exe)
```

Add a `.gitignore`:

```gitignore
build/payload/
*.onnx
*.exe
*.zip
runtime/
model/
__pycache__/
```

## Sanity checks before publishing

- [ ] README images render (relative paths `images/*.png`)
- [ ] `LICENSE` present and matches the claim in README
- [ ] Installer attached to a Release, not committed
- [ ] `.gitignore` excludes `payload/`, `*.onnx`, `*.exe`
- [ ] No personal paths, tokens, or API keys in the sources
- [ ] Model license (MIT) acknowledged in README
