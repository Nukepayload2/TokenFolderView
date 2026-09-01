Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports Tokenizers.Decoders
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors
Imports Tokenizers.Serialization

    ''' <summary>
    ''' The tokenization pipeline facade. Faithful port of the Rust <c>TokenizerImpl</c>
    ''' (tokenizer/mod.rs): normalization, pre-tokenization, model tokenization, truncation,
    ''' post-processing and padding. Supports tokenizer.json serialization/deserialization.
    ''' </summary>
    Public NotInheritable Class Tokenizer

        ''' <summary>The underlying model (one of the four model implementations).</summary>
        Public ReadOnly Property Model As IModel

        ''' <summary>The normalizer applied to the raw text (optional).</summary>
        Public Property Normalizer As INormalizer

        ''' <summary>The pre-tokenizer applied after normalization (optional).</summary>
        Public Property PreTokenizer As IPreTokenizer

        ''' <summary>The post-processor applied after tokenization (optional).</summary>
        Public Property PostProcessor As IPostProcessor

        ''' <summary>The decoder used when decoding ids back to text (optional).</summary>
        Public Property Decoder As IDecoder

        ''' <summary>The added-vocabulary (tokens added on top of the model).</summary>
        Public ReadOnly Property AddedVocabulary As AddedVocabulary

        ''' <summary>The truncation parameters (optional).</summary>
        Public Property Truncation As TruncationParams

        ''' <summary>The padding parameters (optional).</summary>
        Public Property Padding As PaddingParams

        ''' <summary>Whether added special tokens should be kept inside the text when encoding.</summary>
        Public Property EncodeSpecialTokens As Boolean
            Get
                Return AddedVocabulary.GetEncodeSpecialTokens()
            End Get
            Set(value As Boolean)
                AddedVocabulary.SetEncodeSpecialTokens(value)
            End Set
        End Property

        Public Sub New(model As IModel)
            Me.Model = model
            Me.AddedVocabulary = New AddedVocabulary()
            Me.AddedVocabulary.ModelVocab = model.GetVocab()
            Me.AddedVocabulary.Normalizer = Nothing
            Me._countVisitor = New ThreadLocal(Of FusedRangeCountVisitor)(Function() New FusedRangeCountVisitor())
            Me._rangeCounter = New ThreadLocal(Of RangeCountingVisitor)(Function() New RangeCountingVisitor())
        End Sub

        ' M8: reusable per-thread streaming consumers of the fused-range count path. The count
        ' visitor maps+counts each streamed range (state is fields, not captured locals, so the
        ' instance is zero-allocation reusable across calls); the range counter is the profile's
        ' FusedSplit-stage attribution visitor (it only counts ranges, no map/count). Each is a
        ' ThreadLocal so concurrent EncodeCount / ProfileCountStages on the same Tokenizer stay
        ' isolated and the instance is reused across calls on the same thread.
        Private ReadOnly _countVisitor As ThreadLocal(Of FusedRangeCountVisitor)
        Private ReadOnly _rangeCounter As ThreadLocal(Of RangeCountingVisitor)

        ''' <summary>
        ''' Counts final fused ranges without mapping/counting them. Used only by
        ''' <see cref="ProfileCountStages"/> to attribute the FusedSplit-stage allocation of the M8
        ''' streaming path (the real count pass runs the count visitor separately).
        ''' </summary>
        Private NotInheritable Class RangeCountingVisitor
            Implements IFusedRangeVisitor

            Public Count As Integer

            Public Sub BeginSplit(normalized As NormalizedString) Implements IFusedRangeVisitor.BeginSplit
            End Sub

            Public Sub Visit(startByte As Integer, endByte As Integer) Implements IFusedRangeVisitor.Visit
                If endByte > startByte Then Count += 1
            End Sub
        End Class

        ' ------------------------------------------------------------------
        #Region "Component configuration"
        ' ------------------------------------------------------------------

        ''' <summary>Sets the normalizer and refreshes the added tokens' normalized forms.</summary>
        Public Function WithNormalizer(normalizer As INormalizer) As Tokenizer
            Me.Normalizer = normalizer
            Me.AddedVocabulary.Normalizer = normalizer
            Me.AddedVocabulary.RefreshNormalizedTokens(normalizer)
            Return Me
        End Function

        ''' <summary>Sets the pre-tokenizer.</summary>
        Public Function WithPreTokenizer(preTokenizer As IPreTokenizer) As Tokenizer
            Me.PreTokenizer = preTokenizer
            Return Me
        End Function

        ''' <summary>Sets the post-processor.</summary>
        Public Function WithPostProcessor(postProcessor As IPostProcessor) As Tokenizer
            Me.PostProcessor = postProcessor
            Return Me
        End Function

        ''' <summary>Sets the decoder.</summary>
        Public Function WithDecoder(decoder As IDecoder) As Tokenizer
            Me.Decoder = decoder
            Return Me
        End Function

        ''' <summary>Sets the truncation parameters.</summary>
        Public Sub SetTruncation(maxLength As Integer,
                                 Optional stride As Integer = 0,
                                 Optional strategy As TruncationStrategy = TruncationStrategy.LongestFirst,
                                 Optional direction As TruncationDirection = TruncationDirection.Right)
            Dim p As New TruncationParams()
            p.MaxLength = maxLength
            p.Stride = stride
            p.Strategy = strategy
            p.Direction = direction
            Me.Truncation = p
        End Sub

        ''' <summary>Sets the padding parameters.</summary>
        Public Sub SetPadding(params As PaddingParams)
            Me.Padding = params
        End Sub

        ''' <summary>Adds the given tokens to the added vocabulary. Returns the number added.</summary>
        Public Function AddTokens(tokens As IEnumerable(Of AddedToken)) As Integer
            Return AddedVocabulary.AddTokens(Me.Model.VocabSize, tokens)
        End Function

        ''' <summary>Adds the given special tokens to the added vocabulary. Returns the number added.</summary>
        Public Function AddSpecialTokens(tokens As IEnumerable(Of AddedToken)) As Integer
            Return AddedVocabulary.AddSpecialTokens(Me.Model.VocabSize, tokens)
        End Function

        ''' <summary>Returns the number of tokens the post-processor will add for the given pairing.</summary>
        Public Function GetAddedTokens(isPair As Boolean) As Integer
            If PostProcessor IsNot Nothing Then
                Return PostProcessor.GetAddedTokens(isPair)
            End If
            Return 0
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Vocabulary accessors"
        ' ------------------------------------------------------------------

        ''' <summary>Returns the entire vocabulary, optionally including added tokens.</summary>
        Public Function GetVocab(Optional withAddedTokens As Boolean = True) As Dictionary(Of String, Integer)
            Dim result As Dictionary(Of String, Integer) = Me.Model.GetVocab()
            If withAddedTokens Then
                For Each kv As KeyValuePair(Of String, Integer) In AddedVocabulary.AddedTokensMap
                    result(kv.Key) = kv.Value
                Next
            End If
            Return result
        End Function

        ''' <summary>Returns the vocabulary size, optionally including added tokens.</summary>
        Public Function GetVocabSize(Optional withAddedTokens As Boolean = True) As Integer
            Dim base As Integer = Me.Model.VocabSize
            If withAddedTokens Then
                Dim added As Dictionary(Of String, Integer) = AddedVocabulary.AddedTokensMap
                Dim overlapping As Integer = added.Keys.Where(Function(t) Me.Model.TokenToId(t).HasValue).Count()
                Return base + added.Count - overlapping
            End If
            Return base
        End Function

        ''' <summary>Maps a token to its id (added vocabulary first, then the model).</summary>
        Public Function TokenToId(token As String) As Integer?
            Return AddedVocabulary.TokenToId(token)
        End Function

        ''' <summary>Maps an id to its token string (added vocabulary first, then the model).</summary>
        Public Function IdToToken(id As Integer) As String
            Dim t As String = AddedVocabulary.SimpleIdToToken(id)
            If t Is Nothing Then Return Me.Model.IdToToken(id)
            Return t
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Encoding"
        ' ------------------------------------------------------------------

        ''' <summary>Encodes a single sequence with byte offsets.</summary>
        Public Function Encode(text As String, Optional addSpecialTokens As Boolean = True) As Encoding
            Return EncodeSingleSequenceWithPostProcess(text, addSpecialTokens, OffsetType.Byte, Nothing)
        End Function

        ''' <summary>Encodes a pair of sequences with byte offsets.</summary>
        Public Function EncodePair(textA As String, textB As String, Optional addSpecialTokens As Boolean = True) As Encoding
            Dim encA As Encoding = EncodeSingleSequence(textA, 0, OffsetType.Byte, Nothing)
            Dim encB As Encoding = EncodeSingleSequence(textB, 1, OffsetType.Byte, Nothing)
            Return PostProcess(encA, encB, addSpecialTokens)
        End Function

        ''' <summary>Encodes a single sequence with character offsets.</summary>
        Public Function EncodeCharOffsets(text As String, Optional addSpecialTokens As Boolean = True) As Encoding
            Return EncodeSingleSequenceWithPostProcess(text, addSpecialTokens, OffsetType.Char, Nothing)
        End Function

        ''' <summary>Encodes a single sequence with byte offsets (explicit byte-offset variant).</summary>
        Public Function EncodeByteOffsets(text As String, Optional addSpecialTokens As Boolean = True) As Encoding
            Return EncodeSingleSequenceWithPostProcess(text, addSpecialTokens, OffsetType.Byte, Nothing)
        End Function

        ''' <summary>Encodes a single sequence without computing offsets.</summary>
        Public Function EncodeFast(text As String, Optional addSpecialTokens As Boolean = True) As Encoding
            Try
                Return EncodeSingleSequenceWithPostProcess(text, addSpecialTokens, OffsetType.None, Nothing)
            Catch ex As OffsetTrackingRequiredException
                ' Some configurations need the per-byte alignment list even in the offset-free
                ' path (e.g. GPT-2 ByteLevel addPrefixSpace does a partial-range Prepend; a
                ' second-round split slices a no-track piece). The no-track fast path signals this
                ' via the dedicated exception; re-run fully tracked (the pre-R5 EncodeFast
                ' behaviour) so the result is always correct. The catch is deliberately narrow —
                ' only the dedicated no-track signal is caught, never a broad
                ' InvalidOperationException that could mask a genuine bug. The DeepSeek fused path
                ' never throws, so its per-Encode performance is unaffected and the fallback
                ' touches no shared state.
                Return EncodeSingleSequenceWithPostProcess(text, addSpecialTokens, OffsetType.None, Nothing, enableNoTrack:=False)
            End Try
        End Function

        ''' <summary>
        ''' Encodes a pre-tokenized sequence: every element is treated as one word, and the
        ''' resulting per-word encodings are merged (word ids set to the element index).
        ''' </summary>
        Public Function EncodePretokenized(pretokenized As IEnumerable(Of String), Optional addSpecialTokens As Boolean = True) As Encoding
            Dim encs As New List(Of Encoding)()
            Dim idx As Integer = 0
            For Each piece As String In pretokenized
                encs.Add(EncodeSingleSequence(piece, 0, OffsetType.Byte, idx))
                idx += 1
            Next
            Dim merged As Encoding = Encoding.Merge(encs, False)
            Return PostProcess(merged, Nothing, addSpecialTokens)
        End Function

        ''' <summary>Encodes a batch of sequences (applies batch padding when configured).</summary>
        Public Function EncodeBatch(texts As IEnumerable(Of String), Optional addSpecialTokens As Boolean = True) As List(Of Encoding)
            Dim result As List(Of Encoding) = texts.Select(Function(t) Encode(t, addSpecialTokens)).ToList()
            If Me.Padding IsNot Nothing Then
                Global.Tokenizers.Internal.Padding.PadEncodings(result, Me.Padding)
            End If
            Return result
        End Function

        ''' <summary>Encodes a batch of sequences with character offsets.</summary>
        Public Function EncodeBatchCharOffsets(texts As IEnumerable(Of String), Optional addSpecialTokens As Boolean = True) As List(Of Encoding)
            Dim result As List(Of Encoding) = texts.Select(Function(t) EncodeCharOffsets(t, addSpecialTokens)).ToList()
            If Me.Padding IsNot Nothing Then
                Global.Tokenizers.Internal.Padding.PadEncodings(result, Me.Padding)
            End If
            Return result
        End Function

        ''' <summary>Encodes a batch of sequences without computing offsets.</summary>
        Public Function EncodeBatchFast(texts As IEnumerable(Of String), Optional addSpecialTokens As Boolean = True) As List(Of Encoding)
            Dim result As List(Of Encoding) = texts.Select(Function(t) EncodeFast(t, addSpecialTokens)).ToList()
            If Me.Padding IsNot Nothing Then
                Global.Tokenizers.Internal.Padding.PadEncodings(result, Me.Padding)
            End If
            Return result
        End Function

        ''' <summary>
        ''' Runs the encode pipeline and returns the number of tokens. Uses the
        ''' <see cref="OffsetType.None"/> path (mirroring the Rust <c>encode_fast</c>) because the
        ''' token count does not depend on offsets; this avoids the per-split byte-offset
        ''' materialization in <c>IntoEncoding</c> (~50 ms on the 2 MB benchmark).
        '''
        ''' When neither truncation nor padding is configured, takes the count-only fast path
        ''' (<see cref="EncodeCountCore"/>): the pipeline runs the same normalization, added-token
        ''' extraction, pre-tokenization and no-track alignment skipping, but the model tokenizes
        ''' each split via <see cref="IModel.CountTokens"/> and the splits are never materialized
        ''' into a <c>List(Of Token)</c> or an <c>Encoding</c>. This removes the per-token
        ''' <c>Token</c> structs + per-token tuple list that <see cref="EncodeFast"/> still builds,
        ''' which is the dominant per-piece fixed cost on high-piece-density real code. Truncation /
        ''' padding are not counted this way (they need the materialized encoding), so those
        ''' configurations fall back to <see cref="EncodeFast"/>, whose length is the exact
        ''' pre-R7 behaviour.
        ''' </summary>
        Public Function EncodeCount(text As String, Optional addSpecialTokens As Boolean = False) As Integer
            If Me.Truncation Is Nothing AndAlso Me.Padding Is Nothing Then
                Try
                    Return EncodeCountCore(text, addSpecialTokens)
                Catch ex As OffsetTrackingRequiredException
                    ' The count-only fast path hit a configuration it cannot serve — the same
                    ' no-track operations EncodeFast falls back on (ByteLevel addPrefixSpace
                    ' partial transform, a second-round slice of a no-track piece). Delegate to
                    ' EncodeFast, which is correct for any configuration (it internally falls back
                    ' to a fully-tracked encode when the no-track path cannot serve it).
                    Return EncodeFast(text, addSpecialTokens).Length
                End Try
            End If
            Try
                Return EncodeFast(text, addSpecialTokens).Length
            Catch ex As OffsetTrackingRequiredException
                ' Defensive: EncodeFast already falls back internally, so this branch is normally
                ' never reached. It guarantees the count stays correct (matching the pre-R5
                ' behaviour of delegating to the fully-tracked Encode path) even if that changes.
                Return Encode(text, addSpecialTokens).Length
            End Try
        End Function

        ''' <summary>
        ''' The count-only fast path behind <see cref="EncodeCount"/> (no truncation/padding).
        ''' Mirrors <see cref="EncodeSingleSequence"/> exactly through model tokenization, then
        ''' sums <see cref="IModel.CountTokens"/> over the untokenized splits instead of building
        ''' tokens, and finally adds the post-processor's added special tokens when requested.
        ''' </summary>
        Private Function EncodeCountCore(text As String, addSpecialTokens As Boolean) As Integer
            ' M2 fast path detection first (independent of the normalizer): when the pre-tokenizer
            ' is exactly a fused manual-Isolated-split run followed by a pure-map ByteLevel (e.g.
            ' DeepSeek's 3 splits + ByteLevel), the model is driven straight from the fused ranges
            ' instead of materializing the per-piece Split / NormalizedString objects the fuse pass
            ' would escape to Me.Splits.
            Dim isFusedCountConfig As Boolean = False
            Dim fusedPatterns As New List(Of Pattern)()
            If Me.PreTokenizer IsNot Nothing Then
                Dim seq As PreTokenizerSequence = TryCast(Me.PreTokenizer, PreTokenizerSequence)
                If seq IsNot Nothing Then
                    isFusedCountConfig = seq.TryGetFusedCountConfig(fusedPatterns)
                End If
            End If

            ' M3: only use the no-track extract (ExtractAndNormalizeNoTrack, which builds the root
            ' NormalizedString and every piece WITHOUT the per-byte alignment list — the dominant
            ' allocation of the tracked extract) when the count-only path is guaranteed never to
            ' read _alignments downstream: the normalizer is identity AND the pre-tokenizer is
            ' either Nothing (TokenizeCount only reads Get) or the M2/M8 fused-count config
            ' (StreamFusedRangesBySplit + CountFusedRangesStreaming never touch _alignments — the
            ' streaming scan reads Get/Len/ByteToNetIndexCached and ToByteMappedString only). Any
            ' other pre-tokenizer (e.g. Metaspace's Replace) reads _alignments, so it must receive
            ' a fully tracked extract whose alignment data is present.
            Dim useNoTrackExtract As Boolean = IsIdentityNormalizer(Me.Normalizer) AndAlso
                (Me.PreTokenizer Is Nothing OrElse isFusedCountConfig)

            Dim pts As PreTokenizedString
            If useNoTrackExtract Then
                pts = Me.AddedVocabulary.ExtractAndNormalizeNoTrack(text)
            Else
                pts = Me.AddedVocabulary.ExtractAndNormalize(text, Me.Normalizer)
            End If

            ' Same no-track alignment skipping as the offset-free EncodeSingleSequence path.
            For Each s As Split In pts.Splits
                s.Normalized.SetTrackAlignments(False)
            Next

            If Me.PreTokenizer IsNot Nothing Then
                ' M2 fast path: when the pre-tokenizer is exactly a fused manual-Isolated-split run
                ' followed by a pure-map ByteLevel (e.g. DeepSeek's 3 splits + ByteLevel), drive the
                ' model straight from the fused ranges instead of materializing the per-piece
                ' Split / NormalizedString objects the fuse pass would escape to Me.Splits. Each
                ' range builds its byte-mapped string once (NormalizedString.ToByteMappedString) and
                ' feeds Model.CountTokens directly; the mapped string itself is unavoidable (the
                ' model consumes a String), but the per-piece objects are eliminated.
                If isFusedCountConfig Then
                    ' M8: stream the fused final ranges straight into the reusable count visitor
                    ' (map+count), so the final range list is never materialized (the ~249 MB M7
                    ' remnant). The visitor is per-thread and reset per encode; its accumulator is a
                    ' field, so the streaming path allocates no closure and no list.
                    Dim cv As FusedRangeCountVisitor = Me._countVisitor.Value
                    cv.Reset(Me.Model)
                    Dim m2Count As Integer = pts.CountFusedRangesStreaming(fusedPatterns, cv)
                    If addSpecialTokens AndAlso Me.PostProcessor IsNot Nothing Then
                        m2Count += Me.PostProcessor.GetAddedTokens(False)
                    End If
                    Return m2Count
                End If
                Me.PreTokenizer.PreTokenize(pts)
            End If

            Dim n As Integer = pts.TokenizeCount(Function(nm As NormalizedString) Me.Model.CountTokens(nm.Get))

            ' Post-processing without truncation/padding only ever adds the post-processor's
            ' special tokens (single sequence), and only when addSpecialTokens is requested.
            If addSpecialTokens AndAlso Me.PostProcessor IsNot Nothing Then
                n += Me.PostProcessor.GetAddedTokens(False)
            End If
            Return n
        End Function

        ''' <summary>
        ''' Whether the given normalizer is a no-op (identity): Nothing, an empty
        ''' <see cref="NormalizerSequence"/>, or a <see cref="PrecompiledNormalizer"/> with an
        ''' empty charsmap. When identity, the normalized text equals the original, so the
        ''' count-only fast path (M3) can skip building the per-byte alignment list entirely.
        ''' </summary>
        Private Shared Function IsIdentityNormalizer(n As INormalizer) As Boolean
            If n Is Nothing Then Return True
            Dim seq As NormalizerSequence = TryCast(n, NormalizerSequence)
            If seq IsNot Nothing Then Return seq.IsEmpty
            Dim pc As PrecompiledNormalizer = TryCast(n, PrecompiledNormalizer)
            If pc IsNot Nothing Then Return pc.IsNoOp
            Return False
        End Function

        ''' <summary>
        ''' Diagnostic (dev-only): runs the same three stages as the <see cref="EncodeCount"/>
        ''' count-only fast path and returns per-stage wall ticks and allocated bytes. Single-threaded;
        ''' callers should warm up (JIT + thread-local caches) before timing. Allocations use
        ''' <see cref="GC.GetAllocatedBytesForCurrentThread"/>, which is immune to GC-state noise.
        ''' Mirrors <see cref="EncodeCountCore"/> — keep in sync if that fast path changes.
        ''' </summary>
        Public Function ProfileCountStages(text As String) As EncodeCountStageProfile
            Dim profile As New EncodeCountStageProfile()
            profile.InputCharCount = text.Length

            ' Mirror EncodeCountCore: M2 detection first (independent of the normalizer), then gate
            ' the no-track extract on identity normalizer AND (no pre-tokenizer OR the M2
            ' fused-count config) so the count-only path never touches an empty _alignments.
            Dim isFusedCountConfig As Boolean = False
            Dim fusedPatterns As New List(Of Pattern)()
            If Me.PreTokenizer IsNot Nothing Then
                Dim seq As PreTokenizerSequence = TryCast(Me.PreTokenizer, PreTokenizerSequence)
                If seq IsNot Nothing Then
                    isFusedCountConfig = seq.TryGetFusedCountConfig(fusedPatterns)
                End If
            End If
            Dim useNoTrackExtract As Boolean = IsIdentityNormalizer(Me.Normalizer) AndAlso
                (Me.PreTokenizer Is Nothing OrElse isFusedCountConfig)

            Dim s1 As System.Diagnostics.Stopwatch = System.Diagnostics.Stopwatch.StartNew()
            Dim a1 As Long = GC.GetAllocatedBytesForCurrentThread()
            Dim pts As PreTokenizedString
            If useNoTrackExtract Then
                pts = Me.AddedVocabulary.ExtractAndNormalizeNoTrack(text)
            Else
                pts = Me.AddedVocabulary.ExtractAndNormalize(text, Me.Normalizer)
            End If
            For Each s As Split In pts.Splits
                s.Normalized.SetTrackAlignments(False)
            Next
            profile.ExtractTicks = s1.ElapsedTicks
            profile.ExtractAllocated = GC.GetAllocatedBytesForCurrentThread() - a1

            Dim s2 As System.Diagnostics.Stopwatch = System.Diagnostics.Stopwatch.StartNew()
            Dim a2 As Long = GC.GetAllocatedBytesForCurrentThread()
            ' M8 streaming path: when the pre-tokenizer qualifies (fused manual-Isolated run +
            ' pure-map ByteLevel, the whole sequence), the fuse streams its final ranges straight
            ' into the count visitor (map+count) in the Model phase below. The FusedSplit phase is
            ' measured here by streaming into a range-counting visitor (no map/count), so the
            ' stage's allocation is the range production only — the per-thread intermediate buffers
            ' + scratch; the final ranges are never materialized as a list. The profile re-runs the
            ' fuse scan once (in the Model phase below) for the attribution, so its stage ticks
            ' include that extra scan; the allocation numbers are the real path's (the second scan
            ' reuses the per-thread buffers, allocating ~0).
            Dim isM2 As Boolean = False
            Dim pieceCount As Integer = 0
            If Me.PreTokenizer IsNot Nothing Then
                If isFusedCountConfig Then
                    isM2 = True
                    Dim rc As RangeCountingVisitor = Me._rangeCounter.Value
                    rc.Count = 0
                    pts.StreamFusedRangesBySplit(fusedPatterns, rc)
                    pieceCount = rc.Count
                    For Each sp As Split In pts.Splits
                        If sp.Tokens IsNot Nothing Then pieceCount += 1
                    Next
                    profile.FusedSplitTicks = s2.ElapsedTicks
                    profile.FusedSplitAllocated = GC.GetAllocatedBytesForCurrentThread() - a2
                Else
                    Dim seq As PreTokenizerSequence = TryCast(Me.PreTokenizer, PreTokenizerSequence)
                    If seq IsNot Nothing Then
                        Dim subProfile As PreTokenizeStageProfile = seq.PreTokenizeProfiled(pts)
                        profile.FusedSplitTicks = subProfile.FusedSplitTicks
                        profile.FusedSplitAllocated = subProfile.FusedSplitAllocated
                        profile.RemainingStages = subProfile.Remaining
                        pieceCount = pts.Splits.Count
                    Else
                        Me.PreTokenizer.PreTokenize(pts)
                        pieceCount = pts.Splits.Count
                    End If
                End If
            Else
                pieceCount = pts.Splits.Count
            End If
            profile.PretokenizeTicks = s2.ElapsedTicks
            profile.PretokenizeAllocated = GC.GetAllocatedBytesForCurrentThread() - a2
            profile.PieceCount = pieceCount

            Dim s3 As System.Diagnostics.Stopwatch = System.Diagnostics.Stopwatch.StartNew()
            Dim a3 As Long = GC.GetAllocatedBytesForCurrentThread()
            Dim n As Integer
            If isM2 Then
                ' M8: the real streaming count pass — stream the fused ranges into the reusable
                ' count visitor (map+count). The final range list is never built.
                Dim cv As FusedRangeCountVisitor = Me._countVisitor.Value
                cv.Reset(Me.Model)
                n = pts.CountFusedRangesStreaming(fusedPatterns, cv)
            Else
                n = pts.TokenizeCount(Function(nm As NormalizedString) Me.Model.CountTokens(nm.Get))
            End If
            profile.ModelTicks = s3.ElapsedTicks
            profile.ModelAllocated = GC.GetAllocatedBytesForCurrentThread() - a3

            profile.TokenCount = n
            Return profile
        End Function

        ''' <summary>
        ''' Encodes with character offsets and returns per-token (id, char start, char end) spans,
        ''' used by the colored view. The pipeline's char offsets are SCALAR offsets (matching the
        ''' Rust/Python <c>offset_type="char"</c>), but .NET <c>String.Substring</c> uses UTF-16 code
        ''' units, so the spans are converted to UTF-16 boundaries here.
        ''' </summary>
        Public Function EncodeWithSpans(text As String) As List(Of (Integer, Integer, Integer))
            Dim enc As Encoding = EncodeCharOffsets(text, False)
            Dim utf16Boundaries As New List(Of Integer)()
            Dim net As Integer = 0
            For Each r As Global.System.Text.Rune In text.EnumerateRunes()
                utf16Boundaries.Add(net)
                net += r.Utf16SequenceLength
            Next
            utf16Boundaries.Add(net) ' end-of-string boundary (index == scalar count)
            Dim result As New List(Of (Integer, Integer, Integer))()
            For i As Integer = 0 To enc.Ids.Count - 1
                Dim s As Integer = enc.Offsets(i).Item1
                Dim e As Integer = enc.Offsets(i).Item2
                Dim s16 As Integer = If(s < utf16Boundaries.Count, utf16Boundaries(s), text.Length)
                Dim e16 As Integer = If(e < utf16Boundaries.Count, utf16Boundaries(e), text.Length)
                result.Add((enc.Ids(i), s16, e16))
            Next
            Return result
        End Function

        ''' <summary>
        ''' Runs the pipeline for a single raw sequence (with post-processing). Mirrors the Rust
        ''' <c>encode</c> path.
        ''' </summary>
        Private Function EncodeSingleSequenceWithPostProcess(text As String,
                                                              addSpecialTokens As Boolean,
                                                              offsetType As OffsetType,
                                                              wordIdx As Integer?,
                                                              Optional enableNoTrack As Boolean = True) As Encoding
            Dim encoding As Encoding = EncodeSingleSequence(text, 0, offsetType, wordIdx, enableNoTrack)
            Return PostProcess(encoding, Nothing, addSpecialTokens)
        End Function

        ''' <summary>
        ''' Normalizes, pre-tokenizes and model-tokenizes a single sequence. Mirrors the Rust
        ''' <c>encode_single_sequence</c> + <c>do_pre_tokenize</c> + <c>do_tokenize</c>.
        ''' </summary>
        Private Function EncodeSingleSequence(sequence As String,
                                              typeId As Integer,
                                              offsetType As OffsetType,
                                              wordIdx As Integer?,
                                              Optional enableNoTrack As Boolean = True) As Encoding
            Dim pts As PreTokenizedString = Me.AddedVocabulary.ExtractAndNormalize(sequence, Me.Normalizer)

            ' The offset-free path (EncodeCount / EncodeFast) never reads the per-byte alignment
            ' lists, so tell the pre-tokenizer slices and transforms to skip building them. This
            ' eliminates the dominant per-piece allocation of the pre-tokenization hot path.
            ' <paramref name="enableNoTrack"/> is False only on the fallback path, which re-runs
            ' the whole pipeline fully tracked when a no-track NormalizedString hits an operation
            ' that needs the alignment list (OffsetTrackingRequiredException).
            If offsetType = OffsetType.None AndAlso enableNoTrack Then
                For Each s As Split In pts.Splits
                    s.Normalized.SetTrackAlignments(False)
                Next
            End If

            If Me.PreTokenizer IsNot Nothing Then
                Me.PreTokenizer.PreTokenize(pts)
            End If

            Dim trunc As (Integer, TruncationDirection)? = Nothing
            If Me.Truncation IsNot Nothing Then
                Dim t As TruncationParams = Me.Truncation
                If Not (t.Strategy = TruncationStrategy.OnlySecond AndAlso typeId = 1) Then
                    trunc = (t.MaxLength, t.Direction)
                End If
            End If

            If trunc.HasValue Then
                pts.TokenizeWithLimit(Function(n As NormalizedString) Me.Model.Tokenize(n.Get), trunc.Value.Item1, trunc.Value.Item2)
            Else
                pts.Tokenize(Function(n As NormalizedString) Me.Model.Tokenize(n.Get))
            End If

            Return pts.IntoEncoding(wordIdx, typeId, offsetType)
        End Function

        ''' <summary>
        ''' Post-processes the (possibly paired) encoding: truncation, post-processing and padding.
        ''' Mirrors the Rust <c>post_process</c>.
        ''' </summary>
        Private Function PostProcess(encoding As Encoding, pairEncoding As Encoding, addSpecialTokens As Boolean) As Encoding
            Dim enc As Encoding = encoding
            Dim pairEnc As Encoding = pairEncoding

            ' 1. Truncation.
            If Me.Truncation IsNot Nothing Then
                Dim nAdded As Integer = Me.GetAddedTokens(pairEnc IsNot Nothing)
                If addSpecialTokens AndAlso nAdded > 0 Then
                    Dim params As New TruncationParams()
                    params.Direction = Me.Truncation.Direction
                    params.MaxLength = Me.Truncation.MaxLength - nAdded
                    params.Strategy = Me.Truncation.Strategy
                    params.Stride = Me.Truncation.Stride
                    Global.Tokenizers.Internal.Truncation.TruncateEncodings(enc, pairEnc, params)
                Else
                    Global.Tokenizers.Internal.Truncation.TruncateEncodings(enc, pairEnc, Me.Truncation)
                End If
            End If

            ' 2. Post-processing.
            Dim finalEnc As Encoding
            If Me.PostProcessor IsNot Nothing Then
                finalEnc = PostProcessorHelpers.DefaultProcess(Me.PostProcessor, enc, pairEnc, addSpecialTokens)
            Else
                Dim encodings As New List(Of Encoding)()
                If pairEnc Is Nothing Then
                    encodings.Add(enc)
                Else
                    encodings.Add(enc)
                    encodings.Add(pairEnc)
                End If
                If encodings.Count = 1 Then
                    finalEnc = encodings(0)
                Else
                    Dim merged As New Encoding()
                    For i As Integer = 0 To encodings.Count - 1
                        encodings(i).SetSequenceId(i)
                        merged.MergeWith(encodings(i), False)
                    Next
                    finalEnc = merged
                End If
            End If

            ' 3. Padding.
            If Me.Padding IsNot Nothing Then
                Global.Tokenizers.Internal.Padding.PadEncodings(New List(Of Encoding) From {finalEnc}, Me.Padding)
            End If

            Return finalEnc
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Decoding"
        ' ------------------------------------------------------------------

        ''' <summary>Decodes the given ids back to a string, skipping special tokens when requested.</summary>
        Public Function Decode(ids As IEnumerable(Of Integer), Optional skipSpecialTokens As Boolean = True) As String
            Dim tokens As New List(Of String)()
            For Each id As Integer In ids
                Dim token As String = AddedVocabulary.SimpleIdToToken(id)
                If token Is Nothing Then token = Me.Model.IdToToken(id)
                If token Is Nothing Then Continue For
                If skipSpecialTokens AndAlso AddedVocabulary.IsSpecialToken(token) Then Continue For
                tokens.Add(token)
            Next

            If Me.Decoder IsNot Nothing Then
                Return String.Join("", Me.Decoder.DecodeChain(tokens))
            Else
                Return String.Join(" ", tokens)
            End If
        End Function

        ''' <summary>Decodes a batch of id lists back to strings.</summary>
        Public Function DecodeBatch(sentences As IEnumerable(Of IEnumerable(Of Integer)),
                                    Optional skipSpecialTokens As Boolean = True) As List(Of String)
            Return sentences.Select(Function(ids) Decode(ids, skipSpecialTokens)).ToList()
        End Function

        ''' <summary>
        ''' Runs the decoder chain over the given token strings (filtering special tokens when
        ''' requested), without any id lookup. Mirrors <c>Decoder::decode_chain</c>.
        ''' </summary>
        Public Function DecodeChain(tokens As IEnumerable(Of String), Optional skipSpecialTokens As Boolean = True) As List(Of String)
            Dim filtered As List(Of String) = tokens.Where(
                Function(t) Not (skipSpecialTokens AndAlso AddedVocabulary.IsSpecialToken(t))).ToList()
            If Me.Decoder IsNot Nothing Then
                Return Me.Decoder.DecodeChain(filtered)
            End If
            Return filtered
        End Function

        ''' <summary>Creates a stream decoder that produces incremental decode chunks.</summary>
        Public Function DecodeStream(Optional skipSpecialTokens As Boolean = True) As StreamDecoder
            Return New StreamDecoder(Me, skipSpecialTokens)
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Serialization"
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Loads a tokenizer from a tokenizer.json string.
        ''' <paramref name="cacheCapacity"/> / <paramref name="cacheMaxWord"/> are optional BPE word-cache
        ''' overrides (see <see cref="Models.BpeModel"/>) used by the dev benchmark; <c>Nothing</c> keeps
        ''' the model defaults.
        ''' </summary>
        Public Shared Function FromJson(json As String,
                                        Optional cacheCapacity As Integer? = Nothing,
                                        Optional cacheMaxWord As Integer? = Nothing,
                                        Optional sharedCacheCapacity As Integer? = Nothing) As Tokenizer
            Dim node As JsonNode = JsonNode.Parse(json)
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then
                Throw New ArgumentException("Invalid tokenizer JSON")
            End If
            Dim obj As JsonObject = DirectCast(node, JsonObject)

            Dim version As String = SerializationHelpers.GetString(obj, "version")
            If version IsNot Nothing AndAlso version <> "1.0" Then
                Throw New ArgumentException($"Unknown tokenizer version '{version}'")
            End If

            Dim modelNode As JsonNode = SerializationHelpers.GetNode(obj, "model")
            Dim model As Object = ComponentFactory.FromModel(modelNode, cacheCapacity, cacheMaxWord, sharedCacheCapacity)
            If model Is Nothing Then
                Throw New ArgumentException("Model missing.")
            End If
            Dim tokenizer As New Tokenizer(DirectCast(model, IModel))

            tokenizer.Normalizer = ComponentFactory.FromNormalizer(SerializationHelpers.GetNode(obj, "normalizer"))
            tokenizer.PreTokenizer = ComponentFactory.FromPreTokenizer(SerializationHelpers.GetNode(obj, "pre_tokenizer"))
            tokenizer.PostProcessor = ComponentFactory.FromPostProcessor(SerializationHelpers.GetNode(obj, "post_processor"))
            tokenizer.Decoder = ComponentFactory.FromDecoder(SerializationHelpers.GetNode(obj, "decoder"))
            tokenizer.Truncation = ParseTruncation(SerializationHelpers.GetNode(obj, "truncation"))
            tokenizer.Padding = ParsePadding(SerializationHelpers.GetNode(obj, "padding"))

            tokenizer.AddedVocabulary.ModelVocab = DirectCast(model, IModel).GetVocab()
            tokenizer.AddedVocabulary.Normalizer = tokenizer.Normalizer

            Dim addedNode As JsonNode = SerializationHelpers.GetNode(obj, "added_tokens")
            Dim addedTokens As New List(Of AddedToken)()
            If addedNode IsNot Nothing AndAlso TypeOf addedNode Is JsonArray Then
                For Each item As JsonNode In DirectCast(addedNode, JsonArray)
                    Dim entry As JsonObject = TryCast(item, JsonObject)
                    If entry Is Nothing Then Continue For
                    Dim content As String = SerializationHelpers.GetString(entry, "content")
                    Dim special As Boolean = SerializationHelpers.GetBool(entry, "special").GetValueOrDefault(False)
                    Dim at As New AddedToken(content, special)
                    at.SingleWord = SerializationHelpers.GetBool(entry, "single_word").GetValueOrDefault(False)
                    at.LStrip = SerializationHelpers.GetBool(entry, "lstrip").GetValueOrDefault(False)
                    at.RStrip = SerializationHelpers.GetBool(entry, "rstrip").GetValueOrDefault(False)
                    at.Normalized = SerializationHelpers.GetBool(entry, "normalized").GetValueOrDefault(Not special)
                    addedTokens.Add(at)
                Next
            End If
            tokenizer.AddTokens(addedTokens)

            Return tokenizer
        End Function

        ''' <summary>Loads a tokenizer from a tokenizer.json file.</summary>
        Public Shared Function FromFile(path As String,
                                        Optional cacheCapacity As Integer? = Nothing,
                                        Optional cacheMaxWord As Integer? = Nothing,
                                        Optional sharedCacheCapacity As Integer? = Nothing) As Tokenizer
            Dim json As String = File.ReadAllText(path)
            Return FromJson(json, cacheCapacity, cacheMaxWord, sharedCacheCapacity)
        End Function

        ''' <summary>Alias for <see cref="FromFile"/>.</summary>
        Public Shared Function Load(path As String,
                                    Optional cacheCapacity As Integer? = Nothing,
                                    Optional cacheMaxWord As Integer? = Nothing,
                                    Optional sharedCacheCapacity As Integer? = Nothing) As Tokenizer
            Return FromFile(path, cacheCapacity, cacheMaxWord, sharedCacheCapacity)
        End Function

        ''' <summary>Serializes this tokenizer to a tokenizer.json string.</summary>
        Public Function ToJson(Optional pretty As Boolean = True) As String
            Dim o As New JsonObject()
            o("version") = "1.0"
            o("truncation") = TruncationToJson(Me.Truncation)
            o("padding") = PaddingToJson(Me.Padding)
            o("added_tokens") = AddedTokensToJson()
            o("normalizer") = If(Me.Normalizer Is Nothing, Nothing, Me.Normalizer.ToJson())
            o("pre_tokenizer") = If(Me.PreTokenizer Is Nothing, Nothing, Me.PreTokenizer.ToJson())
            o("post_processor") = If(Me.PostProcessor Is Nothing, Nothing, Me.PostProcessor.ToJson())
            o("decoder") = If(Me.Decoder Is Nothing, Nothing, Me.Decoder.ToJson())
            o("model") = Me.Model.ToJson()
            Return o.ToJsonString(New JsonSerializerOptions With {
                .WriteIndented = pretty,
                .Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            })
        End Function

        ''' <summary>Saves this tokenizer to a tokenizer.json file.</summary>
        Public Sub Save(path As String, Optional pretty As Boolean = True)
            File.WriteAllText(path, ToJson(pretty))
        End Sub

        Private Function AddedTokensToJson() As JsonArray
            Dim arr As New JsonArray()
            Dim sorted As List(Of Integer) = AddedVocabulary.AddedTokensDecoder.Keys.OrderBy(Function(k) k).ToList()
            For Each id As Integer In sorted
                Dim token As AddedToken = AddedVocabulary.AddedTokensDecoder(id)
                Dim entry As New JsonObject()
                entry("id") = id
                entry("content") = token.Content
                entry("single_word") = token.SingleWord
                entry("lstrip") = token.LStrip
                entry("rstrip") = token.RStrip
                entry("normalized") = token.Normalized
                entry("special") = token.Special
                arr.Add(entry)
            Next
            Return arr
        End Function

        Private Shared Function TruncationToJson(t As TruncationParams) As JsonObject
            If t Is Nothing Then Return Nothing
            Dim o As New JsonObject()
            o("direction") = SerializationHelpers.TruncationDirectionToString(t.Direction)
            o("max_length") = t.MaxLength
            o("strategy") = SerializationHelpers.TruncationStrategyToString(t.Strategy)
            o("stride") = t.Stride
            Return o
        End Function

        Private Shared Function PaddingToJson(p As PaddingParams) As JsonObject
            If p Is Nothing Then Return Nothing
            Dim o As New JsonObject()
            o("strategy") = SerializationHelpers.PaddingStrategyToNode(p.Strategy)
            o("direction") = SerializationHelpers.PaddingDirectionToString(p.Direction)
            o("pad_to_multiple_of") = If(p.PadToMultipleOf.HasValue, JsonValue.Create(p.PadToMultipleOf.Value), Nothing)
            o("pad_id") = p.PadId
            o("pad_type_id") = p.PadTypeId
            o("pad_token") = p.PadToken
            Return o
        End Function

        Private Shared Function ParseTruncation(node As JsonNode) As TruncationParams
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)
            Dim p As New TruncationParams()
            Dim direction As String = SerializationHelpers.GetString(obj, "direction")
            If direction IsNot Nothing Then p.Direction = SerializationHelpers.ParseTruncationDirection(direction)
            p.MaxLength = SerializationHelpers.GetInt(obj, "max_length").GetValueOrDefault(512)
            Dim strategy As String = SerializationHelpers.GetString(obj, "strategy")
            If strategy IsNot Nothing Then p.Strategy = SerializationHelpers.ParseTruncationStrategy(strategy)
            p.Stride = SerializationHelpers.GetInt(obj, "stride").GetValueOrDefault(0)
            Return p
        End Function

        Private Shared Function ParsePadding(node As JsonNode) As PaddingParams
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)
            Dim p As New PaddingParams()
            Dim strategyNode As JsonNode = SerializationHelpers.GetNode(obj, "strategy")
            If strategyNode IsNot Nothing Then p.Strategy = SerializationHelpers.ParsePaddingStrategy(strategyNode)
            Dim direction As String = SerializationHelpers.GetString(obj, "direction")
            If direction IsNot Nothing Then p.Direction = SerializationHelpers.ParsePaddingDirection(direction)
            p.PadToMultipleOf = SerializationHelpers.GetInt(obj, "pad_to_multiple_of")
            p.PadId = SerializationHelpers.GetInt(obj, "pad_id").GetValueOrDefault(0)
            p.PadTypeId = SerializationHelpers.GetInt(obj, "pad_type_id").GetValueOrDefault(0)
            Dim padToken As String = SerializationHelpers.GetString(obj, "pad_token")
            If padToken IsNot Nothing Then p.PadToken = padToken
            Return p
        End Function

        #End Region

    End Class

    ''' <summary>
    ''' Incremental decoder that keeps state so that decoding one id at a time produces the same
    ''' result as decoding all ids at once (needed for byte-fallback and metaspace decoders).
    ''' Faithful port of the Rust <c>DecodeStream</c> / <c>step_decode_stream</c>.
    ''' </summary>
    Public NotInheritable Class StreamDecoder

        Private ReadOnly _tokenizer As Tokenizer
        Private ReadOnly _skipSpecialTokens As Boolean
        Private _ids As New List(Of Integer)()
        Private _prefix As String = ""
        Private _prefixIndex As Integer = 0

        Friend Sub New(tokenizer As Tokenizer, skipSpecialTokens As Boolean)
            _tokenizer = tokenizer
            _skipSpecialTokens = skipSpecialTokens
        End Sub

        ''' <summary>
        ''' Feeds the next id and returns the chunk of text it produces, or <c>Nothing</c> when the
        ''' id is not enough to produce a valid chunk.
        ''' </summary>
        Public Function [Step](id As Integer) As String
            Return StepInternal(New List(Of Integer) From {id})
        End Function

        Private Function StepInternal(tokenIds As List(Of Integer)) As String
            If _prefix.Length = 0 AndAlso _ids.Count > 0 Then
                Dim newPrefix As String = _tokenizer.Decode(_ids, _skipSpecialTokens)
                If Not newPrefix.EndsWith(ReplacementChar) Then
                    _prefix = newPrefix
                    _prefixIndex = _ids.Count
                End If
            End If

            _ids.AddRange(tokenIds)
            Dim str As String = _tokenizer.Decode(_ids, _skipSpecialTokens)
            If str.Length > _prefix.Length AndAlso Not str.EndsWith(ReplacementChar) Then
                If Not str.StartsWith(_prefix) Then
                    Throw New InvalidOperationException(
                        $"Invalid prefix encountered while decoding stream. Expected prefix: '{_prefix}', Actual string: '{str}'")
                End If

                Dim newText As String = str.Substring(_prefix.Length)
                Dim newPrefixIndex As Integer = _ids.Count - _prefixIndex
                _ids = _ids.GetRange(_prefixIndex, _ids.Count - _prefixIndex)
                _prefix = _tokenizer.Decode(_ids, _skipSpecialTokens)
                _prefixIndex = newPrefixIndex
                Return newText
            Else
                Return Nothing
            End If
        End Function

        Private Shared ReadOnly Property ReplacementChar As String
            Get
                Return ChrW(&HFFFD)
            End Get
        End Property
    End Class
