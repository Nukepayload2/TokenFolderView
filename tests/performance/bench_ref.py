# -*- coding: utf-8 -*-
"""Python reference benchmark for TokenVisualizer.Core's EncodeCount.

Loads the deepseek tokenizer.json through the `tokenizers` library (the Python
binding of the Rust core), encodes the SAME deterministic bytes the .NET harness
(tests/performance/bench/Program.vb) encodes, and reports throughput.
tests/performance/bench.ps1 compares the two.

Usage:
    python tests/performance/bench_ref.py [--path FOLDER_OR_FILE] [--repeat N] [--iterations N]

    --path   Build the benchmark text from a folder (recursively, text files only)
             or a single file, instead of the synthetic ~2 MB paragraph. This is
             the parity spot-check path.
    --repeat Override the number of synthetic-paragraph copies. Default picks ~2 MB.
"""
import argparse
import base64
import os
import time

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEEPSEEK_PATH = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", "deepseek-v4-flash", "tokenizer.json"))

# Same deterministic paragraph the .NET harness decodes from this base64
# (ASCII + CJK + digits + punctuation + emoji, 451 UTF-8 bytes per copy).
PARA_B64 = (
    "RGVlcFNlZWsgaXMgYW4gYWR2YW5jZWQgbGFyZ2UgbGFuZ3VhZ2UgbW9kZWwgcGxhdGZvcm0uClRoZSBxdWljayBicm93biBmb3gganVtcHMgb3ZlciB0aGUgbGF6eSBkb2cgMTIzNDU2Nzg5MCEKSGVsbG8gd29ybGQsIHRoaXMgaXMgYSB0b2tlbml6YXRpb24gYmVuY2htYXJrLgpDaGluZXNlIHdvcmQgc2VnbWVudGF0aW9uIHRlc3Q6IEFJLCBtYWNoaW5lIGxlYXJuaW5nLCBOTFAuClN5bWJvbHM6IEAjJCVeJiooKV8rLT1bXXt9OzpcfH5gCkZ1bGx3aWR0aCBDSksgcHVuY3R1YXRpb246IO+8ge+8n+OAgu+8jO+8m++8muOAge+8iO+8ieOAiuOAi+OAkOOAkQpDSksgd29yZHM6IOS6uuW3peaZuuiDvSDmnLrlmajlrabkuaAg6Ieq54S26K+t6KiA5aSE55CGIOWkp+ivreiogOaooeWeiwpNaXhlZDogYWJjMTIz5Lit5paH5a2X56ym8J+YgGVtb2pp8J+QiWRyYWdvbiBhbmQg5pWw5a2XMTIzNDUuCg==")
PARA = base64.b64decode(PARA_B64).decode("utf-8")

TEXT_LIMIT_BYTES = 4 * 1024 * 1024
FILE_LIMIT_CHARS = 256 * 1024
TEXT_EXTENSIONS = {".vb", ".py", ".cs", ".fs", ".fsx", ".txt", ".md",
                   ".json", ".xml", ".html", ".css", ".js", ".ts"}
SKIP_DIRS = {"bin", "obj", ".vs", ".git", "node_modules", "target"}


def build_text_from_path(path):
    """Recursively concatenate text files under `path` (byte-identical to the .NET harness)."""
    parts = []
    total = 0

    def add_file(fp):
        nonlocal total
        if total >= TEXT_LIMIT_BYTES:
            return
        try:
            # newline="" disables Python's universal-newlines translation (CRLF -> LF), so the
            # read bytes match the .NET harness's File.ReadAllText exactly. Without it the two
            # sides saw different byte counts and different token counts on CRLF files.
            with open(fp, "r", encoding="utf-8-sig", newline="") as f:
                content = f.read(FILE_LIMIT_CHARS)
            if content:
                parts.append(content)
                total += len(content.encode("utf-8"))
        except Exception:
            pass

    def collect(directory):
        nonlocal total
        try:
            entries = sorted(os.listdir(directory))
        except Exception:
            return
        files = [e for e in entries if os.path.isfile(os.path.join(directory, e))]
        dirs = [e for e in entries
                if os.path.isdir(os.path.join(directory, e)) and e not in SKIP_DIRS]
        for name in files:
            if total >= TEXT_LIMIT_BYTES:
                return
            if os.path.splitext(name)[1].lower() in TEXT_EXTENSIONS:
                add_file(os.path.join(directory, name))
        for name in dirs:
            if total >= TEXT_LIMIT_BYTES:
                return
            collect(os.path.join(directory, name))

    if os.path.isdir(path):
        collect(path)
    else:
        add_file(path)
    return "\n".join(parts)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--path")
    ap.add_argument("--repeat", type=int, default=None)
    ap.add_argument("--iterations", type=int, default=3)
    args = ap.parse_args()

    from tokenizers import Tokenizer
    tok = Tokenizer.from_file(DEEPSEEK_PATH)

    if args.path:
        text = build_text_from_path(args.path)
    else:
        repeat = args.repeat if args.repeat else max(1, int(2_000_000 / len(PARA.encode("utf-8"))))
        text = PARA * repeat

    input_bytes = len(text.encode("utf-8"))
    input_mb = input_bytes / (1024.0 * 1024.0)

    # Warmup (Rust caches / regex).
    warm = tok.encode(text, add_special_tokens=False)
    warm_tokens = len(warm.ids)

    # Measure best of N.
    best = None
    token_count = 0
    for _ in range(args.iterations):
        t0 = time.perf_counter()
        enc = tok.encode(text, add_special_tokens=False)
        dt = time.perf_counter() - t0
        token_count = len(enc.ids)
        if best is None or dt < best:
            best = dt

    elapsed = best
    mbps = input_mb / elapsed
    tps = token_count / elapsed
    print("PYTHON|input_mb={:.6f}|tokens={}|elapsed_ms={:.1f}|mb_per_s={:.1f}|tokens_per_s={:.0f}".format(
        input_mb, token_count, elapsed * 1000.0, mbps, tps))
    print("python: {:.2f} MB in {:.1f} ms -> {:.1f} MB/s, {:.0f} tokens/s ({} tokens)  [warmup={}]".format(
        input_mb, elapsed * 1000.0, mbps, tps, token_count, warm_tokens))


if __name__ == "__main__":
    main()
