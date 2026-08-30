# -*- coding: utf-8 -*-
"""
Generates golden vectors from the Python `tokenizers` library (the Rust core binding).

This script builds 8 tokenizer pipelines entirely from `tokenizers` COMPONENTS (no network,
no `from_pretrained`), encodes a fixed sample battery, and dumps the reference ids / byte
offsets / decoded strings plus each pipeline's tokenizer.json (via `to_str()`) into
`tests/performance/golden_vectors.json`.

The VB test suite (`GoldenVectorTests.vb`) loads the SAME tokenizer.json strings via
`Tokenizer.FromJson(configJson)` and asserts the recorded vectors, making these tests the
DEFINITIVE parity gate between the VB port and the Rust core.

Run:  python tests/performance/gen_golden.py
"""
import json
import os

from tokenizers import AddedToken, Tokenizer
from tokenizers.decoders import ByteLevel as ByteLevelDecoder
from tokenizers.decoders import Metaspace as MetaspaceDecoder
from tokenizers.decoders import WordPiece as WordPieceDecoder
from tokenizers.models import BPE, Unigram, WordLevel, WordPiece
from tokenizers.normalizers import BertNormalizer
from tokenizers.pre_tokenizers import BertPreTokenizer, ByteLevel, Metaspace, WhitespaceSplit
from tokenizers.processors import (
    BertProcessing,
    ByteLevel as ByteLevelProcessor,
    RobertaProcessing,
    TemplateProcessing,
)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEEPSEEK_PATH = os.path.join(SCRIPT_DIR, "..", "..", "deepseek-v4-flash", "tokenizer.json")
OUT_PATH = os.path.join(SCRIPT_DIR, "golden_vectors.json")

# ---------------------------------------------------------------------------
# Sample battery
# ---------------------------------------------------------------------------

COMMON_BATTERY = [
    "Hello, world!",
    "The quick brown fox jumps over the lazy dog. 12345 67890 !!! ???",
    "你好世界",
    "こんにちは世界",
    "Mixed 中文 and English 123 with   spaces",
    "a\nb\r\nc\t",
    "  leading and trailing  ",
    "",
    " ",
    "12345678901234567890",
    "a😁b",
    "𠀀",              # CJK ext-B supplementary letter U+20000
    "😁",              # emoji U+1F601
    "‍",          # ZWJ
    "é",         # e + combining acute
    "a𠀀b",            # letter-adjacent supplementary
    "\\abc",           # backslash (DeepSeek-GPT2 divergence path)
    "áb",        # combining mark between letters
    "hëllo",     # decomposed umlaut inside a word
    "中一龯",  # CJK + U+9FAF (top of CJK block)
]


# ---------------------------------------------------------------------------
# BPE vocab / merge helpers
# ---------------------------------------------------------------------------

def add_merge_chain(vocab, merges, seen, pieces):
    """Adds merge rules (and the intermediate prefix tokens) for `pieces`.

    Every intermediate prefix must itself be a vocabulary token for the Rust BPE builder to
    accept the merge rules, exactly like a real trained BPE.
    """
    cur = pieces[0]
    for i in range(1, len(pieces)):
        nxt = cur + pieces[i]
        if nxt not in vocab:
            vocab[nxt] = len(vocab)
        key = (cur, pieces[i])
        if key not in seen:
            seen.add(key)
            merges.append((cur, pieces[i]))
        cur = nxt


def build_gpt2_vocab(words, words_with_prefix):
    """Builds a small GPT-2-style byte-level BPE vocab + merges.

    `words` are bare word tokens (sentence-initial); `words_with_prefix` are the same words
    prefixed with the byte-level space glyph U+0120 (`Ġ`).
    """
    vocab = {}
    merges = []
    seen = set()

    def ensure(tok):
        if tok not in vocab:
            vocab[tok] = len(vocab)
        return tok

    ensure("<unk>")
    ensure("Ġ")
    for c in "abcdefghijklmnopqrstuvwxyz":
        ensure(c)
    for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
        ensure(c)
    for c in "0123456789":
        ensure(c)
    for c in ".,!?;:()\"'`-":
        ensure(c)
    ensure("'")

    # Contractions matched by the GPT-2 regex first alternatives.
    for cont in ("'s", "'t", "'re", "'ve", "'m", "'ll", "'d"):
        add_merge_chain(vocab, merges, seen, list(cont))

    # Prefixed chains get LOWER ranks (higher priority) so that in a "Ġworld" piece the "Ġ"
    # merges into the word before the bare letters merge among themselves. This mirrors a real
    # trained GPT-2 byte-BPE where "Ġ w" -> "Ġw" precedes "w o" -> "wo".
    for w in words_with_prefix:
        add_merge_chain(vocab, merges, seen, ["Ġ"] + list(w))
    for w in words:
        add_merge_chain(vocab, merges, seen, list(w))

    return vocab, merges


GPT2_WORDS = [
    "Hello", "world", "The", "quick", "brown", "fox", "jumps", "over", "the",
    "lazy", "dog", "Mixed", "and", "English", "abc", "leading", "trailing",
    "special", "hello", "foo",
]
GPT2_PREFIX_WORDS = GPT2_WORDS


def build_t5_vocab(words):
    """Builds a small T5-style BPE vocab + merges (Metaspace `▁` prefix pieces)."""
    vocab = {}
    merges = []
    seen = set()

    def ensure(tok):
        if tok not in vocab:
            vocab[tok] = len(vocab)
        return tok

    ensure("<unk>")
    ensure("</s>")
    ensure("▁")
    for c in "abcdefghijklmnopqrstuvwxyz":
        ensure(c)
    for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
        ensure(c)
    for c in "0123456789":
        ensure(c)
    for c in ".,!?;:()\"'`-":
        ensure(c)

    for w in words:
        add_merge_chain(vocab, merges, seen, ["▁"] + list(w))
    for w in words:
        add_merge_chain(vocab, merges, seen, list(w))
    return vocab, merges


T5_WORDS = GPT2_WORDS


# ---------------------------------------------------------------------------
# Pipeline builders
# ---------------------------------------------------------------------------

def build_gpt2(add_prefix_space):
    vocab, merges = build_gpt2_vocab(GPT2_WORDS, GPT2_PREFIX_WORDS)
    tok = Tokenizer(BPE(vocab=vocab, merges=merges, unk_token="<unk>"))
    tok.pre_tokenizer = ByteLevel(add_prefix_space=add_prefix_space, trim_offsets=True, use_regex=True)
    tok.post_processor = ByteLevelProcessor(add_prefix_space=add_prefix_space, trim_offsets=True, use_regex=True)
    tok.decoder = ByteLevelDecoder(add_prefix_space=add_prefix_space, trim_offsets=True, use_regex=True)
    return tok, []


def build_bert():
    vocab = {
        "[PAD]": 0, "[UNK]": 1, "[CLS]": 2, "[SEP]": 3,
        "hello": 4, "world": 5, "the": 6, "quick": 7, "brown": 8, "fox": 9,
        "jumps": 10, "over": 11, "lazy": 12, "dog": 13, "and": 14,
        "mixed": 15, "english": 16, "abc": 17, "leading": 18, "trailing": 19,
        "##ly": 20, "##ing": 21, "##er": 22, "##s": 23,
        "你": 24, "好": 25, "世": 26, "界": 27, "中": 28, "文": 29, "英": 30,
        "こ": 31, "ん": 32, "に": 33, "ち": 34, "は": 35,
        "e": 36, "a": 37, "b": 38, "c": 39, "d": 40, "f": 41, "g": 42,
        "h": 43, "i": 44, "j": 45, "k": 46, "l": 47, "m": 48, "n": 49,
        "o": 50, "p": 51, "q": 52, "r": 53, "s": 54, "t": 55, "u": 56,
        "v": 57, "w": 58, "x": 59, "y": 60, "z": 61,
    }
    tok = Tokenizer(WordPiece(vocab=vocab, unk_token="[UNK]"))
    tok.normalizer = BertNormalizer(
        clean_text=True, handle_chinese_chars=True, strip_accents=True, lowercase=True
    )
    tok.pre_tokenizer = BertPreTokenizer()
    tok.add_special_tokens([AddedToken("[CLS]", special=True), AddedToken("[SEP]", special=True)])
    cls_id = tok.token_to_id("[CLS]")
    sep_id = tok.token_to_id("[SEP]")
    tok.post_processor = BertProcessing(("[SEP]", sep_id), ("[CLS]", cls_id))
    tok.decoder = WordPieceDecoder(prefix="##", cleanup=True)
    return tok, ["[CLS] hello [SEP]", "[CLS]", "[SEP]"]


def build_roberta():
    vocab, merges = build_gpt2_vocab(GPT2_WORDS, GPT2_PREFIX_WORDS)
    # RoBERTa specials must be in the byte-level BPE vocab so their ids are stable.
    for tok_str in ("<s>", "</s>", "<pad>", "<mask>"):
        if tok_str not in vocab:
            vocab[tok_str] = len(vocab)
    tok = Tokenizer(BPE(vocab=vocab, merges=merges, unk_token="<unk>"))
    tok.pre_tokenizer = ByteLevel(add_prefix_space=True, trim_offsets=True, use_regex=True)
    tok.add_special_tokens([
        AddedToken("<s>", special=True),
        AddedToken("</s>", special=True),
        AddedToken("<pad>", special=True),
        AddedToken("<mask>", special=True),
    ])
    cls_id = tok.token_to_id("<s>")
    sep_id = tok.token_to_id("</s>")
    tok.post_processor = RobertaProcessing(("</s>", sep_id), ("<s>", cls_id))
    tok.decoder = ByteLevelDecoder(add_prefix_space=True, trim_offsets=True, use_regex=True)
    return tok, ["<s> hello </s>", "<s>", "</s>", "<mask>"]


def build_t5():
    vocab, merges = build_t5_vocab(T5_WORDS)
    tok = Tokenizer(BPE(vocab=vocab, merges=merges, unk_token="<unk>"))
    tok.pre_tokenizer = Metaspace(replacement="▁", prepend_scheme="always", split=True)
    tok.add_special_tokens([AddedToken("</s>", special=True), AddedToken("<s>", special=True)])
    eos_id = tok.token_to_id("</s>")
    bos_id = tok.token_to_id("<s>")
    tok.post_processor = TemplateProcessing(
        single="</s> $A </s>",
        pair="</s> $A </s> $B:1 </s>:1",
        special_tokens=[("</s>", eos_id), ("<s>", bos_id)],
    )
    tok.decoder = MetaspaceDecoder(replacement="▁", prepend_scheme="always", split=True)
    return tok, ["</s> hello </s>", "</s>", "<s>"]


def build_unigram():
    vocab = [
        ("<unk>", 0.0),
        ("<s>", 0.0),
        ("</s>", 0.0),
        ("hello", -1.0),
        ("world", -1.0),
        ("the", -1.0),
        ("quick", -1.0),
        ("brown", -1.0),
        ("fox", -1.0),
        ("jumps", -1.0),
        ("over", -1.0),
        ("lazy", -1.0),
        ("dog", -1.0),
        ("and", -1.0),
        ("mixed", -1.0),
        ("english", -1.0),
        ("abc", -1.0),
        ("leading", -1.0),
        ("trailing", -1.0),
        ("special", -1.0),
    ]
    for c in "abcdefghijklmnopqrstuvwxyz":
        vocab.append((c, -4.0))
    for c in "0123456789":
        vocab.append((c, -4.0))
    for c in ".,!?;:":
        vocab.append((c, -2.0))
    for ch in "你好世界中文英语こんにちは":
        vocab.append((ch, -2.0))

    tok = Tokenizer(Unigram(vocab=vocab, unk_id=0, byte_fallback=False))
    tok.pre_tokenizer = WhitespaceSplit()
    tok.add_special_tokens([AddedToken("<s>", special=True), AddedToken("</s>", special=True)])
    bos_id = tok.token_to_id("<s>")
    eos_id = tok.token_to_id("</s>")
    tok.post_processor = TemplateProcessing(
        single="<s> $A </s>",
        pair="<s> $A </s> $B:1 </s>:1",
        special_tokens=[("<s>", bos_id), ("</s>", eos_id)],
    )
    return tok, ["<s> hello </s>", "<s>", "</s>"]


def build_template():
    vocab = {
        "hello": 0, "world": 1, "the": 2, "quick": 3, "brown": 4, "fox": 5,
        "[CLS]": 6, "[SEP]": 7, "[UNK]": 8, "you": 9, "are": 10, "how": 11,
        "a": 12, "b": 13, "c": 14, "and": 15, "mixed": 16, "english": 17,
    }
    tok = Tokenizer(WordLevel(vocab=vocab, unk_token="[UNK]"))
    tok.pre_tokenizer = WhitespaceSplit()
    tok.add_special_tokens([AddedToken("[CLS]", special=True), AddedToken("[SEP]", special=True)])
    cls_id = tok.token_to_id("[CLS]")
    sep_id = tok.token_to_id("[SEP]")
    tok.post_processor = TemplateProcessing(
        single="[CLS] $A [SEP]",
        pair="[CLS] $A [SEP] $B:1 [SEP]:1",
        special_tokens=[("[CLS]", cls_id), ("[SEP]", sep_id)],
    )
    return tok, ["[CLS] hello [SEP]", "[CLS] hello world [SEP]"]


def build_deepseek():
    tok = Tokenizer.from_file(DEEPSEEK_PATH)
    # The real special tokens use FULLWIDTH vertical bars (U+FF5C ｜), not ASCII pipes. Pull the
    # exact content from the config so the battery exercises the added-token matching path.
    with open(DEEPSEEK_PATH, encoding="utf-8") as f:
        cfg = json.load(f)
    begin = next(a["content"] for a in cfg["added_tokens"] if a["id"] == 0)
    end = next(a["content"] for a in cfg["added_tokens"] if a["id"] == 1)
    return tok, [
        f"{begin}special{end}",
        begin,
        end,
    ]


# ---------------------------------------------------------------------------
# Recording
# ---------------------------------------------------------------------------

def char_offsets_to_byte_offsets(text, char_offsets):
    """Converts char/scalar offsets to UTF-8 byte offsets in `text`.

    The Python `encode()` binding uses `encode_char_offsets`, so `Encoding.offsets` are
    SCALAR offsets, not byte offsets. The VB engine's `Encode` returns byte offsets, so the
    reference vectors must record byte offsets. Each scalar index `i` starts at the sum of the
    UTF-8 byte lengths of the first `i` scalars.
    """
    boundaries = [0]
    for ch in text:
        boundaries.append(boundaries[-1] + len(ch.encode("utf-8")))
    result = []
    for s, e in char_offsets:
        bs = boundaries[s] if s < len(boundaries) else boundaries[-1]
        be = boundaries[e] if e < len(boundaries) else boundaries[-1]
        result.append([bs, be])
    return result


def record_pipeline(name, tok, extra_texts, with_config):
    """Encodes the battery for `tok` and returns the pipeline record."""
    texts = COMMON_BATTERY + extra_texts
    vectors = []
    for text in texts:
        enc = tok.encode(text, add_special_tokens=False)
        ids = list(enc.ids)
        offsets = char_offsets_to_byte_offsets(text, enc.offsets)
        decoded = tok.decode(ids, skip_special_tokens=False)
        # For special-adding post-processors also record the add_special_tokens=True ids so the
        # VB test can verify the post-processor path.
        rec = {
            "text": text,
            "ids": ids,
            "byte_offsets": offsets,
            "decoded": decoded,
        }
        if name in ("bert", "roberta", "t5", "unigram", "template"):
            enc_full = tok.encode(text, add_special_tokens=True)
            rec["ids_with_specials"] = list(enc_full.ids)
        vectors.append(rec)

    pipeline = {
        "name": name,
        "vocab_size": tok.get_vocab_size(),
        "vectors": vectors,
    }
    if with_config:
        pipeline["config_json"] = tok.to_str()
    return pipeline


def main():
    pipelines = []
    tok, extra = build_gpt2(add_prefix_space=False)
    pipelines.append(record_pipeline("gpt2", tok, extra, True))
    tok, extra = build_gpt2(add_prefix_space=True)
    pipelines.append(record_pipeline("gpt2_prefix", tok, extra, True))
    pipelines.append(record_pipeline("bert", *build_bert(), True))
    pipelines.append(record_pipeline("roberta", *build_roberta(), True))
    pipelines.append(record_pipeline("t5", *build_t5(), True))
    pipelines.append(record_pipeline("unigram", *build_unigram(), True))
    pipelines.append(record_pipeline("template", *build_template(), True))
    pipelines.append(record_pipeline("deepseek", *build_deepseek(), False))

    with open(OUT_PATH, "w", encoding="utf-8") as f:
        json.dump(pipelines, f, ensure_ascii=False, indent=2)

    total_vectors = sum(len(p["vectors"]) for p in pipelines)
    print(f"Wrote {OUT_PATH}")
    for p in pipelines:
        print(f"  {p['name']}: {len(p['vectors'])} vectors, vocab_size={p['vocab_size']}, "
              f"config={len(p.get('config_json', ''))} chars")
    print(f"Total vectors: {total_vectors}")


if __name__ == "__main__":
    main()
