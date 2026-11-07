# -*- coding: utf-8 -*-
"""BGE-small-zh-v1.5 本地嵌入服务（OpenAI 兼容）
纯标准库 + onnxruntime + tokenizers，无 torch 依赖，CPU 运行。
自带一个本地 WebUI：http://127.0.0.1:<port>/
"""
import ctypes
import json
import os
import sys
import threading
import time
import traceback
from ctypes import wintypes
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np
import onnxruntime as ort
from tokenizers import Tokenizer

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(BASE_DIR)

DEFAULTS = {
    "host": "127.0.0.1",
    "port": 18080,
    "model": os.path.join(ROOT_DIR, "model", "bge-small-zh-v1.5.onnx"),
    "tokenizer": os.path.join(ROOT_DIR, "model", "tokenizer.json"),
    "max_length": 512,
    "threads": 2,
    "log_file": os.path.join(ROOT_DIR, "logs", "server.log"),
    "log_max_mb": 4,
    "dim": 512,
}


def load_config():
    cfg = dict(DEFAULTS)
    path = os.path.join(BASE_DIR, "config.json")
    if os.path.exists(path):
        try:
            with open(path, "r", encoding="utf-8") as f:
                user = json.load(f)
            if isinstance(user, dict):
                cfg.update(user)
        except Exception:
            pass
    return cfg


CFG = load_config()
LOG_PATH = CFG["log_file"]
LOG_LOCK = threading.Lock()


def log(msg):
    line = "[%s] %s" % (time.strftime("%Y-%m-%d %H:%M:%S"), msg)
    try:
        os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)
        with LOG_LOCK:
            if os.path.exists(LOG_PATH) and os.path.getsize(LOG_PATH) > CFG["log_max_mb"] * 1024 * 1024:
                try:
                    os.replace(LOG_PATH, LOG_PATH + ".1")
                except Exception:
                    pass
            with open(LOG_PATH, "a", encoding="utf-8") as f:
                f.write(line + "\n")
    except Exception:
        pass


log("启动，Python %s" % sys.version.split()[0])
log("模型: %s" % CFG["model"])

_t0 = time.time()
sess_opts = ort.SessionOptions()
sess_opts.intra_op_num_threads = int(CFG["threads"])
sess_opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
sess_opts.log_severity_level = 3
session = ort.InferenceSession(CFG["model"], sess_opts, providers=["CPUExecutionProvider"])
tokenizer = Tokenizer.from_file(CFG["tokenizer"])
tokenizer.enable_truncation(max_length=int(CFG["max_length"]))
tokenizer.enable_padding(pad_id=0, pad_token="[PAD]")
DIM = int(CFG["dim"])
log("模型加载完成，用时 %.2fs" % (time.time() - _t0))

_enc_lock = threading.Lock()
_stats_lock = threading.Lock()
_stats = {"requests": 0, "texts": 0, "total_ms": 0.0, "last_ms": 0.0}


class _PMC(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("PageFaultCount", wintypes.DWORD),
        ("PeakWorkingSetSize", ctypes.c_size_t),
        ("WorkingSetSize", ctypes.c_size_t),
        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
        ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
        ("PagefileUsage", ctypes.c_size_t),
        ("PeakPagefileUsage", ctypes.c_size_t),
    ]


_psapi = ctypes.WinDLL("psapi", use_last_error=True)
_psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(_PMC), wintypes.DWORD]
_psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
_k32 = ctypes.WinDLL("kernel32", use_last_error=True)
_k32.GetCurrentProcess.restype = wintypes.HANDLE


def memory_mb():
    try:
        pmc = _PMC()
        pmc.cb = ctypes.sizeof(_PMC)
        if not _psapi.GetProcessMemoryInfo(_k32.GetCurrentProcess(), ctypes.byref(pmc), pmc.cb):
            return 0
        return int(pmc.WorkingSetSize / 1024 / 1024)
    except Exception:
        return 0


def record(n_texts, elapsed_ms):
    with _stats_lock:
        _stats["requests"] += 1
        _stats["texts"] += n_texts
        _stats["total_ms"] += elapsed_ms
        _stats["last_ms"] = elapsed_ms


def snapshot():
    with _stats_lock:
        req = _stats["requests"]
        avg = (_stats["total_ms"] / req) if req else 0.0
        return {
            "requests": req,
            "texts": _stats["texts"],
            "avg_ms": round(avg, 1),
            "last_ms": round(_stats["last_ms"], 1),
        }


def embed(texts):
    if not texts:
        return np.zeros((0, DIM), dtype=np.float32)
    with _enc_lock:
        encs = tokenizer.encode_batch(texts)
        ids = np.asarray([e.ids for e in encs], dtype=np.int64)
        mask = np.asarray([e.attention_mask for e in encs], dtype=np.int64)
    feeds = {
        "input_ids": ids,
        "attention_mask": mask,
        "token_type_ids": np.zeros_like(ids),
    }
    hidden = session.run(["last_hidden_state"], feeds)[0][:, 0, :]
    norms = np.linalg.norm(hidden, axis=1, keepdims=True)
    norms[norms == 0] = 1.0
    return hidden / norms


def count_tokens(texts):
    try:
        with _enc_lock:
            encs = tokenizer.encode_batch(texts)
        return sum(len(e.ids) for e in encs)
    except Exception:
        return 0


embed(["预热"])
log("预热完成")

WEBUI_HTML = r"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>BGE 本地嵌入服务</title>
<style>
  :root{
    --bg:#f6f7f9; --card:#fff; --ink:#20242c; --muted:#6e7684;
    --line:#e6e9ef; --accent:#4f8cff; --ok:#27ae60; --dark:#181a1f;
  }
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--ink);
       font-family:"Microsoft YaHei UI","PingFang SC",system-ui,sans-serif}
  header{background:var(--dark);padding:22px 28px;display:flex;align-items:center;gap:14px}
  .dot{width:12px;height:12px;border-radius:50%;background:var(--ok);
       box-shadow:0 0 0 4px rgba(39,174,96,.18)}
  header h1{font-size:19px;margin:0;color:#fff;font-weight:700}
  header .ver{font-family:Consolas,monospace;font-size:12px;color:#8c94a2;margin-left:2px}
  header .sub{color:#9aa2b1;font-size:12.5px;margin-left:auto}
  main{max-width:1040px;margin:0 auto;padding:24px 20px 40px}
  .grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-bottom:18px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px 18px}
  .k{font-size:12px;color:var(--muted);margin-bottom:8px}
  .v{font-size:22px;font-weight:700;letter-spacing:.2px}
  .v small{font-size:12px;color:var(--muted);font-weight:400;margin-left:4px}
  h2{font-size:14px;margin:0 0 12px;font-weight:700}
  .two{display:grid;grid-template-columns:1.15fr .85fr;gap:14px}
  textarea{width:100%;min-height:92px;border:1px solid var(--line);border-radius:8px;
           padding:10px;font:13px/1.6 inherit;resize:vertical;background:#fbfcfe}
  .row{display:flex;gap:10px;align-items:center;margin-top:10px;flex-wrap:wrap}
  button{border:0;border-radius:7px;padding:9px 16px;font:600 13px inherit;cursor:pointer;
         background:var(--accent);color:#fff}
  button.ghost{background:#eef1f6;color:#414855}
  button:disabled{opacity:.6;cursor:default}
  .out{margin-top:12px;background:#1c1f25;color:#cfd6e2;border-radius:8px;padding:12px;
       font:12px/1.6 Consolas,monospace;white-space:pre-wrap;min-height:58px}
  table{width:100%;border-collapse:collapse;font-size:12.5px}
  td{padding:7px 4px;border-bottom:1px solid var(--line);vertical-align:top}
  td:first-child{color:var(--muted);width:150px;white-space:nowrap}
  code{font-family:Consolas,monospace;font-size:12px;background:#f2f4f8;padding:2px 5px;border-radius:4px}
  pre{margin:0;background:#1c1f25;color:#cfd6e2;border-radius:8px;padding:12px;
      font:12px/1.65 Consolas,monospace;overflow:auto}
  footer{color:#9aa2b1;font-size:12px;text-align:center;margin-top:26px}
  @media (max-width:860px){.grid{grid-template-columns:repeat(2,1fr)}.two{grid-template-columns:1fr}}
</style>
</head>
<body>
<header>
  <div class="dot" id="dot"></div>
  <h1>BGE 本地嵌入服务</h1>
  <span class="ver">v1.1.0</span>
  <span class="sub" id="sub">正在连接…</span>
</header>
<main>
  <div class="grid">
    <div class="card"><div class="k">模型</div><div class="v">bge-small-zh<small>v1.5</small></div></div>
    <div class="card"><div class="k">向量维度</div><div class="v">__DIM__<small>dim</small></div></div>
    <div class="card"><div class="k">运行时长</div><div class="v" id="uptime">—</div></div>
    <div class="card"><div class="k">已处理请求</div><div class="v"><span id="reqs">0</span><small id="avg"></small></div></div>
  </div>

  <div class="two">
    <div class="card">
      <h2>试一试嵌入</h2>
      <textarea id="txt">用户喜欢吃火锅，尤其是重庆火锅
明天要下雨了记得带伞</textarea>
      <div class="row">
        <button id="go">生成向量</button>
        <button class="ghost" id="clear">清空结果</button>
        <span style="font-size:12px;color:#6e7684">每行一条，返回 512 维并做 L2 归一化</span>
      </div>
      <div class="out" id="out">等待输入…</div>
    </div>

    <div class="card">
      <h2>接入 AstrBot</h2>
      <table>
        <tr><td>类型</td><td><code>openai_embedding</code></td></tr>
        <tr><td>api_base</td><td><code>__BASE__/v1</code></td></tr>
        <tr><td>api_key</td><td><code>sk-local</code>（本地不校验）</td></tr>
        <tr><td>模型</td><td><code>bge-small-zh-v1.5</code></td></tr>
        <tr><td>维度</td><td><code>512</code></td></tr>
        <tr><td>dimensions_mode</td><td><code>never</code></td></tr>
      </table>
      <p style="font-size:12px;color:#6e7684;line-height:1.7;margin:10px 0 0">
        填好后到「知识库 → 新建」，嵌入模型选 <code>local-bge/bge-small-zh-v1.5</code>。<br>
        服务跑在 CPU 上，<b>显存占用恒为 0 MB</b>，不会和 ComfyUI 抢显卡。
      </p>
    </div>
  </div>

  <div class="card" style="margin-top:14px">
    <h2>直接调用</h2>
    <pre>curl -s __BASE__/v1/embeddings \
  -H "Content-Type: application/json" \
  -d "{\"input\":\"你好\"}"

# Python
import requests
r = requests.post("__BASE__/v1/embeddings", json={"input": ["你好"]})
print(len(r.json()["data"][0]["embedding"]))   # 512</pre>
  </div>

  <footer>
    日志：安装目录\logs\server.log · 进程：Tray-Py.exe（显存 0 MB，纯 CPU）<br>
    老是调用失败？把安装包或日志发给 AstrBot，让它按你的实际配置核对。
  </footer>
</main>
<script>
const BASE = "__BASE__";
function fmtUptime(s){
  s = Math.max(0, s|0);
  const h = Math.floor(s/3600), m = Math.floor(s%3600/60), sec = s%60;
  return h>0 ? (h + "h " + m + "m") : (m>0 ? (m + "m " + sec + "s") : (sec + "s"));
}
async function refresh(){
  try{
    const r = await fetch(BASE + "/health", {cache:"no-store"});
    const j = await r.json();
    document.getElementById("uptime").textContent = fmtUptime(j.uptime_sec);
    document.getElementById("reqs").textContent = j.requests || 0;
    document.getElementById("avg").textContent = j.requests ? (" · 平均 " + j.avg_ms + " ms") : "";
    document.getElementById("sub").innerHTML = '<span style="color:#27ae60">运行中</span> · ' + BASE +
      " · 内存 " + j.memory_mb + " MB · 显存 0 MB";
  }catch(e){
    document.getElementById("dot").style.background = "#e74c3c";
    document.getElementById("sub").innerHTML = '<span style="color:#e74c3c">服务未响应</span>';
  }
}
document.getElementById("go").addEventListener("click", async () => {
  const lines = document.getElementById("txt").value.split("\n").map(s=>s.trim()).filter(Boolean);
  if(!lines.length){ return; }
  const out = document.getElementById("out");
  const btn = document.getElementById("go");
  btn.disabled = true; out.textContent = "计算中…";
  const t0 = performance.now();
  try{
    const r = await fetch(BASE + "/v1/embeddings", {
      method:"POST", headers:{"Content-Type":"application/json"},
      body: JSON.stringify({input: lines, model:"bge-small-zh-v1.5"})
    });
    const j = await r.json();
    const ms = Math.round(performance.now() - t0);
    let s = lines.length + " 条 · " + j.data[0].embedding.length + " 维 · " + ms + " ms\n\n";
    j.data.forEach((d,i)=>{
      s += "#" + (i+1) + " " + lines[i].slice(0,28) + "\n  " +
           d.embedding.slice(0,6).map(x=>x.toFixed(5)).join(", ") + " …\n";
    });
    out.textContent = s;
    refresh();
  }catch(e){
    out.textContent = "调用失败：" + e;
  }finally{ btn.disabled = false; }
});
document.getElementById("clear").addEventListener("click", ()=>{
  document.getElementById("out").textContent = "等待输入…";
});
refresh();
setInterval(refresh, 3000);
</script>
</body>
</html>
"""


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "BGEEmbedding/1.1"

    def _json(self, code, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def _html(self, code, text):
        body = text.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type,Authorization")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        path = self.path.split("?")[0].rstrip("/") or "/"
        if path in ("/", "/webui", "/ui"):
            host = self.headers.get("Host") or ("127.0.0.1:%s" % CFG["port"])
            base = "http://" + host
            html = (
                WEBUI_HTML.replace("__BASE__", base)
                .replace("__DIM__", str(DIM))
                .replace("__MEM__", str(memory_mb()))
            )
            return self._html(200, html)
        if path in ("/health", "/healthz"):
            s = snapshot()
            return self._json(200, {
                "status": "ok",
                "dim": DIM,
                "model": "bge-small-zh-v1.5",
                "uptime_sec": int(time.time() - _t0),
                "requests": s["requests"],
                "texts": s["texts"],
                "avg_ms": s["avg_ms"],
                "last_ms": s["last_ms"],
                "memory_mb": memory_mb(),
                "device": "cpu",
                "vram_mb": 0,
            })
        if path == "/v1/models":
            return self._json(200, {
                "object": "list",
                "data": [{"id": "bge-small-zh-v1.5", "object": "model", "owned_by": "local"}],
            })
        return self._json(404, {"error": {"message": "not found", "type": "invalid_request_error"}})

    def do_POST(self):
        path = self.path.split("?")[0].rstrip("/")
        if path not in ("/v1/embeddings", "/embeddings"):
            return self._json(404, {"error": {"message": "not found", "type": "invalid_request_error"}})

        try:
            length = int(self.headers.get("Content-Length") or 0)
            raw = self.rfile.read(length) if length else b"{}"
            payload = json.loads(raw.decode("utf-8"))
        except Exception as e:
            return self._json(400, {"error": {"message": "invalid json: %s" % e, "type": "invalid_request_error"}})

        inp = payload.get("input", payload.get("inputs", ""))
        if isinstance(inp, str):
            texts = [inp]
        elif isinstance(inp, list):
            texts = [t if isinstance(t, str) else str(t) for t in inp]
        else:
            texts = [str(inp)]

        model_name = payload.get("model") or "bge-small-zh-v1.5"
        dims = payload.get("dimensions")
        started = time.perf_counter()
        try:
            vecs = embed(texts)
            if isinstance(dims, int) and 0 < dims < vecs.shape[1]:
                vecs = vecs[:, :dims]
                n = np.linalg.norm(vecs, axis=1, keepdims=True)
                n[n == 0] = 1.0
                vecs = vecs / n
            data = [
                {"object": "embedding", "index": i, "embedding": [round(float(x), 6) for x in v]}
                for i, v in enumerate(vecs)
            ]
            tokens = count_tokens(texts)
            record(len(texts), (time.perf_counter() - started) * 1000.0)
            return self._json(200, {
                "object": "list",
                "data": data,
                "model": model_name,
                "usage": {"prompt_tokens": tokens, "total_tokens": tokens},
            })
        except Exception as e:
            log("推理失败: %s\n%s" % (e, traceback.format_exc()))
            return self._json(500, {"error": {"message": str(e), "type": "server_error"}})

    def log_message(self, fmt, *args):
        pass


def main():
    host = CFG["host"]
    port = int(CFG["port"])
    httpd = ThreadingHTTPServer((host, port), Handler)
    httpd.daemon_threads = True
    log("WebUI: http://%s:%d/" % (host, port))
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()
        log("服务退出")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        log("致命错误:\n%s" % traceback.format_exc())
        raise
