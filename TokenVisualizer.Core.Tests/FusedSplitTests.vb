Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Guards the R5 fused Isolated-split fast path (PreTokenizerSequence fuses a leading run of
    ''' Isolated manual-pattern splits). The fused path must produce byte-identical splits to the
    ''' sequential reference for every input, including the cross-boundary counter-example
    ''' "a３b" where a naive whole-string Gpt2 scan would wrongly join the number onto the next
    ''' letter.
    ''' </summary>
    <TestClass>
    Public Class FusedSplitTests

        Private Shared Function SplitsTextOffsets(pts As PreTokenizedString) As List(Of (String, (Integer, Integer)))
            Dim result As New List(Of (String, (Integer, Integer)))()
            For Each s In pts.GetSplits(OffsetReferential.Original, OffsetType.Byte)
                result.Add((s.Text, s.Offsets))
            Next
            Return result
        End Function

        Private Shared Sub AssertSameSplits(actual As List(Of (String, (Integer, Integer))),
                                            expected As List(Of (String, (Integer, Integer))),
                                            context As String)
            Assert.HasCount(expected.Count, actual, $"{context}: split count")
            For i As Integer = 0 To actual.Count - 1
                Assert.AreEqual(expected(i).Item1, actual(i).Item1, $"{context}: [{i}].text")
                Assert.AreEqual(expected(i).Item2, actual(i).Item2, $"{context}: [{i}].offsets")
            Next
        End Sub

        ''' <summary>Applies the pre-tokenizers one at a time (the sequential reference).</summary>
        Private Shared Function SequentialSplits(text As String, pretoks As IPreTokenizer()) As List(Of (String, (Integer, Integer)))
            Dim pts As PreTokenizedString = PreTokenizedString.FromString(text)
            For Each pt In pretoks
                pt.PreTokenize(pts)
            Next
            Return SplitsTextOffsets(pts)
        End Function

        ''' <summary>Runs the same pre-tokenizers through a PreTokenizerSequence (fused when it qualifies).</summary>
        Private Shared Function FusedSplits(text As String, pretoks As IPreTokenizer()) As List(Of (String, (Integer, Integer)))
            Dim seq As New PreTokenizerSequence(pretoks)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString(text)
            seq.PreTokenize(pts)
            Return SplitsTextOffsets(pts)
        End Function

        Private Shared Function DeepSeekSplitPretoks() As IPreTokenizer()
            Return New IPreTokenizer() {
                New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekCjkPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False)
            }
        End Function

        Private Shared Sub AssertFusedEqualsSequential(text As String, pretoks As IPreTokenizer(), context As String,
                                                        Optional verifyOriginalRoundTrip As Boolean = True)
            Dim expected As List(Of (String, (Integer, Integer))) = SequentialSplits(text, pretoks)
            Dim actual As List(Of (String, (Integer, Integer))) = FusedSplits(text, pretoks)
            AssertSameSplits(actual, expected, context)
            ' For identity-normalizer cases (no ByteLevel transform) every piece must slice back to
            ' the exact original text.
            If verifyOriginalRoundTrip Then
                For i As Integer = 0 To expected.Count - 1
                    Dim offs As (Integer, Integer) = expected(i).Item2
                    Dim back As String = Utf8Helpers.SliceByUtf8(text, offs.Item1, offs.Item2)
                    Assert.AreEqual(expected(i).Item1, back, $"{context}: piece [{i}] original-slice round-trip")
                Next
            End If
        End Sub

        ' ------------------------------------------------------------------
        ' Counter-example battery (numbers <-> gpt2 cross-boundary cases)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub CounterExample_NumberBeforeLetter_IsolatedByNumbers()
            ' ３ is a fullwidth digit (Nd). Numbers must isolate it BEFORE Gpt2 runs, so a naive
            ' whole-string Gpt2 scan that would append 'b' to '３' (optional [^\r\n\p{L}\p{P}\p{S}]?
            ' prefix + \p{L}+) must NOT happen. Sequential (and the fused path) yield 3 pieces.
            AssertFusedEqualsSequential("a３b", DeepSeekSplitPretoks(), "a３b")
        End Sub

        <TestMethod>
        Public Sub CounterExample_NumberRun_ThenLetter()
            AssertFusedEqualsSequential("１２３a", DeepSeekSplitPretoks(), "１２３a")
            AssertFusedEqualsSequential("abc 123 ４５６x", DeepSeekSplitPretoks(), "abc 123 ４５６x")
        End Sub

        <TestMethod>
        Public Sub CounterExample_SpaceBeforeLetter()
            AssertFusedEqualsSequential("a b", DeepSeekSplitPretoks(), "a b")
        End Sub

        <TestMethod>
        Public Sub CounterExample_WhitespaceAndNewlines()
            AssertFusedEqualsSequential("a" & vbLf & "b" & vbCrLf & "c", DeepSeekSplitPretoks(), "newlines")
            AssertFusedEqualsSequential("a  b", DeepSeekSplitPretoks(), "double space")
        End Sub

        <TestMethod>
        Public Sub CounterExample_PunctuationNumberCjkMix()
            AssertFusedEqualsSequential("！？a３b。", DeepSeekSplitPretoks(), "punct-num-cjk")
            AssertFusedEqualsSequential("你好 世界 123", DeepSeekSplitPretoks(), "cjk digits")
            AssertFusedEqualsSequential("3人目の彼", DeepSeekSplitPretoks(), "digit-cjk-mix")
        End Sub

        <TestMethod>
        Public Sub CounterExample_Edges()
            AssertFusedEqualsSequential("", DeepSeekSplitPretoks(), "empty")
            AssertFusedEqualsSequential(" ", DeepSeekSplitPretoks(), "single space")
            AssertFusedEqualsSequential("a", DeepSeekSplitPretoks(), "single letter")
            AssertFusedEqualsSequential("123", DeepSeekSplitPretoks(), "all digits")
            AssertFusedEqualsSequential("３", DeepSeekSplitPretoks(), "single fullwidth digit")
            AssertFusedEqualsSequential("a" & vbTab & "b", DeepSeekSplitPretoks(), "tab")
        End Sub

        <TestMethod>
        Public Sub CounterExample_Gpt2OwnBoundariesDoNotCrossCjk()
            ' CJK pieces are barriers for Gpt2 too: "世３界" must keep 世 and 界 separated by the
            ' digit, and Gpt2 must not join the digit to a CJK run.
            AssertFusedEqualsSequential("世３界", DeepSeekSplitPretoks(), "cjk-digit-cjk")
        End Sub

        ' ------------------------------------------------------------------
        ' Differential fuzz over random strings (DeepSeek 3-split case)
        ' ------------------------------------------------------------------

        Private Shared ReadOnly Pool As String() = {
            "a", "b", "A", "z", "m", "o",
            "0", "1", "7",
            "３", "４", "９",
            " ", "  ", vbTab, vbLf, vbCrLf,
            "!", "?", ",", ".", "。", "、", "＠", "#",
            "你", "好", "世", "界",
            "か", "な", "カ", "ナ",
            "'", "s", "t", "re",
            "€", "×", "ー", "〜",
            "😀"
        }

        <TestMethod>
        Public Sub Fuzz_DeepSeekSplit_FusedMatchesSequential()
            Dim rnd As New Random(1234567)
            For iter As Integer = 0 To 3000
                Dim len As Integer = rnd.Next(0, 40)
                Dim sb As New System.Text.StringBuilder()
                For i As Integer = 0 To len - 1
                    sb.Append(Pool(rnd.Next(0, Pool.Length)))
                Next
                Dim text As String = sb.ToString()
                AssertFusedEqualsSequential(text, DeepSeekSplitPretoks(), $"fuzz#{iter} '{text}'")
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' Full DeepSeek pre-tokenizer (3 splits + ByteLevel transform)
        ' ------------------------------------------------------------------

        Private Shared Function FullDeepSeekPretoks() As IPreTokenizer()
            Return New IPreTokenizer() {
                New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekCjkPattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False),
                New ByteLevelPreTokenizer(False, True, False)
            }
        End Function

        <TestMethod>
        Public Sub FullDeepSeekSequence_FusedMatchesSequential()
            Dim cases As String() = {
                "Hello my friend, how's it going? I'm fine.",
                "Hello  世界 123",
                "a３b",
                "abc123 ４５６x",
                "3人目の彼",
                "a" & vbLf & "b" & vbCrLf & "c",
                "１２３a",
                "",
                "€£¥~"
            }
            For Each c In cases
                ' The ByteLevel transform changes the piece text, so no original round-trip check.
                AssertFusedEqualsSequential(c, FullDeepSeekPretoks(), $"full '{c}'", verifyOriginalRoundTrip:=False)
            Next
        End Sub

        <TestMethod>
        Public Sub FullDeepSeekSequence_Fuzz_FusedMatchesSequential()
            Dim rnd As New Random(98765)
            For iter As Integer = 0 To 800
                Dim len As Integer = rnd.Next(0, 25)
                Dim sb As New System.Text.StringBuilder()
                For i As Integer = 0 To len - 1
                    sb.Append(Pool(rnd.Next(0, Pool.Length)))
                Next
                Dim text As String = sb.ToString()
                AssertFusedEqualsSequential(text, FullDeepSeekPretoks(), $"full-fuzz#{iter} '{text}'", verifyOriginalRoundTrip:=False)
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' The fused path must NOT trigger for non-qualifying sequences.
        ' A single Split (no fusion), a RegexPattern split, or a non-Isolated behavior
        ' all still produce correct results through the sequential loop.
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub NonQualifying_RegexPatternSplit_MatchesSequential()
            Dim pretoks As IPreTokenizer() = {
                New SplitPreTokenizer("Regex", "\s+", SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False)
            }
            AssertFusedEqualsSequential("a b 3c", pretoks, "regex+manual")
            AssertFusedEqualsSequential("你好 世界 123", pretoks, "regex+manual cjk")
        End Sub

        <TestMethod>
        Public Sub NonQualifying_SingleSplit_MatchesSequential()
            Dim pretoks As IPreTokenizer() = {
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False)
            }
            AssertFusedEqualsSequential("a３b 123", pretoks, "single split")
        End Sub

        <TestMethod>
        Public Sub NonQualifying_RemovedBehavior_MatchesSequential()
            Dim pretoks As IPreTokenizer() = {
                New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Removed, False),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False)
            }
            AssertFusedEqualsSequential("a123b ４５６c", pretoks, "removed+isolated")
        End Sub

        <TestMethod>
        Public Sub NonQualifying_InvertedSplit_MatchesSequential()
            Dim pretoks As IPreTokenizer() = {
                New SplitPreTokenizer("Regex", DeepSeekNumbersPattern.Canonical, SplitDelimiterBehavior.Isolated, True),
                New SplitPreTokenizer("Regex", DeepSeekGpt2Pattern.Canonical, SplitDelimiterBehavior.Isolated, False)
            }
            AssertFusedEqualsSequential("a123b ４５６c", pretoks, "inverted+isolated")
        End Sub

        ' ------------------------------------------------------------------
        ' P1 guard: EncodeCount uses the offset-free (OffsetType.None) path.
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub EncodeCount_MatchesEncodeFastLength()
            ' Reads the real tokenizer.json (matching the other integration tests' convention).
            Dim path As String = "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"
            If Not IO.File.Exists(path) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
            End If
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(path)
            Dim texts As String() = {
                "Hello  世界 123 a３b",
                "a３b",
                "3人目の彼",
                "The quick brown fox jumps over the lazy dog 1234567890!",
                "",
                "a" & vbLf & "b" & vbCrLf & "c"
            }
            For Each text As String In texts
                Dim byteEnc As Encoding = tokenizer.Encode(text, False)
                Dim noneEnc As Encoding = tokenizer.EncodeFast(text, False)
                ' Count is unaffected by the offset type (Ids are identical).
                Assert.HasCount(byteEnc.Ids.Count, noneEnc.Ids)
                CollectionAssert.AreEqual(byteEnc.Ids, noneEnc.Ids)
                Assert.AreEqual(byteEnc.Ids.Count, tokenizer.EncodeCount(text, False))
                Assert.AreEqual(noneEnc.Ids.Count, tokenizer.EncodeCount(text, False))
            Next
        End Sub

    End Class

End Namespace
