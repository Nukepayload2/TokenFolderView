Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models

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

    End Class

End Namespace
