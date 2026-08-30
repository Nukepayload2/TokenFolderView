Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' R6 correctness guards for the offset-free <see cref="Tokenizer.EncodeCount"/> /
    ''' <see cref="Tokenizer.EncodeFast"/> fast path (OffsetType.None + no-track alignment
    ''' skipping, introduced in R5). A no-track NormalizedString cannot serve every operation
    ''' (a partial-range transform such as ByteLevel addPrefixSpace Prepend; a second-round Slice
    ''' of a no-track piece). Those paths throw the internal
    ''' <see cref="OffsetTrackingRequiredException"/> and EncodeFast/EncodeCount fall back to a
    ''' fully-tracked encode, so any configuration returns the same Ids as
    ''' <see cref="Tokenizer.Encode"/> instead of throwing.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class EncodeFastRegressionTests

        ''' <summary>
        ''' A synthetic GPT-2-style BPE model whose vocabulary covers the byte-transformed output
        ''' of the ByteLevel pre-tokenizer (ASCII + the GPT-2 byte-table space "Ġ"). Unknown
        ''' scalars are silently omitted (no unk token), so CJK input still yields deterministic
        ''' (possibly empty) tokens that must be identical across Encode / EncodeFast / EncodeCount.
        ''' </summary>
        Private Shared Function BuildBpeTokenizer() As Tokenizer
            Dim vocab As New Dictionary(Of String, Integer)()
            Dim id As Integer = 0
            For Each c As Char In " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,!?;:'-+=()[]{}@#$%&*<>ĠĊ".ToCharArray()
                vocab(c.ToString()) = id
                id += 1
            Next
            For Each p As String In {"th", "he", "qu", "ic", "ck", "br", "ow", "fo", "ox", "ju", "mp", "ov", "la", "zy", "lo", "is", "un", "re", "er", "ed"}
                If Not vocab.ContainsKey(p) Then
                    vocab(p) = id
                    id += 1
                End If
            Next
            For Each w As String In {"the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "hello", "world", "tokenization", "The", "is", "a", "b", "c"}
                If Not vocab.ContainsKey(w) Then
                    vocab(w) = id
                    id += 1
                End If
            Next
            Dim merges As New List(Of String)() From {
                "t h", "h e", "q u", "i c", "c k", "b r", "o w", "f o", "o x",
                "j u", "m p", "o v", "l a", "z y", "d o", "o g", "l o", "i s",
                "u n", "r e", "e r", "e d"
            }
            Dim bpe As New BpeModel(vocab, merges, cacheCapacity:=10000)
            Return New Tokenizer(bpe)
        End Function

        ''' <summary>Asserts EncodeFast/EncodeCount agree with Encode on every text (no throw, Ids identical).</summary>
        Private Shared Sub AssertFastMatchesEncode(tokenizer As Tokenizer, texts As String(), context As String)
            For Each text As String In texts
                Dim enc As Encoding = tokenizer.Encode(text, False)
                Dim fast As Encoding = tokenizer.EncodeFast(text, False)
                Assert.HasCount(enc.Ids.Count, fast.Ids, $"{context}: EncodeFast count for '{text}'")
                CollectionAssert.AreEqual(enc.Ids, fast.Ids, $"{context}: EncodeFast ids for '{text}'")
                Assert.AreEqual(enc.Ids.Count, tokenizer.EncodeCount(text, False), $"{context}: EncodeCount for '{text}'")
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' PROBE(a): GPT-2 style ByteLevel(addPrefixSpace:=True, useRegex:=True). The addPrefixSpace
        ' Prepend(" ") is a partial-range transform that a no-track NormalizedString cannot serve.
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteLevelAddPrefixSpace_EncodeFastAndCount_DoNotThrow_AndMatchEncode()
            Dim tokenizer As Tokenizer = BuildBpeTokenizer()
            tokenizer.WithPreTokenizer(New ByteLevelPreTokenizer(True, True, True))
            Dim texts As String() = {
                "hello world",
                "The quick brown fox jumps over the lazy dog",
                "a b c 123",
                "Hello  世界 123",
                ""
            }
            AssertFastMatchesEncode(tokenizer, texts, "ByteLevel(addPrefixSpace:=True)")
        End Sub

        ' ------------------------------------------------------------------
        ' PROBE(b): non-fused multi-Split sequence. The first SplitByFunction produces no-track
        ' slices (empty alignment list); the second round Slice on those slices needs the alignment
        ' that was skipped.
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub MultiSplitSequence_EncodeFastAndCount_DoNotThrow_AndMatchEncode()
            Dim tokenizer As Tokenizer = BuildBpeTokenizer()
            tokenizer.WithPreTokenizer(
                New PreTokenizerSequence(New IPreTokenizer() {
                    New SplitPreTokenizer("Regex", "\s+", SplitDelimiterBehavior.Isolated, False),
                    New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False)
                }))
            Dim texts As String() = {
                "hello world",
                "a b 3c",
                "你好 世界 123",
                "a３b",
                ""
            }
            AssertFastMatchesEncode(tokenizer, texts, "non-fused multi-Split sequence")
        End Sub

        ' ------------------------------------------------------------------
        ' The DeepSeek fused path (3 manual Isolated splits + ByteLevel transform) must still take
        ' the no-track fast path: it must not throw, must match Encode, and must allocate strictly
        ' less than the fully-tracked Encode path (it skips building every piece's per-byte
        ' alignment list). If EncodeFast had silently fallen back, the two allocations would match.
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub DeepSeekFusedPath_FastPathTaken_MatchesEncode_AndAllocatesLess()
            Dim tokenizer As Tokenizer = BuildBpeTokenizer()
            tokenizer.WithPreTokenizer(
                New PreTokenizerSequence(New IPreTokenizer() {
                    New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                    New SplitPreTokenizer("Regex", DeepSeekCjkPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                    New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                    New ByteLevelPreTokenizer(False, True, False)
                }))

            Dim text As String = "Hello 世界 123 a３b tokenization is fast and correct."
            Dim enc As Encoding = tokenizer.Encode(text, False)
            Dim fast As Encoding = tokenizer.EncodeFast(text, False)
            Assert.HasCount(enc.Ids.Count, fast.Ids)
            CollectionAssert.AreEqual(enc.Ids, fast.Ids)
            Assert.AreEqual(enc.Ids.Count, tokenizer.EncodeCount(text, False))

            ' Warm up the ThreadLocal transform buffer and the BPE word cache so the measured
            ' calls only pay for the encode itself.
            For i As Integer = 0 To 5
                tokenizer.EncodeFast(text, False)
            Next

            Dim before As Long = GC.GetAllocatedBytesForCurrentThread()
            tokenizer.EncodeFast(text, False)
            Dim fastAlloc As Long = GC.GetAllocatedBytesForCurrentThread() - before

            before = GC.GetAllocatedBytesForCurrentThread()
            tokenizer.Encode(text, False)
            Dim byteAlloc As Long = GC.GetAllocatedBytesForCurrentThread() - before

            ' MSTest v4 IsLessThan(upperBound, value) asserts value < upperBound.
            Assert.IsLessThan(
                upperBound:=byteAlloc,
                value:=fastAlloc,
                message:=$"DeepSeek fused EncodeFast allocated {fastAlloc} B vs Encode {byteAlloc} B; " &
                          "expected the no-track fast path to allocate less (fallback must not have triggered).")
        End Sub

    End Class

End Namespace
