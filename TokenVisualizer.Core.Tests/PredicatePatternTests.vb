Imports System.Text
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for <see cref="PredicatePattern"/>, the scalar-aware predicate-backed pattern that
    ''' mirrors the Rust <c>impl&lt;F: Fn(char) -&gt; bool&gt; Pattern for F</c>.
    ''' </summary>
    <TestClass>
    Public Class PredicatePatternTests

        Private Shared Function MatchSpans(pattern As Pattern, inside As String) As List(Of (Integer, Integer, Boolean))
            Dim result As New List(Of (Integer, Integer, Boolean))()
            For Each m In pattern.FindMatches(inside)
                result.Add((m.Start, m.End, m.IsMatch))
            Next
            Return result
        End Function

        <TestMethod>
        Public Sub WhitespaceRuns_AreIndividualMatches()
            ' Mirrors the Rust closure pattern: consecutive matching scalars are each their own
            ' match span (this is what makes Digits(individual) and Bert punctuation work).
            Dim pattern As New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r))
            Dim actual = MatchSpans(pattern, "a   b")
            CollectionAssert.AreEqual(
                New (Integer, Integer, Boolean)() {
                    (0, 1, False),
                    (1, 2, True),
                    (2, 3, True),
                    (3, 4, True),
                    (4, 5, False)
                },
                actual)
        End Sub

        <TestMethod>
        Public Sub NoMatches_CoversWholeStringAsGap()
            Dim pattern As New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r))
            Dim actual = MatchSpans(pattern, "abc")
            CollectionAssert.AreEqual(New (Integer, Integer, Boolean)() {(0, 3, False)}, actual)
        End Sub

        <TestMethod>
        Public Sub AllMatches_EveryScalarIsAMatch()
            Dim pattern As New PredicatePattern(Function(r As Rune) Rune.IsNumber(r))
            Dim actual = MatchSpans(pattern, "123")
            CollectionAssert.AreEqual(
                New (Integer, Integer, Boolean)() {
                    (0, 1, True),
                    (1, 2, True),
                    (2, 3, True)
                },
                actual)
        End Sub

        <TestMethod>
        Public Sub EmptyString()
            Dim pattern As New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r))
            Dim actual = MatchSpans(pattern, "")
            CollectionAssert.AreEqual(New (Integer, Integer, Boolean)() {(0, 0, False)}, actual)
        End Sub

        <TestMethod>
        Public Sub ScalarAware_SurrogatePairIsOneScalar()
            ' A supplementary letter (surrogate pair) must be treated as a single scalar.
            Dim suppLetter As String = Char.ConvertFromUtf32(&H20000) ' U+20000, OtherLetter
            Dim letterPattern As New PredicatePattern(Function(r As Rune) Rune.IsLetter(r))
            Dim actual = MatchSpans(letterPattern, suppLetter)
            CollectionAssert.AreEqual(New (Integer, Integer, Boolean)() {(0, 4, True)}, actual)

            ' The same scalar is not whitespace, so it is one non-match span.
            Dim wsPattern As New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r))
            Dim actual2 = MatchSpans(wsPattern, suppLetter)
            CollectionAssert.AreEqual(New (Integer, Integer, Boolean)() {(0, 4, False)}, actual2)
        End Sub

        <TestMethod>
        Public Sub ScalarAware_MixedSurrogateAndAscii()
            ' "a" + U+1F44B (symbol) + "b" => gaps only, but byte offsets respect the 4-byte scalar.
            Dim emoji As String = Char.ConvertFromUtf32(&H1F44B)
            Dim text As String = "a" & emoji & "b"
            Dim wsPattern As New PredicatePattern(Function(r As Rune) Rune.IsWhiteSpace(r))
            Dim actual = MatchSpans(wsPattern, text)
            CollectionAssert.AreEqual(New (Integer, Integer, Boolean)() {(0, 6, False)}, actual)

            ' With an IsLetter predicate, the symbol is a gap between the two letters.
            Dim letterPattern As New PredicatePattern(Function(r As Rune) Rune.IsLetter(r))
            Dim actual2 = MatchSpans(letterPattern, text)
            CollectionAssert.AreEqual(
                New (Integer, Integer, Boolean)() {
                    (0, 1, True),
                    (1, 5, False),
                    (5, 6, True)
                },
                actual2)
        End Sub

        <TestMethod>
        Public Sub PunctuationMatches()
            Dim pattern As New PredicatePattern(Function(r As Rune) Rune.IsPunctuation(r))
            Dim actual = MatchSpans(pattern, "Hey, man!")
            CollectionAssert.AreEqual(
                New (Integer, Integer, Boolean)() {
                    (0, 3, False),
                    (3, 4, True),
                    (4, 8, False),
                    (8, 9, True)
                },
                actual)
        End Sub

    End Class

End Namespace
