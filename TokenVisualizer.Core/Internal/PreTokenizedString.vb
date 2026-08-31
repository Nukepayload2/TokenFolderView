Imports System.Linq
Imports System.Threading
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
    ''' Streaming consumer of the fused manual-Isolated-split range pass: called once per final
    ''' piece range (byte offsets in a split's normalized referential) as the final pattern's scan
    ''' produces them, so the final range list is never materialized (M8). A visitor instance is
    ''' reusable: the caller holds one (a field or <see cref="ThreadLocal(Of T)"/>) and resets its
    ''' per-call state before each use. <see cref="BeginSplit"/> is invoked once per split before
    ''' its ranges stream, so a visitor that needs the split's <see cref="NormalizedString"/>
    ''' (e.g. to build the piece or its byte-mapped string) can hold it in a field instead of
    ''' capturing it per call (zero closure).
    ''' </summary>
    Public Interface IFusedRangeVisitor
        ''' <summary>Called before a split's ranges stream, to set the split context.</summary>
        Sub BeginSplit(normalized As NormalizedString)
        ''' <summary>Called once per final piece range (byte offsets in the split's normalized text).</summary>
        Sub Visit(startByte As Integer, endByte As Integer)
    End Interface

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

        ''' <summary>Builds a <see cref="PreTokenizedString"/> over a no-track root
        ''' <see cref="NormalizedString"/> (no per-byte alignment list). Only for the offset-free
        ''' count-only path; see <see cref="NormalizedString.FromStringNoTrack"/>.</summary>
        Public Shared Function FromStringNoTrack(text As String) As PreTokenizedString
            Return FromNormalizedString(NormalizedString.FromStringNoTrack(text))
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
            FuseIsolatedSplitsCore(patterns, False)
        End Sub

        ''' <summary>
        ''' Fuse variant that additionally applies the pure-map ByteLevel byte→char mapping to each
        ''' final piece, so a trailing ByteLevel(use_regex:=False, add_prefix_space:=False)
        ''' pre-tokenizer (e.g. DeepSeek's) can be folded into the fused pass and its independent
        ''' traversal of every piece skipped. Produces byte-identical pieces to
        ''' <see cref="FuseIsolatedSplits"/> followed by ByteLevel.PreTokenize (the mapping is
        ''' applied by <see cref="NormalizedString.SliceWithByteMap"/>).
        ''' </summary>
        Friend Sub FuseIsolatedSplitsWithByteMap(patterns As List(Of Pattern))
            FuseIsolatedSplitsCore(patterns, True)
        End Sub

        Private Sub FuseIsolatedSplitsCore(patterns As List(Of Pattern), applyByteMap As Boolean)
            Dim newSplits As New List(Of Split)()
            Dim builder As SplitBuildingVisitor = s_splitBuilder.Value
            builder.NewSplits = newSplits
            builder.ApplyByteMap = applyByteMap
            For Each split As Split In Me.Splits
                If split.Tokens IsNot Nothing Then
                    newSplits.Add(split)
                    Continue For
                End If
                builder.BeginSplit(split.Normalized)
                FuseRangesStreaming(split.Normalized, patterns, builder)
            Next
            Me.Splits = newSplits
        End Sub

        ''' <summary>
        ''' Reusable <see cref="IFusedRangeVisitor"/> for <see cref="FuseIsolatedSplitsCore"/>: each
        ''' <see cref="Visit"/> materializes the piece (<see cref="NormalizedString.Slice"/> or
        ''' <see cref="NormalizedString.SliceWithByteMap"/>) and appends it to
        ''' <see cref="NewSplits"/>. Fields, not captured locals, so the instance is zero-allocation
        ''' reusable across calls; the caller sets <see cref="NewSplits"/> / <see cref="ApplyByteMap"/>
        ''' per call and <see cref="BeginSplit"/> sets the per-split source. M8: switching the
        ''' tracked fused path to the streaming producer removes the final range list this path
        ''' used to materialize (<see cref="ComputeFusedRanges"/>).
        ''' </summary>
        Private NotInheritable Class SplitBuildingVisitor
            Implements IFusedRangeVisitor

            ''' <summary>The split list being built (set once per <see cref="FuseIsolatedSplitsCore"/> call).</summary>
            Public NewSplits As List(Of Split)
            ''' <summary>Whether the pure-map ByteLevel mapping is folded into the piece (set per call).</summary>
            Public ApplyByteMap As Boolean

            Private _ns As NormalizedString

            Public Sub BeginSplit(normalized As NormalizedString) Implements IFusedRangeVisitor.BeginSplit
                Me._ns = normalized
            End Sub

            Public Sub Visit(startByte As Integer, endByte As Integer) Implements IFusedRangeVisitor.Visit
                If endByte > startByte Then
                    If ApplyByteMap Then
                        NewSplits.Add(Split.FromNormalizedString(
                            _ns.SliceWithByteMap(New OffsetRange(False, startByte, endByte))))
                    Else
                        NewSplits.Add(Split.FromNormalizedString(
                            _ns.Slice(New OffsetRange(False, startByte, endByte))))
                    End If
                End If
            End Sub
        End Class

        ''' <summary>
        ''' Runs the fused manual-Isolated-split patterns over one split's normalized text and
        ''' returns the final piece ranges (byte offsets in that split's normalized referential)
        ''' that the sequential Isolated splits would produce. Shared by the piece-materializing
        ''' fused path (<see cref="FuseIsolatedSplitsCore"/>) and the M2 range-driven count path
        ''' (<see cref="FusedRangesBySplit"/> / <see cref="CountFusedRanges"/>), so both compute
        ''' byte-identical ranges. One scratch match list is reused across every per-piece scan:
        ''' the scanner writes into it and it is cleared by <c>FindMatchesInto</c>, so no
        ''' <see cref="List(Of MatchInfo)"/> is allocated per piece (a dominant per-piece cost on
        ''' high-piece-density corpora).
        '''
        ''' M7: the per-pattern intermediate output lists (ranges1/ranges2 in the DeepSeek 3-pattern
        ''' config) and the scratch match list are per-thread (<see cref="ThreadLocal(Of T)"/>) and
        ''' retained across pieces. The intermediate range buffers alternate (a list is never written
        ''' while the same pass scans it); Clear() + refill reuses the retained backing array, so the
        ''' per-piece geometric growth of intermediate lists is eliminated after the first piece that
        ''' reaches each size. Only the final pattern's output is a fresh list, because it escapes to
        ''' the caller (<see cref="CountFusedRanges"/> / <see cref="FuseIsolatedSplitsCore"/> iterate
        ''' it) and must not be clobbered by the next piece.
        ''' </summary>
        Private Shared ReadOnly s_rangeBufferA As New ThreadLocal(Of List(Of (Integer, Integer)))(
            Function() New List(Of (Integer, Integer))(1))
        Private Shared ReadOnly s_rangeBufferB As New ThreadLocal(Of List(Of (Integer, Integer)))(
            Function() New List(Of (Integer, Integer))(16))
        Private Shared ReadOnly s_matchScratch As New ThreadLocal(Of List(Of MatchInfo))(
            Function() New List(Of MatchInfo)(16))
        ''' <summary>Per-thread reusable <see cref="SplitBuildingVisitor"/> for the tracked fused path (M8).</summary>
        Private Shared ReadOnly s_splitBuilder As New ThreadLocal(Of SplitBuildingVisitor)(
            Function() New SplitBuildingVisitor())
        Private Shared Function ComputeFusedRanges(ns As NormalizedString, patterns As List(Of Pattern)) As List(Of (Integer, Integer))
            Dim text As String = ns.Get
            Dim utf8Len As Integer = ns.Len()
            If patterns.Count = 0 Then
                Return New List(Of (Integer, Integer))(1) From {(0, utf8Len)}
            End If

            ' M7: the intermediate pattern outputs (ranges1, ranges2 in the DeepSeek 3-pattern
            ' config) are consumed by the next pattern and then die, so their backing arrays are
            ' reused across pieces via two per-thread alternating buffers (a list being scanned by
            ' the current pattern is never the write target of the same pass). A buffer is Clear()ed
            ' and refilled, retaining its capacity from previous pieces, so the per-piece geometric
            ' growth of the intermediate lists (measured ~164 MB in the 235-sample harness) is
            ' eliminated after the first piece that reaches each size. The final pattern's output
            ' escapes to the caller (CountFusedRanges / FuseIsolatedSplitsCore iterates it), so it
            ' is always a fresh list. The scratch match list is likewise per-thread and reused
            ' across pieces (it is cleared by FindMatchesInto before every scan, and consumed within
            ' the pass, so it never aliases).
            Dim scratch As List(Of MatchInfo) = s_matchScratch.Value
            Dim needScratchCap As Integer = Math.Max(16, text.Length \ 32)
            If scratch.Capacity < needScratchCap Then scratch.Capacity = needScratchCap

            Dim bufA As List(Of (Integer, Integer)) = s_rangeBufferA.Value
            Dim bufB As List(Of (Integer, Integer)) = s_rangeBufferB.Value
            bufA.Clear()
            bufA.Add((0, utf8Len))
            Dim ranges As List(Of (Integer, Integer)) = bufA

            For pi As Integer = 0 To patterns.Count - 1
                Dim p As Pattern = patterns(pi)
                Dim preSize As Integer
                If pi = patterns.Count - 1 Then
                    ' Final pattern: fresh list, pre-sized as before (M6 estimate), escapes to caller.
                    preSize = Math.Min(utf8Len, Math.Max(16, ranges.Count * 2 + utf8Len \ 5))
                    Dim finalRanges As New List(Of (Integer, Integer))(preSize)
                    FillRangesInto(ns, text, p, ranges, scratch, finalRanges)
                    Return finalRanges
                End If
                ' Intermediate pattern: reuse the other alternating buffer. Pre-size so the common
                ' high-piece-density case avoids geometric reallocation; an intermediate pattern's
                ' output is at least one piece per input range (Isolated never drops a segment).
                ' EnsureCapacity only grows, so the buffer's retained backing stays valid.
                Dim target As List(Of (Integer, Integer)) = If(ranges Is bufA, bufB, bufA)
                preSize = Math.Max(16, ranges.Count * 2)
                If target.Capacity < preSize Then target.Capacity = preSize
                target.Clear()
                FillRangesInto(ns, text, p, ranges, scratch, target)
                ranges = target
            Next
            ' Unreachable when patterns.Count > 0 (the final pattern returns above).
            Return New List(Of (Integer, Integer))(1) From {(0, utf8Len)}
        End Function

        ''' <summary>
        ''' Runs one pattern's scan over every range of <paramref name="ranges"/> and writes the
        ''' resulting sub-ranges into <paramref name="target"/> (the same loop body as the original
        ''' fused pass; extracted so the intermediate buffer-reuse path and the fresh final list share
        ''' one implementation). The scanner writes into <paramref name="scratch"/> (pre-cleared by
        ''' <c>FindMatchesInto</c>); match offsets are slice-relative and are offset back to the root
        ''' normalized byte referential before being added to <paramref name="target"/>.
        ''' </summary>
        Private Shared Sub FillRangesInto(ns As NormalizedString, text As String, p As Pattern,
                                          ranges As List(Of (Integer, Integer)), scratch As List(Of MatchInfo),
                                          target As List(Of (Integer, Integer)))
            For Each r In ranges
                Dim b1 As Integer = r.Item1
                Dim b2 As Integer = r.Item2
                If b2 <= b1 Then Continue For
                ' Run the pattern directly on the text slice via the cached boundary index (binary
                ' search): the manual scanners accept a (string, start, length) slice and scan it in
                ' place, so no per-(piece × pattern) substring is materialized. Matches come back
                ' with byte offsets relative to the slice and are offset back below.
                Dim n1 As Integer = ns.ByteToNetIndexCached(b1)
                Dim n2 As Integer = ns.ByteToNetIndexCached(b2)
                If n2 <= n1 Then Continue For
                p.FindMatchesInto(text, n1, n2 - n1, scratch)
                For i As Integer = 0 To scratch.Count - 1
                    Dim m As MatchInfo = scratch(i)
                    Dim mb1 As Integer = m.Start
                    Dim mb2 As Integer = m.End
                    If mb2 > mb1 Then
                        target.Add((b1 + mb1, b1 + mb2))
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' M8 streaming twin of <see cref="ComputeFusedRanges"/>: runs the same fused manual-Isolated
        ''' patterns over one split's normalized text, but the FINAL pattern's output is streamed to
        ''' <paramref name="visitor"/> (one <see cref="IFusedRangeVisitor.Visit"/> per final piece
        ''' range) instead of being materialized into a fresh list. The intermediate patterns still
        ''' write into the two per-thread alternating range buffers (<see cref="s_rangeBufferA"/> /
        ''' <see cref="s_rangeBufferB"/>, M7) so their per-piece geometric growth stays eliminated;
        ''' only the final range list (the dominant remaining FusedSplit allocation, ~249 MB in the
        ''' 235-sample harness) is not built. Shares the per-thread scratch match list
        ''' (<see cref="s_matchScratch"/>).
        ''' </summary>
        Private Shared Sub FuseRangesStreaming(ns As NormalizedString, patterns As List(Of Pattern),
                                               visitor As IFusedRangeVisitor)
            Dim text As String = ns.Get
            Dim utf8Len As Integer = ns.Len()
            If patterns.Count = 0 Then
                visitor.Visit(0, utf8Len)
                Return
            End If

            Dim scratch As List(Of MatchInfo) = s_matchScratch.Value
            Dim needScratchCap As Integer = Math.Max(16, text.Length \ 32)
            If scratch.Capacity < needScratchCap Then scratch.Capacity = needScratchCap

            Dim bufA As List(Of (Integer, Integer)) = s_rangeBufferA.Value
            Dim bufB As List(Of (Integer, Integer)) = s_rangeBufferB.Value
            bufA.Clear()
            bufA.Add((0, utf8Len))
            Dim ranges As List(Of (Integer, Integer)) = bufA

            For pi As Integer = 0 To patterns.Count - 1
                Dim p As Pattern = patterns(pi)
                If pi = patterns.Count - 1 Then
                    ' Final pattern: stream each final range directly to the visitor; no list is
                    ' built (the M8 target).
                    StreamRangesInto(ns, text, p, ranges, scratch, visitor)
                    Return
                End If
                ' Intermediate pattern: reuse the other alternating buffer (same loop as
                ' ComputeFusedRanges, so the intermediate partitions are byte-identical).
                Dim target As List(Of (Integer, Integer)) = If(ranges Is bufA, bufB, bufA)
                Dim preSize As Integer = Math.Max(16, ranges.Count * 2)
                If target.Capacity < preSize Then target.Capacity = preSize
                target.Clear()
                FillRangesInto(ns, text, p, ranges, scratch, target)
                ranges = target
            Next
            ' Unreachable when patterns.Count > 0 (the final pattern returns above).
        End Sub

        ''' <summary>
        ''' Runs one pattern's scan over every range of <paramref name="ranges"/> and streams the
        ''' resulting sub-ranges to <paramref name="visitor"/> (the same loop body as
        ''' <see cref="FillRangesInto"/>, but calling <see cref="IFusedRangeVisitor.Visit"/> instead
        ''' of writing to a target list). The scanner writes into <paramref name="scratch"/>
        ''' (pre-cleared by <c>FindMatchesInto</c>); match offsets are slice-relative and are offset
        ''' back to the root normalized byte referential before being visited.
        ''' </summary>
        Private Shared Sub StreamRangesInto(ns As NormalizedString, text As String, p As Pattern,
                                            ranges As List(Of (Integer, Integer)), scratch As List(Of MatchInfo),
                                            visitor As IFusedRangeVisitor)
            For Each r In ranges
                Dim b1 As Integer = r.Item1
                Dim b2 As Integer = r.Item2
                If b2 <= b1 Then Continue For
                Dim n1 As Integer = ns.ByteToNetIndexCached(b1)
                Dim n2 As Integer = ns.ByteToNetIndexCached(b2)
                If n2 <= n1 Then Continue For
                p.FindMatchesInto(text, n1, n2 - n1, scratch)
                For i As Integer = 0 To scratch.Count - 1
                    Dim m As MatchInfo = scratch(i)
                    Dim mb1 As Integer = m.Start
                    Dim mb2 As Integer = m.End
                    If mb2 > mb1 Then
                        visitor.Visit(b1 + mb1, b1 + mb2)
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' M2 range-driven twin of the fused pre-tokenization: computes the final ranges (byte
        ''' offsets in each untokenized split's normalized text) that
        ''' <see cref="FuseIsolatedSplitsCore"/> would produce, WITHOUT materializing the per-range
        ''' <see cref="Split"/> / <see cref="NormalizedString"/> pieces (the dominant allocation of
        ''' the fused pass, which escapes to <c>Me.Splits</c> and cannot be stack-eliminated). Splits
        ''' that already carry tokens are skipped — their count is contributed by
        ''' <see cref="CountFusedRanges"/>. The count-only fast path
        ''' (<c>Tokenizer.EncodeCount</c>) feeds the model directly from these ranges.
        ''' </summary>
        Friend Function FusedRangesBySplit(patterns As List(Of Pattern)) As List(Of (NormalizedString, List(Of (Integer, Integer))))
            Dim result As New List(Of (NormalizedString, List(Of (Integer, Integer))))()
            For Each split As Split In Me.Splits
                If split.Tokens IsNot Nothing Then Continue For
                result.Add((split.Normalized, ComputeFusedRanges(split.Normalized, patterns)))
            Next
            Return result
        End Function

        ''' <summary>
        ''' Counts the tokens the ranges produced by <see cref="FusedRangesBySplit"/> would yield:
        ''' splits that already carry attached tokens contribute their token count; every other
        ''' range builds its byte-mapped normalized string via
        ''' <see cref="NormalizedString.ToByteMappedString"/> and calls <paramref name="countFn"/>.
        ''' Returns exactly the total that <c>FuseIsolatedSplitsWithByteMap</c> followed by
        ''' <see cref="TokenizeCount"/> would report, provided <c>countFn(s) = Model.CountTokens(s)</c>,
        ''' but without constructing a piece object per range.
        ''' </summary>
        Friend Function CountFusedRanges(rangesBySplit As List(Of (NormalizedString, List(Of (Integer, Integer)))),
                                         countFn As Func(Of String, Integer)) As Integer
            Dim total As Integer = 0
            For Each split As Split In Me.Splits
                If split.Tokens IsNot Nothing Then
                    total += split.Tokens.Count
                End If
            Next
            For Each pair As (NormalizedString, List(Of (Integer, Integer))) In rangesBySplit
                Dim ns As NormalizedString = pair.Item1
                For Each r As (Integer, Integer) In pair.Item2
                    If r.Item2 > r.Item1 Then
                        total += countFn(ns.ToByteMappedString(r.Item1, r.Item2))
                    End If
                Next
            Next
            Return total
        End Function

        ''' <summary>
        ''' M8 streaming twin of <see cref="FusedRangesBySplit"/>: computes the final ranges (byte
        ''' offsets in each untokenized split's normalized text) that
        ''' <see cref="FuseIsolatedSplitsCore"/> would produce, WITHOUT materializing the per-range
        ''' list or the per-piece <see cref="Split"/> / <see cref="NormalizedString"/> objects.
        ''' Splits that already carry tokens are skipped. Each final range is streamed to
        ''' <paramref name="visitor"/> (<see cref="IFusedRangeVisitor.BeginSplit"/> once per split,
        ''' then <see cref="IFusedRangeVisitor.Visit"/> per range) as the final pattern's scan
        ''' produces it.
        ''' </summary>
        Friend Sub StreamFusedRangesBySplit(patterns As List(Of Pattern), visitor As IFusedRangeVisitor)
            For Each split As Split In Me.Splits
                If split.Tokens IsNot Nothing Then Continue For
                visitor.BeginSplit(split.Normalized)
                FuseRangesStreaming(split.Normalized, patterns, visitor)
            Next
        End Sub

        ''' <summary>
        ''' M8 streaming twin of <see cref="CountFusedRanges"/>: counts the tokens the fused ranges
        ''' would yield without materializing the per-range list. Splits that already carry attached
        ''' tokens contribute their token count; every other split's final ranges are streamed to
        ''' <paramref name="visitor"/> (a reusable <see cref="FusedRangeCountVisitor"/>), which
        ''' builds each range's byte-mapped string via
        ''' <see cref="NormalizedString.ToByteMappedString"/> and calls its count function. Returns
        ''' exactly the total that <c>FuseIsolatedSplitsWithByteMap</c> followed by
        ''' <see cref="TokenizeCount"/> would report. The visitor must be
        ''' <see cref="FusedRangeCountVisitor.Reset"/> before each call.
        ''' </summary>
        Friend Function CountFusedRangesStreaming(patterns As List(Of Pattern), visitor As FusedRangeCountVisitor) As Integer
            Dim total As Integer = 0
            For Each split As Split In Me.Splits
                If split.Tokens IsNot Nothing Then
                    total += split.Tokens.Count
                Else
                    visitor.BeginSplit(split.Normalized)
                    FuseRangesStreaming(split.Normalized, patterns, visitor)
                End If
            Next
            Return total + visitor.Total
        End Function

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

    ''' <summary>
    ''' Reusable <see cref="IFusedRangeVisitor"/> for the M8 range-driven count path: each
    ''' <see cref="Visit"/> maps the range's bytes to chars and counts it via the held
    ''' <see cref="IModel"/> directly (no delegate), summing into <see cref="Total"/>. State is
    ''' fields, not captured locals, so the instance is zero-allocation reusable across calls — no
    ''' closure and no per-call <see cref="Func(Of String, Integer)"/> delegate; the caller resets
    ''' per-call state via <see cref="Reset"/> before each encode and reads <see cref="Total"/>
    ''' afterwards. M9: when the model is a <see cref="BpeModel"/> (the DeepSeek count path), the
    ''' mapped chars are written into a pooled per-thread buffer (<see cref="_mapBuffer"/>) and
    ''' counted via <see cref="BpeModel.CountTokensSpan"/> over a <see cref="ReadOnlySpan(Of Char)"/>,
    ''' so a word-cache hit (~96%) never materializes the mapped <see cref="String"/>; non-BPE models
    ''' keep the unchanged <see cref="NormalizedString.ToByteMappedString"/> + String path.
    ''' </summary>
    Friend NotInheritable Class FusedRangeCountVisitor
        Implements IFusedRangeVisitor

        ''' <summary>The model whose <c>CountTokens</c> counts each range's mapped string; set by <see cref="Reset"/>.</summary>
        Private _model As IModel
        ''' <summary>The model when it is a <see cref="BpeModel"/> (the M9 span fast path); Nothing for non-BPE models.</summary>
        Private _bpe As BpeModel
        ''' <summary>M9: per-thread reusable byte-mapped char buffer. The visitor is per-thread
        ''' (a <see cref="ThreadLocal(Of T)"/> in <c>Tokenizer</c>), so this array is never shared
        ''' across threads. Grown on demand and retained across ranges; the span over it is consumed
        ''' synchronously by <see cref="BpeModel.CountTokensSpan"/> before the next
        ''' <see cref="Visit"/>, so reuse cannot alias a cached value (the BPE cache stores only
        ''' materialized Strings, never this buffer).</summary>
        Private _mapBuffer As Char()
        ''' <summary>Accumulated token count over the ranges visited since the last <see cref="Reset"/>.</summary>
        Public Total As Integer

        Private _ns As NormalizedString

        ''' <summary>Resets per-call state; the instance is then ready for a new encode.</summary>
        Public Sub Reset(model As IModel)
            Me._model = model
            Me._bpe = TryCast(model, BpeModel)
            Me.Total = 0
            Me._ns = Nothing
        End Sub

        Public Sub BeginSplit(normalized As NormalizedString) Implements IFusedRangeVisitor.BeginSplit
            Me._ns = normalized
        End Sub

        Public Sub Visit(startByte As Integer, endByte As Integer) Implements IFusedRangeVisitor.Visit
            If endByte > startByte Then
                If Me._bpe IsNot Nothing Then
                    ' M9: map into the pooled buffer and count via a ReadOnlyMemory over it — a BPE
                    ' word-cache hit (~96%) resolves with zero String allocation. The memory is a
                    ' stack struct over the buffer; it is consumed synchronously by
                    ' CountTokensMemory before the next Visit, so the buffer reuse cannot alias a
                    ' cached value (the BPE cache stores only materialized Strings).
                    Dim count As Integer = _ns.MapToBuffer(startByte, endByte, _mapBuffer)
                    Total += _bpe.CountTokensMemory(_mapBuffer.AsMemory(0, count))
                Else
                    Total += _model.CountTokens(_ns.ToByteMappedString(startByte, endByte))
                End If
            End If
        End Sub
    End Class

End Namespace
