Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' R9 correctness gates:
    ''' ① the union-value BPE word cache (<c>CacheValue</c>: single-token entries store a
    '''    (id, byteLen) tuple and hold no <c>List</c>; multi-token entries store the List)
    '''    produces byte-identical <see cref="BpeModel.Tokenize"/> / <see cref="BpeModel.CountTokens"/>
    '''    results to a cache-less model, across golden words, repeated calls (forcing cache
    '''    hits), a seeded fuzz battery, and every config flag (ignoreMerges / byteFallback /
    '''    fuseUnk / maxWordLength / dropout=0 / cache off). The cache-on vs cache-off
    '''    differential is the ground truth: MergeWord runs fresh on the cache-less side, so
    '''    any union-cache insert/read mistake surfaces as a mismatch.
    ''' ② the ByteLevel no-track whole-range transform (assembled via ArrayPool(Of Char), no
    '''    intermediate StringBuilder) yields exactly the same byte-mapped normalized string as
    '''    mapping the source's UTF-8 bytes through the GPT-2 table.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class R9DifferentialTests

        Private Shared Function BuildBpe(Optional ignoreMerges As Boolean = False,
                                          Optional byteFallback As Boolean = False,
                                          Optional fuseUnk As Boolean = False,
                                          Optional maxWordLength As Integer? = Nothing,
                                          Optional cacheCapacity As Integer = 10000,
                                          Optional dropout As Double? = Nothing,
                                          Optional seededRandom As Object = Nothing) As BpeModel
            Dim vocab As New Dictionary(Of String, Integer)()
            Dim id As Integer = 0
            For Each c As Char In " abcdefghijklmnopqrstuvwxyz0123456789.,!?".ToCharArray()
                vocab(c.ToString()) = id
                id += 1
            Next
            vocab("<unk>") = id : id += 1
            For Each b As Integer In {0, 1, 2, 3}
                vocab($"<0x{b:X2}>") = id
                id += 1
            Next
            For Each p As String In {"th", "he", "qu", "ic", "ck", "br", "ow", "fo", "ox",
                                      "ju", "mp", "ov", "la", "zy", "lo", "is", "un", "re",
                                      "er", "ed", "##a", "##b", "##c"}
                If Not vocab.ContainsKey(p) Then
                    vocab(p) = id
                    id += 1
                End If
            Next
            For Each w As String In {"the", "quick", "brown", "fox", "jumps", "over", "lazy",
                                     "dog", "hello", "world", "tokenization", "abc", "a", "b", "c"}
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
            Return New BpeModel(
                vocab, merges,
                unkToken:="<unk>",
                fuseUnk:=fuseUnk,
                byteFallback:=byteFallback,
                ignoreMerges:=ignoreMerges,
                cacheCapacity:=cacheCapacity,
                maxWordLength:=maxWordLength,
                dropout:=dropout,
                seededRandom:=seededRandom)
        End Function

        ''' <summary>
        ''' A deterministic word battery: golden single-token / multi-token words, then a seeded
        ''' fuzz of random length words. Each fuzz word is emitted twice so the second call hits
        ''' the union cache; the battery also includes words longer than any maxWordLength
        ''' threshold (to exercise the bypass) and words whose scalars are all unknown (to
        ''' exercise empty-result handling).
        ''' </summary>
        Private Shared Function Battery() As List(Of String)
            Dim words As New List(Of String)()
            words.Add("")
            words.Add("a")
            words.Add("b")
            words.Add("the")
            words.Add("quick")
            words.Add("brown fox")
            words.Add("abc123")
            words.Add("un")
            words.Add("re")
            words.Add("er")
            words.Add("hello world")
            words.Add("tokenization")
            words.Add("xyzzy")
            words.Add("zz")
            words.Add("a" & ChrW(&H3042))
            words.Add("３")
            words.Add("the quick brown fox jumps over the lazy dog")
            words.Add(String.Join("", Enumerable.Repeat("ab", 40)))
            words.Add(String.Concat(Enumerable.Repeat("x", 300)))
            ' Words with no vocab token and no byte tokens: every scalar silently omitted.
            words.Add(ChrW(&H3042) & ChrW(&H3043) & ChrW(&H3044))

            Dim rng As New Random(424242)
            Dim pool As String =
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" &
                " .,!?;:'" & """" & "-+=()[]{}@#$%^&*<>/\|`~" &
                "你好世界中文编程测试日本語かなカナ한국어３４５" & vbCrLf & vbTab
            For i As Integer = 0 To 1500
                Dim len As Integer = rng.Next(0, 45)
                Dim chars As New List(Of Char)(len)
                For k As Integer = 0 To len - 1
                    chars.Add(pool(rng.Next(pool.Length)))
                Next
                Dim w As String = New String(chars.ToArray())
                words.Add(w)
                words.Add(w) ' duplicate: second call must be a union-cache hit
            Next
            Return words
        End Function

        <TestMethod>
        Public Sub UnionCache_MatchesNoCache_TokenizeAndCount()
            Dim configs As (String, BpeModel, BpeModel)() = {
                ("default", BuildBpe(cacheCapacity:=10000), BuildBpe(cacheCapacity:=0)),
                ("ignoreMerges", BuildBpe(ignoreMerges:=True, cacheCapacity:=10000), BuildBpe(ignoreMerges:=True, cacheCapacity:=0)),
                ("byteFallback", BuildBpe(byteFallback:=True, cacheCapacity:=10000), BuildBpe(byteFallback:=True, cacheCapacity:=0)),
                ("fuseUnk", BuildBpe(fuseUnk:=True, cacheCapacity:=10000), BuildBpe(fuseUnk:=True, cacheCapacity:=0)),
                ("maxWordLength", BuildBpe(maxWordLength:=16, cacheCapacity:=10000), BuildBpe(maxWordLength:=16, cacheCapacity:=0)),
                ("dropout0", BuildBpe(dropout:=0.0, cacheCapacity:=10000), BuildBpe(dropout:=0.0, cacheCapacity:=0))
            }
            Dim words As List(Of String) = Battery()
            For Each cfg As (String, BpeModel, BpeModel) In configs
                For Each w As String In words
                    Dim cachedToks As List(Of Token) = cfg.Item2.Tokenize(w)
                    Dim freshToks As List(Of Token) = cfg.Item3.Tokenize(w)
                    Assert.HasCount(freshToks.Count, cachedToks, $"{cfg.Item1}: Tokenize count '{w}'")
                    For i As Integer = 0 To cachedToks.Count - 1
                        Assert.AreEqual(freshToks(i), cachedToks(i), $"{cfg.Item1}: Tokenize[{i}] '{w}'")
                    Next
                    Assert.AreEqual(freshToks.Count, cfg.Item2.CountTokens(w), $"{cfg.Item1}: CountTokens(cache) '{w}'")
                    Assert.AreEqual(freshToks.Count, cfg.Item3.CountTokens(w), $"{cfg.Item1}: CountTokens(no cache) '{w}'")
                Next
            Next
        End Sub

        <TestMethod>
        Public Sub UnionCache_HitAndMiss_StatsAndParity()
            Dim bpe As BpeModel = BuildBpe(cacheCapacity:=10000)
            bpe.EnableCacheStats()
            bpe.ResetCacheStats()

            ' Single-token words: the BuildBpe merge map forms these as one merged token
            ' ("th" <- "t h", "he" <- "h e", "lo" <- "l o", "is" <- "i s", "un" <- "u n",
            ' "re" <- "r e", "er" <- "e r", "ed" <- "e d"), exercising the Single_ union arm.
            Dim singleWords As String() = {"th", "he", "lo", "is", "un", "re", "er", "ed"}
            Dim multiWords As String() = {"the", "quick", "tokenization", "hello world", "xyzzy", "a" & ChrW(&H3042)}

            For Each w As String In singleWords
                Dim c1 As Integer = bpe.CountTokens(w)
                Dim c2 As Integer = bpe.CountTokens(w)
                Assert.AreEqual(c1, c2, $"CountTokens repeat single '{w}'")
                Assert.AreEqual(1, c1, $"single-token word '{w}' must merge to 1 token")
                Assert.AreEqual(bpe.Tokenize(w).Count, c2, $"Tokenize parity single '{w}'")
            Next
            For Each w As String In multiWords
                Dim c1 As Integer = bpe.CountTokens(w)
                Dim c2 As Integer = bpe.CountTokens(w)
                Assert.AreEqual(c1, c2, $"CountTokens repeat multi '{w}'")
                Assert.AreEqual(bpe.Tokenize(w).Count, c2, $"Tokenize parity multi '{w}'")
            Next

            ' Every word was seen twice: the second CountTokens call must be a cache hit.
            Dim stats As CacheStats = bpe.GetCacheStats()
            Assert.IsGreaterThanOrEqualTo(singleWords.Length + multiWords.Length, stats.Hits,
                "each repeated word's second call must be a union-cache hit")
            Assert.IsGreaterThanOrEqualTo(singleWords.Length + multiWords.Length, stats.Misses,
                "each first call must be a union-cache miss")
        End Sub

        <TestMethod>
        Public Sub UnionCache_EmptyResultWord_NotCached_StillCorrect()
            ' A BPE without an unk token silently omits unknown scalars -> 0 tokens. The union
            ' cache must treat such words as misses (Count = 0) and keep returning 0 without
            ' corrupting other entries.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"b", 1}
            }
            Dim withCache As New BpeModel(vocab, New List(Of String)(), cacheCapacity:=10000)
            Dim fresh As New BpeModel(vocab, New List(Of String)(), cacheCapacity:=0)
            Dim word As String = ChrW(&H3042) & ChrW(&H3043) ' あい: unknown, no unk -> omitted
            Assert.IsEmpty(fresh.Tokenize(word))
            Assert.IsEmpty(withCache.Tokenize(word))
            Assert.IsEmpty(withCache.Tokenize(word)) ' repeat: must not crash / corrupt
            Assert.AreEqual(0, withCache.CountTokens(word))
            Assert.AreEqual(0, withCache.CountTokens(word))
            ' A normal word must still work after the empty-result word.
            Assert.HasCount(1, withCache.Tokenize("a"))
            Assert.AreEqual(1, withCache.CountTokens("a"))
        End Sub

        <TestMethod>
        Public Sub UnionCache_NoCache_SingleAndMultiToken_TokenStreamsEqual()
            ' Direct single-token vs multi-token cache-entry comparison: the cached result must
            ' equal a freshly merged result for both the tuple-stored (single) and list-stored
            ' (multi) union arms.
            Dim cached As BpeModel = BuildBpe(cacheCapacity:=10000)
            Dim fresh As BpeModel = BuildBpe(cacheCapacity:=0)
            Dim words As String() = {"the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog",
                                     "tokenization", "hello world", "abc123", "the quick brown fox"}
            For Each w As String In words
                ' First call inserts into the union cache; second call reads it back.
                Dim first As List(Of Token) = cached.Tokenize(w)
                Dim second As List(Of Token) = cached.Tokenize(w)
                Dim reference As List(Of Token) = fresh.Tokenize(w)
                Assert.HasCount(reference.Count, first, $"count '{w}'")
                Assert.HasCount(reference.Count, second, $"repeat count '{w}'")
                For i As Integer = 0 To reference.Count - 1
                    Assert.AreEqual(reference(i), first(i), $"first[{i}] '{w}'")
                    Assert.AreEqual(reference(i), second(i), $"second[{i}] '{w}'")
                Next
                Assert.AreEqual(reference.Count, cached.CountTokens(w), $"CountTokens '{w}'")
            Next
        End Sub

        ''' <summary>
        ''' ② Gate: the no-track whole-range (Char, Integer) transform — the ByteLevel count-path
        ''' hot spot, now assembled through <c>ArrayPool(Of Char).Rent</c> — must produce exactly
        ''' the byte-mapped normalized string. The expected value is computed independently by
        ''' mapping the source's UTF-8 bytes through the GPT-2 table.
        ''' </summary>
        <TestMethod>
        Public Sub ByteLevel_NoTrackWholeRange_MatchesDirectByteMapping()
            Dim texts As String() = {
                "",
                "a",
                "Hello my friend 123",
                "你好世界 ３４５ abc",
                "a３b emoji 😀 x",
                "  leading and trailing  ",
                "abc" & vbCrLf & "def" & vbTab & "ghi",
                String.Join("", Enumerable.Repeat("The quick brown fox 你好 ", 20))
            }
            Dim setTrack As MethodInfo = GetType(NormalizedString).GetMethod(
                "SetTrackAlignments", BindingFlags.Instance Or BindingFlags.NonPublic)
            Dim appendBt As MethodInfo = GetType(NormalizedString).GetMethod(
                "AppendByteTransform", BindingFlags.Instance Or BindingFlags.NonPublic)
            Dim table As Char() = BytesToUnicodeTable.GetBytesToCharArray()

            For Each txt As String In texts
                Dim root As NormalizedString = NormalizedString.FromString(txt)
                setTrack.Invoke(root, New Object() {False})
                Dim dest As New List(Of (Char, Integer))()
                appendBt.Invoke(root, New Object() {dest})

                ' The no-track whole-range transform concatenates the dest chars into _normalized
                ' (ArrayPool(Of Char) assembly path).
                root.Transform(dest, 0)

                Dim expectedBytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(txt)
                Dim expectedChars As New List(Of Char)(expectedBytes.Length)
                For Each b As Byte In expectedBytes
                    expectedChars.Add(table(b))
                Next
                Dim expected As String = New String(expectedChars.ToArray())

                Assert.AreEqual(expected, root.Get, $"Get after no-track ByteLevel transform for '{txt}'")
                ' Len() is the UTF-8 byte length; mapped chars can be multi-byte (e.g. 'Ġ' is
                ' U+0120 = 2 UTF-8 bytes), so compare against the expected string's UTF-8 length.
                Assert.AreEqual(
                    Global.System.Text.Encoding.UTF8.GetByteCount(expected),
                    root.Len(),
                    $"Len after no-track ByteLevel transform for '{txt}'")
            Next
        End Sub

        ''' <summary>
        ''' M1 differential: the new Char-array byte-map emitter
        ''' (<see cref="BytesToUnicodeTable.AppendByteTransformChars"/>) must emit the same chars in
        ''' the same order as the (Char, Integer) stream of
        ''' <see cref="BytesToUnicodeTable.AppendByteTransform"/>, and agree on the emitted byte
        ''' count, across every scalar that appears in real text (1/2/3-byte ranges, a sample of
        ''' 4-byte scalars, and lone surrogates mapped as U+FFFD). Guards the fused ByteLevel
        ''' direct-build against drift from the table transform it mirrors.
        ''' </summary>
        <TestMethod>
        Public Sub AppendByteTransformChars_MirrorsAppendByteTransform()
            Dim buf As Char() = New Char(3) {}
            Dim tupleList As New List(Of (Char, Integer))()
            For cp As Integer = 0 To &H2FFFF
                tupleList.Clear()
                Dim tupleCount As Integer = BytesToUnicodeTable.AppendByteTransform(tupleList, cp)
                Dim charCount As Integer = BytesToUnicodeTable.AppendByteTransformChars(buf, 0, cp)
                Assert.AreEqual(tupleCount, charCount, $"byte count mismatch for cp=U+{cp:X4}")
                Assert.AreEqual(tupleList.Count, charCount, $"stream length mismatch for cp=U+{cp:X4}")
                For i As Integer = 0 To charCount - 1
                    Assert.AreEqual(tupleList(i).Item1, buf(i), $"char mismatch for cp=U+{cp:X4} byte {i}")
                Next
            Next
            ' A few 4-byte scalars and lone-surrogate boundaries.
            For Each cp As Integer In {&H10000, &H10001, &H10FFFF, &H1F600, &H1F680, &HD800, &HDFFF, &HD7FF, &HE000}
                tupleList.Clear()
                Dim tupleCount As Integer = BytesToUnicodeTable.AppendByteTransform(tupleList, cp)
                Dim charCount As Integer = BytesToUnicodeTable.AppendByteTransformChars(buf, 0, cp)
                Assert.AreEqual(tupleCount, charCount, $"byte count mismatch for cp=U+{cp:X4}")
                Assert.AreEqual(tupleList.Count, charCount, $"stream length mismatch for cp=U+{cp:X4}")
                For i As Integer = 0 To charCount - 1
                    Assert.AreEqual(tupleList(i).Item1, buf(i), $"char mismatch for cp=U+{cp:X4} byte {i}")
                Next
            Next
        End Sub

        ''' <summary>
        ''' M1 differential: the no-track fused-split-with-ByteLevel path
        ''' (<see cref="PreTokenizedString.FuseIsolatedSplitsWithByteMap"/>, the EncodeCount/EncodeFast
        ''' hot path) produces pieces whose final Get (the byte-mapped string, built directly by
        ''' <see cref="NormalizedString.SliceWithByteMap"/>) matches the sequential tracked reference
        ''' piece for piece over the full DeepSeek pre-tokenizer. Guards the direct-build's mapped
        ''' string against the reference Transform output.
        ''' </summary>
        <TestMethod>
        Public Sub FusedWithByteMap_NoTrack_Get_MatchesSequentialTracked()
            Dim patterns As IPreTokenizer() = {
                New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekCjkPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New ByteLevelPreTokenizer(False, True, False)
            }
            Dim texts As String() = {
                "Hello my friend, how's it going? I'm fine.",
                "Hello  世界 123 a３b",
                "abc123 ４５６x 3人目の彼",
                "a３b",
                "return await Task.WhenAll(tasks.Select(Function(t) t.RunAsync()))",
                "   leading/trailing  ",
                "€£¥~ 你好 😀 x"
            }
            Dim setTrack As MethodInfo = GetType(NormalizedString).GetMethod(
                "SetTrackAlignments", BindingFlags.Instance Or BindingFlags.NonPublic)
            Dim fuseWithByteMap As MethodInfo = GetType(PreTokenizedString).GetMethod(
                "FuseIsolatedSplitsWithByteMap", BindingFlags.Instance Or BindingFlags.NonPublic)
            Assert.IsNotNull(fuseWithByteMap)

            For Each txt In texts
                ' Sequential tracked reference.
                Dim seqPts As PreTokenizedString = PreTokenizedString.FromString(txt)
                For Each pt In patterns
                    pt.PreTokenize(seqPts)
                Next
                Dim expected As New List(Of String)()
                For Each s As Split In seqPts.Splits
                    expected.Add(s.Normalized.Get)
                Next

                ' No-track fused path with the ByteLevel map folded in.
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(txt)
                setTrack.Invoke(pts.Splits(0).Normalized, New Object() {False})
                Dim splitPatterns As New List(Of Pattern)() From {
                    New DeepSeekNumbersPattern(),
                    New DeepSeekCjkPattern(),
                    New DeepSeekGpt2Pattern()
                }
                fuseWithByteMap.Invoke(pts, New Object() {splitPatterns})

                Dim actual As New List(Of String)()
                For Each s As Split In pts.Splits
                    actual.Add(s.Normalized.Get)
                Next
                CollectionAssert.AreEqual(expected, actual, $"fused-with-byte-map no-track Get parity for '{txt}'")
            Next
        End Sub

        ''' <summary>
        ''' Pipeline-level regression guard for R9: the real DeepSeek tokenizer's EncodeCount
        ''' (lazy no-track + union cache + pooled merges) must still equal the full Encode length
        ''' on high-piece-density real-code lines and a seeded fuzz battery. Mirrors the R8 gate
        ''' so the R8 lazy-slice / S1 fixes are confirmed non-regressed after the R9 changes.
        ''' </summary>
        <TestMethod>
        Public Sub DeepSeekRealFile_EncodeCount_MatchesEncode_R9()
            Dim path As String = "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"
            If Not IO.File.Exists(path) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(path)
            Dim realCode As String() = {
                "public NotInheritable Class Foo(Of T) Implements IModel" & vbLf,
                "Dim x As Integer = a + b * (c - d) / e % f" & vbLf,
                "If (a IsNot Nothing) AndAlso (b <> 0) Then Return String.Format(""{0:X2}"", value)" & vbLf,
                "return await Task.WhenAll(tasks.Select(Function(t) t.RunAsync()))" & vbLf,
                "    ->  =>  ==  !=  >=  <=  &&  ||  ++  --  /* */  //  #region" & vbLf,
                "你好世界 中文编程 字符串 12345 （中文括号）" & vbLf,
                "Private ReadOnly _cache As ThreadLocal(Of Cache(Of String, List(Of (Integer, Integer))))" & vbLf
            }
            For Each t As String In realCode
                Dim enc As Encoding = tokenizer.Encode(t, False)
                Assert.AreEqual(enc.Ids.Count, tokenizer.EncodeCount(t, False), $"deepseek real-code '{t}'")
            Next

            Dim rng As New Random(20260830)
            Dim pool As String = "abcXYZ019 )(*+-=<>/{}[];:,.!? 你好世界３４５"
            For iter As Integer = 0 To 300
                Dim len As Integer = rng.Next(1, 90)
                Dim chars As New List(Of Char)(len)
                For i As Integer = 0 To len - 1
                    chars.Add(pool(rng.Next(pool.Length)))
                Next
                Dim txt As String = New String(chars.ToArray())
                Dim enc2 As Encoding = tokenizer.Encode(txt, False)
                Assert.AreEqual(enc2.Ids.Count, tokenizer.EncodeCount(txt, False), $"deepseek fuzz#{iter} '{txt}'")
            Next
        End Sub

        ''' <summary>
        ''' M2 differential: the range-driven count path (<see cref="PreTokenizedString.FusedRangesBySplit"/>
        ''' + <see cref="PreTokenizedString.CountFusedRanges"/>, the EncodeCount fast path for the
        ''' DeepSeek fused+pure-map config) produces the exact token total the sequential tracked
        ''' pre-tokenization + model would, without materializing a per-piece
        ''' <see cref="Split"/> / <see cref="NormalizedString"/>. Guards the M2 range loop
        ''' (byte-mapped strings built directly via <see cref="NormalizedString.ToByteMappedString"/>,
        ''' attached-token splits counted separately) against the tracked reference over a battery +
        ''' seeded fuzz. Read-only: never touches the filesystem or process state.
        ''' </summary>
        <TestMethod>
        Public Sub M2_FusedRangeCount_MatchesSequentialTracked()
            Dim pretokenizers As IPreTokenizer() = {
                New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekCjkPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New ByteLevelPreTokenizer(False, True, False)
            }
            Dim patterns As New List(Of Pattern)() From {
                New DeepSeekNumbersPattern(),
                New DeepSeekCjkPattern(),
                New DeepSeekGpt2Pattern()
            }
            ' No BPE cache: both sides re-merge fresh, so a shared model instance is deterministic.
            Dim model As BpeModel = BuildBpe(byteFallback:=True, cacheCapacity:=0)

            Dim setTrack As MethodInfo = GetType(NormalizedString).GetMethod(
                "SetTrackAlignments", BindingFlags.Instance Or BindingFlags.NonPublic)
            Dim fusedRanges As MethodInfo = GetType(PreTokenizedString).GetMethod(
                "FusedRangesBySplit", BindingFlags.Instance Or BindingFlags.NonPublic)
            Dim countRanges As MethodInfo = GetType(PreTokenizedString).GetMethod(
                "CountFusedRanges", BindingFlags.Instance Or BindingFlags.NonPublic)
            Assert.IsNotNull(fusedRanges, "FusedRangesBySplit must exist (Friend M2 method)")
            Assert.IsNotNull(countRanges, "CountFusedRanges must exist (Friend M2 method)")

            Dim countFn As New Func(Of String, Integer)(Function(m As String) model.CountTokens(m))

            For Each txt As String In M2TextBattery()
                ' Sequential tracked reference: run each pre-tokenizer in order on a tracked
                ' PreTokenizedString (the ByteLevel transform builds the mapped pieces), then sum
                ' the model count over the pieces.
                Dim refPts As PreTokenizedString = PreTokenizedString.FromString(txt)
                For Each pt In pretokenizers
                    pt.PreTokenize(refPts)
                Next
                Dim expected As Integer = 0
                For Each s As Split In refPts.Splits
                    expected += model.CountTokens(s.Normalized.Get)
                Next

                ' M2 range-driven path: same fused patterns, but no piece objects are created.
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(txt)
                For Each s As Split In pts.Splits
                    setTrack.Invoke(s.Normalized, New Object() {False})
                Next
                Dim rangesObj As Object = fusedRanges.Invoke(pts, New Object() {patterns})
                Dim actual As Integer = CInt(countRanges.Invoke(pts, New Object() {rangesObj, countFn}))

                Assert.AreEqual(expected, actual, $"M2 fused-range count parity for '{txt}'")
            Next
        End Sub

        ''' <summary>
        ''' M2 pipeline gate: the real DeepSeek tokenizer's <see cref="Tokenizer.EncodeCount"/> —
        ''' which takes the M2 range-driven path when the fused+pure-map config applies — must equal
        ''' the full <see cref="Tokenizer.Encode"/> length, and its per-stage profile must be
        ''' self-consistent (<c>TokenCount</c> matches <c>EncodeCount</c>) with the fuse phase
        ''' allocating strictly less than the model phase. The latter proves the count path actually
        ''' ran M2: the old fused pass materialized per-piece objects in the FusedSplit phase, whose
        ''' allocation would otherwise dominate the Model phase. Read-only.
        ''' </summary>
        <TestMethod>
        Public Sub M2_DeepSeekRealFile_EncodeCount_MatchesEncode_AndProfileConsistent()
            Dim path As String = "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"
            If Not IO.File.Exists(path) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(path)

            Dim lines As String() = {
                "Hello my friend, how's it going? I'm fine." & vbLf,
                "Hello  世界 123 a３b" & vbLf,
                "public NotInheritable Class Foo(Of T) Implements IModel" & vbLf,
                "Dim x As Integer = a + b * (c - d) / e % f" & vbLf,
                "If (a IsNot Nothing) AndAlso (b <> 0) Then Return String.Format(""{0:X2}"", value)" & vbLf,
                "return await Task.WhenAll(tasks.Select(Function(t) t.RunAsync()))" & vbLf,
                "    ->  =>  ==  !=  >=  <=  &&  ||  ++  --  /* */  //  #region" & vbLf,
                "你好世界 中文编程 字符串 12345 （中文括号）" & vbLf,
                "Private ReadOnly _cache As ThreadLocal(Of Cache(Of String, List(Of (Integer, Integer))))" & vbLf
            }
            For Each t As String In lines
                Assert.AreEqual(tokenizer.Encode(t, False).Ids.Count, tokenizer.EncodeCount(t, False),
                    $"M2 EncodeCount parity for '{t}'")
            Next

            ' Profile consistency + M2-taken proof on a high-piece-density text (repeat the battery
            ' 8x so the model phase's mapped-string allocation clearly dominates the range phase).
            Dim big As String = String.Concat(Enumerable.Repeat(String.Concat(lines), 8))
            For i As Integer = 0 To 3
                tokenizer.ProfileCountStages(big)
                tokenizer.EncodeCount(big, False)
            Next
            Dim p As EncodeCountStageProfile = tokenizer.ProfileCountStages(big)
            Assert.AreEqual(tokenizer.EncodeCount(big, False), p.TokenCount,
                "M2 profile TokenCount must match EncodeCount")
            ' IsGreaterThan(lowerBound, value) asserts value > lowerBound, so to prove the fuse
            ' phase allocates strictly less than the model phase, FusedSplit is the lower bound.
            Assert.IsGreaterThan(p.FusedSplitAllocated, p.ModelAllocated,
                "M2 fuse phase (ranges only) must allocate less than the model phase (mapped strings + BPE)")
        End Sub

        ''' <summary>
        ''' M3 differential: the no-track extract (<see cref="AddedVocabulary.ExtractAndNormalizeNoTrack"/>,
        ''' used by <see cref="Tokenizer.EncodeCount"/> when the normalizer is identity and the
        ''' pre-tokenizer is the M2 fused-count config) produces the exact token total the fully
        ''' tracked <see cref="Tokenizer.Encode"/> does — including inputs where added-token
        ''' literals match the text (so the no-track slice path is exercised) — and its Extract
        ''' phase allocates far below the tracked ~8 B/char identity alignment list (proving the
        ''' no-track path was actually taken). Read-only.
        ''' </summary>
        <TestMethod>
        Public Sub M3_DeepSeek_NoTrackExtract_CountMatchesEncode_AndIsNoTrack()
            Dim path As String = "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"
            If Not IO.File.Exists(path) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(path)

            ' Golden battery: plain code plus added-token literals embedded in otherwise ordinary
            ' text, so both the "no match" and the "match → no-track slice" branches of
            ' ExtractAndNormalizeNoTrack run, including leading/trailing/adjacent/Chinese matches.
            Dim texts As String() = {
                "Hello my friend, how's it going? I'm fine.",
                "public NotInheritable Class Foo(Of T) Implements IModel" & vbLf,
                "prefix<｜begin▁of▁sentence｜>suffix",
                "a<｜end▁of▁sentence｜>b",
                "x<think>y</think>z",
                "<dsml:foo></dsml:bar>",
                "<｜tool▁calls▁begin｜>",
                "<｜image2｜> <｜table｜> row <｜/table｜>",
                "你好世界 <｜end▁of▁sentence｜> 中文编程",
                String.Join("", Enumerable.Repeat("ab<｜end▁of▁sentence｜>cd ", 20))
            }
            For Each t In texts
                Assert.AreEqual(tokenizer.Encode(t, False).Ids.Count, tokenizer.EncodeCount(t, False),
                    $"M3 no-track EncodeCount parity for '{t}'")
            Next

            ' Big text with matches: EncodeCount must equal the tracked Encode (exercises the
            ' no-track slice path) and the profile must agree with EncodeCount.
            Dim big As String = String.Concat(Enumerable.Repeat("Hello 世界 <｜end▁of▁sentence｜> abc123 ", 40))
            Assert.AreEqual(tokenizer.Encode(big, False).Ids.Count, tokenizer.EncodeCount(big, False),
                "M3 no-track EncodeCount parity on the big battery")
            Assert.AreEqual(tokenizer.EncodeCount(big, False), tokenizer.ProfileCountStages(big).TokenCount,
                "M3 profile TokenCount must match EncodeCount on the big battery")

            ' No-track proof on a plain code text (no added-token matches): the tracked extract
            ' allocates ~8 B/char for the identity alignment list; the no-track extract skips it
            ' entirely, so the Extract phase must be well under 3 B/char. (A match-heavy text would
            ' legitimately allocate per-piece objects, so it is not used for this bound.)
            Dim plain As String = String.Concat(Enumerable.Repeat(
                "public NotInheritable Class Foo(Of T) Implements IModel" & vbLf, 30))
            Assert.AreEqual(tokenizer.Encode(plain, False).Ids.Count, tokenizer.EncodeCount(plain, False),
                "M3 no-track EncodeCount parity on the plain code text")
            For i As Integer = 0 To 2
                tokenizer.ProfileCountStages(plain)
            Next
            Dim pp As EncodeCountStageProfile = tokenizer.ProfileCountStages(plain)
            Assert.AreEqual(tokenizer.EncodeCount(plain, False), pp.TokenCount,
                "M3 profile TokenCount must match EncodeCount on the plain code text")
            Assert.IsLessThan(plain.Length * 3L, pp.ExtractAllocated,
                $"M3 no-track extract must allocate well under the tracked ~8B/char alignment list: {pp.ExtractAllocated} B for {plain.Length} chars")
        End Sub

        ''' <summary>
        ''' M6 gate for the sparse scalar-boundary index: <see cref="NormalizedString.ByteToNetIndexCached"/>
        ''' (backed by the sparse breakpoint index) must match the reference
        ''' <see cref="Utf8Helpers.ByteToNetIndex"/> (an O(n) walk) for EVERY byte offset, including
        ''' offsets inside a multi-byte scalar (which must floor to the scalar start), and
        ''' <see cref="NormalizedString.Slice"/> must succeed exactly on the scalar boundaries the
        ''' reference <see cref="Utf8Helpers.IsUtf8CharBoundary"/> reports. The battery spans pure
        ''' ASCII, mixed ASCII/non-ASCII, dense CJK, supplementary (surrogate-pair) scalars and lone
        ''' surrogates, so a sparse-index breakpoint/floor error surfaces as a mismatch. Read-only.
        ''' </summary>
        <TestMethod>
        Public Sub M6_SparseIndex_ByteToNetAndBoundaries_MatchReference()
            Dim byteToNet As MethodInfo = GetType(NormalizedString).GetMethod(
                "ByteToNetIndexCached", BindingFlags.Instance Or BindingFlags.NonPublic)
            Assert.IsNotNull(byteToNet, "ByteToNetIndexCached must exist (Friend M6 method)")

            Dim texts As String() = {
                "",
                "a",
                "Hello world, how are you?",
                "Hello 世界",
                "世界 你好 😀 x",
                "a３b",
                String.Join("", Enumerable.Repeat("The quick brown fox 你好 😀 ", 10)),
                "é" & vbTab & "汉字" & vbLf & "😀😀",
                New String(ChrW(&HD800), 1),
                "abc" & ChrW(&HDC00) & "def",
                "123 ４５６ xyz"
            }
            For Each txt In texts
                Dim ns As NormalizedString = NormalizedString.FromString(txt)
                Dim byteLen As Integer = Utf8Helpers.Utf8Length(txt)
                For b As Integer = 0 To byteLen
                    Dim cached As Integer = CInt(byteToNet.Invoke(ns, New Object() {b}))
                    Dim reference As Integer = Utf8Helpers.ByteToNetIndex(txt, b)
                    Assert.AreEqual(reference, cached,
                        $"ByteToNetIndexCached({b})={cached} != reference {reference} for byte {b} of '{txt}'")
                Next
                For b As Integer = 0 To byteLen
                    Dim isBoundary As Boolean = Utf8Helpers.IsUtf8CharBoundary(txt, b)
                    Dim slicedOk As Boolean = True
                    Try
                        ns.Slice(New OffsetRange(False, b, byteLen))
                    Catch ex As InvalidOperationException
                        slicedOk = False
                    End Try
                    Assert.AreEqual(isBoundary, slicedOk,
                        $"Slice boundary check mismatch at byte {b} of '{txt}' (boundary={isBoundary}, sliced={slicedOk})")
                Next
            Next
        End Sub

        ''' <summary>
        ''' M5 gate for the shared per-thread symbol scratch (<see cref="BpeModel"/>._symbolScratch,
        ''' reused across cache misses by <see cref="BpeModel.CountTokens"/>). Interleaves cache
        ''' misses (each refills the same scratch) with cache hits, and asserts
        ''' <c>CountTokens(w) = Tokenize(w).Count</c> on every repeat. A scratch-aliasing bug would
        ''' clobber a cached multi-token value when a later word reuses the scratch, and
        ''' <see cref="BpeModel.Tokenize"/> reads that cached list on a hit, so the equality fails.
        ''' The real DeepSeek pipeline (EncodeCount's M2 fused path feeds CountTokens) must also
        ''' equal the full Encode length. Read-only.
        ''' </summary>
        <TestMethod>
        Public Sub M5_SymbolScratchReuse_CountTokensOutputUnchanged()
            Dim bpe As BpeModel = BuildBpe(cacheCapacity:=10000)
            Dim words As String() = {
                "the", "quick brown", "tokenization", "hello world", "abc123",
                "jumps over lazy dog", "a" & ChrW(&H3042), "the quick brown fox jumps over the lazy dog",
                String.Join("", Enumerable.Repeat("ab", 40)),
                "x" & String.Concat(Enumerable.Repeat("x", 200))
            }
            ' Repeat 6x: iteration 0 is all misses (cache fill + scratch reuse between words),
            ' iterations 1-5 are all hits (cached values read back after later words reused the
            ' scratch). A cached multi-token list aliased by the scratch would return the wrong
            ' count on the hit passes.
            For r As Integer = 0 To 5
                For Each w In words
                    Assert.AreEqual(bpe.Tokenize(w).Count, bpe.CountTokens(w),
                        $"CountTokens must equal Tokenize count (repeat {r}, '{w}')")
                Next
            Next

            ' Pipeline-level on the real DeepSeek tokenizer: the M2 range-driven path builds each
            ' piece's mapped string and feeds CountTokens (with the shared scratch); the total must
            ' equal the fully materialized Encode length.
            Dim path As String = "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"
            If IO.File.Exists(path) Then
                Dim tokenizer As Tokenizer = Tokenizer.FromFile(path)
                For Each t As String In M2TextBattery()
                    Assert.AreEqual(tokenizer.Encode(t, False).Ids.Count, tokenizer.EncodeCount(t, False),
                        $"M5 deepseek EncodeCount parity for '{t}'")
                Next
            Else
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
            End If
        End Sub

        ''' <summary>
        ''' M7 gate for the reused per-thread range/scratch buffers in
        ''' <see cref="PreTokenizedString.ComputeFusedRanges"/>: the actual fused ranges (produced via
        ''' the two alternating per-thread range buffers and the reused scratch match list) must equal
        ''' an INDEPENDENT reference that recomputes the same ranges with a fresh
        ''' <see cref="List(Of MatchInfo)"/> per range, materializes each slice substring, and walks
        ''' byte offsets with the reference <see cref="Utf8Helpers.ByteToNetIndex"/> (not the sparse
        ''' cached index). Iterating the whole battery through the same per-thread buffers stresses
        ''' reuse across calls — a Clear/refill, buffer-alternation, or scratch-aliasing mistake
        ''' surfaces as a range mismatch. Read-only.
        ''' </summary>
        <TestMethod>
        Public Sub M7_FusedRangeReuse_RangesMatchIndependentReference()
            Dim patterns As New List(Of Pattern)() From {
                New DeepSeekNumbersPattern(),
                New DeepSeekCjkPattern(),
                New DeepSeekGpt2Pattern()
            }
            Dim setTrack As MethodInfo = GetType(NormalizedString).GetMethod(
                "SetTrackAlignments", BindingFlags.Instance Or BindingFlags.NonPublic)
            Dim fusedRanges As MethodInfo = GetType(PreTokenizedString).GetMethod(
                "FusedRangesBySplit", BindingFlags.Instance Or BindingFlags.NonPublic)
            Assert.IsNotNull(fusedRanges, "FusedRangesBySplit must exist (Friend M2 method)")

            For Each txt In M2TextBattery()
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(txt)
                For Each s As Split In pts.Splits
                    setTrack.Invoke(s.Normalized, New Object() {False})
                Next
                Dim rangesObj As Object = fusedRanges.Invoke(pts, New Object() {patterns})
                Dim actualList As List(Of (NormalizedString, List(Of (Integer, Integer)))) =
                    DirectCast(rangesObj, List(Of (NormalizedString, List(Of (Integer, Integer)))))
                Assert.AreEqual(pts.Splits.Count, actualList.Count, $"split count for '{txt}'")

                For Each pair As (NormalizedString, List(Of (Integer, Integer))) In actualList
                    Dim text As String = pair.Item1.Get
                    Dim actual As List(Of (Integer, Integer)) = pair.Item2
                    Dim reference As List(Of (Integer, Integer)) = IndependentFusedRanges(text, patterns)
                    Assert.AreEqual(reference.Count, actual.Count,
                        $"range count for '{txt}' (reference {reference.Count} != actual {actual.Count})")
                    For i As Integer = 0 To reference.Count - 1
                        Assert.AreEqual(reference(i), actual(i),
                            $"range[{i}] for '{txt}': reference {reference(i)} != actual {actual(i)}")
                    Next
                Next
            Next
        End Sub

        ''' <summary>
        ''' Independent sequential reference for <see cref="PreTokenizedString.ComputeFusedRanges"/>:
        ''' starts from the whole-text range, then for each pattern splits every current range by
        ''' <see cref="Pattern.FindMatches"/> on a materialized substring (fresh match list, reference
        ''' <see cref="Utf8Helpers.ByteToNetIndex"/> byte walk). No per-thread buffers, no slice-scan,
        ''' no sparse index — a deliberately different implementation of the same fused-ranges
        ''' semantics.
        ''' </summary>
        Private Shared Function IndependentFusedRanges(text As String, patterns As List(Of Pattern)) As List(Of (Integer, Integer))
            Dim byteLen As Integer = Utf8Helpers.Utf8Length(text)
            Dim ranges As New List(Of (Integer, Integer))(1) From {(0, byteLen)}
            For Each p As Pattern In patterns
                Dim nextRanges As New List(Of (Integer, Integer))()
                For Each r In ranges
                    Dim b1 As Integer = r.Item1
                    Dim b2 As Integer = r.Item2
                    If b2 <= b1 Then Continue For
                    Dim n1 As Integer = Utf8Helpers.ByteToNetIndex(text, b1)
                    Dim n2 As Integer = Utf8Helpers.ByteToNetIndex(text, b2)
                    If n2 <= n1 Then Continue For
                    Dim slice As String = text.Substring(n1, n2 - n1)
                    Dim matches As List(Of MatchInfo) = p.FindMatches(slice)
                    For Each m As MatchInfo In matches
                        If m.End > m.Start Then
                            nextRanges.Add((b1 + m.Start, b1 + m.End))
                        End If
                    Next
                Next
                ranges = nextRanges
            Next
            Return ranges
        End Function

        ''' <summary>Deterministic battery for the M2 differential: golden counter-examples + seeded fuzz.</summary>
        Private Shared Function M2TextBattery() As String()
            Dim texts As New List(Of String)() From {
                "",
                "a",
                "Hello my friend, how's it going? I'm fine.",
                "Hello  世界 123 a３b",
                "abc123 ４５６x 3人目の彼",
                "a３b",
                "return await Task.WhenAll(tasks.Select(Function(t) t.RunAsync()))",
                "   leading/trailing  ",
                "€£¥~ 你好 😀 x",
                "a" & vbLf & "b" & vbCrLf & "c"
            }
            Dim rng As New Random(20260901)
            Dim pool As String = "abcXYZ019 )(*+-=<>/{}[];:,.!? 你好世界３４５"
            For iter As Integer = 0 To 200
                Dim len As Integer = rng.Next(0, 60)
                Dim chars As New List(Of Char)(len)
                For i As Integer = 0 To len - 1
                    chars.Add(pool(rng.Next(pool.Length)))
                Next
                texts.Add(New String(chars.ToArray()))
            Next
            Return texts.ToArray()
        End Function

    End Class

End Namespace
