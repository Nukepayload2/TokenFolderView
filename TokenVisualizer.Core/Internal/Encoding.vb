Imports System.Linq
Imports Tokenizers.Models

Namespace Internal

    ''' <summary>
    ''' Represents the output of a <c>Tokenizer</c>. Faithful port of the Rust
    ''' <c>tokenizer::encoding::Encoding</c>.
    ''' </summary>
    Public Class Encoding

        ''' <summary>IDs produced by the <c>Tokenizer</c>.</summary>
        Public Ids As List(Of Integer)
        ''' <summary>Type of the IDs.</summary>
        Public TypeIds As List(Of Integer)
        ''' <summary>Tokens associated to each ID.</summary>
        Public Tokens As List(Of String)
        ''' <summary>Indice of the word associated to each token/ID.</summary>
        Public Words As List(Of Integer?)
        ''' <summary>Offsets of the token/ID from the NormalizedString (byte offsets).</summary>
        Public Offsets As List(Of (Integer, Integer))
        ''' <summary>Mask identifying special tokens.</summary>
        Public SpecialTokensMask As List(Of Integer)
        ''' <summary>Mask identifying padding tokens for the attention mechanism.</summary>
        Public AttentionMask As List(Of Integer)
        ''' <summary>A list of overflowing Encodings generated when we got truncated.</summary>
        Public Overflowing As List(Of Encoding)
        ''' <summary>
        ''' Ranges of tokens covered by each sequence. If this is empty we consider there is only
        ''' one sequence in this Encoding, and that it covers the entire range. Each entry is a
        ''' (start, end) token range.
        ''' </summary>
        Public SequenceRanges As Dictionary(Of Integer, (Integer, Integer))

        Public Sub New()
            Ids = New List(Of Integer)()
            TypeIds = New List(Of Integer)()
            Tokens = New List(Of String)()
            Words = New List(Of Integer?)()
            Offsets = New List(Of (Integer, Integer))()
            SpecialTokensMask = New List(Of Integer)()
            AttentionMask = New List(Of Integer)()
            Overflowing = New List(Of Encoding)()
            SequenceRanges = New Dictionary(Of Integer, (Integer, Integer))()
        End Sub

        ''' <summary>
        ''' Builds an encoding from a list of tokens, all sharing the given type id. Words are all
        ''' <c>Nothing</c>, the attention mask is all 1 and the special-tokens mask all 0.
        ''' Mirrors the Rust <c>Encoding::from_tokens</c>.
        ''' </summary>
        Public Shared Function FromTokens(tokens As IEnumerable(Of Token), typeId As Integer) As Encoding
            Dim list As List(Of Token) = tokens.ToList()
            Dim length As Integer = list.Count
            Dim e As New Encoding()
            e.Ids = list.Select(Function(t) t.Id).ToList()
            e.Tokens = list.Select(Function(t) t.Value).ToList()
            e.Offsets = list.Select(Function(t) t.Offsets).ToList()
            e.Words = Enumerable.Repeat(Of Integer?)(Nothing, length).ToList()
            e.TypeIds = Enumerable.Repeat(typeId, length).ToList()
            e.AttentionMask = Enumerable.Repeat(1, length).ToList()
            e.SpecialTokensMask = Enumerable.Repeat(0, length).ToList()
            Return e
        End Function

        ''' <summary>
        ''' Builds an encoding with only the ids populated and every other vector empty. Mirrors the
        ''' <c>Encoding::new(vec![...], vec![], ...)</c> construction used by the padding tests.
        ''' </summary>
        Public Shared Function FromIds(ids As IEnumerable(Of Integer)) As Encoding
            Dim e As New Encoding()
            e.Ids = ids.ToList()
            Return e
        End Function

        ''' <summary>
        ''' Builds an encoding from (id, token, offsets, word, type_id) tuples, mirroring the Rust
        ''' <c>FromIterator&lt;(u32, String, (usize, usize), Option&lt;u32&gt;, u32)&gt;</c> impl:
        ''' special mask 0, attention mask 1.
        ''' </summary>
        Public Shared Function FromTuples(items As IEnumerable(Of (Integer, String, (Integer, Integer), Integer?, Integer))) As Encoding
            Dim e As New Encoding()
            For Each item In items
                e.Ids.Add(item.Item1)
                e.Tokens.Add(item.Item2)
                e.Offsets.Add(item.Item3)
                e.TypeIds.Add(item.Item5)
                e.Words.Add(item.Item4)
                e.SpecialTokensMask.Add(0)
                e.AttentionMask.Add(1)
            Next
            Return e
        End Function

        ''' <summary>Whether this Encoding is empty.</summary>
        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return Ids.Count = 0
            End Get
        End Property

        ''' <summary>Total length of this Encoding.</summary>
        Public ReadOnly Property Length As Integer
            Get
                Return Ids.Count
            End Get
        End Property

        ''' <summary>Alias for <see cref="Length"/>.</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return Ids.Count
            End Get
        End Property

        ''' <summary>Number of sequences combined in this Encoding.</summary>
        Public ReadOnly Property NSequences As Integer
            Get
                If SequenceRanges.Count = 0 Then
                    Return 1
                End If
                Return SequenceRanges.Count
            End Get
        End Property

        ''' <summary>Sets the given sequence id for the whole range of tokens contained in this Encoding.</summary>
        Public Sub SetSequenceId(sequenceId As Integer)
            SequenceRanges(sequenceId) = (0, Me.Length)
        End Sub

        ''' <summary>
        ''' Returns the token range to target for the given sequence id, defaulting to the entire
        ''' encoding when the id is not present.
        ''' </summary>
        Public Function SequenceRange(sequenceId As Integer) As (Integer, Integer)
            Dim range As (Integer, Integer)
            If SequenceRanges.TryGetValue(sequenceId, range) Then Return range
            Return (0, Me.Length)
        End Function

        ''' <summary>
        ''' Returns one entry per token, <c>Nothing</c> for tokens not covered by any sequence and
        ''' <c>Some(seq_id)</c> otherwise. Mirrors the Rust <c>get_sequence_ids</c>.
        ''' </summary>
        Public Function GetSequenceIds() As List(Of Integer?)
            Dim sequences As New List(Of Integer?)()
            For i As Integer = 0 To Me.Length - 1
                sequences.Add(Nothing)
            Next
            For seqId As Integer = 0 To Me.NSequences - 1
                Dim range As (Integer, Integer) = Me.SequenceRange(seqId)
                For i As Integer = range.Item1 To range.Item2 - 1
                    If i >= 0 AndAlso i < sequences.Count Then
                        sequences(i) = seqId
                    End If
                Next
            Next
            Return sequences
        End Function

        ''' <summary>Returns the index of the sequence containing the given token, if any.</summary>
        Public Function TokenToSequence(token As Integer) As Integer?
            If token > Me.Length Then
                Return Nothing
            ElseIf SequenceRanges.Count = 0 Then
                Return 0
            Else
                For Each kv In SequenceRanges
                    If token >= kv.Value.Item1 AndAlso token < kv.Value.Item2 Then
                        Return kv.Key
                    End If
                Next
                Return Nothing
            End If
        End Function

        ''' <summary>
        ''' Returns the encoded tokens corresponding to the word at the given index in the input
        ''' sequence, with the form (start_token, end_token + 1).
        ''' </summary>
        Public Function WordToTokens(word As Integer, sequenceId As Integer) As (Integer, Integer)?
            Dim seqRange As (Integer, Integer) = Me.SequenceRange(sequenceId)
            If seqRange.Item1 < 0 OrElse seqRange.Item1 > seqRange.Item2 OrElse seqRange.Item2 > Me.Words.Count Then
                Return Nothing
            End If

            Dim start As Integer? = Nothing
            Dim endIdx As Integer? = Nothing
            For i As Integer = 0 To seqRange.Item2 - seqRange.Item1 - 1
                Dim w As Integer? = Me.Words(seqRange.Item1 + i)
                ' Mirrors `take_while(|(_, w)| **w <= Some(word))`: stop at the first word > word.
                If w.HasValue AndAlso w.Value > word Then Exit For
                If w.HasValue AndAlso w.Value = word Then
                    If Not start.HasValue OrElse i < start.Value Then start = i
                    If Not endIdx.HasValue OrElse i >= endIdx.Value Then endIdx = i + 1
                End If
            Next

            If start.HasValue AndAlso endIdx.HasValue Then
                Return (seqRange.Item1 + start.Value, seqRange.Item1 + endIdx.Value)
            Else
                Return Nothing
            End If
        End Function

        ''' <summary>Returns the offsets of the word at the given index in the input sequence.</summary>
        Public Function WordToChars(word As Integer, sequenceId As Integer) As (Integer, Integer)?
            Dim wt As (Integer, Integer)? = WordToTokens(word, sequenceId)
            If Not wt.HasValue Then Return Nothing
            Dim start As Integer = wt.Value.Item1
            Dim endIdx As Integer = wt.Value.Item2
            If endIdx = 0 Then Return Nothing
            If start < 0 OrElse endIdx - 1 >= Me.Offsets.Count Then Return Nothing
            Return (Me.Offsets(start).Item1, Me.Offsets(endIdx - 1).Item2)
        End Function

        ''' <summary>Returns the offsets of the token at the given index, along with its sequence id.</summary>
        Public Function TokenToChars(token As Integer) As (Integer, (Integer, Integer))?
            Dim seq As Integer? = TokenToSequence(token)
            If Not seq.HasValue Then Return Nothing
            If token < 0 OrElse token >= Me.Offsets.Count Then Return Nothing
            Return (seq.Value, Me.Offsets(token))
        End Function

        ''' <summary>Returns the word that contains the token at the given index, along with its sequence id.</summary>
        Public Function TokenToWord(token As Integer) As (Integer, Integer)?
            Dim seq As Integer? = TokenToSequence(token)
            If Not seq.HasValue Then Return Nothing
            If token < 0 OrElse token >= Me.Words.Count Then Return Nothing
            Dim w As Integer? = Me.Words(token)
            If Not w.HasValue Then Return Nothing
            Return (seq.Value, w.Value)
        End Function

        ''' <summary>Returns the token that contains the given char.</summary>
        Public Function CharToToken(pos As Integer, sequenceId As Integer) As Integer?
            Dim seqRange As (Integer, Integer) = Me.SequenceRange(sequenceId)
            If seqRange.Item1 < 0 OrElse seqRange.Item1 > seqRange.Item2 OrElse seqRange.Item2 > Me.Offsets.Count Then
                Return Nothing
            End If
            For i As Integer = seqRange.Item1 To seqRange.Item2 - 1
                Dim o As (Integer, Integer) = Me.Offsets(i)
                If pos >= o.Item1 AndAlso pos < o.Item2 Then Return i
            Next
            Return Nothing
        End Function

        ''' <summary>Returns the word that contains the given char.</summary>
        Public Function CharToWord(pos As Integer, sequenceId As Integer) As Integer?
            Dim token As Integer? = CharToToken(pos, sequenceId)
            If Not token.HasValue Then Return Nothing
            Dim tw As (Integer, Integer)? = TokenToWord(token.Value)
            If Not tw.HasValue Then Return Nothing
            Return tw.Value.Item2
        End Function

        ''' <summary>
        ''' Truncates the current <c>Encoding</c>. Mirrors the Rust
        ''' <c>Encoding::truncate</c> exactly, including the overflowing behavior.
        ''' </summary>
        Public Sub Truncate(maxLen As Integer, stride As Integer, direction As TruncationDirection)
            Dim encodingLen As Integer = Ids.Count
            If maxLen >= encodingLen Then Return

            If maxLen = 0 Then
                Dim o As Encoding = Me.Clone()
                Me.Ids.Clear()
                Me.TypeIds.Clear()
                Me.Tokens.Clear()
                Me.Words.Clear()
                Me.Offsets.Clear()
                Me.SpecialTokensMask.Clear()
                Me.AttentionMask.Clear()
                Me.Overflowing = New List(Of Encoding)()
                Me.SequenceRanges = New Dictionary(Of Integer, (Integer, Integer))()
                Me.Overflowing.Add(o)
                Return
            End If

            If stride >= maxLen Then
                Throw New ArgumentException($"`stride` must be strictly less than `max_len={maxLen}` (note that `max_len` may be shorter than the max length of the original model, as it subtracts the number of special characters)")
            End If

            ' When truncating, we lose the `sequence_ranges` information.
            Me.SequenceRanges.Clear()

            Dim offset As Integer = maxLen - stride
            Dim partsRanges As New List(Of (Integer, Integer))()
            If direction = TruncationDirection.Right Then
                Dim isLast As Boolean = False
                Dim start As Integer = 0
                While start < encodingLen
                    If Not isLast Then
                        Dim partEnd As Integer = Math.Min(start + maxLen, encodingLen)
                        isLast = (partEnd = encodingLen)
                        partsRanges.Add((start, partEnd))
                    End If
                    start += offset
                End While
            Else
                Dim isLast As Boolean = False
                Dim partEnd As Integer = encodingLen - 1
                While partEnd >= 0
                    Dim partStop As Integer = partEnd + 1
                    Dim start As Integer = Math.Max(partStop - maxLen, 0)
                    If start < partStop AndAlso Not isLast Then
                        isLast = (start = 0)
                        partsRanges.Add((start, partStop))
                    End If
                    partEnd -= offset
                End While
            End If

            Dim newEncoding As Encoding = Me.BuildSlice(partsRanges(0).Item1, partsRanges(0).Item2)
            For i As Integer = 1 To partsRanges.Count - 1
                newEncoding.Overflowing.Add(Me.BuildSlice(partsRanges(i).Item1, partsRanges(i).Item2))
            Next

            Me.Ids = newEncoding.Ids
            Me.TypeIds = newEncoding.TypeIds
            Me.Tokens = newEncoding.Tokens
            Me.Words = newEncoding.Words
            Me.Offsets = newEncoding.Offsets
            Me.SpecialTokensMask = newEncoding.SpecialTokensMask
            Me.AttentionMask = newEncoding.AttentionMask
            Me.Overflowing = newEncoding.Overflowing
            Me.SequenceRanges = newEncoding.SequenceRanges
        End Sub

        ''' <summary>Merges this encoding with <paramref name="other"/> (not growing offsets).</summary>
        Public Sub Merge(other As Encoding)
            Me.MergeWith(other, False)
        End Sub

        ''' <summary>
        ''' Merges ourself with the given <c>Encoding</c>, in place. Mirrors the Rust
        ''' <c>Encoding::merge_with</c>, including overflowing recombination and the optional
        ''' growing-offset shift.
        ''' </summary>
        Public Sub MergeWith(other As Encoding, growingOffsets As Boolean)
            ' Handle merging the overflowing parts too: combine them all.
            Dim overflowings As New List(Of Encoding)()

            ' 1. All our overflowings with all the others.
            For Each self_o In Me.Overflowing
                ' 1. The pair itself.
                Dim n1 As Encoding = self_o.Clone()
                n1.MergeWith(other, growingOffsets)
                overflowings.Add(n1)
                ' 2. Its overflowings (this should rarely happen...).
                For Each other_o In other.Overflowing
                    Dim n2 As Encoding = self_o.Clone()
                    n2.MergeWith(other_o, growingOffsets)
                    overflowings.Add(n2)
                Next
            Next
            ' 2. Ourself with all the other overflowings (this should rarely happen too...).
            For Each other_o In other.Overflowing
                Dim n3 As Encoding = Me.Clone()
                n3.MergeWith(other_o, growingOffsets)
                overflowings.Add(n3)
            Next

            ' Finish by merging ourself with the other encoding.
            Dim originalSelfLen As Integer = Me.Length ' Must be before any modification to Me.Ids

            For Each kv In other.SequenceRanges
                Me.SequenceRanges(kv.Key) = (originalSelfLen + kv.Value.Item1, originalSelfLen + kv.Value.Item2)
            Next
            Me.Ids.AddRange(other.Ids)
            Me.TypeIds.AddRange(other.TypeIds)
            Me.Tokens.AddRange(other.Tokens)
            Me.Words.AddRange(other.Words)

            Dim startingOffset As Integer = 0
            If growingOffsets AndAlso Me.Offsets.Count > 0 Then
                startingOffset = Me.Offsets(Me.Offsets.Count - 1).Item2
            End If
            For Each o As (Integer, Integer) In other.Offsets
                Me.Offsets.Add((o.Item1 + startingOffset, o.Item2 + startingOffset))
            Next
            Me.SpecialTokensMask.AddRange(other.SpecialTokensMask)
            Me.AttentionMask.AddRange(other.AttentionMask)
            Me.Overflowing = overflowings
        End Sub

        ''' <summary>
        ''' Merges all the given encodings together, in order. Mirrors the Rust
        ''' <c>Encoding::merge</c> (tokenizer/encoding.rs).
        ''' </summary>
        Public Shared Function Merge(encodings As List(Of Encoding), growingOffsets As Boolean) As Encoding
            Dim result As New Encoding()
            For Each subEncoding As Encoding In encodings
                result.MergeWith(subEncoding, growingOffsets)
            Next
            Return result
        End Function

        ''' <summary>
        ''' Pads this encoding (and its overflowings) to the target length. Mirrors the Rust
        ''' <c>Encoding::pad</c>.
        ''' </summary>
        Public Sub Pad(targetLength As Integer, padId As Integer, padTypeId As Integer, padToken As String, direction As PaddingDirection)
            ' Dispatch call to all the overflowings first.
            For Each encoding In Me.Overflowing
                encoding.Pad(targetLength, padId, padTypeId, padToken, direction)
            Next

            ' Then check if we should pad ourself.
            If Ids.Count >= targetLength Then Return
            Dim padLength As Integer = targetLength - Ids.Count

            If direction = PaddingDirection.Left Then
                Dim newIds As New List(Of Integer)()
                newIds.AddRange(Enumerable.Repeat(padId, padLength))
                newIds.AddRange(Ids)
                Ids = newIds

                Dim newTypeIds As New List(Of Integer)()
                newTypeIds.AddRange(Enumerable.Repeat(padTypeId, padLength))
                newTypeIds.AddRange(TypeIds)
                TypeIds = newTypeIds

                Dim newTokens As New List(Of String)()
                newTokens.AddRange(Enumerable.Repeat(padToken, padLength))
                newTokens.AddRange(Tokens)
                Tokens = newTokens

                Dim newWords As New List(Of Integer?)()
                newWords.AddRange(Enumerable.Repeat(Of Integer?)(Nothing, padLength))
                newWords.AddRange(Words)
                Words = newWords

                Dim newAttention As New List(Of Integer)()
                newAttention.AddRange(Enumerable.Repeat(0, padLength))
                newAttention.AddRange(AttentionMask)
                AttentionMask = newAttention

                Dim newSpecial As New List(Of Integer)()
                newSpecial.AddRange(Enumerable.Repeat(1, padLength))
                newSpecial.AddRange(SpecialTokensMask)
                SpecialTokensMask = newSpecial

                Dim newOffsets As New List(Of (Integer, Integer))()
                newOffsets.AddRange(Enumerable.Repeat(Of (Integer, Integer))((0, 0), padLength))
                newOffsets.AddRange(Offsets)
                Offsets = newOffsets

                Dim newRanges As New Dictionary(Of Integer, (Integer, Integer))()
                For Each kv In SequenceRanges
                    newRanges(kv.Key) = (kv.Value.Item1 + padLength, kv.Value.Item2 + padLength)
                Next
                SequenceRanges = newRanges
            Else
                Ids.AddRange(Enumerable.Repeat(padId, padLength))
                TypeIds.AddRange(Enumerable.Repeat(padTypeId, padLength))
                Tokens.AddRange(Enumerable.Repeat(padToken, padLength))
                Words.AddRange(Enumerable.Repeat(Of Integer?)(Nothing, padLength))
                AttentionMask.AddRange(Enumerable.Repeat(0, padLength))
                SpecialTokensMask.AddRange(Enumerable.Repeat(1, padLength))
                Offsets.AddRange(Enumerable.Repeat(Of (Integer, Integer))((0, 0), padLength))
            End If
        End Sub

        ''' <summary>Deep-clones this encoding, including the overflowing list.</summary>
        Public Function Clone() As Encoding
            Dim e As New Encoding()
            e.Ids = New List(Of Integer)(Ids)
            e.TypeIds = New List(Of Integer)(TypeIds)
            e.Tokens = New List(Of String)(Tokens)
            e.Words = New List(Of Integer?)(Words)
            e.Offsets = New List(Of (Integer, Integer))(Offsets)
            e.SpecialTokensMask = New List(Of Integer)(SpecialTokensMask)
            e.AttentionMask = New List(Of Integer)(AttentionMask)
            e.Overflowing = Overflowing.Select(Function(x) x.Clone()).ToList()
            e.SequenceRanges = New Dictionary(Of Integer, (Integer, Integer))(SequenceRanges)
            Return e
        End Function

        ''' <summary>Builds a fresh encoding from the token range [start, stop) of this one.</summary>
        Private Function BuildSlice(start As Integer, [stop] As Integer) As Encoding
            Dim e As New Encoding()
            e.Ids = Ids.GetRange(start, [stop] - start)
            e.TypeIds = TypeIds.GetRange(start, [stop] - start)
            e.Tokens = Tokens.GetRange(start, [stop] - start)
            e.Words = Words.GetRange(start, [stop] - start)
            e.Offsets = Offsets.GetRange(start, [stop] - start)
            e.SpecialTokensMask = SpecialTokensMask.GetRange(start, [stop] - start)
            e.AttentionMask = AttentionMask.GetRange(start, [stop] - start)
            e.Overflowing = New List(Of Encoding)()
            e.SequenceRanges = New Dictionary(Of Integer, (Integer, Integer))()
            Return e
        End Function

    End Class

End Namespace
