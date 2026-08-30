Imports System.Linq
Imports Tokenizers.Models

Namespace Internal

    ''' <summary>
    ''' A subpart of a <see cref="PreTokenizedString"/>, holding the underlying
    ''' <see cref="NormalizedString"/> as well as the optional tokens associated to it. Mirrors the
    ''' Rust <c>PreTokenizedString::Split</c>.
    ''' </summary>
    Public Class Split
        ''' <summary>The underlying <see cref="NormalizedString"/> for this split.</summary>
        Public Normalized As NormalizedString
        ''' <summary>Optional tokens attached to this split (<c>Nothing</c> when not yet tokenized).</summary>
        Public Tokens As List(Of Token)

        Public Sub New(normalized As NormalizedString, tokens As List(Of Token))
            Me.Normalized = normalized
            Me.Tokens = tokens
        End Sub

        Public Shared Function FromNormalizedString(normalized As NormalizedString) As Split
            Return New Split(normalized, Nothing)
        End Function

        Public Shared Function From(normalized As NormalizedString, tokens As List(Of Token)) As Split
            Return New Split(normalized, tokens)
        End Function
    End Class

    ''' <summary>
    ''' Holds a string to be pre-tokenized as a list of <see cref="Split"/>s. Mirrors the Rust
    ''' <c>PreTokenizedString</c> for the parts needed by pre-tokenizers (P7 adds
    ''' <c>tokenize</c>/<c>into_encoding</c>).
    ''' </summary>
    Public Class PreTokenizedString
        ''' <summary>The original (un-normalized) input string.</summary>
        Public Original As String
        ''' <summary>The current list of splits.</summary>
        Public Splits As List(Of Split)

        Public Shared Function FromString(text As String) As PreTokenizedString
            Return FromNormalizedString(NormalizedString.FromString(text))
        End Function

        ''' <summary>Builds a <see cref="PreTokenizedString"/> from an existing <see cref="NormalizedString"/>.</summary>
        Public Shared Function FromNormalizedString(normalized As NormalizedString) As PreTokenizedString
            Dim pts As New PreTokenizedString()
            pts.Original = normalized.Original
            pts.Splits = New List(Of Split) From {Split.FromNormalizedString(normalized)}
            Return pts
        End Function

        ''' <summary>
        ''' Splits every split that has no attached tokens using the given pattern, with the given
        ''' delimiter behavior; splits that already carry tokens pass through unchanged. Empty
        ''' pieces are dropped.
        ''' </summary>
        Public Sub SplitBy(pattern As Pattern, behavior As SplitDelimiterBehavior)
            Me.SplitByFunction(
                Function(i As Integer, normalized As NormalizedString) As IEnumerable(Of NormalizedString)
                    Return normalized.Split(pattern, behavior)
                End Function)
        End Sub

        ''' <summary>
        ''' Splits every split that has no attached tokens using the given closure; splits that
        ''' already carry tokens pass through unchanged. Empty pieces are dropped.
        ''' <paramref name="splitFn"/> receives the split index and the split's
        ''' <see cref="NormalizedString"/>, and returns the produced pieces.
        ''' </summary>
        Public Sub SplitByFunction(splitFn As Func(Of Integer, NormalizedString, IEnumerable(Of NormalizedString)))
            Dim newSplits As New List(Of Split)()
            For i As Integer = 0 To Me.Splits.Count - 1
                Dim originalSplit As Split = Me.Splits(i)
                If originalSplit.Tokens IsNot Nothing Then
                    newSplits.Add(originalSplit)
                    Continue For
                End If
                Dim pieces As IEnumerable(Of NormalizedString) = splitFn(i, originalSplit.Normalized)
                If pieces IsNot Nothing Then
                    For Each piece In pieces
                        If piece IsNot Nothing AndAlso Not piece.IsEmpty() Then
                            newSplits.Add(Split.FromNormalizedString(piece))
                        End If
                    Next
                End If
            Next
            Me.Splits = newSplits
        End Sub

        ''' <summary>
        ''' Fuses a run of isolated manual-pattern splits into a single pass. Each pattern's
        ''' <c>FindMatches</c> is run on the previous partition's pieces, so a later pattern can
        ''' never join two pieces that an earlier pattern separated (the earlier boundaries act as
        ''' hard barriers), and the root <see cref="NormalizedString"/> is sliced exactly once per
        ''' final piece. This produces byte-identical splits to running the same Isolated splits
        ''' sequentially via <see cref="SplitByFunction"/>, but avoids materializing the
        ''' intermediate <see cref="NormalizedString"/> pieces and rebuilding the splits list
        ''' between patterns (the S2 pre-tokenization bottleneck).
        '''
        ''' Semantics preserved: splits that already carry tokens pass through unchanged, and
        ''' empty pieces are dropped, exactly like <see cref="SplitByFunction"/>.
        ''' </summary>
        Friend Sub FuseIsolatedSplits(patterns As List(Of Pattern))
            Dim newSplits As New List(Of Split)()
            For Each split As Split In Me.Splits
                If split.Tokens IsNot Nothing Then
                    newSplits.Add(split)
                    Continue For
                End If

                Dim ns As NormalizedString = split.Normalized
                Dim text As String = ns.Get
                Dim utf8Len As Integer = ns.Len()
                Dim ranges As New List(Of (Integer, Integer))(1)
                ranges.Add((0, utf8Len))

                ' One scratch match list reused across every per-piece scan: the scanner writes
                ' into it and it is cleared by FindMatchesInto, so no List(Of MatchInfo) is
                ' allocated per piece (a dominant per-piece cost on high-piece-density corpora).
                Dim scratch As New List(Of MatchInfo)()

                For Each p As Pattern In patterns
                    Dim nextRanges As New List(Of (Integer, Integer))()
                    For Each r In ranges
                        Dim b1 As Integer = r.Item1
                        Dim b2 As Integer = r.Item2
                        If b2 <= b1 Then Continue For
                        ' Extract the piece's normalized substring via the cached boundary index
                        ' (binary search), then run the next pattern exactly as the sequential
                        ' path does (FindMatches on that substring), offsetting matches back to the
                        ' root normalized byte referential.
                        Dim n1 As Integer = ns.ByteToNetIndexCached(b1)
                        Dim n2 As Integer = ns.ByteToNetIndexCached(b2)
                        If n2 <= n1 Then Continue For
                        Dim seg As String = text.Substring(n1, n2 - n1)
                        p.FindMatchesInto(seg, scratch)
                        For i As Integer = 0 To scratch.Count - 1
                            Dim m As MatchInfo = scratch(i)
                            Dim mb1 As Integer = m.Start
                            Dim mb2 As Integer = m.End
                            If mb2 > mb1 Then
                                nextRanges.Add((b1 + mb1, b1 + mb2))
                            End If
                        Next
                    Next
                    ranges = nextRanges
                Next

                For Each r In ranges
                    If r.Item2 > r.Item1 Then
                        Dim slice As NormalizedString = ns.Slice(New OffsetRange(False, r.Item1, r.Item2))
                        newSplits.Add(Split.FromNormalizedString(slice))
                    End If
                Next
            Next
            Me.Splits = newSplits
        End Sub

        ''' <summary>
        ''' Applies <paramref name="normalizer"/> to every split that has no attached tokens.
        ''' </summary>
        Public Sub Normalize(normalizer As Action(Of NormalizedString))
            For Each s In Me.Splits
                If s.Tokens Is Nothing Then
                    normalizer(s.Normalized)
                End If
            Next
        End Sub

        ''' <summary>
        ''' Returns the current splits as (text, offsets, tokens) triplets. Offsets are either in
        ''' the original or the normalized referential, and either byte or scalar based.
        ''' </summary>
        Public Function GetSplits(offsetRef As OffsetReferential, offsetType As OffsetType) As List(Of (Text As String, Offsets As (Integer, Integer), Tokens As List(Of Token)))
            Dim result As New List(Of (Text As String, Offsets As (Integer, Integer), Tokens As List(Of Token)))()
            Dim offset As Integer = 0
            For Each split In Me.Splits
                Dim offs As (Integer, Integer)
                If offsetRef = OffsetReferential.Original Then
                    offs = split.Normalized.OffsetsOriginal()
                Else
                    Dim len As Integer = split.Normalized.Len()
                    offset += len
                    offs = (offset - len, offset)
                End If
                If offsetType = OffsetType.Char Then
                    offs = (ByteOffsetToCharIndex(Me.Original, offs.Item1), ByteOffsetToCharIndex(Me.Original, offs.Item2))
                End If
                result.Add((split.Normalized.Get, offs, split.Tokens))
            Next
            Return result
        End Function

        ''' <summary>Converts a UTF-8 byte offset in <paramref name="text"/> to a scalar (char) index.</summary>
        Private Shared Function ByteOffsetToCharIndex(text As String, byteOffset As Integer) As Integer
            Dim totalBytes As Integer = Utf8Helpers.Utf8Length(text)
            If byteOffset <= 0 Then Return 0
            If byteOffset >= totalBytes Then Return Utf8Helpers.ScalarCount(text)
            Dim byteAcc As Integer = 0
            Dim charIdx As Integer = 0
            For Each sc In Utf8Helpers.EnumerateScalars(text)
                If byteOffset <= byteAcc Then Return charIdx
                byteAcc += sc.Utf8Len
                charIdx += 1
            Next
            Return charIdx
        End Function

        ''' <summary>
        ''' Tokenizes all the splits that do not have attached tokens, using the provided
        ''' <c>tokenize</c> function. Mirrors the Rust <c>PreTokenizedString::tokenize</c>.
        ''' </summary>
        Public Sub Tokenize(tokenizeFn As Func(Of NormalizedString, List(Of Token)))
            For Each split In Me.Splits
                If split.Tokens Is Nothing Then
                    split.Tokens = tokenizeFn(split.Normalized)
                End If
            Next
        End Sub

        ''' <summary>
        ''' Count-only twin of <see cref="Tokenize"/>. Returns the total number of tokens the splits
        ''' would produce without building any <c>List(Of Token)</c>: splits that already carry
        ''' attached tokens contribute their attached token count; the remaining splits contribute
        ''' <c>countFn(split.Normalized)</c>. Because <see cref="Tokenize"/> calls
        ''' <c>tokenizeFn</c> once per untokenized split and reads <c>Tokens.Count</c> for attached
        ''' splits, this returns exactly the same total as <c>Tokenize(...)</c> would, provided
        ''' <c>countFn(n) = tokenizeFn(n).Count</c>. The count-only fast path uses
        ''' <c>countFn = Model.CountTokens</c>.
        ''' </summary>
        Public Function TokenizeCount(countFn As Func(Of NormalizedString, Integer)) As Integer
            Dim total As Integer = 0
            For Each split In Me.Splits
                If split.Tokens Is Nothing Then
                    total += countFn(split.Normalized)
                Else
                    total += split.Tokens.Count
                End If
            Next
            Return total
        End Function

        ''' <summary>
        ''' Early-exits when <paramref name="maxTokens"/> have been produced,
        ''' <see cref="TruncationDirection"/> aware. Mirrors the Rust
        ''' <c>PreTokenizedString::tokenize_with_limit</c>: Left drains leading splits,
        ''' Right truncates trailing splits.
        ''' </summary>
        Public Sub TokenizeWithLimit(tokenizeFn As Func(Of NormalizedString, List(Of Token)), maxTokens As Integer, direction As TruncationDirection)
            Dim totalTokens As Integer = 0
            If direction = TruncationDirection.Left Then
                Dim firstTokenizedIdx As Integer = Me.Splits.Count
                For i As Integer = Me.Splits.Count - 1 To 0 Step -1
                    Dim split As Split = Me.Splits(i)
                    If split.Tokens IsNot Nothing Then
                        totalTokens += split.Tokens.Count
                        firstTokenizedIdx = i
                        Continue For
                    End If
                    Dim tokens As List(Of Token) = tokenizeFn(split.Normalized)
                    totalTokens += tokens.Count
                    split.Tokens = tokens
                    firstTokenizedIdx = i
                    If totalTokens >= maxTokens Then Exit For
                Next
                Me.Splits.RemoveRange(0, firstTokenizedIdx)
            Else
                Dim lastTokenizedIdx As Integer = 0
                For i As Integer = 0 To Me.Splits.Count - 1
                    Dim split As Split = Me.Splits(i)
                    If split.Tokens IsNot Nothing Then
                        totalTokens += split.Tokens.Count
                        lastTokenizedIdx = i + 1
                        Continue For
                    End If
                    Dim tokens As List(Of Token) = tokenizeFn(split.Normalized)
                    totalTokens += tokens.Count
                    split.Tokens = tokens
                    lastTokenizedIdx = i + 1
                    If totalTokens >= maxTokens Then Exit For
                Next
                Me.Splits.RemoveRange(lastTokenizedIdx, Me.Splits.Count - lastTokenizedIdx)
            End If
        End Sub

        ''' <summary>
        ''' Transforms the current <c>PreTokenizedString</c> into an <c>Encoding</c>. If a
        ''' <paramref name="wordIdx"/> is provided, any word in the generated encoding will be set
        ''' to this value; otherwise each split's tokens get the split index as their word id.
        ''' Mirrors the Rust <c>PreTokenizedString::into_encoding</c>.
        ''' </summary>
        Public Function IntoEncoding(wordIdx As Integer?, typeId As Integer, offsetType As OffsetType) As Encoding
            If Me.Splits.Count = 0 Then
                Return New Encoding()
            End If
            For Each s In Me.Splits
                If s.Tokens Is Nothing Then
                    Throw New InvalidOperationException("Split has not been tokenized, call Tokenize first")
                End If
            Next

            Dim offsetConverter As BytesToCharOffsetConverter = Nothing
            Dim totalTokenCount As Integer = 0
            For Each s As Split In Me.Splits
                totalTokenCount += s.Tokens.Count
            Next
            If offsetType = OffsetType.Char Then
                offsetConverter = New BytesToCharOffsetConverter(Me.Original)
            ElseIf offsetType = OffsetType.None Then
                Dim tuples As New List(Of (Integer, String, (Integer, Integer), Integer?, Integer))(totalTokenCount)
                For Each s In Me.Splits
                    For Each token In s.Tokens
                        tuples.Add((token.Id, "", (0, 0), Nothing, typeId))
                    Next
                Next
                Return Encoding.FromTuples(tuples)
            End If

            Dim items As New List(Of (Integer, String, (Integer, Integer), Integer?, Integer))(totalTokenCount)
            For idx As Integer = 0 To Me.Splits.Count - 1
                Dim split As Split = Me.Splits(idx)
                Dim normalized As NormalizedString = split.Normalized
                Dim offsets As (Integer, Integer) = normalized.OffsetsOriginal()
                For Each token In split.Tokens
                    Dim tokenOffsets As (Integer, Integer) = token.Offsets
                    Dim converted As (Integer, Integer)? = normalized.ConvertOffsets(New OffsetRange(False, token.Offsets.Item1, token.Offsets.Item2))
                    If converted.HasValue Then
                        tokenOffsets = (offsets.Item1 + converted.Value.Item1, offsets.Item1 + converted.Value.Item2)
                    Else
                        tokenOffsets = token.Offsets
                    End If

                    If offsetConverter IsNot Nothing Then
                        Dim conv As (Integer, Integer)? = offsetConverter.Convert(tokenOffsets)
                        If conv.HasValue Then tokenOffsets = conv.Value
                    End If

                    Dim word As Integer? = If(wordIdx.HasValue, wordIdx, idx)
                    items.Add((token.Id, token.Value, tokenOffsets, word, typeId))
                Next
            Next
            Return Encoding.FromTuples(items)
        End Function
    End Class

    ''' <summary>
    ''' Converts UTF-8 byte offsets to scalar (char) offsets for the original sequence. Mirrors the
    ''' Rust <c>BytesToCharOffsetConverter</c>.
    ''' </summary>
    Public NotInheritable Class BytesToCharOffsetConverter
        Private ReadOnly _map As Dictionary(Of Integer, Integer)

        Public Sub New(sequence As String)
            _map = New Dictionary(Of Integer, Integer)()
            Dim charIdx As Integer = 0
            For Each sc In Utf8Helpers.EnumerateScalars(sequence)
                For n As Integer = 0 To sc.Utf8Len - 1
                    _map(sc.Utf8Start + n) = charIdx
                Next
                charIdx += 1
            Next
        End Sub

        ''' <summary>Converts a byte-offset range to a char-offset range, or Nothing when the start is out of range.</summary>
        Public Function Convert(offsets As (Integer, Integer)) As (Integer, Integer)?
            Dim start As Integer
            If _map.TryGetValue(offsets.Item1, start) Then
                Dim [end] As Integer
                If _map.TryGetValue(offsets.Item2, [end]) Then
                    Return (start, [end])
                Else
                    ' If we reached the end, `end` is not in the map, but the one just before should be.
                    Dim last As Integer
                    If Not _map.TryGetValue(offsets.Item2 - 1, last) Then
                        last = start + 1
                    End If
                    Return (start, last + 1)
                End If
            End If
            Return Nothing
        End Function
    End Class

End Namespace
