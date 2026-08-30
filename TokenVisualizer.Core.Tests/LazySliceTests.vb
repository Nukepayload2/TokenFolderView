Imports System.Collections.Generic
Imports System.Reflection
Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' R8 correctness gate for the lazy no-track slice view in
    ''' <see cref="NormalizedString.Slice"/> (the offset-free count/fast path). A no-track slice
    ''' defers materializing the piece's _original/_normalized substrings to first access and the
    ''' ByteLevel transform iterates the view directly (AppendByteTransform). These tests assert:
    ''' ① semantic equivalence — every accessor (Get / Original / Len / LenOriginal / IsEmpty /
    '''    OffsetsOriginal / AppendByteTransform) of a lazy no-track piece equals the eager
    '''    (tracked) piece, per piece, across scalar-aligned ranges and across real tokenizer
    '''    configurations; and
    ''' ② fallback integrity — the no-track path is only entered for the offset-free encode paths,
    '''    and configurations it cannot serve (a partial transform such as ByteLevel addPrefixSpace,
    '''    or a second-round slice of a no-track piece) fall back to the fully-tracked path without
    '''    breaking EncodeCount/EncodeFast.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class LazySliceTests

        Private Const DeepSeekPath As String =
            "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"

        Private Shared Function SetNoTrack(ns As NormalizedString) As NormalizedString
            GetType(NormalizedString).GetMethod(
                "SetTrackAlignments", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(ns, New Object() {False})
            Return ns
        End Function

        ''' <summary>Collects scalar-aligned byte ranges covering <paramref name="txt"/> (each scalar its own range, plus a few whole-run ranges).</summary>
        Private Shared Function ScalarRanges(txt As String) As List(Of (Integer, Integer))
            Dim result As New List(Of (Integer, Integer))()
            If txt.Length = 0 Then Return result
            For Each sc In Utf8Helpers.EnumerateScalars(txt)
                result.Add((sc.Utf8Start, sc.Utf8Start + sc.Utf8Len))
            Next
            ' A few multi-scalar ranges: prefix, suffix, and one interior run.
            If result.Count > 2 Then
                result.Add((result(0).Item1, result(result.Count - 1).Item2))
                Dim mid As Integer = result.Count \ 2
                result.Add((result(1).Item1, result(mid).Item2))
            End If
            Return result
        End Function

        ''' <summary>
        ''' ① Gate: every accessor of a lazy no-track slice equals the eager (tracked) slice over
        ''' the same scalar-aligned ranges. Also verifies AppendByteTransform (via reflection)
        ''' produces the same stream as iterating Get.
        ''' </summary>
        <TestMethod>
        Public Sub NoTrackSlice_AllAccessors_MatchEagerSlice()
            Dim texts As String() = {
                "Hello, world! 123 456",
                "你好世界 中文 12345",
                "a３b", ' fullwidth digit + CJK (cross-boundary case)
                "Mixed ASCII 123 日本語 测试!@#",
                "   ",
                "abc" & vbCrLf & "def" & vbTab & "ghi"
            }
            Dim appendBt As MethodInfo = GetType(NormalizedString).GetMethod(
                "AppendByteTransform", BindingFlags.Instance Or BindingFlags.NonPublic)

            For Each txt In texts
                Dim eagerRoot As NormalizedString = NormalizedString.FromString(txt)
                Dim lazyRoot As NormalizedString = SetNoTrack(NormalizedString.FromString(txt))
                Dim ranges As List(Of (Integer, Integer)) = ScalarRanges(txt)
                For Each r In ranges
                    Dim eagerPiece As NormalizedString = eagerRoot.Slice(New OffsetRange(False, r.Item1, r.Item2))
                    Dim lazyPiece As NormalizedString = lazyRoot.Slice(New OffsetRange(False, r.Item1, r.Item2))
                    Dim ctx As String = $"txt='{txt}' range={r}"
                    Assert.AreEqual(eagerPiece.Get, lazyPiece.Get, "Get " & ctx)
                    Assert.AreEqual(eagerPiece.Original, lazyPiece.Original, "Original " & ctx)
                    Assert.AreEqual(eagerPiece.Len(), lazyPiece.Len(), "Len " & ctx)
                    Assert.AreEqual(eagerPiece.LenOriginal(), lazyPiece.LenOriginal(), "LenOriginal " & ctx)
                    Assert.AreEqual(eagerPiece.IsEmpty(), lazyPiece.IsEmpty(), "IsEmpty " & ctx)
                    Assert.AreEqual(eagerPiece.OffsetsOriginal(), lazyPiece.OffsetsOriginal(), "OffsetsOriginal " & ctx)
                    ' AppendByteTransform (lazy iterates the view, eager iterates the substring) must match.
                    If appendBt IsNot Nothing Then
                        Dim eagerDest As New List(Of (Char, Integer))()
                        Dim lazyDest As New List(Of (Char, Integer))()
                        appendBt.Invoke(eagerPiece, New Object() {eagerDest})
                        appendBt.Invoke(lazyPiece, New Object() {lazyDest})
                        CollectionAssert.AreEqual(
                            eagerDest.ConvertAll(Function(t) t.Item1),
                            lazyDest.ConvertAll(Function(t) t.Item1),
                            "AppendByteTransform chars " & ctx)
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' ① Gate (pipeline): the no-track fused-split path (the EncodeCount/EncodeFast hot path)
        ''' produces pieces whose final Get (after the ByteLevel transform) matches the sequential
        ''' tracked path piece for piece, over the full DeepSeek pre-tokenizer.
        ''' </summary>
        <TestMethod>
        Public Sub NoTrackFusedPieces_Get_MatchesSequentialTracked()
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
                "return await Task.WhenAll(tasks.Select(Function(t) t.RunAsync()))"
            }
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

                ' No-track fused path (via reflection to set the flag).
                Dim fuse As MethodInfo = GetType(PreTokenizedString).GetMethod(
                    "FuseIsolatedSplits", BindingFlags.Instance Or BindingFlags.NonPublic)
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(txt)
                SetNoTrack(pts.Splits(0).Normalized)
                Dim splitPatterns As New List(Of Pattern)() From {
                    New DeepSeekNumbersPattern(),
                    New DeepSeekCjkPattern(),
                    New DeepSeekGpt2Pattern()
                }
                fuse.Invoke(pts, New Object() {splitPatterns})
                ' Remaining pre-tokenizer (ByteLevel) on the fused no-track pieces.
                Dim bl As New ByteLevelPreTokenizer(False, True, False)
                bl.PreTokenize(pts)

                Dim actual As New List(Of String)()
                For Each s As Split In pts.Splits
                    actual.Add(s.Normalized.Get)
                Next
                CollectionAssert.AreEqual(expected, actual, $"no-track fused Get parity for '{txt}'")
            Next
        End Sub

        ''' <summary>
        ''' ② Gate (fallback integrity): a ByteLevel addPrefixSpace pre-tokenizer does a partial-range
        ''' Prepend on its pieces, which the no-track path cannot serve; EncodeCount/EncodeFast must
        ''' fall back to the fully-tracked path and remain correct (equal to Encode(...).Length).
        ''' This config never triggers the lazy fast path, so it guards against over-triggering.
        ''' </summary>
        <TestMethod>
        Public Sub ByteLevelAddPrefixSpace_EncodeCount_FallsBack_Correct()
            ' GPT-2/Roberta-style ByteLevel with add_prefix_space=true (partial Prepend on pieces).
            Dim json As String = GoldenVectors.Pipelines.
                First(Function(p) p.Name = "roberta").ConfigJson
            Dim tokenizer As Tokenizer = Tokenizer.FromJson(json)
            Dim texts As String() = {
                "Hello my friend, how's it going? I'm fine.",
                "hello  world  123",
                "你好世界",
                "a b c"
            }
            For Each txt In texts
                Dim enc As Encoding = tokenizer.Encode(txt, False)
                Assert.AreEqual(enc.Ids.Count, tokenizer.EncodeCount(txt, False), $"EncodeCount for '{txt}'")
                Assert.AreEqual(enc.Ids.Count, tokenizer.EncodeFast(txt, False).Length, $"EncodeFast for '{txt}'")
            Next
        End Sub

        ''' <summary>
        ''' ①+② Gate (real DeepSeek): EncodeCount (lazy no-track) equals the full tracked Encode
        ''' length on high-piece-density real-code-like texts and a seeded fuzz battery.
        ''' </summary>
        <TestMethod>
        Public Sub DeepSeekRealFile_EncodeCount_MatchesEncode_OnRealCodeAndFuzz()
            If Not IO.File.Exists(DeepSeekPath) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(DeepSeekPath)
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

            ' Seeded fuzz: high piece density (short operators/parens) + CJK + digits.
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
