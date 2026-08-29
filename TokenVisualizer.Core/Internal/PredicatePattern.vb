Imports System.Linq
Imports System.Text

Namespace Internal

    ''' <summary>
    ''' A <see cref="Pattern"/> whose matches are the individual scalars satisfying a predicate.
    ''' This mirrors the Rust <c>impl&lt;F: Fn(char) -&gt; bool&gt; Pattern for F</c> (used by
    ''' <c>char::is_whitespace</c>, <c>char::is_numeric</c>, <c>is_punc</c>, ...): each scalar for
    ''' which the predicate returns <c>True</c> is emitted as its own match span, and the gaps
    ''' between them are emitted as non-match spans, so the output always covers the whole string
    ''' with contiguous ordered slices.
    '''
    ''' The predicate receives the full Unicode scalar value as a <see cref="Rune"/>, making the
    ''' pattern scalar-aware (surrogate pairs are one scalar; lone surrogates are presented as
    ''' <see cref="Rune.ReplacementChar"/>).
    ''' </summary>
    Public NotInheritable Class PredicatePattern
        Inherits Pattern

        Private ReadOnly _predicate As Func(Of Rune, Boolean)

        Public Sub New(predicate As Func(Of Rune, Boolean))
            _predicate = predicate
        End Sub

        Public Overrides Function FindMatches(inside As String) As List(Of MatchInfo)
            If inside Is Nothing Then inside = String.Empty
            If inside.Length = 0 Then
                Return New List(Of MatchInfo) From {New MatchInfo(0, 0, False)}
            End If

            Dim result As New List(Of MatchInfo)()
            Dim lastOffset As Integer = 0
            Dim lastSeen As Integer = 0

            For Each sc In Utf8Helpers.EnumerateScalars(inside)
                Dim b As Integer = sc.Utf8Start
                Dim e As Integer = sc.Utf8Start + sc.Utf8Len
                lastSeen = e
                If _predicate(ScalarToRune(inside, sc.NetStart)) Then
                    If lastOffset < b Then
                        result.Add(New MatchInfo(lastOffset, b, False))
                    End If
                    result.Add(New MatchInfo(b, e, True))
                    lastOffset = e
                End If
            Next
            If lastSeen > lastOffset Then
                result.Add(New MatchInfo(lastOffset, lastSeen, False))
            End If
            Return result
        End Function

        ''' <summary>Builds a <see cref="Rune"/> for the scalar at the given .NET index, guarding lone surrogates.</summary>
        Private Shared Function ScalarToRune(text As String, netStart As Integer) As Rune
            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(text, netStart)
            If cp >= &HD800 AndAlso cp <= &HDFFF Then Return Rune.ReplacementChar
            Return New Rune(cp)
        End Function
    End Class

End Namespace
