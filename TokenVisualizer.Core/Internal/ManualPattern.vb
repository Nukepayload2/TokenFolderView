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
    '''
    ''' Scanners iterate the input by .NET index and maintain the running UTF-8 byte offset
    ''' incrementally (<see cref="AdvanceOne"/>); no <c>List(Of ScalarInfo)</c> is materialized,
    ''' so a scan is allocation-free apart from the match list itself.
    ''' </summary>
    Public MustInherit Class ManualPatternBase
        Inherits Pattern

        ''' <summary>Emits all match spans for <paramref name="inside"/> into <paramref name="result"/>.</summary>
        Protected MustOverride Sub Scan(inside As String, result As List(Of MatchInfo), ByRef prev As Integer)

        Public Overrides Function FindMatches(inside As String) As List(Of MatchInfo)
            If inside Is Nothing Then inside = String.Empty
            If inside.Length = 0 Then
                Return New List(Of MatchInfo) From {New MatchInfo(0, 0, False)}
            End If

            ' The scanner emits MatchInfo entries directly (with implicit gap filling via
            ' <see cref="EmitMatch"/>), so no intermediate (start,end) span list is materialized.
            Dim result As New List(Of MatchInfo)()
            Dim prev As Integer = 0
            Me.Scan(inside, result, prev)
            Dim total As Integer = Utf8Helpers.Utf8Length(inside)
            If prev < total Then
                result.Add(New MatchInfo(prev, total, False))
            End If
            Return result
        End Function

        ''' <summary>
        ''' Appends a match span, first filling any gap between <paramref name="prev"/> (the end of
        ''' the previous emitted span) and <paramref name="startByte"/> with a non-match segment.
        ''' Mirrors the base <c>find_matches</c> gap-filling contract.
        ''' </summary>
        Protected Shared Sub EmitMatch(result As List(Of MatchInfo), ByRef prev As Integer, startByte As Integer, endByte As Integer)
            If prev < startByte Then
                result.Add(New MatchInfo(prev, startByte, False))
            End If
            result.Add(New MatchInfo(startByte, endByte, True))
            prev = endByte
        End Sub

        ' ------------------------------------------------------------------
        ' Shared helpers
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Advances <paramref name="net"/> (UTF-16 index) and <paramref name="byteOff"/> (UTF-8
        ''' byte offset) past the scalar currently starting at <paramref name="net"/>. Caller
        ''' guarantees <c>net &lt; text.Length</c>.
        ''' </summary>
        Protected Shared Sub AdvanceOne(text As String, ByRef net As Integer, ByRef byteOff As Integer)
            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(text, net)
            byteOff += Utf8Helpers.Utf8LengthOfCodePoint(cp)
            net += Utf8Helpers.NetLengthOfCodePoint(cp)
        End Sub

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

        Protected Shared Function IsLetter(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsLetter(text, net)
        End Function

        Protected Shared Function IsNumber(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsNumber(text, net)
        End Function

        Protected Shared Function IsPunctuation(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsPunctuation(text, net)
        End Function

        Protected Shared Function IsSymbol(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsSymbol(text, net)
        End Function

        Protected Shared Function IsMark(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsMark(text, net)
        End Function

        ''' <summary><c>\s</c>: scalar-aware; on net10 <c>Char.IsWhiteSpace</c> matches the Rust set (excludes U+FEFF).</summary>
        Protected Shared Function IsWhiteSpace(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsWhiteSpace(text, net)
        End Function

        ''' <summary>Rust <c>\w</c> = [\p{Alphabetic}\p{M}\p{Nd}\p{Pc}\p{Join_Control}].</summary>
        Protected Shared Function IsWord(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsWord(text, net)
        End Function

        ''' <summary><c>[A-Za-z]</c>: ASCII letters only (a supplementary scalar is never ASCII).</summary>
        Protected Shared Function IsAsciiLetter(text As String, net As Integer) As Boolean
            Return UnicodePredicates.IsAsciiLetter(text(net))
        End Function

        Protected Shared Function IsCrLf(text As String, net As Integer) As Boolean
            Dim c As Char = text(net)
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

        Protected Overrides Sub Scan(inside As String, result As List(Of MatchInfo), ByRef prev As Integer)
            Dim n As Integer = inside.Length
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff

                ' Alt 1: contractions, in order.
                If inside(net) = "'"c Then
                    Dim matchedContraction As Boolean = False
                    For Each lit In Contractions
                        If net + 1 + lit.Length <= n AndAlso MatchLiteral(inside, net + 1, lit) Then
                            EmitMatch(result, prev, startByte, startByte + 1 + lit.Length)
                            net += 1 + lit.Length
                            byteOff += 1 + lit.Length
                            matchedContraction = True
                            Exit For
                        End If
                    Next
                    If matchedContraction Then Continue While
                End If

                ' Alt 2: ?\p{L}+
                If inside(net) = " "c Then
                    Dim nextNet As Integer = net
                    Dim nextByte As Integer = byteOff
                    AdvanceOne(inside, nextNet, nextByte)
                    If nextNet < n AndAlso IsLetter(inside, nextNet) Then
                        Dim jNet As Integer = nextNet
                        Dim jByte As Integer = nextByte
                        Dim endByte As Integer = nextByte
                        While jNet < n AndAlso IsLetter(inside, jNet)
                            AdvanceOne(inside, jNet, jByte)
                            endByte = jByte
                        End While
                        EmitMatch(result, prev, startByte, endByte)
                        net = jNet
                        byteOff = jByte
                        Continue While
                    End If
                ElseIf IsLetter(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso IsLetter(inside, jNet)
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                End If

                ' Alt 3: ?\p{N}+
                If inside(net) = " "c Then
                    Dim nextNet As Integer = net
                    Dim nextByte As Integer = byteOff
                    AdvanceOne(inside, nextNet, nextByte)
                    If nextNet < n AndAlso IsNumber(inside, nextNet) Then
                        Dim jNet As Integer = nextNet
                        Dim jByte As Integer = nextByte
                        Dim endByte As Integer = nextByte
                        While jNet < n AndAlso IsNumber(inside, jNet)
                            AdvanceOne(inside, jNet, jByte)
                            endByte = jByte
                        End While
                        EmitMatch(result, prev, startByte, endByte)
                        net = jNet
                        byteOff = jByte
                        Continue While
                    End If
                ElseIf IsNumber(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso IsNumber(inside, jNet)
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                End If

                ' Alt 4: ?[^\s\p{L}\p{N}]+
                If inside(net) = " "c Then
                    Dim nextNet As Integer = net
                    Dim nextByte As Integer = byteOff
                    AdvanceOne(inside, nextNet, nextByte)
                    If nextNet < n AndAlso IsNotWsLetterNumber(inside, nextNet) Then
                        Dim jNet As Integer = nextNet
                        Dim jByte As Integer = nextByte
                        Dim endByte As Integer = nextByte
                        While jNet < n AndAlso IsNotWsLetterNumber(inside, jNet)
                            AdvanceOne(inside, jNet, jByte)
                            endByte = jByte
                        End While
                        EmitMatch(result, prev, startByte, endByte)
                        net = jNet
                        byteOff = jByte
                        Continue While
                    End If
                ElseIf IsNotWsLetterNumber(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso IsNotWsLetterNumber(inside, jNet)
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                End If

                ' Alt 5 then Alt 6: \s+(?!\S)  then  \s+
                If IsWhiteSpace(inside, net) Then
                    Dim qNet As Integer = net
                    Dim qByte As Integer = byteOff
                    Dim lastNet As Integer = net
                    Dim lastByte As Integer = byteOff
                    Dim runCount As Integer = 0
                    While qNet < n AndAlso IsWhiteSpace(inside, qNet)
                        If runCount >= 1 Then
                            lastNet = qNet
                            lastByte = qByte
                        End If
                        AdvanceOne(inside, qNet, qByte)
                        runCount += 1
                    End While
                    If qNet = n Then
                        ' \s+(?!\S) matches the run to end of string.
                        EmitMatch(result, prev, startByte, qByte)
                        net = qNet
                        byteOff = qByte
                        Continue While
                    ElseIf runCount >= 2 Then
                        ' \s+(?!\S) backtracks: drop the last whitespace scalar.
                        EmitMatch(result, prev, startByte, lastByte)
                        net = lastNet
                        byteOff = lastByte
                        Continue While
                    Else
                        ' \s+(?!\S) fails (single space before non-space); \s+ matches it.
                        EmitMatch(result, prev, startByte, qByte)
                        net = qNet
                        byteOff = qByte
                        Continue While
                    End If
                End If

                ' No alternative matched: one-scalar gap.
                AdvanceOne(inside, net, byteOff)
            End While
        End Sub

        Private Shared Function IsNotWsLetterNumber(text As String, net As Integer) As Boolean
            Return Not (IsWhiteSpace(text, net) OrElse IsLetter(text, net) OrElse IsNumber(text, net))
        End Function
    End Class

    ''' <summary>
    ''' Hand-written scanner for the DeepSeek numbers regex: <c>\p{N}{1,3}</c>.
    ''' Consumes at most three consecutive numbers greedily.
    ''' </summary>
    Public NotInheritable Class DeepSeekNumbersPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "\p{N}{1,3}"

        Protected Overrides Sub Scan(inside As String, result As List(Of MatchInfo), ByRef prev As Integer)
            Dim n As Integer = inside.Length
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                If IsNumber(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    Dim count As Integer = 0
                    While jNet < n AndAlso count < 3 AndAlso IsNumber(inside, jNet)
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                        count += 1
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                Else
                    AdvanceOne(inside, net, byteOff)
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

        Protected Overrides Sub Scan(inside As String, result As List(Of MatchInfo), ByRef prev As Integer)
            Dim n As Integer = inside.Length
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                If IsCjkScalar(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso IsCjkScalar(inside, jNet)
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                Else
                    AdvanceOne(inside, net, byteOff)
                End If
            End While
        End Sub

        Private Shared Function IsCjkScalar(text As String, net As Integer) As Boolean
            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(text, net)
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

        Protected Overrides Sub Scan(inside As String, result As List(Of MatchInfo), ByRef prev As Integer)
            Dim n As Integer = inside.Length
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff

                ' Alt 1: [punct][A-Za-z]+  (ASCII punctuation set, then 1+ ASCII letters)
                If IsDg2Punct(UnicodePredicates.ScalarCodePoint(inside, net)) Then
                    Dim nextNet As Integer = net
                    Dim nextByte As Integer = byteOff
                    AdvanceOne(inside, nextNet, nextByte)
                    If nextNet < n AndAlso IsAsciiLetter(inside, nextNet) Then
                        Dim jNet As Integer = nextNet
                        Dim jByte As Integer = nextByte
                        Dim endByte As Integer = nextByte
                        While jNet < n AndAlso IsAsciiLetter(inside, jNet)
                            AdvanceOne(inside, jNet, jByte)
                            endByte = jByte
                        End While
                        EmitMatch(result, prev, startByte, endByte)
                        net = jNet
                        byteOff = jByte
                        Continue While
                    End If
                End If

                ' Alt 2: [^\r\n\p{L}\p{P}\p{S}]?[\p{L}\p{M}]+
                If IsLetter(inside, net) OrElse IsMark(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso (IsLetter(inside, jNet) OrElse IsMark(inside, jNet))
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                ElseIf IsValidOptional(inside, net) Then
                    Dim nextNet As Integer = net
                    Dim nextByte As Integer = byteOff
                    AdvanceOne(inside, nextNet, nextByte)
                    If nextNet < n AndAlso (IsLetter(inside, nextNet) OrElse IsMark(inside, nextNet)) Then
                        Dim jNet As Integer = nextNet
                        Dim jByte As Integer = nextByte
                        Dim endByte As Integer = nextByte
                        While jNet < n AndAlso (IsLetter(inside, jNet) OrElse IsMark(inside, jNet))
                            AdvanceOne(inside, jNet, jByte)
                            endByte = jByte
                        End While
                        EmitMatch(result, prev, startByte, endByte)
                        net = jNet
                        byteOff = jByte
                        Continue While
                    End If
                End If

                ' Alt 3: ?[\p{P}\p{S}]+[\r\n]*
                If inside(net) = " "c Then
                    Dim nextNet As Integer = net
                    Dim nextByte As Integer = byteOff
                    AdvanceOne(inside, nextNet, nextByte)
                    If nextNet < n AndAlso (IsPunctuation(inside, nextNet) OrElse IsSymbol(inside, nextNet)) Then
                        Dim jNet As Integer = nextNet
                        Dim jByte As Integer = nextByte
                        Dim endByte As Integer = nextByte
                        While jNet < n AndAlso (IsPunctuation(inside, jNet) OrElse IsSymbol(inside, jNet))
                            AdvanceOne(inside, jNet, jByte)
                            endByte = jByte
                        End While
                        Dim kNet As Integer = jNet
                        Dim kByte As Integer = jByte
                        While kNet < n AndAlso IsCrLf(inside, kNet)
                            AdvanceOne(inside, kNet, kByte)
                            endByte = kByte
                        End While
                        EmitMatch(result, prev, startByte, endByte)
                        net = kNet
                        byteOff = kByte
                        Continue While
                    End If
                ElseIf IsPunctuation(inside, net) OrElse IsSymbol(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso (IsPunctuation(inside, jNet) OrElse IsSymbol(inside, jNet))
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    Dim kNet As Integer = jNet
                    Dim kByte As Integer = jByte
                    While kNet < n AndAlso IsCrLf(inside, kNet)
                        AdvanceOne(inside, kNet, kByte)
                        endByte = kByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = kNet
                    byteOff = kByte
                    Continue While
                End If

                ' Alt 4: \s*[\r\n]+  (greedy \s* backtracking: the match ends at the newline run
                ' starting at the last newline scalar inside the whitespace run).
                If IsWhiteSpace(inside, net) Then
                    Dim qNet As Integer = net
                    Dim qByte As Integer = byteOff
                    Dim lastCrLfEndNet As Integer = -1
                    Dim lastCrLfEndByte As Integer = 0
                    While qNet < n AndAlso IsWhiteSpace(inside, qNet)
                        If IsCrLf(inside, qNet) Then
                            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(inside, qNet)
                            lastCrLfEndNet = qNet + Utf8Helpers.NetLengthOfCodePoint(cp)
                            lastCrLfEndByte = qByte + Utf8Helpers.Utf8LengthOfCodePoint(cp)
                        End If
                        AdvanceOne(inside, qNet, qByte)
                    End While
                    If lastCrLfEndNet >= 0 Then
                        EmitMatch(result, prev, startByte, lastCrLfEndByte)
                        net = lastCrLfEndNet
                        byteOff = lastCrLfEndByte
                        Continue While
                    End If
                End If

                ' Alt 5 then Alt 6: \s+(?!\S)  then  \s+
                If IsWhiteSpace(inside, net) Then
                    Dim qNet As Integer = net
                    Dim qByte As Integer = byteOff
                    Dim lastNet As Integer = net
                    Dim lastByte As Integer = byteOff
                    Dim runCount As Integer = 0
                    While qNet < n AndAlso IsWhiteSpace(inside, qNet)
                        If runCount >= 1 Then
                            lastNet = qNet
                            lastByte = qByte
                        End If
                        AdvanceOne(inside, qNet, qByte)
                        runCount += 1
                    End While
                    If qNet = n Then
                        EmitMatch(result, prev, startByte, qByte)
                        net = qNet
                        byteOff = qByte
                        Continue While
                    ElseIf runCount >= 2 Then
                        EmitMatch(result, prev, startByte, lastByte)
                        net = lastNet
                        byteOff = lastByte
                        Continue While
                    Else
                        EmitMatch(result, prev, startByte, qByte)
                        net = qNet
                        byteOff = qByte
                        Continue While
                    End If
                End If

                ' No alternative matched: one-scalar gap.
                AdvanceOne(inside, net, byteOff)
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
        Private Shared Function IsValidOptional(text As String, net As Integer) As Boolean
            If IsCrLf(text, net) Then Return False
            If IsLetter(text, net) Then Return False
            If IsPunctuation(text, net) Then Return False
            If IsSymbol(text, net) Then Return False
            Return True
        End Function
    End Class

    ''' <summary>
    ''' Hand-written scanner for the Whitespace pre-tokenizer regex: <c>\w+|[^\w\s]+</c>.
    ''' </summary>
    Public NotInheritable Class WordPunctPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "\w+|[^\w\s]+"

        Protected Overrides Sub Scan(inside As String, result As List(Of MatchInfo), ByRef prev As Integer)
            Dim n As Integer = inside.Length
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                If IsWord(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso IsWord(inside, jNet)
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                ElseIf Not IsWhiteSpace(inside, net) Then
                    Dim jNet As Integer = net
                    Dim jByte As Integer = byteOff
                    Dim endByte As Integer = byteOff
                    While jNet < n AndAlso (Not IsWord(inside, jNet)) AndAlso (Not IsWhiteSpace(inside, jNet))
                        AdvanceOne(inside, jNet, jByte)
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                Else
                    AdvanceOne(inside, net, byteOff)
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
