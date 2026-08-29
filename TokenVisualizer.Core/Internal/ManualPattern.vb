Imports System.Linq

Namespace Internal

    ''' <summary>
    ''' Shared base for hand-written, regex-free pattern scanners. A subclass overrides
    ''' <see cref="Scan"/> to emit only the <c>(start, end)</c> UTF-8 byte spans that match
    ''' (in ascending, non-overlapping order); this base fills the gaps with
    ''' <c>isMatch=False</c> segments so the result covers the whole string, mirroring the
    ''' Rust <c>Pattern::find_matches</c> contract.
    '''
    ''' The scanners follow the Rust <c>regex</c> crate semantics, not .NET Regex: a surrogate
    ''' pair is ONE Unicode scalar value, so a supplementary letter IS <c>\p{L}</c>, a
    ''' supplementary mark IS <c>\p{M}</c>, a supplementary symbol IS <c>\p{S}</c>, and a
    ''' supplementary number IS <c>\p{N}</c>. Predicates delegate to
    ''' <see cref="UnicodePredicates"/> which is surrogate-pair aware via
    ''' <c>CharUnicodeInfo.GetUnicodeCategory(text, netIndex)</c>.
    ''' </summary>
    Public MustInherit Class ManualPatternBase
        Inherits Pattern

        ''' <summary>Emits all match spans for <paramref name="inside"/> into <paramref name="matches"/>.</summary>
        Protected MustOverride Sub Scan(inside As String, scalars As List(Of ScalarInfo), matches As List(Of (Integer, Integer)))

        Public Overrides Function FindMatches(inside As String) As List(Of MatchInfo)
            If inside Is Nothing Then inside = String.Empty
            If inside.Length = 0 Then
                Return New List(Of MatchInfo) From {New MatchInfo(0, 0, False)}
            End If

            Dim scalars As List(Of ScalarInfo) = Utf8Helpers.EnumerateScalars(inside).ToList()
            Dim matches As New List(Of (Integer, Integer))()
            Me.Scan(inside, scalars, matches)

            Dim result As New List(Of MatchInfo)()
            Dim prev As Integer = 0
            For Each m In matches
                If prev < m.Item1 Then
                    result.Add(New MatchInfo(prev, m.Item1, False))
                End If
                result.Add(New MatchInfo(m.Item1, m.Item2, True))
                prev = m.Item2
            Next
            Dim total As Integer = Utf8Helpers.Utf8Length(inside)
            If prev < total Then
                result.Add(New MatchInfo(prev, total, False))
            End If
            Return result
        End Function

        ' ------------------------------------------------------------------
        ' Shared helpers
        ' ------------------------------------------------------------------

        ''' <summary>UTF-8 byte offset just past the scalar at <paramref name="lastIndex"/>.</summary>
        Protected Shared Function ByteEnd(scalars As List(Of ScalarInfo), lastIndex As Integer) As Integer
            Dim sc As ScalarInfo = scalars(lastIndex)
            Return sc.Utf8Start + sc.Utf8Len
        End Function

        ''' <summary>Whether the text at <paramref name="startNet"/> equals the literal <paramref name="lit"/>.</summary>
        Protected Shared Function MatchLiteral(inside As String, startNet As Integer, lit As String) As Boolean
            For k As Integer = 0 To lit.Length - 1
                If inside(startNet + k) <> lit(k) Then Return False
            Next
            Return True
        End Function

        ' ------------------------------------------------------------------
        #Region "Scalar-aware predicates (Rust regex semantics)"
        ' These delegate to UnicodePredicates, which is surrogate-pair aware. Under Rust regex
        ' semantics a supplementary scalar IS \p{L}/\p{N}/\p{P}/\p{S}/\p{M}, so there is no
        ' UTF-16 code-unit concept here.
        ' ------------------------------------------------------------------

        Protected Shared Function IsLetter(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsLetter(text, sc.NetStart)
        End Function

        Protected Shared Function IsNumber(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsNumber(text, sc.NetStart)
        End Function

        Protected Shared Function IsPunctuation(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsPunctuation(text, sc.NetStart)
        End Function

        Protected Shared Function IsSymbol(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsSymbol(text, sc.NetStart)
        End Function

        Protected Shared Function IsMark(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsMark(text, sc.NetStart)
        End Function

        ''' <summary><c>\s</c>: scalar-aware; on net10 <c>Char.IsWhiteSpace</c> matches the Rust set (excludes U+FEFF).</summary>
        Protected Shared Function IsWhiteSpace(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsWhiteSpace(text, sc.NetStart)
        End Function

        ''' <summary>Rust <c>\w</c> = [\p{Alphabetic}\p{M}\p{Nd}\p{Pc}\p{Join_Control}].</summary>
        Protected Shared Function IsWord(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsWord(text, sc.NetStart)
        End Function

        ''' <summary><c>[A-Za-z]</c>: ASCII letters only (a supplementary scalar is never ASCII).</summary>
        Protected Shared Function IsAsciiLetter(text As String, sc As ScalarInfo) As Boolean
            Return UnicodePredicates.IsAsciiLetter(text(sc.NetStart))
        End Function

        Protected Shared Function IsCrLf(text As String, sc As ScalarInfo) As Boolean
            Dim c As Char = text(sc.NetStart)
            Return c = ControlChars.Cr OrElse c = ControlChars.Lf
        End Function

        #End Region
    End Class

    ''' <summary>
    ''' Hand-written scanner for the GPT-2/ByteLevel regex:
    ''' <c>'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+</c>
    ''' </summary>
    Public NotInheritable Class Gpt2ByteLevelPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+"

        Private Shared ReadOnly Contractions As String() = {"s", "t", "re", "ve", "m", "ll", "d"}

        Protected Overrides Sub Scan(inside As String, scalars As List(Of ScalarInfo), matches As List(Of (Integer, Integer)))
            Dim n As Integer = scalars.Count
            Dim i As Integer = 0
            While i < n
                Dim sc As ScalarInfo = scalars(i)
                Dim startByte As Integer = sc.Utf8Start

                ' Alt 1: contractions, in order.
                If inside(sc.NetStart) = "'"c Then
                    Dim matchedContraction As Boolean = False
                    For Each lit In Contractions
                        If i + 1 + lit.Length <= n AndAlso MatchLiteral(inside, sc.NetStart + 1, lit) Then
                            matches.Add((startByte, startByte + 1 + lit.Length))
                            i += 1 + lit.Length
                            matchedContraction = True
                            Exit For
                        End If
                    Next
                    If matchedContraction Then Continue While
                End If

                ' Alt 2: ?\p{L}+
                If inside(sc.NetStart) = " "c Then
                    If i + 1 < n AndAlso IsLetter(inside, scalars(i + 1)) Then
                        Dim j As Integer = i + 1
                        While j < n AndAlso IsLetter(inside, scalars(j)) : j += 1 : End While
                        matches.Add((startByte, ByteEnd(scalars, j - 1)))
                        i = j
                        Continue While
                    End If
                ElseIf IsLetter(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso IsLetter(inside, scalars(j)) : j += 1 : End While
                    matches.Add((startByte, ByteEnd(scalars, j - 1)))
                    i = j
                    Continue While
                End If

                ' Alt 3: ?\p{N}+
                If inside(sc.NetStart) = " "c Then
                    If i + 1 < n AndAlso IsNumber(inside, scalars(i + 1)) Then
                        Dim j As Integer = i + 1
                        While j < n AndAlso IsNumber(inside, scalars(j)) : j += 1 : End While
                        matches.Add((startByte, ByteEnd(scalars, j - 1)))
                        i = j
                        Continue While
                    End If
                ElseIf IsNumber(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso IsNumber(inside, scalars(j)) : j += 1 : End While
                    matches.Add((startByte, ByteEnd(scalars, j - 1)))
                    i = j
                    Continue While
                End If

                ' Alt 4: ?[^\s\p{L}\p{N}]+
                If inside(sc.NetStart) = " "c Then
                    If i + 1 < n AndAlso IsNotWsLetterNumber(inside, scalars(i + 1)) Then
                        Dim j As Integer = i + 1
                        While j < n AndAlso IsNotWsLetterNumber(inside, scalars(j)) : j += 1 : End While
                        matches.Add((startByte, ByteEnd(scalars, j - 1)))
                        i = j
                        Continue While
                    End If
                ElseIf IsNotWsLetterNumber(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso IsNotWsLetterNumber(inside, scalars(j)) : j += 1 : End While
                    matches.Add((startByte, ByteEnd(scalars, j - 1)))
                    i = j
                    Continue While
                End If

                ' Alt 5 then Alt 6: \s+(?!\S)  then  \s+
                If IsWhiteSpace(inside, sc) Then
                    Dim q As Integer = i
                    While q < n AndAlso IsWhiteSpace(inside, scalars(q)) : q += 1 : End While
                    If q = n Then
                        ' \s+(?!\S) matches the run to end of string.
                        matches.Add((startByte, ByteEnd(scalars, q - 1)))
                        i = q
                        Continue While
                    ElseIf q - i >= 2 Then
                        ' \s+(?!\S) backtracks: drop the last whitespace scalar.
                        matches.Add((startByte, ByteEnd(scalars, q - 2)))
                        i = q - 1
                        Continue While
                    Else
                        ' \s+(?!\S) fails (single space before non-space); \s+ matches it.
                        matches.Add((startByte, ByteEnd(scalars, i)))
                        i = q
                        Continue While
                    End If
                End If

                ' No alternative matched: one-scalar gap.
                i += 1
            End While
        End Sub

        Private Shared Function IsNotWsLetterNumber(text As String, sc As ScalarInfo) As Boolean
            Return Not (IsWhiteSpace(text, sc) OrElse IsLetter(text, sc) OrElse IsNumber(text, sc))
        End Function
    End Class

    ''' <summary>
    ''' Hand-written scanner for the DeepSeek numbers regex: <c>\p{N}{1,3}</c>.
    ''' Consumes at most three consecutive numbers greedily.
    ''' </summary>
    Public NotInheritable Class DeepSeekNumbersPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "\p{N}{1,3}"

        Protected Overrides Sub Scan(inside As String, scalars As List(Of ScalarInfo), matches As List(Of (Integer, Integer)))
            Dim n As Integer = scalars.Count
            Dim i As Integer = 0
            While i < n
                Dim sc As ScalarInfo = scalars(i)
                If IsNumber(inside, sc) Then
                    Dim j As Integer = i
                    Dim count As Integer = 0
                    While j < n AndAlso count < 3 AndAlso IsNumber(inside, scalars(j))
                        j += 1
                        count += 1
                    End While
                    matches.Add((sc.Utf8Start, ByteEnd(scalars, j - 1)))
                    i = j
                Else
                    i += 1
                End If
            End While
        End Sub
    End Class

    ''' <summary>
    ''' Hand-written scanner for the DeepSeek CJK regex:
    ''' <c>[一-龥぀-ゟ゠-ヿ]+</c> (CJK Unified Ideographs U+4E00..U+9FA5,
    ''' Hiragana U+3040..U+309F, Katakana U+30A0..U+30FF).
    ''' </summary>
    Public NotInheritable Class DeepSeekCjkPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "[一-龥぀-ゟ゠-ヿ]+"

        Protected Overrides Sub Scan(inside As String, scalars As List(Of ScalarInfo), matches As List(Of (Integer, Integer)))
            Dim n As Integer = scalars.Count
            Dim i As Integer = 0
            While i < n
                Dim sc As ScalarInfo = scalars(i)
                If IsCjkScalar(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso IsCjkScalar(inside, scalars(j)) : j += 1 : End While
                    matches.Add((sc.Utf8Start, ByteEnd(scalars, j - 1)))
                    i = j
                Else
                    i += 1
                End If
            End While
        End Sub

        Private Shared Function IsCjkScalar(text As String, sc As ScalarInfo) As Boolean
            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(text, sc.NetStart)
            Return (cp >= &H4E00 AndAlso cp <= &H9FA5) OrElse
                   (cp >= &H3040 AndAlso cp <= &H309F) OrElse
                   (cp >= &H30A0 AndAlso cp <= &H30FF)
        End Function
    End Class

    ''' <summary>
    ''' Hand-written scanner for the DeepSeek GPT-2-like regex (the exact pattern from the DeepSeek
    ''' tokenizer config):
    ''' <c>[!"#$%&amp;'()*+,\-./:;&lt;=&gt;?@\[\\\]^_`{|}~][A-Za-z]+|[^\r\n\p{L}\p{P}\p{S}]?[\p{L}\p{M}]+| ?[\p{P}\p{S}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+</c>
    ''' </summary>
    Public NotInheritable Class DeepSeekGpt2Pattern
        Inherits ManualPatternBase

        ' The exact decoded config regex (deepseek-v4-flash\tokenizer.json, pretokenizers[2]).
        ' NOTE: the JSON \r and \n escapes decode to actual CR/LF characters, so the char-class
        ' CR/LF literals here are real control characters (ControlChars.Cr / ControlChars.Lf),
        ' not the two-character "\r\n" escape sequence.
        Public Const Canonical As String = "[!""#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~][A-Za-z]+" &
            "|[^" & ControlChars.Cr & ControlChars.Lf & "\p{L}\p{P}\p{S}]?[\p{L}\p{M}]+" &
            "| ?[\p{P}\p{S}]+[" & ControlChars.Cr & ControlChars.Lf & "]*" &
            "|\s*[" & ControlChars.Cr & ControlChars.Lf & "]+|\s+(?!\S)|\s+"

        Protected Overrides Sub Scan(inside As String, scalars As List(Of ScalarInfo), matches As List(Of (Integer, Integer)))
            Dim n As Integer = scalars.Count
            Dim i As Integer = 0
            While i < n
                Dim sc As ScalarInfo = scalars(i)
                Dim startByte As Integer = sc.Utf8Start

                ' Alt 1: [punct][A-Za-z]+  (ASCII punctuation set, then 1+ ASCII letters)
                If IsDg2Punct(UnicodePredicates.ScalarCodePoint(inside, sc.NetStart)) Then
                    If i + 1 < n AndAlso IsAsciiLetter(inside, scalars(i + 1)) Then
                        Dim j As Integer = i + 1
                        While j < n AndAlso IsAsciiLetter(inside, scalars(j)) : j += 1 : End While
                        matches.Add((startByte, ByteEnd(scalars, j - 1)))
                        i = j
                        Continue While
                    End If
                End If

                ' Alt 2: [^\r\n\p{L}\p{P}\p{S}]?[\p{L}\p{M}]+
                If IsLetter(inside, sc) OrElse IsMark(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso (IsLetter(inside, scalars(j)) OrElse IsMark(inside, scalars(j))) : j += 1 : End While
                    matches.Add((startByte, ByteEnd(scalars, j - 1)))
                    i = j
                    Continue While
                ElseIf IsValidOptional(inside, sc) AndAlso i + 1 < n AndAlso (IsLetter(inside, scalars(i + 1)) OrElse IsMark(inside, scalars(i + 1))) Then
                    Dim j As Integer = i + 1
                    While j < n AndAlso (IsLetter(inside, scalars(j)) OrElse IsMark(inside, scalars(j))) : j += 1 : End While
                    matches.Add((startByte, ByteEnd(scalars, j - 1)))
                    i = j
                    Continue While
                End If

                ' Alt 3: ?[\p{P}\p{S}]+[\r\n]*
                If inside(sc.NetStart) = " "c Then
                    If i + 1 < n AndAlso (IsPunctuation(inside, scalars(i + 1)) OrElse IsSymbol(inside, scalars(i + 1))) Then
                        Dim j As Integer = i + 1
                        While j < n AndAlso (IsPunctuation(inside, scalars(j)) OrElse IsSymbol(inside, scalars(j))) : j += 1 : End While
                        Dim k As Integer = j
                        While k < n AndAlso IsCrLf(inside, scalars(k)) : k += 1 : End While
                        matches.Add((startByte, ByteEnd(scalars, k - 1)))
                        i = k
                        Continue While
                    End If
                ElseIf IsPunctuation(inside, sc) OrElse IsSymbol(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso (IsPunctuation(inside, scalars(j)) OrElse IsSymbol(inside, scalars(j))) : j += 1 : End While
                    Dim k As Integer = j
                    While k < n AndAlso IsCrLf(inside, scalars(k)) : k += 1 : End While
                    matches.Add((startByte, ByteEnd(scalars, k - 1)))
                    i = k
                    Continue While
                End If

                ' Alt 4: \s*[\r\n]+  (greedy \s* backtracking: the match ends at the newline run
                ' starting at the last newline scalar inside the whitespace run).
                If IsWhiteSpace(inside, sc) Then
                    Dim q As Integer = i
                    While q < n AndAlso IsWhiteSpace(inside, scalars(q)) : q += 1 : End While
                    Dim p As Integer = -1
                    For idx As Integer = q - 1 To i Step -1
                        If IsCrLf(inside, scalars(idx)) Then
                            p = idx
                            Exit For
                        End If
                    Next
                    If p >= 0 Then
                        Dim k As Integer = p
                        While k < n AndAlso IsCrLf(inside, scalars(k)) : k += 1 : End While
                        matches.Add((startByte, ByteEnd(scalars, k - 1)))
                        i = k
                        Continue While
                    End If
                End If

                ' Alt 5 then Alt 6: \s+(?!\S)  then  \s+
                If IsWhiteSpace(inside, sc) Then
                    Dim q As Integer = i
                    While q < n AndAlso IsWhiteSpace(inside, scalars(q)) : q += 1 : End While
                    If q = n Then
                        matches.Add((startByte, ByteEnd(scalars, q - 1)))
                        i = q
                        Continue While
                    ElseIf q - i >= 2 Then
                        matches.Add((startByte, ByteEnd(scalars, q - 2)))
                        i = q - 1
                        Continue While
                    Else
                        matches.Add((startByte, ByteEnd(scalars, i)))
                        i = q
                        Continue While
                    End If
                End If

                ' No alternative matched: one-scalar gap.
                i += 1
            End While
        End Sub

        ''' <summary>Whether <paramref name="cp"/> is in the DeepSeek ASCII punctuation/symbol set (includes backslash).</summary>
        Private Shared Function IsDg2Punct(cp As Integer) As Boolean
            Return (cp >= &H21 AndAlso cp <= &H2F) OrElse    ' ! " # $ % & ' ( ) * + , - . /
                   (cp >= &H3A AndAlso cp <= &H40) OrElse    ' : ; < = > ? @
                   (cp >= &H5B AndAlso cp <= &H60) OrElse    ' [ \ ] ^ _ `
                   (cp >= &H7B AndAlso cp <= &H7E)           ' { | } ~
        End Function

        ''' <summary>Whether the scalar is a valid <c>[^\r\n\p{L}\p{P}\p{S}]</c> optional char.</summary>
        Private Shared Function IsValidOptional(text As String, sc As ScalarInfo) As Boolean
            If IsCrLf(text, sc) Then Return False
            If IsLetter(text, sc) Then Return False
            If IsPunctuation(text, sc) Then Return False
            If IsSymbol(text, sc) Then Return False
            Return True
        End Function
    End Class

    ''' <summary>
    ''' Hand-written scanner for the Whitespace pre-tokenizer regex: <c>\w+|[^\w\s]+</c>.
    ''' </summary>
    Public NotInheritable Class WordPunctPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "\w+|[^\w\s]+"

        Protected Overrides Sub Scan(inside As String, scalars As List(Of ScalarInfo), matches As List(Of (Integer, Integer)))
            Dim n As Integer = scalars.Count
            Dim i As Integer = 0
            While i < n
                Dim sc As ScalarInfo = scalars(i)
                If IsWord(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso IsWord(inside, scalars(j)) : j += 1 : End While
                    matches.Add((sc.Utf8Start, ByteEnd(scalars, j - 1)))
                    i = j
                ElseIf Not IsWhiteSpace(inside, sc) Then
                    Dim j As Integer = i
                    While j < n AndAlso (Not IsWord(inside, scalars(j))) AndAlso (Not IsWhiteSpace(inside, scalars(j))) : j += 1 : End While
                    matches.Add((sc.Utf8Start, ByteEnd(scalars, j - 1)))
                    i = j
                Else
                    i += 1
                End If
            End While
        End Sub
    End Class

    ''' <summary>
    ''' Registry mapping the exact canonical regex strings to their hand-written scanners.
    ''' Unknown strings yield <c>Nothing</c> so callers fall back to <see cref="RegexPattern"/>.
    ''' </summary>
    Public NotInheritable Class ManualPatternFactory

        ''' <summary>Returns the matching manual pattern for <paramref name="regexString"/>, or <c>Nothing</c> if unknown.</summary>
        Public Shared Function TryCreate(regexString As String) As Pattern
            If regexString Is Nothing Then Return Nothing
            If String.Equals(regexString, Gpt2ByteLevelPattern.Canonical, StringComparison.Ordinal) Then
                Return New Gpt2ByteLevelPattern()
            End If
            If String.Equals(regexString, DeepSeekNumbersPattern.Canonical, StringComparison.Ordinal) Then
                Return New DeepSeekNumbersPattern()
            End If
            If String.Equals(regexString, DeepSeekCjkPattern.Canonical, StringComparison.Ordinal) Then
                Return New DeepSeekCjkPattern()
            End If
            If String.Equals(regexString, DeepSeekGpt2Pattern.Canonical, StringComparison.Ordinal) Then
                Return New DeepSeekGpt2Pattern()
            End If
            If String.Equals(regexString, WordPunctPattern.Canonical, StringComparison.Ordinal) Then
                Return New WordPunctPattern()
            End If
            Return Nothing
        End Function
    End Class

End Namespace
