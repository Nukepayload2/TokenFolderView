Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class UnicodePredicateTests

        <TestMethod>
        Public Sub IsWord_CoreCases()
            ' Letters, digits, connector punctuation, marks and Join_Control are word chars.
            Assert.IsTrue(UnicodePredicates.IsWord("A"c), "uppercase letter")
            Assert.IsTrue(UnicodePredicates.IsWord("z"c), "lowercase letter")
            Assert.IsTrue(UnicodePredicates.IsWord("5"c), "decimal digit")
            Assert.IsTrue(UnicodePredicates.IsWord("_"c), "connector punctuation")
            Assert.IsTrue(UnicodePredicates.IsWord(ChrW(&H301)), "combining acute (Mn mark)")
            Assert.IsTrue(UnicodePredicates.IsWord(ChrW(&H200D)), "ZWJ (Join_Control)")
            Assert.IsTrue(UnicodePredicates.IsWord(ChrW(&H200C)), "ZWNJ (Join_Control)")

            Assert.IsFalse(UnicodePredicates.IsWord("-"c), "hyphen")
            Assert.IsFalse(UnicodePredicates.IsWord(" "c), "space")
            Assert.IsFalse(UnicodePredicates.IsWord("€"c), "currency symbol")
        End Sub

        <TestMethod>
        Public Sub IsWhiteSpace_CoreCases()
            Assert.IsTrue(UnicodePredicates.IsWhiteSpace(" "c), "space")
            Assert.IsTrue(UnicodePredicates.IsWhiteSpace(vbTab), "tab")
            Assert.IsTrue(UnicodePredicates.IsWhiteSpace(vbLf), "newline")
            Assert.IsTrue(UnicodePredicates.IsWhiteSpace(ChrW(&H3000)), "ideographic space")

            Assert.IsFalse(UnicodePredicates.IsWhiteSpace(ChrW(&H200B)), "zero-width space is not \s")
        End Sub

        <TestMethod>
        Public Sub SurrogatePairsAreScalarAware()
            Dim suppLetter As String = Char.ConvertFromUtf32(&H20000) ' U+20000, OtherLetter
            Assert.IsTrue(UnicodePredicates.IsLetter(suppLetter, 0), "U+20000 is a letter")
            Assert.AreEqual(&H20000, UnicodePredicates.ScalarCodePoint(suppLetter, 0))

            Dim suppMark As String = Char.ConvertFromUtf32(&H1D16D) ' U+1D16D, mark
            Assert.IsTrue(UnicodePredicates.IsMark(suppMark, 0), "U+1D16D is a mark")

            Dim emoji As String = Char.ConvertFromUtf32(&H1F44B) ' U+1F44B, OtherSymbol
            Assert.IsTrue(UnicodePredicates.IsSymbol(emoji, 0), "U+1F44B is a symbol")
            Assert.IsFalse(UnicodePredicates.IsLetter(emoji, 0), "U+1F44B is not a letter")
        End Sub

        <TestMethod>
        Public Sub IsAsciiLetter_OnlyAscii()
            Assert.IsTrue(UnicodePredicates.IsAsciiLetter("A"c))
            Assert.IsTrue(UnicodePredicates.IsAsciiLetter("z"c))
            Assert.IsFalse(UnicodePredicates.IsAsciiLetter("é"c))
            Assert.IsFalse(UnicodePredicates.IsAsciiLetter("世"c))
            Assert.IsFalse(UnicodePredicates.IsAsciiLetter("5"c))
        End Sub

    End Class

End Namespace
