# -*- coding: utf-8 -*-
"""把 bge-small-zh-v1.5 导出为 ONNX（fp32 + int8 动态量化），并与 PyTorch 结果比对"""
import json
import os
import time

import numpy as np
import torch
from transformers import AutoModel, AutoTokenizer

SRC = r"C:\Models\bge-small-zh-v1.5"
OUT_DIR = r"E:\BGE-build\model"
os.makedirs(OUT_DIR, exist_ok=True)

fp32_path = os.path.join(OUT_DIR, "bge-small-zh-v1.5.onnx")
int8_path = os.path.join(OUT_DIR, "bge-small-zh-v1.5-int8.onnx")

tok = AutoTokenizer.from_pretrained(SRC)
model = AutoModel.from_pretrained(SRC).eval()


class Wrap(torch.nn.Module):
    def __init__(self, m):
        super().__init__()
        self.m = m

    def forward(self, input_ids, attention_mask, token_type_ids):
        return self.m(
            input_ids=input_ids,
            attention_mask=attention_mask,
            token_type_ids=token_type_ids,
        ).last_hidden_state


wrap = Wrap(model).eval()
dummy = tok(["导出测试 sentence"], padding=True, truncation=True, max_length=512, return_tensors="pt")
inputs = (dummy["input_ids"], dummy["attention_mask"], dummy["token_type_ids"])

t0 = time.time()
torch.onnx.export(
    wrap,
    inputs,
    fp32_path,
    input_names=["input_ids", "attention_mask", "token_type_ids"],
    output_names=["last_hidden_state"],
    dynamic_axes={
        "input_ids": {0: "batch", 1: "sequence"},
        "attention_mask": {0: "batch", 1: "sequence"},
        "token_type_ids": {0: "batch", 1: "sequence"},
        "last_hidden_state": {0: "batch", 1: "sequence"},
    },
    opset_version=17,
    do_constant_folding=True,
    dynamo=False,
)
print(f"export fp32 done in {time.time()-t0:.1f}s, size={os.path.getsize(fp32_path)/1e6:.1f} MB")

from onnxruntime.quantization import QuantType, quantize_dynamic

t0 = time.time()
quantize_dynamic(fp32_path, int8_path, weight_type=QuantType.QInt8)
print(f"quantize done in {time.time()-t0:.1f}s, size={os.path.getsize(int8_path)/1e6:.1f} MB")
print("fp32 size MB:", round(os.path.getsize(fp32_path) / 1e6, 1))

# ---------- 精度比对 ----------
import onnxruntime as ort

texts = [
    "用户喜欢吃火锅，尤其是重庆火锅",
    "明天要下雨了记得带伞",
    "股票市场今天大跌",
    "我家猫很可爱，喜欢趴在键盘上",
    "量子计算机的基本原理是利用叠加态",
    "红烧肉要先焯水再炒糖色",
    "I love programming in Python",
    "The quick brown fox jumps over the lazy dog",
    "今天心情不错，出去散步了",
    "会议推迟到下周三下午三点",
]


def torch_embed(ts):
    enc = tok(ts, padding=True, truncation=True, max_length=512, return_tensors="pt")
    with torch.no_grad():
        out = model(**enc)
    v = out.last_hidden_state[:, 0]
    return torch.nn.functional.normalize(v, p=2, dim=1).numpy()


def ort_embed(sess, ts):
    enc = tok(ts, padding=True, truncation=True, max_length=512, return_tensors="pt")
    feeds = {
        "input_ids": enc["input_ids"].numpy().astype(np.int64),
        "attention_mask": enc["attention_mask"].numpy().astype(np.int64),
        "token_type_ids": enc["token_type_ids"].numpy().astype(np.int64),
    }
    out = sess.run(["last_hidden_state"], feeds)[0][:, 0]
    n = np.linalg.norm(out, axis=1, keepdims=True)
    return out / n


ref = torch_embed(texts)
for name, path in (("fp32", fp32_path), ("int8", int8_path)):
    so = ort.SessionOptions()
    so.intra_op_num_threads = 2
    so.log_severity_level = 3
    sess = ort.InferenceSession(path, so, providers=["CPUExecutionProvider"])
    v = ort_embed(sess, texts)
    cos = np.sum(ref * v, axis=1)
    print(f"{name}: min cos vs torch = {cos.min():.6f}, mean = {cos.mean():.6f}, max = {cos.max():.6f}")
    # 两两相似度，确认语义没坏
    sim = v @ v.T
    print(f"  unrelated pair 0-1 cos = {sim[0,1]:.4f}")
    t0 = time.time()
    for _ in range(20):
        ort_embed(sess, ["预热"])
    print(f"  warm latency = {(time.time()-t0)/20*1000:.1f} ms/次")

# 保存一维校验用结果，安装后自检可以对比
np.save(os.path.join(OUT_DIR, "_ref_vec.npy"), ref)
with open(os.path.join(OUT_DIR, "_check_texts.json"), "w", encoding="utf-8") as f:
    json.dump(texts, f, ensure_ascii=False)
print("done")
