Imports System.Text.RegularExpressions

Namespace Internal

    ''' <summary>
    ''' A single match position produced by a <see cref="Pattern"/>. Offsets are UTF-8 byte
    ''' offsets into the string being searched. <see cref="IsMatch"/> indicates whether this
    ''' segment matched the pattern.
    ''' </summary>
    Public Structure MatchInfo
        Public Start As Integer
        Public [End] As Integer
        Public IsMatch As Boolean

        Public Sub New(start As Integer, [end] As Integer, isMatch As Boolean)
            Me.Start = start
            Me.End = [end]
            Me.IsMatch = isMatch
        End Sub

        Public Overrides Function ToString() As String
            Return $"({Me.Start},{Me.End}) {Me.IsMatch}"
        End Function
    End Structure

    ''' <summary>
    ''' Pattern used to split a NormalizedString. Mirrors the Rust <c>Pattern</c> trait:
    ''' <c>find_matches</c> must cover the whole string with contiguous ordered slices, each
    ''' flagged with whether it is a match.
    ''' </summary>
    Public MustInherit Class Pattern
        ''' <summary>
        ''' Slices the given string in a list of pattern match positions, with a boolean
        ''' indicating whether each segment is a match. The output covers the whole string,
        ''' with contiguous ordered slices.
        ''' </summary>
        Public Function FindMatches(inside As String) As List(Of MatchInfo)
            Dim result As New List(Of MatchInfo)()
            FindMatchesInto(inside, result)
            Return result
        End Function

        ''' <summary>
        ''' Writes the matches for <paramref name="inside"/> into <paramref name="result"/>,
        ''' clearing it first. The hot fused-split path reuses one scratch list across every
        ''' per-piece scan, avoiding one <see cref="List(Of MatchInfo)"/> allocation per piece.
        ''' Semantics are identical to <see cref="FindMatches"/>; a caller must consume (or copy)
        ''' the entries before the next call clears the list.
        ''' </summary>
        Friend Sub FindMatchesInto(inside As String, result As List(Of MatchInfo))
            result.Clear()
            FindMatchesCore(inside, 0, inside.Length, result)
        End Sub

        ''' <summary>
        ''' Slice-aware overload: matches the <paramref name="inside"/>(<paramref name="startNet"/>,
        ''' startNet + <paramref name="lenNet"/>) slice. The default materializes the slice and
        ''' delegates to the full-string scanner (semantics unchanged); the hot fused-split path's
        ''' manual scanners override <see cref="FindMatchesCore(String, Integer, Integer, List(Of MatchInfo))"/>
        ''' to scan the slice in place, avoiding the substring allocation.
        ''' </summary>
        Friend Sub FindMatchesInto(inside As String, startNet As Integer, lenNet As Integer, result As List(Of MatchInfo))
            result.Clear()
            FindMatchesCore(inside, startNet, lenNet, result)
        End Sub

        ''' <summary>
        ''' Writes the match segments of <paramref name="inside"/> into <paramref name="result"/>
        ''' (which is pre-cleared). Subclasses implement the pattern-specific scanning here.
        ''' </summary>
        Protected MustOverride Sub FindMatchesCore(inside As String, result As List(Of MatchInfo))

        ''' <summary>
        ''' Slice match scan. The default materializes the slice substring and delegates to
        ''' <see cref="FindMatchesCore(String, List(Of MatchInfo))"/>, so patterns that are not in
        ''' the fused hot path (regex, string, invert) keep their exact current behaviour.
        ''' </summary>
        Protected Overridable Sub FindMatchesCore(inside As String, startNet As Integer, lenNet As Integer, result As List(Of MatchInfo))
            Dim seg As String = inside.Substring(startNet, lenNet)
            FindMatchesCore(seg, result)
        End Sub

        ''' <summary>
        ''' Factory used by later phases to dispatch to the right pattern implementation.
        ''' For "Regex" patterns, a known canonical pattern string is routed to its hand-written
        ''' manual scanner; unknown patterns fall back to <see cref="RegexPattern"/>.
        ''' </summary>
        Public Shared Function Create(kind As String, pattern As String) As Pattern
            If String.Equals(kind, "Regex", StringComparison.OrdinalIgnoreCase) Then
                Dim manual As Pattern = ManualPatternFactory.TryCreate(pattern)
                If manual IsNot Nothing Then Return manual
                Return New RegexPattern(pattern)
            End If
            Return New StringPattern(pattern)
        End Function
    End Class

    ''' <summary>
    ''' A compiled regular expression pattern. Ports the Rust <c>&amp;Regex</c> implementation of
    ''' <c>find_matches</c>: iterates non-overlapping matches and fills in the gaps.
    ''' </summary>
    Public NotInheritable Class RegexPattern
        Inherits Pattern

        Private ReadOnly _regex As Regex

        Public Sub New(pattern As String)
            _regex = New Regex(pattern, RegexOptions.Compiled Or RegexOptions.CultureInvariant)
        End Sub

        Protected Overrides Sub FindMatchesCore(inside As String, result As List(Of MatchInfo))
            If inside Is Nothing Then inside = String.Empty
            If inside.Length = 0 Then
                result.Add(New MatchInfo(0, 0, False))
                Return
            End If

            Dim prev As Integer = 0
            For Each m As Match In _regex.Matches(inside)
                Dim startNet As Integer = m.Index
                Dim endNet As Integer = m.Index + m.Length
                Dim startByte As Integer = Utf8Helpers.NetIndexToUtf8(inside, startNet)
                Dim endByte As Integer = Utf8Helpers.NetIndexToUtf8(inside, endNet)
                If prev <> startByte Then
                    result.Add(New MatchInfo(prev, startByte, False))
                End If
                result.Add(New MatchInfo(startByte, endByte, True))
                prev = endByte
            Next
            Dim total As Integer = Utf8Helpers.Utf8Length(inside)
            If prev <> total Then
                result.Add(New MatchInfo(prev, total, False))
            End If
        End Sub
    End Class

    ''' <summary>
    ''' A literal string pattern. In Rust, string patterns are regex-escaped; here we implement
    ''' the same non-overlapping leftmost-first scanning with ordinal <c>IndexOf</c>.
    ''' </summary>
    Public NotInheritable Class StringPattern
        Inherits Pattern

        Private ReadOnly _pattern As String

        Public Sub New(pattern As String)
            _pattern = pattern
        End Sub

        Protected Overrides Sub FindMatchesCore(inside As String, result As List(Of MatchInfo))
            If inside Is Nothing Then inside = String.Empty

            ' Empty pattern matches nothing: the whole input is one non-match segment
            ' (Rust reports the char count here, not the byte count).
            If _pattern.Length = 0 Then
                If inside.Length = 0 Then
                    result.Add(New MatchInfo(0, 0, False))
                    Return
                End If
                Dim count As Integer = Utf8Helpers.ScalarCount(inside)
                result.Add(New MatchInfo(0, count, False))
                Return
            End If

            If inside.Length = 0 Then
                result.Add(New MatchInfo(0, 0, False))
                Return
            End If

            Dim prevByte As Integer = 0
            Dim searchNet As Integer = 0
            While True
                Dim idx As Integer = inside.IndexOf(_pattern, searchNet, StringComparison.Ordinal)
                If idx < 0 Then Exit While
                Dim startByte As Integer = Utf8Helpers.NetIndexToUtf8(inside, idx)
                Dim endByte As Integer = Utf8Helpers.NetIndexToUtf8(inside, idx + _pattern.Length)
                If prevByte <> startByte Then
                    result.Add(New MatchInfo(prevByte, startByte, False))
                End If
                result.Add(New MatchInfo(startByte, endByte, True))
                prevByte = endByte
                searchNet = idx + _pattern.Length
                If searchNet >= inside.Length Then Exit While
            End While
            Dim total As Integer = Utf8Helpers.Utf8Length(inside)
            If prevByte <> total Then
                result.Add(New MatchInfo(prevByte, total, False))
            End If
        End Sub
    End Class

    ''' <summary>
    ''' Inverts the <c>IsMatch</c> flags of a wrapped pattern. Ports the Rust <c>Invert&lt;P&gt;</c>.
    ''' </summary>
    Public NotInheritable Class InvertPattern
        Inherits Pattern

        Private ReadOnly _inner As Pattern

        Public Sub New(inner As Pattern)
            _inner = inner
        End Sub

        Protected Overrides Sub FindMatchesCore(inside As String, result As List(Of MatchInfo))
            ' The inner pattern writes into the same list; invert the flags in place afterwards.
            _inner.FindMatchesInto(inside, result)
            For i As Integer = 0 To result.Count - 1
                Dim m As MatchInfo = result(i)
                result(i) = New MatchInfo(m.Start, m.End, Not m.IsMatch)
            Next
        End Sub
    End Class

End Namespace
