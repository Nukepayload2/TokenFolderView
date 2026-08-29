Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Asserts the Rust <c>regex</c> scalar-value semantics that the manual scanners implement
    ''' (and which differ from the .NET Regex engine on the divergence scalars: surrogate pairs,
    ''' Mc/Me marks, and Join_Control ZWJ/ZWNJ).
    ''' </summary>
    <TestClass>
    Public Class RustSemanticsTests

        Private Shared Function MatchedTokens(text As String, p As Pattern) As List(Of String)
            Dim result As New List(Of String)()
            For Each m In p.FindMatches(text)
                If m.IsMatch Then
                    result.Add(Utf8Helpers.SliceByUtf8(text, m.Start, m.End))
                End If
            Next
            Return result
        End Function

        Private Shared Sub AssertTokens(text As String, p As Pattern, ParamArray expected() As String)
            Dim actual As List(Of String) = MatchedTokens(text, p)
            Assert.HasCount(expected.Length, actual, $"token count for '{text}'")
            For i As Integer = 0 To expected.Length - 1
                Assert.AreEqual(expected(i), actual(i), $"token[{i}] for '{text}'")
            Next
        End Sub

        <TestMethod>
        Public Sub Gpt2_SupplementaryLetterIsOneLetterRun()
            Dim s As String = "a" & Char.ConvertFromUtf32(&H20000) & "b"
            Dim p As Pattern = ManualPatternFactory.TryCreate(Gpt2ByteLevelPattern.Canonical)
            ' U+20000 IS \p{L}, so a + U+20000 + b is a single \p{L}+ token.
            AssertTokens(s, p, s)
        End Sub

        <TestMethod>
        Public Sub Gpt2_SupplementaryNumberIsOneNumberToken()
            Dim digit As String = Char.ConvertFromUtf32(&H1D7CE) ' MATHEMATICAL BOLD DIGIT ZERO
            Dim s As String = "x" & digit & "y"
            Dim p As Pattern = ManualPatternFactory.TryCreate(Gpt2ByteLevelPattern.Canonical)
            AssertTokens(s, p, "x", digit, "y")
        End Sub

        <TestMethod>
        Public Sub DeepSeekGpt2_SupplementarySymbolIsOwnPunctToken()
            Dim emoji As String = Char.ConvertFromUtf32(&H1F44B)
            Dim s As String = "a" & emoji & "b"
            Dim p As Pattern = ManualPatternFactory.TryCreate(DeepSeekGpt2Pattern.Canonical)
            ' U+1F44B IS \p{S}, so alt3 [\p{P}\p{S}]+ gives it its own token (not merged).
            AssertTokens(s, p, "a", emoji, "b")
        End Sub

        <TestMethod>
        Public Sub DeepSeekGpt2_BackslashMatchesAlt1WithLetters()
            Dim p As Pattern = ManualPatternFactory.TryCreate(DeepSeekGpt2Pattern.Canonical)
            ' The ASCII punct set includes the backslash, so \ + "abc" is one [punct][A-Za-z]+ token.
            AssertTokens("\abc", p, "\abc")
        End Sub

        <TestMethod>
        Public Sub WordPunct_MarksAndJoinControlsAreWordChars()
            Dim p As Pattern = ManualPatternFactory.TryCreate(WordPunctPattern.Canonical)
            Dim mc As String = ChrW(&H93E)  ' DEVANAGARI VOWEL SIGN AA (Mc)
            Dim en As String = ChrW(&H488)  ' COMBINING CYRILLIC MILLIONS SIGN (Me)
            Dim zwj As String = ChrW(&H200D)
            Dim zwnj As String = ChrW(&H200C)

            AssertTokens("a" & mc & "b", p, "a" & mc & "b")
            AssertTokens("a" & en & "b", p, "a" & en & "b")
            AssertTokens("a" & zwj & "b", p, "a" & zwj & "b")
            AssertTokens("a" & zwnj & "b", p, "a" & zwnj & "b")
        End Sub

        <TestMethod>
        Public Sub DeepSeekNumbers_SupplementaryNumberMatches()
            Dim digit As String = Char.ConvertFromUtf32(&H1D7CE)
            Dim s As String = "x" & digit & "y"
            Dim p As Pattern = ManualPatternFactory.TryCreate(DeepSeekNumbersPattern.Canonical)
            AssertTokens(s, p, digit)
        End Sub

        <TestMethod>
        Public Sub UnicodePredicates_RustScalarSemantics()
            Assert.IsTrue(UnicodePredicates.IsLetter(Char.ConvertFromUtf32(&H20000), 0), "U+20000 is a letter")
            Assert.IsTrue(UnicodePredicates.IsMark(Char.ConvertFromUtf32(&H1D16D), 0), "U+1D16D is a mark")
            Assert.IsTrue(UnicodePredicates.IsSymbol(Char.ConvertFromUtf32(&H1F44B), 0), "U+1F44B is a symbol")
            Assert.IsTrue(UnicodePredicates.IsWord(Char.ConvertFromUtf32(&H200D), 0), "U+200D (ZWJ) is a word char")
            Assert.IsTrue(UnicodePredicates.IsWord(ChrW(&H93E), 0), "U+093E (Mc) is a word char")
        End Sub

        <TestMethod>
        Public Sub DeepSeekGpt2ConfigStringRoutesToManualScanner()
            ' The exact decoded pre_tokenizer pretokenizers[2].pattern.Regex from
            ' deepseek-v4-flash\tokenizer.json (includes the escaped backslash \\ in the class and
            ' real CR/LF control characters where the JSON used \r and \n escapes).
            Dim config As String =
                "[!""#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~][A-Za-z]+" &
                "|[^" & ControlChars.Cr & ControlChars.Lf & "\p{L}\p{P}\p{S}]?[\p{L}\p{M}]+" &
                "| ?[\p{P}\p{S}]+[" & ControlChars.Cr & ControlChars.Lf & "]*" &
                "|\s*[" & ControlChars.Cr & ControlChars.Lf & "]+|\s+(?!\S)|\s+"

            Assert.AreEqual(DeepSeekGpt2Pattern.Canonical, config,
                "DeepSeekGpt2Pattern.Canonical must equal the real config regex")
            Assert.IsInstanceOfType(ManualPatternFactory.TryCreate(config), GetType(DeepSeekGpt2Pattern))
        End Sub

    End Class

End Namespace
