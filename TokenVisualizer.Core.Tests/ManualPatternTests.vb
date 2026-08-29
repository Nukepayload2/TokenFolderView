Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' The key guard: every hand-written manual pattern must produce byte-identical match spans
    ''' to the equivalent .NET Regex across a corpus battery.
    ''' </summary>
    <TestClass>
    Public Class ManualPatternTests

        Private Shared ReadOnly SharedCorpus As String() = {
            "Hello my friend, how's it going? I'm fine.",
            "  a   b  ",
            "Hello  世界 123",
            "abc 12345 6 x7",
            "!!!...???",
            "€£¥~",
            "a" & vbLf & "b" & vbCrLf & "c",
            "  " & vbLf & "  ",
            "你好世界",
            "かなカナ",
            "mixed スクリプト 123!",
            "it's can't rock'n'roll 're",
            "'s",
            "''",
            " " & vbTab & " ",
            "",
            " ",
            vbLf,
            "12345",
            "a1b2c3",
            "!a?b",
            vbCrLf,
            "  x  "
        }

        Private Shared Function AllCases() As (String, String, String())()
            Dim pairs As New List(Of (String, String, String()))()
            pairs.Add(("gpt2", Gpt2ByteLevelPattern.Canonical, SharedCorpus))
            pairs.Add(("deepseekN", DeepSeekNumbersPattern.Canonical, SharedCorpus))
            pairs.Add(("deepseekCjk", DeepSeekCjkPattern.Canonical, SharedCorpus))
            pairs.Add(("deepseekGpt2", DeepSeekGpt2Pattern.Canonical, SharedCorpus))
            pairs.Add(("wordPunct", WordPunctPattern.Canonical, SharedCorpus))
            Return pairs.ToArray()
        End Function

        <TestMethod>
        Public Sub EveryManualPatternMatchesNetRegexAcrossCorpus()
            For Each c In AllCases()
                Dim manual As Pattern = ManualPatternFactory.TryCreate(c.Item2)
                Assert.IsNotNull(manual, $"ManualPatternFactory returned Nothing for '{c.Item1}'")
                Dim regex As Pattern = New RegexPattern(c.Item2)
                For Each s In c.Item3
                    AssertMatchesEqual(regex.FindMatches(s), manual.FindMatches(s), $"{c.Item1} on '{s}'")
                Next
            Next
        End Sub

        <TestMethod>
        Public Sub FactoryRecognizesAllCanonicalStrings()
            Assert.IsInstanceOfType(ManualPatternFactory.TryCreate(Gpt2ByteLevelPattern.Canonical), GetType(Gpt2ByteLevelPattern))
            Assert.IsInstanceOfType(ManualPatternFactory.TryCreate(DeepSeekNumbersPattern.Canonical), GetType(DeepSeekNumbersPattern))
            Assert.IsInstanceOfType(ManualPatternFactory.TryCreate(DeepSeekCjkPattern.Canonical), GetType(DeepSeekCjkPattern))
            Assert.IsInstanceOfType(ManualPatternFactory.TryCreate(DeepSeekGpt2Pattern.Canonical), GetType(DeepSeekGpt2Pattern))
            Assert.IsInstanceOfType(ManualPatternFactory.TryCreate(WordPunctPattern.Canonical), GetType(WordPunctPattern))
        End Sub

        <TestMethod>
        Public Sub FactoryReturnsNothingForUnknownPattern()
            Assert.IsNull(ManualPatternFactory.TryCreate("\s+"))
            Assert.IsNull(ManualPatternFactory.TryCreate(""))
            Assert.IsNull(ManualPatternFactory.TryCreate(Nothing))
            ' A near miss (case differs) must not match.
            Assert.IsNull(ManualPatternFactory.TryCreate("WordPunctPattern"))
        End Sub

        <TestMethod>
        Public Sub PatternCreateRoutesCanonicalRegexToManualPattern()
            Assert.IsInstanceOfType(Pattern.Create("Regex", Gpt2ByteLevelPattern.Canonical), GetType(Gpt2ByteLevelPattern))
            Assert.IsInstanceOfType(Pattern.Create("Regex", WordPunctPattern.Canonical), GetType(WordPunctPattern))
            Assert.IsInstanceOfType(Pattern.Create("Regex", "\s+"), GetType(RegexPattern))
            Assert.IsInstanceOfType(Pattern.Create("String", "abc"), GetType(StringPattern))
        End Sub

        Private Shared Sub AssertMatchesEqual(expected As List(Of MatchInfo), actual As List(Of MatchInfo), context As String)
            Assert.HasCount(expected.Count, actual, $"{context}: match count")
            For i As Integer = 0 To expected.Count - 1
                Assert.AreEqual(expected(i).Start, actual(i).Start, $"{context}: [{i}].Start")
                Assert.AreEqual(expected(i).End, actual(i).End, $"{context}: [{i}].End")
                Assert.AreEqual(expected(i).IsMatch, actual(i).IsMatch, $"{context}: [{i}].IsMatch")
            Next
        End Sub

    End Class

End Namespace
