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
    ''' supplementary number IS <c>\p{N}</c>. Each scalar is classified exactly once per scan
    ''' via <see cref="ScalarClassifier.Classify"/>, which derives all regex predicates from a
    ''' single <c>CharUnicodeInfo</c> query into a bit field (ASCII is a precomputed table lookup,
    ''' no <c>CharUnicodeInfo</c> call at all).
    '''
    ''' Scanners iterate the input by .NET index and maintain the running UTF-8 byte offset
    ''' incrementally using the classifier's precomputed <c>Utf8Len</c>/<c>NetLen</c>; no
    ''' <c>List(Of ScalarInfo)</c> is materialized, so a scan is allocation-free apart from the
    ''' match list itself.
    ''' </summary>
    Public MustInherit Class ManualPatternBase
        Inherits Pattern

        ''' <summary>
        ''' Emits all match spans for the <paramref name="inside"/>(<paramref name="startNet"/>,
        ''' startNet + <paramref name="lenNet"/>) slice into <paramref name="result"/>. Byte offsets
        ''' in emitted matches are relative to the slice start. <paramref name="totalBytes"/> is set
        ''' to the slice's total UTF-8 byte length so the caller can fill the trailing gap.
        ''' </summary>
        Protected MustOverride Sub Scan(inside As String, startNet As Integer, lenNet As Integer,
                                        result As List(Of MatchInfo), ByRef prev As Integer, ByRef totalBytes As Integer)

        Protected Overrides Sub FindMatchesCore(inside As String, result As List(Of MatchInfo))
            If inside Is Nothing Then inside = String.Empty
            FindMatchesCore(inside, 0, inside.Length, result)
        End Sub

        Protected Overrides Sub FindMatchesCore(inside As String, startNet As Integer, lenNet As Integer, result As List(Of MatchInfo))
            If inside Is Nothing Then inside = String.Empty
            If lenNet <= 0 Then
                result.Add(New MatchInfo(0, 0, False))
                Return
            End If

            ' The scanner emits MatchInfo entries directly (with implicit gap filling via
            ' <see cref="EmitMatch"/>) with byte offsets relative to the slice start, so no
            ' intermediate (start,end) span list and no slice substring are materialized.
            Dim prev As Integer = 0
            Dim totalBytes As Integer = 0
            Me.Scan(inside, startNet, lenNet, result, prev, totalBytes)
            If prev < totalBytes Then
                result.Add(New MatchInfo(prev, totalBytes, False))
            End If
        End Sub

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
    End Class

    ''' <summary>
    ''' Hand-written scanner for the GPT-2/ByteLevel regex:
    ''' <c>'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+</c>
    ''' </summary>
    Public NotInheritable Class Gpt2ByteLevelPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+"

        Private Shared ReadOnly Contractions As String() = {"s", "t", "re", "ve", "m", "ll", "d"}

        Protected Overrides Sub Scan(inside As String, startNet As Integer, lenNet As Integer,
                                     result As List(Of MatchInfo), ByRef prev As Integer, ByRef totalBytes As Integer)
            ' net is an absolute index into <c>inside</c> bounded to the slice; byteOff stays
            ' relative to the slice start, so emitted match byte offsets are slice-relative.
            Dim n As Integer = startNet + lenNet
            Dim net As Integer = startNet
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                Dim sc As ScalarClass = ScalarClassifier.Classify(inside, net)

                ' Alt 1: contractions, in order.
                If sc.CodePoint = &H27 Then ' "'"
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

                ' Alt 2/3/4 with the optional exact U+0020 space, then without it.
                ' The next scalar after a space is classified once and dispatched by its flags,
                ' preserving the leftmost-first alternation order (letter, then number, then
                ' [^\s\p{L}\p{N}]).
                If (sc.Flags And ScalarClassifier.FlagExactSpace) <> 0 Then
                    Dim nextNet As Integer = net + sc.NetLen
                    Dim nextByte As Integer = byteOff + sc.Utf8Len
                    If nextNet < n Then
                        Dim nsc As ScalarClass = ScalarClassifier.Classify(inside, nextNet)
                        If (nsc.Flags And ScalarClassifier.FlagLetter) <> 0 Then
                            ' Alt 2: ?\p{L}+
                            Dim jNet As Integer = nextNet + nsc.NetLen
                            Dim jByte As Integer = nextByte + nsc.Utf8Len
                            Dim endByte As Integer = jByte
                            While jNet < n
                                Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                                If (jsc.Flags And ScalarClassifier.FlagLetter) = 0 Then Exit While
                                jNet += jsc.NetLen
                                jByte += jsc.Utf8Len
                                endByte = jByte
                            End While
                            EmitMatch(result, prev, startByte, endByte)
                            net = jNet
                            byteOff = jByte
                            Continue While
                        End If
                        If (nsc.Flags And ScalarClassifier.FlagNumber) <> 0 Then
                            ' Alt 3: ?\p{N}+
                            Dim jNet As Integer = nextNet + nsc.NetLen
                            Dim jByte As Integer = nextByte + nsc.Utf8Len
                            Dim endByte As Integer = jByte
                            While jNet < n
                                Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                                If (jsc.Flags And ScalarClassifier.FlagNumber) = 0 Then Exit While
                                jNet += jsc.NetLen
                                jByte += jsc.Utf8Len
                                endByte = jByte
                            End While
                            EmitMatch(result, prev, startByte, endByte)
                            net = jNet
                            byteOff = jByte
                            Continue While
                        End If
                        If (nsc.Flags And ScalarClassifier.MaskWsLetterNumber) = 0 Then
                            ' Alt 4: ?[^\s\p{L}\p{N}]+
                            Dim jNet As Integer = nextNet + nsc.NetLen
                            Dim jByte As Integer = nextByte + nsc.Utf8Len
                            Dim endByte As Integer = jByte
                            While jNet < n
                                Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                                If (jsc.Flags And ScalarClassifier.MaskWsLetterNumber) <> 0 Then Exit While
                                jNet += jsc.NetLen
                                jByte += jsc.Utf8Len
                                endByte = jByte
                            End While
                            EmitMatch(result, prev, startByte, endByte)
                            net = jNet
                            byteOff = jByte
                            Continue While
                        End If
                    End If
                ElseIf (sc.Flags And ScalarClassifier.FlagLetter) <> 0 Then
                    ' Alt 2: \p{L}+
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.FlagLetter) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                ElseIf (sc.Flags And ScalarClassifier.FlagNumber) <> 0 Then
                    ' Alt 3: \p{N}+
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.FlagNumber) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                ElseIf (sc.Flags And ScalarClassifier.MaskWsLetterNumber) = 0 Then
                    ' Alt 4: [^\s\p{L}\p{N}]+
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.MaskWsLetterNumber) <> 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                End If

                ' Alt 5 then Alt 6: \s+(?!\S)  then  \s+
                If (sc.Flags And ScalarClassifier.FlagWhiteSpace) <> 0 Then
                    Dim qNet As Integer = net
                    Dim qByte As Integer = byteOff
                    Dim lastNet As Integer = net
                    Dim lastByte As Integer = byteOff
                    Dim runCount As Integer = 0
                    Dim qsc As ScalarClass = sc
                    While qNet < n AndAlso (qsc.Flags And ScalarClassifier.FlagWhiteSpace) <> 0
                        If runCount >= 1 Then
                            lastNet = qNet
                            lastByte = qByte
                        End If
                        qNet += qsc.NetLen
                        qByte += qsc.Utf8Len
                        runCount += 1
                        If qNet < n Then qsc = ScalarClassifier.Classify(inside, qNet)
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
                net += sc.NetLen
                byteOff += sc.Utf8Len
            End While
            ' The scan consumed the whole slice, so byteOff is the slice's total UTF-8 byte length.
            totalBytes = byteOff
        End Sub
    End Class

    ''' <summary>
    ''' Hand-written scanner for the DeepSeek numbers regex: <c>\p{N}{1,3}</c>.
    ''' Consumes at most three consecutive numbers greedily.
    ''' </summary>
    Public NotInheritable Class DeepSeekNumbersPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "\p{N}{1,3}"

        Protected Overrides Sub Scan(inside As String, startNet As Integer, lenNet As Integer,
                                     result As List(Of MatchInfo), ByRef prev As Integer, ByRef totalBytes As Integer)
            ' net is an absolute index into <c>inside</c> bounded to the slice; byteOff stays
            ' relative to the slice start, so emitted match byte offsets are slice-relative.
            Dim n As Integer = startNet + lenNet
            Dim net As Integer = startNet
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                Dim sc As ScalarClass = ScalarClassifier.Classify(inside, net)
                If (sc.Flags And ScalarClassifier.FlagNumber) <> 0 Then
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    Dim count As Integer = 1
                    While jNet < n AndAlso count < 3
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.FlagNumber) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                        count += 1
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                Else
                    net += sc.NetLen
                    byteOff += sc.Utf8Len
                End If
            End While
            ' The scan consumed the whole slice, so byteOff is the slice's total UTF-8 byte length.
            totalBytes = byteOff
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

        Protected Overrides Sub Scan(inside As String, startNet As Integer, lenNet As Integer,
                                     result As List(Of MatchInfo), ByRef prev As Integer, ByRef totalBytes As Integer)
            ' net is an absolute index into <c>inside</c> bounded to the slice; byteOff stays
            ' relative to the slice start, so emitted match byte offsets are slice-relative.
            Dim n As Integer = startNet + lenNet
            Dim net As Integer = startNet
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                Dim sc As ScalarClass = ScalarClassifier.Classify(inside, net)
                If (sc.Flags And ScalarClassifier.FlagCjk) <> 0 Then
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.FlagCjk) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                Else
                    net += sc.NetLen
                    byteOff += sc.Utf8Len
                End If
            End While
            ' The scan consumed the whole slice, so byteOff is the slice's total UTF-8 byte length.
            totalBytes = byteOff
        End Sub
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

        Protected Overrides Sub Scan(inside As String, startNet As Integer, lenNet As Integer,
                                     result As List(Of MatchInfo), ByRef prev As Integer, ByRef totalBytes As Integer)
            ' net is an absolute index into <c>inside</c> bounded to the slice; byteOff stays
            ' relative to the slice start, so emitted match byte offsets are slice-relative.
            Dim n As Integer = startNet + lenNet
            Dim net As Integer = startNet
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                Dim sc As ScalarClass = ScalarClassifier.Classify(inside, net)

                ' Alt 1: [punct][A-Za-z]+  (ASCII punctuation set, then 1+ ASCII letters)
                If (sc.Flags And ScalarClassifier.FlagDg2Punct) <> 0 Then
                    Dim nextNet As Integer = net + sc.NetLen
                    Dim nextByte As Integer = byteOff + sc.Utf8Len
                    If nextNet < n Then
                        Dim nsc As ScalarClass = ScalarClassifier.Classify(inside, nextNet)
                        If (nsc.Flags And ScalarClassifier.FlagAsciiLetter) <> 0 Then
                            Dim jNet As Integer = nextNet + nsc.NetLen
                            Dim jByte As Integer = nextByte + nsc.Utf8Len
                            Dim endByte As Integer = jByte
                            While jNet < n
                                Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                                If (jsc.Flags And ScalarClassifier.FlagAsciiLetter) = 0 Then Exit While
                                jNet += jsc.NetLen
                                jByte += jsc.Utf8Len
                                endByte = jByte
                            End While
                            EmitMatch(result, prev, startByte, endByte)
                            net = jNet
                            byteOff = jByte
                            Continue While
                        End If
                    End If
                End If

                ' Alt 2: [^\r\n\p{L}\p{P}\p{S}]?[\p{L}\p{M}]+
                If (sc.Flags And ScalarClassifier.MaskLetterOrMark) <> 0 Then
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.MaskLetterOrMark) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                    Continue While
                ElseIf (sc.Flags And ScalarClassifier.MaskCrLfLetterPunctSymbol) = 0 Then
                    ' Valid [^\r\n\p{L}\p{P}\p{S}] optional char (can be a number, mark, whitespace, ...).
                    Dim nextNet As Integer = net + sc.NetLen
                    Dim nextByte As Integer = byteOff + sc.Utf8Len
                    If nextNet < n Then
                        Dim nsc As ScalarClass = ScalarClassifier.Classify(inside, nextNet)
                        If (nsc.Flags And ScalarClassifier.MaskLetterOrMark) <> 0 Then
                            Dim jNet As Integer = nextNet + nsc.NetLen
                            Dim jByte As Integer = nextByte + nsc.Utf8Len
                            Dim endByte As Integer = jByte
                            While jNet < n
                                Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                                If (jsc.Flags And ScalarClassifier.MaskLetterOrMark) = 0 Then Exit While
                                jNet += jsc.NetLen
                                jByte += jsc.Utf8Len
                                endByte = jByte
                            End While
                            EmitMatch(result, prev, startByte, endByte)
                            net = jNet
                            byteOff = jByte
                            Continue While
                        End If
                    End If
                End If

                ' Alt 3: ?[\p{P}\p{S}]+[\r\n]*  (exact U+0020 space, then punctuation/symbol run, then CR/LF run)
                If (sc.Flags And ScalarClassifier.FlagExactSpace) <> 0 Then
                    Dim nextNet As Integer = net + sc.NetLen
                    Dim nextByte As Integer = byteOff + sc.Utf8Len
                    If nextNet < n Then
                        Dim nsc As ScalarClass = ScalarClassifier.Classify(inside, nextNet)
                        If (nsc.Flags And ScalarClassifier.MaskPunctOrSymbol) <> 0 Then
                            Dim jNet As Integer = nextNet + nsc.NetLen
                            Dim jByte As Integer = nextByte + nsc.Utf8Len
                            Dim endByte As Integer = jByte
                            While jNet < n
                                Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                                If (jsc.Flags And ScalarClassifier.MaskPunctOrSymbol) = 0 Then Exit While
                                jNet += jsc.NetLen
                                jByte += jsc.Utf8Len
                                endByte = jByte
                            End While
                            Dim kNet As Integer = jNet
                            Dim kByte As Integer = jByte
                            While kNet < n
                                Dim ksc As ScalarClass = ScalarClassifier.Classify(inside, kNet)
                                If (ksc.Flags And ScalarClassifier.FlagCrLf) = 0 Then Exit While
                                kNet += ksc.NetLen
                                kByte += ksc.Utf8Len
                                endByte = kByte
                            End While
                            EmitMatch(result, prev, startByte, endByte)
                            net = kNet
                            byteOff = kByte
                            Continue While
                        End If
                    End If
                ElseIf (sc.Flags And ScalarClassifier.MaskPunctOrSymbol) <> 0 Then
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.MaskPunctOrSymbol) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    Dim kNet As Integer = jNet
                    Dim kByte As Integer = jByte
                    While kNet < n
                        Dim ksc As ScalarClass = ScalarClassifier.Classify(inside, kNet)
                        If (ksc.Flags And ScalarClassifier.FlagCrLf) = 0 Then Exit While
                        kNet += ksc.NetLen
                        kByte += ksc.Utf8Len
                        endByte = kByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = kNet
                    byteOff = kByte
                    Continue While
                End If

                ' Alt 4 then Alt 5/6 share ONE whitespace-run scan (the run is scanned once, then
                ' dispatched): \s*[\r\n]+  (greedy, ends at the last CR/LF in the run) is tried
                ' first; if the run has no CR/LF, \s+(?!\S) then \s+ handle it.
                If (sc.Flags And ScalarClassifier.FlagWhiteSpace) <> 0 Then
                    Dim qNet As Integer = net
                    Dim qByte As Integer = byteOff
                    Dim lastCrLfEndNet As Integer = -1
                    Dim lastCrLfEndByte As Integer = 0
                    Dim lastNet As Integer = net
                    Dim lastByte As Integer = byteOff
                    Dim runCount As Integer = 0
                    Dim qsc As ScalarClass = sc
                    While qNet < n AndAlso (qsc.Flags And ScalarClassifier.FlagWhiteSpace) <> 0
                        If (qsc.Flags And ScalarClassifier.FlagCrLf) <> 0 Then
                            lastCrLfEndNet = qNet + qsc.NetLen
                            lastCrLfEndByte = qByte + qsc.Utf8Len
                        End If
                        If runCount >= 1 Then
                            lastNet = qNet
                            lastByte = qByte
                        End If
                        qNet += qsc.NetLen
                        qByte += qsc.Utf8Len
                        runCount += 1
                        If qNet < n Then qsc = ScalarClassifier.Classify(inside, qNet)
                    End While

                    If lastCrLfEndNet >= 0 Then
                        ' \s*[\r\n]+ greedy match ends at the newline run starting at the last
                        ' newline scalar inside the whitespace run.
                        EmitMatch(result, prev, startByte, lastCrLfEndByte)
                        net = lastCrLfEndNet
                        byteOff = lastCrLfEndByte
                        Continue While
                    End If

                    ' Alt 5 then Alt 6: \s+(?!\S)  then  \s+
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
                net += sc.NetLen
                byteOff += sc.Utf8Len
            End While
            ' The scan consumed the whole slice, so byteOff is the slice's total UTF-8 byte length.
            totalBytes = byteOff
        End Sub
    End Class

    ''' <summary>
    ''' Hand-written scanner for the Whitespace pre-tokenizer regex: <c>\w+|[^\w\s]+</c>.
    ''' </summary>
    Public NotInheritable Class WordPunctPattern
        Inherits ManualPatternBase

        Public Const Canonical As String = "\w+|[^\w\s]+"

        Protected Overrides Sub Scan(inside As String, startNet As Integer, lenNet As Integer,
                                     result As List(Of MatchInfo), ByRef prev As Integer, ByRef totalBytes As Integer)
            ' net is an absolute index into <c>inside</c> bounded to the slice; byteOff stays
            ' relative to the slice start, so emitted match byte offsets are slice-relative.
            Dim n As Integer = startNet + lenNet
            Dim net As Integer = startNet
            Dim byteOff As Integer = 0
            While net < n
                Dim startByte As Integer = byteOff
                Dim sc As ScalarClass = ScalarClassifier.Classify(inside, net)
                If (sc.Flags And ScalarClassifier.FlagWord) <> 0 Then
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And ScalarClassifier.FlagWord) = 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                ElseIf (sc.Flags And ScalarClassifier.FlagWhiteSpace) = 0 Then
                    Dim jNet As Integer = net + sc.NetLen
                    Dim jByte As Integer = byteOff + sc.Utf8Len
                    Dim endByte As Integer = jByte
                    While jNet < n
                        Dim jsc As ScalarClass = ScalarClassifier.Classify(inside, jNet)
                        If (jsc.Flags And (ScalarClassifier.FlagWord Or ScalarClassifier.FlagWhiteSpace)) <> 0 Then Exit While
                        jNet += jsc.NetLen
                        jByte += jsc.Utf8Len
                        endByte = jByte
                    End While
                    EmitMatch(result, prev, startByte, endByte)
                    net = jNet
                    byteOff = jByte
                Else
                    net += sc.NetLen
                    byteOff += sc.Utf8Len
                End If
            End While
            ' The scan consumed the whole slice, so byteOff is the slice's total UTF-8 byte length.
            totalBytes = byteOff
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
