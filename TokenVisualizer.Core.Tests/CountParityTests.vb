Imports System.Collections.Generic
Imports System.Linq
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' R7 correctness gate for the count-only fast path behind <see cref="Tokenizer.EncodeCount"/>
    ''' (<see cref="IModel.CountTokens"/> + <see cref="PreTokenizedString.TokenizeCount"/>).
    '''
    ''' The invariant under test is count-parity: the count-only fast path must return the exact
    ''' same token count as the full encode path for every configuration. The differential tests
    ''' here assert <c>EncodeCount == Encode(...).Length</c> across golden vectors, the real DeepSeek
    ''' tokenizer, cross-model fallbacks (WordPiece / WordLevel / Unigram), truncation / padding
    ''' fallback, and a deterministic fuzz corpus. A dedicated allocation assertion proves the
    ''' DeepSeek path actually takes the fast path (it allocates strictly less than EncodeFast);
    ''' a silent fallback would make the two allocations equal and fail the test.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class CountParityTests

        Private Const DeepSeekPath As String =
            "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"

        ' ------------------------------------------------------------------
        ' BPE model-level CountTokens == Tokenize(...).Count unit tests.
        ' ------------------------------------------------------------------

        Private Shared Function BuildBpe(Optional ignoreMerges As Boolean = False,
                                          Optional byteFallback As Boolean = False,
                                          Optional fuseUnk As Boolean = False,
                                          Optional maxWordLength As Integer? = Nothing,
                                          Optional cacheCapacity As Integer = 10000) As BpeModel
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
                maxWordLength:=maxWordLength)
        End Function

        Private Shared Function BpeWords() As String()
            Return {
                "", "a", "b", "the", "quick", "brown fox", "abc123", "un", "re", "er",
                "hello world", "tokenization", "xyzzy", "zz", "a" & ChrW(&H3042), "３",
                "the quick brown fox jumps over the lazy dog", String.Join("", Enumerable.Repeat("ab", 40)),
                String.Concat(Enumerable.Repeat("x", 300))
            }
        End Function

        <TestMethod>
        Public Sub Bpe_CountTokens_Equals_TokenizeCount()
            Dim bpe As BpeModel = BuildBpe()
            For Each w As String In BpeWords()
                Assert.AreEqual(bpe.Tokenize(w).Count, bpe.CountTokens(w), $"CountTokens vs Tokenize.Count for '{w}'")
            Next
        End Sub

        <TestMethod>
        Public Sub Bpe_CountTokens_CacheHit_ReturnsSame_AndHits()
            Dim bpe As BpeModel = BuildBpe()
            bpe.EnableCacheStats()
            bpe.ResetCacheStats()
            Dim w As String = "the quick brown fox"
            Dim first As Integer = bpe.CountTokens(w)
            Dim second As Integer = bpe.CountTokens(w)
            Assert.AreEqual(first, second)
            Assert.AreEqual(bpe.Tokenize(w).Count, second)
            Dim stats As CacheStats = bpe.GetCacheStats()
            Assert.IsGreaterThanOrEqualTo(1, stats.Hits, "second CountTokens call should be a cache hit")
        End Sub

        <TestMethod>
        Public Sub Bpe_CountTokens_MaxWordLengthBypass_StillParity()
            Dim bpe As BpeModel = BuildBpe(maxWordLength:=16)
            For Each w As String In BpeWords()
                Assert.AreEqual(bpe.Tokenize(w).Count, bpe.CountTokens(w), $"maxWordLength parity for '{w}'")
            Next
        End Sub

        <TestMethod>
        Public Sub Bpe_CountTokens_IgnoreMerges_WholeWordInVocabIsOne()
            Dim bpe As BpeModel = BuildBpe(ignoreMerges:=True)
            For Each w As String In BpeWords()
                Assert.AreEqual(bpe.Tokenize(w).Count, bpe.CountTokens(w), $"ignoreMerges parity for '{w}'")
            Next
            ' Whole word present -> exactly one token.
            Assert.AreEqual(1, bpe.CountTokens("the"))
            ' Unknown word -> merged normally, parity holds.
            Assert.AreEqual(bpe.Tokenize("xyzzy").Count, bpe.CountTokens("xyzzy"))
        End Sub

        <TestMethod>
        Public Sub Bpe_CountTokens_ByteFallback_FuseUnk_StillParity()
            ' byteFallback with an unknown scalar (CJK) that has no byte tokens in vocab: bytes are
            ' not present, so the scalar is silently omitted (no unk); count parity still holds.
            Dim bpeBf As BpeModel = BuildBpe(byteFallback:=True)
            For Each w As String In BpeWords()
                Assert.AreEqual(bpeBf.Tokenize(w).Count, bpeBf.CountTokens(w), $"byteFallback parity for '{w}'")
            Next

            ' fuseUnk: consecutive unknowns fuse into one unk token.
            Dim bpeFuse As BpeModel = BuildBpe(fuseUnk:=True)
            For Each w As String In BpeWords()
                Assert.AreEqual(bpeFuse.Tokenize(w).Count, bpeFuse.CountTokens(w), $"fuseUnk parity for '{w}'")
            Next
        End Sub

        <TestMethod>
        Public Sub Bpe_CountTokens_NoCache_StillParity()
            Dim bpe As BpeModel = BuildBpe(cacheCapacity:=0)
            For Each w As String In BpeWords()
                Assert.AreEqual(bpe.Tokenize(w).Count, bpe.CountTokens(w), $"no-cache parity for '{w}'")
            Next
        End Sub

        ' ------------------------------------------------------------------
        #Region "Pipeline differential: golden vectors + real DeepSeek + fuzz"
        ' ------------------------------------------------------------------

        ''' <summary>Asserts EncodeCount equals the full Encode path length for both addSpecialTokens modes.</summary>
        Private Shared Sub AssertCountParity(tokenizer As Tokenizer, text As String, label As String)
            Dim enc As Encoding = tokenizer.Encode(text, False)
            Assert.AreEqual(enc.Ids.Count, tokenizer.EncodeCount(text, False), $"{label}: count (addSpecialTokens=False) for '{text}'")

            Dim encFull As Encoding = tokenizer.Encode(text, True)
            Assert.AreEqual(encFull.Ids.Count, tokenizer.EncodeCount(text, True), $"{label}: count (addSpecialTokens=True) for '{text}'")
        End Sub

        <TestMethod>
        Public Sub GoldenInlinePipelines_EncodeCount_MatchesEncode()
            Dim inlinePipelines = GoldenVectors.Pipelines.Where(Function(p) p.ConfigJson.Length > 0).ToList()
            Assert.IsGreaterThanOrEqualTo(7, inlinePipelines.Count)
            For Each pipeline In inlinePipelines
                Dim tokenizer As Tokenizer = Tokenizer.FromJson(pipeline.ConfigJson)
                For Each v As TextVector In pipeline.Vectors
                    AssertCountParity(tokenizer, v.Text, pipeline.Name)
                Next
            Next
        End Sub

        <TestMethod>
        Public Sub DeepSeekRealFile_EncodeCount_MatchesEncode()
            If Not IO.File.Exists(DeepSeekPath) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(DeepSeekPath)

            ' Golden deepseek vectors.
            Dim pipeline = GoldenVectors.Pipelines.First(Function(p) p.Name = "deepseek")
            For Each v As TextVector In pipeline.Vectors
                AssertCountParity(tokenizer, v.Text, "deepseek")
            Next

            ' Real-code-like texts (high piece density: operators, punctuation, identifiers).
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
                AssertCountParity(tokenizer, t, "deepseek real-code")
            Next
        End Sub

        <TestMethod>
        Public Sub Fuzz_EncodeCount_MatchesEncode()
            ' Deterministic seeded fuzz: mixed ASCII / CJK / punctuation / digits, no dropout.
            Dim inlinePipelines = GoldenVectors.Pipelines.Where(Function(p) p.ConfigJson.Length > 0).ToList()
            Dim tokenizers As New List(Of (String, Tokenizer))()
            For Each p In inlinePipelines
                tokenizers.Add((p.Name, Tokenizer.FromJson(p.ConfigJson)))
            Next

            Dim rng As New Random(12345)
            Dim alphabet As String =
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" &
                " .,!?;:'" & """" & "-+=()[]{}@#$%^&*<>/\|`~" &
                "你好世界中文编程测试日本語かなカナ한국어"
            For iter As Integer = 0 To 200
                Dim len As Integer = rng.Next(1, 120)
                Dim chars As New List(Of Char)(len)
                For i As Integer = 0 To len - 1
                    chars.Add(alphabet(rng.Next(alphabet.Length)))
                Next
                Dim text As String = New String(chars.ToArray())
                For Each t As (String, Tokenizer) In tokenizers
                    AssertCountParity(t.Item2, text, $"fuzz {t.Item1} iter={iter}")
                Next
            Next
        End Sub

        ''' <summary>
        ''' Cross-model fallback: WordPiece / WordLevel / Unigram CountTokens go through
        ''' Tokenize(...).Count; the pipeline count path must still equal the full Encode length.
        ''' </summary>
        <TestMethod>
        Public Sub NonBpeModels_EncodeCount_MatchesEncode()
            Dim wordVocab As New Dictionary(Of String, Integer)()
            Dim wid As Integer = 0
            For Each w As String In {"hello", "world", "a", "b", "c", "<unk>", "[UNK]"}
                wordVocab(w) = wid
                wid += 1
            Next

            Dim wpVocab As New Dictionary(Of String, Integer)(wordVocab)
            wpVocab("[CLS]") = wid + 10
            wpVocab("[SEP]") = wid + 11
            Dim wp As New WordPieceModel(
                wpVocab,
                unkToken:="[UNK]", continuingSubwordPrefix:="##", maxInputCharsPerWord:=100)
            Dim wpTok As New Tokenizer(wp)
            wpTok.WithPreTokenizer(New WhitespaceSplitPreTokenizer())
            For Each t As String In {"hello world", "a b", "unknownword", "hello 123"}
                AssertCountParity(wpTok, t, "wordpiece")
            Next

            Dim wl As New WordLevelModel(wordVocab, unkToken:="<unk>")
            Dim wlTok As New Tokenizer(wl)
            wlTok.WithPreTokenizer(New WhitespaceSplitPreTokenizer())
            For Each t As String In {"hello world", "a b", "unknownword hello"}
                AssertCountParity(wlTok, t, "wordlevel")
            Next

            Dim uniVocab As New List(Of (String, Double))() From {
                ("<unk>", -10.0), ("hello", -1.0), ("world", -2.0), ("a", -3.0), ("b", -4.0), ("c", -5.0)
            }
            Dim uni As New UnigramModel(uniVocab, unkId:=0, byteFallback:=False)
            Dim uniTok As New Tokenizer(uni)
            uniTok.WithPreTokenizer(New WhitespaceSplitPreTokenizer())
            For Each t As String In {"hello world", "a b", "unknownword"}
                AssertCountParity(uniTok, t, "unigram")
            Next
        End Sub

        ''' <summary>
        ''' Truncation and padding configured: the count fast path must fall back to the full path
        ''' and still equal Encode(...).Length (EncodeCount's pre-R7 behaviour).
        ''' </summary>
        <TestMethod>
        Public Sub TruncationAndPadding_EncodeCount_MatchesEncode()
            Dim bpe As BpeModel = BuildBpe()
            Dim tok As New Tokenizer(bpe)
            tok.WithPreTokenizer(New WhitespaceSplitPreTokenizer())

            tok.SetTruncation(maxLength:=4, stride:=0, strategy:=TruncationStrategy.LongestFirst, direction:=TruncationDirection.Right)
            For Each t As String In {"a b c d e f", "hello world", "x y"}
                AssertCountParity(tok, t, "truncation")
            Next

            Dim noTrunc As New Tokenizer(BuildBpe())
            noTrunc.WithPreTokenizer(New WhitespaceSplitPreTokenizer())
            Dim pad As New PaddingParams()
            pad.Strategy = PaddingStrategy.Fixed(8)
            pad.Direction = PaddingDirection.Right
            pad.PadToMultipleOf = Nothing
            pad.PadId = 0
            pad.PadTypeId = 0
            pad.PadToken = "[PAD]"
            noTrunc.SetPadding(pad)
            For Each t As String In {"a b c d e f", "hello world", "x y"}
                AssertCountParity(noTrunc, t, "padding")
            Next
        End Sub

        ''' <summary>
        ''' Proves the DeepSeek path actually takes the count-only fast path: it must allocate
        ''' strictly less than EncodeFast (which still builds List(Of Token) + the OffsetType.None
        ''' tuple list). A silent fallback to EncodeFast would make the allocations equal.
        ''' </summary>
        <TestMethod>
        Public Sub DeepSeekRealFile_EncodeCount_FastPath_AllocatesLessThanEncodeFast()
            If Not IO.File.Exists(DeepSeekPath) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(DeepSeekPath)

            ' Warm the BPE word cache + ThreadLocal transform buffers so the measured calls pay
            ' only for the encode itself.
            Dim text As String =
                "public NotInheritable Class Foo(Of T) Implements IModel" & vbLf &
                "Dim x As Integer = a + b * (c - d) / e % f" & vbLf &
                "return await Task.WhenAll(tasks.Select(Function(t) t.RunAsync()))" & vbLf &
                "你好世界 中文编程 字符串 12345 （中文括号）" & vbLf
            For i As Integer = 0 To 8
                tokenizer.EncodeCount(text, False)
            Next

            Dim before As Long = GC.GetAllocatedBytesForCurrentThread()
            tokenizer.EncodeCount(text, False)
            Dim countAlloc As Long = GC.GetAllocatedBytesForCurrentThread() - before

            before = GC.GetAllocatedBytesForCurrentThread()
            tokenizer.EncodeFast(text, False)
            Dim fastAlloc As Long = GC.GetAllocatedBytesForCurrentThread() - before

            Assert.IsLessThan(
                upperBound:=fastAlloc,
                value:=countAlloc,
                message:=$"DeepSeek EncodeCount allocated {countAlloc} B vs EncodeFast {fastAlloc} B; " &
                          "expected the count-only fast path to allocate less (fast path must have been taken).")
        End Sub

        #End Region

    End Class

End Namespace
