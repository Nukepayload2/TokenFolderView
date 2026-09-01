Imports System.Buffers
Imports System.Linq
Imports System.Text
Imports System.Threading

Namespace Internal

    ''' <summary>
    ''' A <c>NormalizedString</c> takes care of processing an "original" string to modify it and
    ''' obtain a "normalized" string. It keeps both versions, alignment information between them,
    ''' and provides an interface to retrieve ranges of each string using offsets from either.
    ''' Faithful port of the Rust <c>tokenizer::normalizer::NormalizedString</c>.
    ''' </summary>
    Public Class NormalizedString

        Private _original As String
        Private _normalized As String
        ''' <summary>Mapping from normalized string to original one: (start, end) byte range for each byte of the normalized string.</summary>
        Private _alignments As List(Of (Integer, Integer))
        ''' <summary>Track of the missing part when this is a slice of a bigger NormalizedString.</summary>
        Private _originalShift As Integer
        ''' <summary>
        ''' When False (set only by the offset-free <see cref="OffsetType.None"/> encode path), the
        ''' alignments are never read downstream, so <see cref="Slice"/> and <see cref="Transform"/>
        ''' skip building them. The alignment is a per-byte (start, end) List; skipping it avoids the
        ''' dominant per-piece allocation of the pre-tokenization hot path. When True (the default),
        ''' behaviour is byte-identical to a fully tracked NormalizedString.
        ''' </summary>
        Private _trackAlignments As Boolean = True

        ' ---- Lazy no-track slice view. ----
        ' A no-track Slice does not eagerly materialize the piece's _original / _normalized
        ' substrings. Instead it stores a (root, byte-range) view; the substrings are materialized
        ' on first access via the accessors below. In the count-only path the piece's _original is
        ' never read (so it is never materialized) and _normalized is read once by the ByteLevel
        ' transform, which iterates the view directly (AppendByteTransform). Kept only when
        ' _trackAlignments = False (a fully-tracked Slice stays eager); _root = Nothing otherwise.
        Private _root As NormalizedString
        Private _viewNormStart As Integer
        Private _viewNormEnd As Integer
        Private _viewOrigStart As Integer
        Private _viewOrigEnd As Integer

        ''' <summary>
        ''' Single shared empty alignment list for no-track slices/transforms. The no-track path
        ''' never mutates _alignments (it only replaces it with another empty list), so all
        ''' no-track pieces can share one instance instead of allocating one empty List per piece.
        ''' </summary>
        Private Shared ReadOnly _emptyAlignments As New List(Of (Integer, Integer))()

        ' ---- Lazy caches for the hot byte<->net conversion helpers. ----
        ' NormalizedString.Slice is called once per pre-tokenizer piece (O(n) pieces), so the
        ' static Utf8Helpers.ByteToNetIndex / IsUtf8CharBoundary (each O(n) from the string
        ' start) made splitting an O(n^2) operation. These caches turn those lookups into
        ' O(log n) binary searches, built once per NormalizedString. The cache stores plain
        ' integer arrays (scalar-start byte offsets and net indices), NOT ScalarInfo lists, so
        ' building it never allocates per-scalar strings.
        '
        ' A NormalizedString is created per-Encode and not shared across threads, but the
        ' check-then-act lazy build is hardened anyway (reference-typed index + Volatile reads/
        ' writes + an Interlocked publish) so a concurrent reader can never observe a partially
        ' published cache or tear a multi-word struct. Building is idempotent (derived purely
        ' from the immutable string), so a lost compare-exchange is harmless.
        Private NotInheritable Class ScalarBoundaryIndex
            ''' <summary>Byte offset of each non-ASCII scalar start (empty when the string is pure ASCII).</summary>
            Public ReadOnly BreakBytes As Integer()
            ''' <summary>Cumulative byte-over-net excess (byteOff - net) BEFORE the scalar at the matching <see cref="BreakBytes"/> entry.</summary>
            Public ReadOnly BreakExcess As Integer()
            ''' <summary>UTF-8 byte length of the non-ASCII scalar at the matching <see cref="BreakBytes"/> entry.</summary>
            Public ReadOnly BreakLens As Integer()
            ''' <summary>Total UTF-8 byte length of the string.</summary>
            Public ReadOnly Utf8Len As Integer

            Public Sub New(breakBytes As Integer(), breakExcess As Integer(), breakLens As Integer(), utf8Len As Integer)
                Me.BreakBytes = breakBytes
                Me.BreakExcess = breakExcess
                Me.BreakLens = breakLens
                Me.Utf8Len = utf8Len
            End Sub
        End Class

        Private _originalIndex As ScalarBoundaryIndex
        Private _normalizedIndex As ScalarBoundaryIndex
        ''' <summary>Utf8 length of the original string, or -1 when not yet computed.</summary>
        Private _originalUtf8Len As Integer = -1
        ''' <summary>Utf8 length of the normalized string, or -1 when not yet computed.</summary>
        Private _normalizedUtf8Len As Integer = -1

        Private Sub New()
            _original = String.Empty
            _normalized = String.Empty
            _alignments = New List(Of (Integer, Integer))()
            _originalShift = 0
        End Sub

        ' ------------------------------------------------------------------
        ' Construction
        ' ------------------------------------------------------------------

        ''' <summary>Builds a NormalizedString from a string, with identity alignments.</summary>
        Public Shared Function FromString(s As String) As NormalizedString
            Dim n As New NormalizedString()
            n._original = s
            n._normalized = s
            n._originalShift = 0
            n._alignments = New List(Of (Integer, Integer))(Utf8Helpers.Utf8Length(s))
            For Each sc In Utf8Helpers.EnumerateScalars(s)
                Dim start As Integer = sc.Utf8Start
                Dim [end] As Integer = sc.Utf8Start + sc.Utf8Len
                For k As Integer = 0 To sc.Utf8Len - 1
                    n._alignments.Add((start, [end]))
                Next
            Next
            Return n
        End Function

        ''' <summary>
        ''' Builds a NormalizedString from a string WITHOUT the per-byte identity alignment list and
        ''' with alignment tracking disabled. Only for the offset-free count-only path (the M3
        ''' no-track extract): that path never reads the alignment list, so skipping it removes the
        ''' dominant per-encode allocation (one (start, end) tuple per UTF-8 byte). The original and
        ''' normalized strings are the same reference (identity). A no-track NormalizedString that
        ''' later needs the alignment list throws <see cref="OffsetTrackingRequiredException"/> (the
        ''' existing count-only fallback contract), so configurations that need alignments fall back
        ''' to a fully tracked encode via <see cref="Tokenizer.EncodeCount"/> and stay correct.
        ''' </summary>
        Public Shared Function FromStringNoTrack(s As String) As NormalizedString
            Dim n As New NormalizedString()
            n._original = s
            n._normalized = s
            n._originalShift = 0
            n._trackAlignments = False
            n._alignments = _emptyAlignments
            Return n
        End Function

        ' ------------------------------------------------------------------
        #Region "Basic accessors"
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Materializes the normalized substring of a lazy no-track view on first access. For an
        ''' eager NormalizedString (or one already materialized) this is a no-op.
        ''' </summary>
        Private Sub MaterializeNormalized()
            If _normalized Is Nothing AndAlso _root IsNot Nothing Then
                _root.MaterializeNormalized()
                ' Use the root's cached scalar-boundary index (binary search) instead of the
                ' O(n) Utf8Helpers.SliceByUtf8. Semantics are byte-identical (ByteToNetCached
                ' matches ByteToNetIndex, flooring mid-scalar offsets to the scalar start); the
                ' index is built once per root and shared by every piece, so materializing a
                ' match-heavy piece (many pieces of one large root) is O(log n) instead of O(n).
                _normalized = SliceByUtf8Cached(_root.NormalizedIndex(), _root._normalized, _viewNormStart, _viewNormEnd)
                Volatile.Write(_normalizedUtf8Len, _viewNormEnd - _viewNormStart)
            End If
        End Sub

        ''' <summary>
        ''' Materializes the original substring of a lazy no-track view on first access. For an
        ''' eager NormalizedString (or one already materialized) this is a no-op.
        ''' </summary>
        Private Sub MaterializeOriginal()
            If _original Is Nothing AndAlso _root IsNot Nothing Then
                _root.MaterializeOriginal()
                ' Same binary-search slicing as MaterializeNormalized (see there).
                _original = SliceByUtf8Cached(_root.OriginalIndex(), _root._original, _viewOrigStart, _viewOrigEnd)
            End If
        End Sub

        ''' <summary>Returns the normalized string.</summary>
        Public ReadOnly Property [Get]() As String
            Get
                MaterializeNormalized()
                Return _normalized
            End Get
        End Property

        ''' <summary>Returns the original string.</summary>
        Public ReadOnly Property Original() As String
            Get
                MaterializeOriginal()
                Return _original
            End Get
        End Property

        ''' <summary>Returns the original offsets of this NormalizedString.</summary>
        Public Function OffsetsOriginal() As (Integer, Integer)
            Return (_originalShift, _originalShift + LenOriginal())
        End Function

        ''' <summary>
        ''' Access to the alignment list: one (start, end) entry per byte of the normalized
        ''' string, mapping that normalized byte to a byte range in the original string.
        ''' </summary>
        Public ReadOnly Property Alignments() As List(Of (Integer, Integer))
            Get
                Return _alignments
            End Get
        End Property

        ''' <summary>Length of the normalized string in UTF-8 bytes.</summary>
        Public Function Len() As Integer
            If _normalized Is Nothing AndAlso _root IsNot Nothing Then Return _viewNormEnd - _viewNormStart
            Return Utf8Helpers.Utf8Length(_normalized)
        End Function

        ''' <summary>
        ''' Enables or disables alignment tracking on this instance. When disabled, subsequent
        ''' <see cref="Slice"/> and <see cref="Transform"/> calls do not build the per-byte
        ''' alignment list (used by the offset-free <see cref="OffsetType.None"/> count path, where
        ''' alignments are never read). Slices inherit the flag from their source.
        ''' </summary>
        Friend Sub SetTrackAlignments(value As Boolean)
            _trackAlignments = value
        End Sub

        ''' <summary>Length of the original string in UTF-8 bytes.</summary>
        Public Function LenOriginal() As Integer
            If _original Is Nothing AndAlso _root IsNot Nothing Then Return _viewOrigEnd - _viewOrigStart
            Return Utf8Helpers.Utf8Length(_original)
        End Function

        ''' <summary>Whether the normalized string is empty.</summary>
        Public Function IsEmpty() As Boolean
            If _normalized Is Nothing AndAlso _root IsNot Nothing Then Return _viewNormEnd <= _viewNormStart
            Return _normalized.Length = 0
        End Function

        ''' <summary>Whether the original string is empty (O(1); a lazy view is not materialized).</summary>
        Private Function IsOriginalEmpty() As Boolean
            If _original Is Nothing AndAlso _root IsNot Nothing Then Return _viewOrigEnd <= _viewOrigStart
            Return _original.Length = 0
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Cached byte<->net conversions (hot paths)"
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Builds a scalar boundary index for <paramref name="s"/>. The index is stored as a SPARSE
        ''' breakpoint list rather than one entry per scalar: between two consecutive non-ASCII
        ''' scalars the byte→net excess is constant, so only the non-ASCII scalar starts need to be
        ''' recorded. Real code is mostly ASCII with scattered non-ASCII (comments/strings), so the
        ''' breakpoint list is typically orders of magnitude smaller than the scalar count — the
        ''' dominant per-encode allocation of the fused split pass (two <see cref="Integer"/> arrays
        ''' of scalar-count length before this change) shrinks to a handful of entries. A pure-ASCII
        ''' string has an empty breakpoint list and is handled by <see cref="ByteToNetCached"/> /
        ''' <see cref="IsBoundaryCached"/> as byte==net identity.
        '''
        ''' Each breakpoint records the non-ASCII scalar's start byte offset, its UTF-8 byte length
        ''' and the cumulative excess (byteOff - net) BEFORE it. A query byte offset b maps to the
        ''' net index as: b - excessBefore when b is inside the scalar, or b - excessAfter (the next
        ''' breakpoint's excessBefore, or the final total excess) when b lies in the ASCII run after
        ''' it. This reproduces the full per-scalar index exactly for every boundary query.
        ''' </summary>
        Private Shared Function BuildScalarIndex(s As String) As ScalarBoundaryIndex
            If s Is Nothing OrElse s.Length = 0 Then
                Return New ScalarBoundaryIndex(Array.Empty(Of Integer)(), Array.Empty(Of Integer)(), Array.Empty(Of Integer)(), 0)
            End If
            ' Pass 1: count the non-ASCII scalars (utf8Len > netLen) to size the arrays exactly.
            Dim net As Integer = 0
            Dim nonAscii As Integer = 0
            While net < s.Length
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                net += Utf8Helpers.NetLengthOfCodePoint(cp)
                If Utf8Helpers.Utf8LengthOfCodePoint(cp) > 1 Then nonAscii += 1
            End While
            If nonAscii = 0 Then
                Return New ScalarBoundaryIndex(Array.Empty(Of Integer)(), Array.Empty(Of Integer)(), Array.Empty(Of Integer)(), s.Length)
            End If
            ' Pass 2: emit one breakpoint per non-ASCII scalar.
            Dim breaks(nonAscii - 1) As Integer
            Dim excesses(nonAscii - 1) As Integer
            Dim lens(nonAscii - 1) As Integer
            Dim byteOff As Integer = 0
            net = 0
            Dim excess As Integer = 0
            Dim idx As Integer = 0
            While net < s.Length
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                Dim utf8Len As Integer = Utf8Helpers.Utf8LengthOfCodePoint(cp)
                Dim netLen As Integer = Utf8Helpers.NetLengthOfCodePoint(cp)
                If utf8Len > netLen Then
                    breaks(idx) = byteOff
                    excesses(idx) = excess ' excess BEFORE this scalar (= byteOff - net)
                    lens(idx) = utf8Len
                    idx += 1
                End If
                byteOff += utf8Len
                net += netLen
                excess += utf8Len - netLen
            End While
            Return New ScalarBoundaryIndex(breaks, excesses, lens, byteOff)
        End Function

        ''' <summary>Lazily built scalar boundary index for the original string (never mutates).</summary>
        Private Function OriginalIndex() As ScalarBoundaryIndex
            MaterializeOriginal()
            Dim idx As ScalarBoundaryIndex = Volatile.Read(_originalIndex)
            If idx Is Nothing Then
                Dim built As ScalarBoundaryIndex = BuildScalarIndex(_original)
                Dim existing As ScalarBoundaryIndex = Interlocked.CompareExchange(_originalIndex, built, Nothing)
                If existing IsNot Nothing Then Return existing
                idx = built
            End If
            Return idx
        End Function

        ''' <summary>Lazily built scalar boundary index for the normalized string (invalidated on transform).</summary>
        Private Function NormalizedIndex() As ScalarBoundaryIndex
            MaterializeNormalized()
            Dim idx As ScalarBoundaryIndex = Volatile.Read(_normalizedIndex)
            If idx Is Nothing Then
                Dim built As ScalarBoundaryIndex = BuildScalarIndex(_normalized)
                Dim existing As ScalarBoundaryIndex = Interlocked.CompareExchange(_normalizedIndex, built, Nothing)
                If existing IsNot Nothing Then Return existing
                idx = built
            End If
            Return idx
        End Function

        Private Function OriginalUtf8LenCached() As Integer
            If _original Is Nothing AndAlso _root IsNot Nothing Then Return _viewOrigEnd - _viewOrigStart
            Dim v As Integer = Volatile.Read(_originalUtf8Len)
            If v < 0 Then
                v = Utf8Helpers.Utf8Length(_original)
                Volatile.Write(_originalUtf8Len, v)
            End If
            Return v
        End Function

        Private Function NormalizedUtf8LenCached() As Integer
            If _normalized Is Nothing AndAlso _root IsNot Nothing Then Return _viewNormEnd - _viewNormStart
            Dim v As Integer = Volatile.Read(_normalizedUtf8Len)
            If v < 0 Then
                v = Utf8Helpers.Utf8Length(_normalized)
                Volatile.Write(_normalizedUtf8Len, v)
            End If
            Return v
        End Function

        ''' <summary>Invalidates the normalized-side caches (the original side never mutates).</summary>
        Private Sub InvalidateNormalizedCaches()
            Volatile.Write(_normalizedIndex, Nothing)
            Volatile.Write(_normalizedUtf8Len, -1)
        End Sub

        ''' <summary>
        ''' Converts a UTF-8 byte offset in the normalized string to a .NET (UTF-16) index using
        ''' the cached scalar-boundary index (O(log n) binary search). Internal: used by the fused
        ''' pre-tokenizer split path (<see cref="PreTokenizedString.FuseIsolatedSplits"/>) to
        ''' extract per-piece substrings for the next pattern's scan.
        ''' </summary>
        Friend Function ByteToNetIndexCached(byteOffset As Integer) As Integer
            Return ByteToNetCached(NormalizedIndex(), _normalized, byteOffset)
        End Function

        ''' <summary>
        ''' Cursor-aware twin of <see cref="ByteToNetIndexCached"/> for MONOTONICALLY NON-DECREASING
        ''' byte offsets: the fused range scan visits ranges in ascending byte order, so a shared
        ''' cursor (the index into <see cref="ScalarBoundaryIndex.BreakBytes"/> of the last breakpoint
        ''' at or below the previous query; start at -1) turns consecutive lookups into an amortized
        ''' O(1) linear advance instead of an O(log n) binary search. The caller MUST guarantee
        ''' <c>byteOffset >= every earlier call's byteOffset</c> within the same cursor's lifetime.
        ''' </summary>
        Friend Function ByteToNetIndexCachedMonotonic(byteOffset As Integer, ByRef cursor As Integer) As Integer
            Return ByteToNetCachedMonotonic(NormalizedIndex(), _normalized, byteOffset, cursor)
        End Function

        ''' <summary>
        ''' Converts a UTF-8 byte offset to a .NET string index using a cached boundary index.
        ''' Semantics match <see cref="Utf8Helpers.ByteToNetIndex"/>: floors to the enclosing scalar
        ''' start. A pure-ASCII index (empty breakpoints, from <see cref="BuildScalarIndex"/>) maps
        ''' byte offset to net index by identity; a sparse breakpoint index maps b to
        ''' <c>b - excess</c>, where the excess is constant between non-ASCII scalars (excessBefore
        ''' inside the scalar, excessAfter in the ASCII run following it).
        ''' </summary>
        Private Shared Function ByteToNetCached(index As ScalarBoundaryIndex, s As String, byteOffset As Integer) As Integer
            If byteOffset <= 0 Then Return 0
            If byteOffset >= index.Utf8Len Then Return s.Length
            If index.BreakBytes.Length = 0 Then Return byteOffset ' pure ASCII: identity
            ' Binary search for the last breakpoint whose start byte offset is <= byteOffset.
            Dim lo As Integer = 0
            Dim hi As Integer = index.BreakBytes.Length - 1
            Dim i As Integer = -1
            While lo <= hi
                Dim mid As Integer = (lo + hi) \ 2
                If index.BreakBytes(mid) <= byteOffset Then
                    i = mid
                    lo = mid + 1
                Else
                    hi = mid - 1
                End If
            End While
            If i < 0 Then Return byteOffset ' before the first non-ASCII scalar: excess is 0
            ' Inside (or at the start of) the non-ASCII scalar: floor to the scalar's net start.
            If byteOffset < index.BreakBytes(i) + index.BreakLens(i) Then
                Return index.BreakBytes(i) - index.BreakExcess(i)
            End If
            ' In the ASCII run after the scalar: excess is constant at the value AFTER the scalar
            ' (== the next breakpoint's excessBefore, or the final total excess).
            Dim excessAfter As Integer
            If i + 1 < index.BreakBytes.Length Then
                excessAfter = index.BreakExcess(i + 1)
            Else
                excessAfter = index.Utf8Len - s.Length
            End If
            Return byteOffset - excessAfter
        End Function

        ''' <summary>
        ''' Monotonic twin of <see cref="ByteToNetCached"/>; see
        ''' <see cref="ByteToNetIndexCachedMonotonic"/> for the cursor contract.
        ''' </summary>
        Private Shared Function ByteToNetCachedMonotonic(index As ScalarBoundaryIndex, s As String,
                                                         byteOffset As Integer, ByRef cursor As Integer) As Integer
            If byteOffset <= 0 Then Return 0
            If byteOffset >= index.Utf8Len Then Return s.Length
            If index.BreakBytes.Length = 0 Then Return byteOffset ' pure ASCII: identity
            ' Advance the cursor: the last breakpoint start <= byteOffset. Breakpoints are sorted
            ' and byteOffset is non-decreasing, so the cursor only ever moves forward.
            While cursor + 1 < index.BreakBytes.Length AndAlso index.BreakBytes(cursor + 1) <= byteOffset
                cursor += 1
            End While
            If cursor < 0 Then Return byteOffset ' before the first non-ASCII scalar: excess is 0
            If byteOffset < index.BreakBytes(cursor) + index.BreakLens(cursor) Then
                Return index.BreakBytes(cursor) - index.BreakExcess(cursor)
            End If
            ' In the ASCII run after the scalar: excess is constant at the value AFTER the scalar
            ' (== the next breakpoint's excessBefore, or the final total excess).
            Dim excessAfter As Integer
            If cursor + 1 < index.BreakBytes.Length Then
                excessAfter = index.BreakExcess(cursor + 1)
            Else
                excessAfter = index.Utf8Len - s.Length
            End If
            Return byteOffset - excessAfter
        End Function

        ''' <summary>
        ''' Whether the given UTF-8 byte offset lies on a scalar boundary, using a cached boundary index.
        ''' Semantics match <see cref="Utf8Helpers.IsUtf8CharBoundary"/>.
        ''' </summary>
        Private Shared Function IsBoundaryCached(index As ScalarBoundaryIndex, byteOffset As Integer) As Boolean
            If byteOffset <= 0 OrElse byteOffset >= index.Utf8Len Then Return True
            If index.BreakBytes.Length = 0 Then Return True ' pure ASCII: every byte offset is a scalar boundary
            ' Binary search for the last breakpoint whose start byte offset is <= byteOffset.
            Dim lo As Integer = 0
            Dim hi As Integer = index.BreakBytes.Length - 1
            Dim i As Integer = -1
            While lo <= hi
                Dim mid As Integer = (lo + hi) \ 2
                If index.BreakBytes(mid) <= byteOffset Then
                    i = mid
                    lo = mid + 1
                Else
                    hi = mid - 1
                End If
            End While
            If i < 0 Then Return True ' before the first non-ASCII scalar: all bytes are ASCII boundaries
            If byteOffset = index.BreakBytes(i) Then Return True ' the scalar's start is a boundary
            Return byteOffset >= index.BreakBytes(i) + index.BreakLens(i) ' inside the scalar -> not a boundary
        End Function

        ''' <summary>
        ''' Slices the string by a UTF-8 byte range using a cached boundary index.
        ''' Semantics match <see cref="Utf8Helpers.SliceByUtf8"/>.
        ''' </summary>
        Private Shared Function SliceByUtf8Cached(index As ScalarBoundaryIndex, s As String, startByte As Integer, endByte As Integer) As String
            Dim startNet As Integer = ByteToNetCached(index, s, startByte)
            Dim endNet As Integer = ByteToNetCached(index, s, endByte)
            If endNet <= startNet Then Return String.Empty
            Return s.Substring(startNet, endNet - startNet)
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Offset conversion & ranges"
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Converts the given offsets range from one referential to the other:
        ''' Original =&gt; Normalized or Normalized =&gt; Original. Returns Nothing when targeting
        ''' something that is outside range.
        ''' </summary>
        Public Function ConvertOffsets(range As OffsetRange) As (Integer, Integer)?
            Dim origLen As Integer = OriginalUtf8LenCached()
            Dim normLen As Integer = NormalizedUtf8LenCached()

            Dim target As (Integer, Integer)
            Dim original As Boolean
            If range.IsOriginal Then
                target = range.IntoFullRange(origLen)
                original = True
            Else
                target = range.IntoFullRange(normLen)
                original = False
            End If

            ' If we target an empty range, let's return the same
            If target.Item1 = target.Item2 Then Return target
            ' If the target goes reverse, return Nothing
            If target.Item1 > target.Item2 Then Return Nothing

            ' If we target 0..0 on an empty string, we want to expand to the entire equivalent.
            ' Use O(1) empty checks (a lazy view is not materialized; a large eager string is not
            ' byte-counted) — this method is on the per-piece pre-tokenization hot path.
            If original AndAlso IsOriginalEmpty() AndAlso target.Item1 = 0 AndAlso target.Item2 = 0 Then
                Return (0, normLen)
            End If
            If Not original AndAlso IsEmpty() AndAlso target.Item1 = 0 AndAlso target.Item2 = 0 Then
                Return (0, origLen)
            End If

            If original Then
                Dim start As Integer? = Nothing
                Dim [end] As Integer? = Nothing
                For i As Integer = 0 To _alignments.Count - 1
                    Dim alignment = _alignments(i)
                    If target.Item2 < alignment.Item2 Then Exit For ' take_while stops
                    If Not start.HasValue AndAlso target.Item1 <= alignment.Item1 Then
                        ' For now, don't update if width == 0
                        If alignment.Item1 <> alignment.Item2 Then
                            start = i
                        End If
                    End If
                    If target.Item2 >= alignment.Item2 Then
                        [end] = i + 1
                    End If
                Next

                Select Case True
                    Case start.HasValue AndAlso Not [end].HasValue
                        Return (start.Value, start.Value)
                    Case Not start.HasValue AndAlso [end].HasValue
                        Return ([end].Value, [end].Value)
                    Case start.HasValue AndAlso [end].HasValue
                        Return (start.Value, [end].Value)
                    Case Else
                        Return Nothing
                End Select
            Else
                If target.Item1 >= 0 AndAlso target.Item2 <= _alignments.Count AndAlso target.Item1 <= target.Item2 Then
                    Return ExpandAlignments(target.Item1, target.Item2)
                End If
                Return Nothing
            End If
        End Function

        Private Function ExpandAlignments(startIdx As Integer, endIdx As Integer) As (Integer, Integer)?
            If startIdx >= endIdx Then Return Nothing
            Dim start As Integer = _alignments(startIdx).Item1
            Dim [end] As Integer = _alignments(endIdx - 1).Item2
            Return (start, [end])
        End Function

        ''' <summary>Returns a range of the normalized string.</summary>
        Public Function GetRange(range As OffsetRange) As String
            MaterializeNormalized()
            If range.IsOriginal Then
                Dim converted = ConvertOffsets(range)
                If converted.HasValue Then
                    Return Utf8Helpers.SliceByUtf8(_normalized, converted.Value.Item1, converted.Value.Item2)
                End If
                Return Nothing
            Else
                Dim r = range.IntoFullRange(Utf8Helpers.Utf8Length(_normalized))
                Return Utf8Helpers.SliceByUtf8(_normalized, r.Item1, r.Item2)
            End If
        End Function

        ''' <summary>Returns a range of the original string.</summary>
        Public Function GetRangeOriginal(range As OffsetRange) As String
            MaterializeNormalized()
            MaterializeOriginal()
            If range.IsOriginal Then
                Dim r = range.IntoFullRange(Utf8Helpers.Utf8Length(_original))
                Return Utf8Helpers.SliceByUtf8(_original, r.Item1, r.Item2)
            Else
                Dim converted = ConvertOffsets(range)
                If converted.HasValue Then
                    Return Utf8Helpers.SliceByUtf8(_original, converted.Value.Item1, converted.Value.Item2)
                End If
                Return Nothing
            End If
        End Function

        ''' <summary>
        ''' Validates the given range, to make sure it is on scalar boundaries. Returns the
        ''' concrete full range, or Nothing when not on boundaries.
        ''' </summary>
        Private Function ValidateRange(range As OffsetRange) As OffsetRange?
            If range.IsOriginal Then
                Dim len As Integer = OriginalUtf8LenCached()
                Dim r = range.IntoFullRange(len)
                If Not IsBoundaryCached(OriginalIndex(), r.Item1) OrElse
                   Not IsBoundaryCached(OriginalIndex(), r.Item2) Then
                    Return Nothing
                End If
                Return New OffsetRange(True, r.Item1, r.Item2)
            Else
                Dim len As Integer = NormalizedUtf8LenCached()
                Dim r = range.IntoFullRange(len)
                If Not IsBoundaryCached(NormalizedIndex(), r.Item1) OrElse
                   Not IsBoundaryCached(NormalizedIndex(), r.Item2) Then
                    Return Nothing
                End If
                Return New OffsetRange(False, r.Item1, r.Item2)
            End If
        End Function

        ''' <summary>
        ''' Returns a slice of the current NormalizedString. If the range is not on scalar
        ''' boundaries, throws.
        ''' </summary>
        Public Function Slice(range As OffsetRange) As NormalizedString
            Dim fullRangeOpt As OffsetRange? = ValidateRange(range)
            If Not fullRangeOpt.HasValue Then
                Throw New InvalidOperationException("NormalizedString bad slice: range not on char boundaries.")
            End If
            Dim fullRange As OffsetRange = fullRangeOpt.Value

            Dim normalizedRange As (Integer, Integer)
            Dim originalRange As (Integer, Integer)
            If fullRange.IsOriginal Then
                Dim converted = ConvertOffsets(fullRange)
                If Not converted.HasValue Then
                    Throw SliceConversionFailure()
                End If
                normalizedRange = converted.Value
                originalRange = fullRange.IntoFullRange(OriginalUtf8LenCached())
            Else
                normalizedRange = fullRange.IntoFullRange(NormalizedUtf8LenCached())
                Dim converted = ConvertOffsets(fullRange)
                If Not converted.HasValue Then
                    Throw SliceConversionFailure()
                End If
                originalRange = converted.Value
            End If

            Dim nShift As Integer = originalRange.Item1

            Dim result As New NormalizedString()
            result._trackAlignments = Me._trackAlignments
            If Me._trackAlignments Then
                ' Fully-tracked (or eager no-track) slice: materialize both substrings eagerly,
                ' exactly as before.
                result._original = SliceByUtf8Cached(OriginalIndex(), _original, originalRange.Item1, originalRange.Item2)
                result._normalized = SliceByUtf8Cached(NormalizedIndex(), _normalized, normalizedRange.Item1, normalizedRange.Item2)
                ' Pre-size the piece's alignment list to the slice's normalized byte length so the
                ' per-piece rebuild (the pre-tokenizer Split hot path) never pays List growth
                ' doubling.
                Dim pieceLen As Integer = normalizedRange.Item2 - normalizedRange.Item1
                result._alignments = New List(Of (Integer, Integer))(pieceLen)
                For i As Integer = normalizedRange.Item1 To normalizedRange.Item2 - 1
                    result._alignments.Add((_alignments(i).Item1 - nShift, _alignments(i).Item2 - nShift))
                Next
            Else
                ' No-alignments mode (the OffsetType.None count path): defer BOTH substrings as a
                ' lazy (root, byte-range) view. The piece's _original is never read in the count
                ' path (so never materialized) and _normalized is consumed by the ByteLevel
                ' transform via AppendByteTransform without materializing a substring. `Get` /
                ' `Original` materialize on first access, so any downstream consumer sees exactly
                ' the same strings an eager slice would. The shared empty list avoids one List
                ' allocation per piece.
                result._root = Me
                result._viewNormStart = normalizedRange.Item1
                result._viewNormEnd = normalizedRange.Item2
                result._viewOrigStart = originalRange.Item1
                result._viewOrigEnd = originalRange.Item2
                result._alignments = _emptyAlignments
                ' The private ctor defaults these to String.Empty; the lazy-view sentinel is
                ' _normalized/_original = Nothing, so clear them (the view is only reachable when
                ' no-track, and both are re-materialized on first access).
                result._original = Nothing
                result._normalized = Nothing
            End If
            result._originalShift = _originalShift + originalRange.Item1
            Return result
        End Function

        ''' <summary>
        ''' Creates a no-track lazy slice view of this NormalizedString over the normalized byte
        ''' range [startByte, endByte), WITHOUT reading or building the alignment list. Only valid
        ''' when this instance is no-track AND identity-aligned (normalized bytes == original
        ''' bytes) — exactly the pieces produced by <see cref="FromStringNoTrack"/> before any
        ''' transform. The count-only path never reads a piece's original offsets, so the identity
        ''' original range (== the normalized range) is sufficient. Kept distinct from
        ''' <see cref="Slice"/> because Slice resolves offsets through the alignment list, which is
        ''' unavailable here. Both substrings materialize lazily on first access.
        ''' </summary>
        Public Function SliceNoTrack(startByte As Integer, endByte As Integer) As NormalizedString
            Dim result As New NormalizedString()
            result._trackAlignments = False
            result._root = Me
            result._viewNormStart = startByte
            result._viewNormEnd = endByte
            result._viewOrigStart = startByte
            result._viewOrigEnd = endByte
            result._alignments = _emptyAlignments
            result._original = Nothing
            result._normalized = Nothing
            result._originalShift = Me._originalShift + startByte
            Return result
        End Function

        ''' <summary>
        ''' Slices this NormalizedString by <paramref name="range"/> and applies the ByteLevel
        ''' byte→char mapping in the same step, returning a piece whose <see cref="Get"/> is the
        ''' mapped string and whose original offsets / alignments are byte-identical to
        ''' <c>Slice(range)</c> followed by the pure-map ByteLevel transform
        ''' (<c>AppendByteTransform</c> + <c>Transform(dest, 0)</c>). Used by the fused
        ''' pre-tokenizer fast path (<see cref="PreTokenizedString.FuseIsolatedSplitsWithByteMap"/>)
        ''' to fold a trailing pure-map ByteLevel pre-tokenizer into the fused split pass and skip
        ''' the independent second traversal of the pieces.
        '''
        ''' On a no-track source (the OffsetType.None count path) the mapped string is built
        ''' directly from this instance's scalars in one pass — no intermediate (Char, Integer)
        ''' transform-item list and no per-piece view re-walk. On a tracked source the piece is
        ''' produced by the exact existing ByteLevel transform (slice + AppendByteTransform +
        ''' Transform) so the per-byte alignment list is byte-identical to the sequential reference.
        ''' </summary>
        Friend Function SliceWithByteMap(range As OffsetRange) As NormalizedString
            If Not Me._trackAlignments Then
                Return SliceWithByteMapNoTrack(range)
            End If
            ' Tracked: delegate to the exact ByteLevel transform so the alignment list matches
            ' the sequential reference byte for byte.
            Dim slice As NormalizedString = Me.Slice(range)
            Dim transformations As New List(Of (Char, Integer))()
            Dim hint As Integer = slice.Len()
            If transformations.Capacity < hint Then transformations.Capacity = hint
            slice.AppendByteTransform(transformations)
            slice.Transform(transformations, 0)
            Return slice
        End Function

        Private Function SliceWithByteMapNoTrack(range As OffsetRange) As NormalizedString
            ' Mirror Slice's range resolution (the root keeps its alignment list even when
            ' no-track, so ConvertOffsets below works exactly as in Slice).
            Dim fullRangeOpt As OffsetRange? = ValidateRange(range)
            If Not fullRangeOpt.HasValue Then
                Throw New InvalidOperationException("NormalizedString bad slice: range not on char boundaries.")
            End If
            Dim fullRange As OffsetRange = fullRangeOpt.Value

            Dim normalizedRange As (Integer, Integer)
            Dim originalRange As (Integer, Integer)
            If fullRange.IsOriginal Then
                Dim converted = ConvertOffsets(fullRange)
                If Not converted.HasValue Then Throw SliceConversionFailure()
                normalizedRange = converted.Value
                originalRange = fullRange.IntoFullRange(OriginalUtf8LenCached())
            Else
                normalizedRange = fullRange.IntoFullRange(NormalizedUtf8LenCached())
                Dim converted = ConvertOffsets(fullRange)
                If Not converted.HasValue Then Throw SliceConversionFailure()
                originalRange = converted.Value
            End If

            Dim normStart As Integer = normalizedRange.Item1
            Dim normEnd As Integer = normalizedRange.Item2

            Dim result As New NormalizedString()
            result._trackAlignments = False
            result._originalShift = Me._originalShift + originalRange.Item1
            result._normalized = ToByteMappedString(normStart, normEnd)
            result._alignments = _emptyAlignments
            result._original = Nothing
            result._root = Me
            result._viewNormStart = normStart
            result._viewNormEnd = normEnd
            result._viewOrigStart = originalRange.Item1
            result._viewOrigEnd = originalRange.Item2
            Return result
        End Function

        ''' <summary>
        ''' Returns the GPT-2 byte-mapped string for the normalized byte range
        ''' <c>[startByte, endByte)</c> of this NormalizedString: each UTF-8 byte of each scalar in
        ''' the range is mapped through the byte→char table and written directly into a rented
        ''' <see cref="Char"/> buffer (ArrayPool), then copied once into the returned
        ''' <see cref="String"/>. This is the exact inner loop
        ''' <see cref="SliceWithByteMapNoTrack"/> uses to build a piece's mapped string, extracted
        ''' so the M2 range-driven count path (<see cref="PreTokenizedString.CountFusedRanges"/>)
        ''' can build the same string without materializing a piece
        ''' <see cref="NormalizedString"/>. A lazy no-track source is materialized on demand
        ''' (via <see cref="NormalizedIndex"/>), so the string is byte-identical to
        ''' <c>SliceWithByteMap(range).Get</c>. Lone surrogates map as U+FFFD (3 bytes), exactly
        ''' like <see cref="BytesToUnicodeTable.AppendByteTransformChars"/>.
        ''' </summary>
        Friend Function ToByteMappedString(startByte As Integer, endByte As Integer) As String
            If endByte <= startByte Then Return String.Empty
            Dim nBytes As Integer = endByte - startByte
            Dim buf As Char() = ArrayPool(Of Char).Shared.Rent(nBytes)
            Try
                Dim idx As ScalarBoundaryIndex = NormalizedIndex()
                Dim netA As Integer = ByteToNetCached(idx, _normalized, startByte)
                Dim netB As Integer = ByteToNetCached(idx, _normalized, endByte)
                Dim net As Integer = netA
                Dim count As Integer = 0
                While net < netB
                    Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, net)
                    count += BytesToUnicodeTable.AppendByteTransformChars(buf, count, cp)
                    net += Utf8Helpers.NetLengthOfCodePoint(cp)
                End While
                Return New String(buf, 0, count)
            Finally
                ArrayPool(Of Char).Shared.Return(buf)
            End Try
        End Function

        ''' <summary>
        ''' M9: pooled-buffer twin of <see cref="ToByteMappedString"/>: writes the GPT-2 byte-mapped
        ''' chars for the normalized byte range <c>[startByte, endByte)</c> into
        ''' <paramref name="buffer"/> (a caller-owned array, grown on demand and retained across
        ''' calls — a per-thread reusable buffer, NOT a <see cref="String"/>) and returns the char
        ''' count. The M2/M8 range-driven count path (<see cref="PreTokenizedString.FusedRangeCountVisitor"/>)
        ''' uses this to feed the BPE word cache via a <see cref="ReadOnlySpan(Of Char)"/> over the
        ''' buffer, so a cache hit never materializes the mapped string (the ~800 MB M9 target).
        ''' Char content is byte-identical to <see cref="ToByteMappedString"/> (the same inner loop,
        ''' no <c>New String</c>); a lazy no-track source is materialized on demand (via
        ''' <see cref="NormalizedIndex"/>), exactly like <see cref="ToByteMappedString"/>. The buffer
        ''' is never exposed to the model (only a span read synchronously inside the count call), so
        ''' the caller may reuse it for the next range.
        ''' </summary>
        Friend Function MapToBuffer(startByte As Integer, endByte As Integer, ByRef buffer As Char()) As Integer
            If endByte <= startByte Then Return 0
            ' One mapped Char per UTF-8 byte (a lone surrogate maps as U+FFFD, 3 bytes), so the
            ' byte-range length is a safe upper bound for the char count.
            Dim nBytes As Integer = endByte - startByte
            If buffer Is Nothing OrElse buffer.Length < nBytes Then
                buffer = New Char(nBytes - 1) {}
            End If
            Dim idx As ScalarBoundaryIndex = NormalizedIndex()
            Dim netA As Integer = ByteToNetCached(idx, _normalized, startByte)
            Dim netB As Integer = ByteToNetCached(idx, _normalized, endByte)
            Dim net As Integer = netA
            Dim count As Integer = 0
            While net < netB
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, net)
                count += BytesToUnicodeTable.AppendByteTransformChars(buffer, count, cp)
                net += Utf8Helpers.NetLengthOfCodePoint(cp)
            End While
            Return count
        End Function

        ''' <summary>
        ''' Returns the exception to throw when <see cref="ConvertOffsets"/> fails during
        ''' <see cref="Slice"/>. On a no-track NormalizedString this is the dedicated
        ''' <see cref="OffsetTrackingRequiredException"/> (a signal to the offset-free fast path to
        ''' fall back to a fully-tracked encode); on a tracked NormalizedString it is a genuine
        ''' "bad slice" error and must remain an <see cref="InvalidOperationException"/>.
        ''' </summary>
        Private Function SliceConversionFailure() As Exception
            If Not _trackAlignments Then
                Return New OffsetTrackingRequiredException(
                    "Slicing a no-track NormalizedString requires the alignment list, which was skipped.")
            End If
            Return New InvalidOperationException("NormalizedString bad slice: cannot convert offsets.")
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Transformations"
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Applies transformations to the current normalized version of the string while
        ''' updating the alignments. The <c>dest</c> stream yields each scalar of the new
        ''' normalized string with a change value: 0 replaces the current char, positive inserts a
        ''' new char (with the previous alignment), negative replaces the current char and removes
        ''' that many following chars. <paramref name="initialOffset"/> is the number of chars
        ''' removed at the very beginning of the transformed range.
        ''' </summary>
        Public Sub Transform(dest As IEnumerable(Of (String, Integer)), initialOffset As Integer)
            Me.TransformRange(OffsetRange.WholeOriginal(), dest, initialOffset)
        End Sub

        ''' <summary>Applies transformations over a specific byte range.</summary>
        Public Sub TransformRange(range As OffsetRange, dest As IEnumerable(Of (String, Integer)), initialOffset As Integer)
            If Not _trackAlignments Then
                ' No-alignments mode: see the (Char, Integer) overload. Only a whole-range
                ' transform is supported; the result normalized string is the concatenation of the
                ' dest strings (a whole-range splice replaces the entire normalized text). The
                ' source's normalized byte length is read through Len() so a lazy no-track view is
                ' never materialized here.
                Dim normLen As Integer = Len()
                Dim isWhole As Boolean
                If range.IsOriginal Then
                    isWhole = (range.Start = 0 AndAlso range.End = -1)
                Else
                    Dim r As (Integer, Integer) = range.IntoFullRange(normLen)
                    isWhole = (r.Item1 = 0 AndAlso r.Item2 = normLen)
                End If
                If initialOffset = 0 AndAlso isWhole Then
                    Dim b As New StringBuilder()
                    For Each item In dest
                        b.Append(item.Item1)
                    Next
                    _normalized = b.ToString()
                    _alignments = _emptyAlignments
                    InvalidateNormalizedCaches()
                    Return
                End If
                Throw New OffsetTrackingRequiredException("Partial transform on a no-track NormalizedString.")
            End If

            Dim nRange As (Integer, Integer)
            If Not range.IsOriginal Then
                nRange = range.IntoFullRange(Utf8Helpers.Utf8Length(_normalized))
            Else
                Dim converted = ConvertOffsets(range)
                If Not converted.HasValue Then Return
                nRange = converted.Value
            End If

            Dim netStart As Integer = Utf8Helpers.ByteToNetIndex(_normalized, nRange.Item1)
            Dim netEnd As Integer = Utf8Helpers.ByteToNetIndex(_normalized, nRange.Item2)

            ' Mirrors the Rust `(&mut iter).take(initial_offset)` call: the first
            ' `initial_offset` characters of the replaced range are dropped before the loop.
            ' The scalar count in range is only needed when initialOffset > 0 (the common
            ' whole-range ByteLevel transform uses initialOffset = 0, so the scan is skipped).
            Dim replacedNet As Integer = netStart
            Dim initialRemoved As Integer = 0
            Dim toSkip As Integer = 0
            If initialOffset > 0 Then
                Dim scanNet As Integer = netStart
                Dim scalarCountInRange As Integer = 0
                While scanNet < netEnd
                    scanNet += Utf8Helpers.NetLengthOfCodePoint(UnicodePredicates.ScalarCodePoint(_normalized, scanNet))
                    scalarCountInRange += 1
                End While
                toSkip = Math.Min(initialOffset, scalarCountInRange)
                For k As Integer = 0 To toSkip - 1
                    Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                    initialRemoved += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                    replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                Next
            End If

            Dim offset As Integer = initialRemoved + nRange.Item1

            ' Build ONE alignment list for the result: a whole-range transform publishes it
            ' directly (no List.InsertRange temporary array); a partial transform pre-copies the
            ' prefix and appends the suffix. The new part cannot be pre-sized because dest is a
            ' single-pass IEnumerable.
            Dim oldAlignments As List(Of (Integer, Integer)) = _alignments
            Dim spliceStart As Integer = nRange.Item1
            Dim spliceEnd As Integer = nRange.Item2
            If spliceStart < 0 OrElse spliceEnd > oldAlignments.Count OrElse spliceStart > spliceEnd Then
                Throw New InvalidOperationException("NormalizedString bad transform range.")
            End If
            Dim isWholeRange As Boolean = (spliceStart = 0) AndAlso (spliceEnd = oldAlignments.Count)
            Dim target As List(Of (Integer, Integer))
            If isWholeRange Then
                target = New List(Of (Integer, Integer))()
            Else
                target = New List(Of (Integer, Integer))(spliceStart + (oldAlignments.Count - spliceEnd))
                For i As Integer = 0 To spliceStart - 1
                    target.Add(oldAlignments(i))
                Next
            End If
            Dim normalizedBuilder As New StringBuilder()

            For Each item In dest
                Dim c As String = item.Item1
                Dim changes As Integer = item.Item2

                Dim idx As Integer = offset
                Dim align As (Integer, Integer)
                If changes > 0 Then
                    If idx < 1 Then
                        align = (0, 0)
                    Else
                        align = oldAlignments(idx - 1)
                    End If
                Else
                    align = oldAlignments(idx)
                End If

                ' If we are replacing a character, find it and compute the change in size.
                Dim replacedCharSize As Integer = 0
                If changes <= 0 Then
                    If replacedNet < netEnd Then
                        Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                        replacedCharSize = Utf8Helpers.Utf8LengthOfCodePoint(cp)
                        replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                    End If
                End If

                ' If we are removing some characters, find them too.
                Dim totalBytesToRemove As Integer = 0
                If changes < 0 Then
                    For k As Integer = 0 To (-changes) - 1
                        If replacedNet < netEnd Then
                            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                            totalBytesToRemove += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                            replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                        End If
                    Next
                End If

                ' Keep track of the changes for next offsets.
                offset += replacedCharSize + totalBytesToRemove

                ' New normalized alignment entries.
                Dim cLen As Integer = Utf8Helpers.Utf8Length(c)
                For k As Integer = 0 To cLen - 1
                    target.Add(align)
                Next
                normalizedBuilder.Append(c)
            Next

            ' Publish the spliced alignments.
            If Not isWholeRange Then
                For i As Integer = spliceEnd To oldAlignments.Count - 1
                    target.Add(oldAlignments(i))
                Next
            End If
            _alignments = target

            ' Splice normalized string without intermediate String.Concat: a whole-range
            ' transform uses the builder directly; a partial transform appends the untouched
            ' prefix/suffix ranges of _normalized into one pre-sized StringBuilder.
            If isWholeRange Then
                _normalized = normalizedBuilder.ToString()
            Else
                Dim prefixLen As Integer = netStart
                Dim suffixNetStart As Integer = netEnd
                Dim builder As New StringBuilder(prefixLen + normalizedBuilder.Length + (_normalized.Length - suffixNetStart))
                builder.Append(_normalized, 0, prefixLen)
                builder.Append(normalizedBuilder.ToString())
                builder.Append(_normalized, suffixNetStart, _normalized.Length - suffixNetStart)
                _normalized = builder.ToString()
            End If
            InvalidateNormalizedCaches()
        End Sub

        ''' <summary>
        ''' Applies transformations to the current normalized version of the string while
        ''' updating the alignments, exactly like the <c>(String, Integer)</c> overload, but
        ''' reading each emitted character as a single <see cref="Char"/>. The hot paths
        ''' (ByteLevel byte→unicode) use this overload so no per-character <see cref="String"/> is
        ''' ever allocated for the transform stream. The stream is accessed by index (an
        ''' <see cref="IReadOnlyList(Of (Char, Integer))"/>), so enumeration allocates nothing.
        ''' </summary>
        Public Sub Transform(dest As IReadOnlyList(Of (Char, Integer)), initialOffset As Integer)
            Me.TransformRange(OffsetRange.WholeOriginal(), dest, initialOffset)
        End Sub

        ''' <summary>Applies a <c>(Char, Integer)</c> transform stream over a specific byte range.</summary>
        Public Sub TransformRange(range As OffsetRange, dest As IReadOnlyList(Of (Char, Integer)), initialOffset As Integer)
            If Not _trackAlignments Then
                ' No-alignments mode (OffsetType.None count path): the only transform that runs
                ' downstream is the whole-range ByteLevel transform, whose result normalized string
                ' is simply the concatenation of the dest chars (a whole-range splice replaces the
                ' entire normalized text). The alignment list is not built. A partial transform on a
                ' no-track NormalizedString is unsupported and throws (it never happens in the
                ' count path). The source's normalized byte length is read through Len() so a lazy
                ' no-track view is never materialized here.
                Dim normLen As Integer = Len()
                Dim isWhole As Boolean
                If range.IsOriginal Then
                    ' Only a whole-original transform is supported on a no-track NormalizedString
                    ' (the count path's ByteLevel hot path); a partial original range cannot be
                    ' mapped without alignments.
                    isWhole = (range.Start = 0 AndAlso range.End = -1)
                Else
                    Dim r As (Integer, Integer) = range.IntoFullRange(normLen)
                    isWhole = (r.Item1 = 0 AndAlso r.Item2 = normLen)
                End If
                If initialOffset = 0 AndAlso isWhole Then
                    ' Zero-intermediate whole-range splice: the result normalized string is
                    ' exactly one UTF-16 char per dest item, so a rented char buffer (returned to
                    ' ArrayPool.Shared and reused across pieces) replaces the per-piece
                    ' StringBuilder. The single `New String` copy is the unavoidable floor; the
                    ' StringBuilder object and its internal buffer are eliminated. The buffer is
                    ' never exposed and the string is a fresh copy, so returning the rental is
                    ' always safe.
                    Dim n As Integer = dest.Count
                    Dim buf As Char() = ArrayPool(Of Char).Shared.Rent(n)
                    Try
                        For i As Integer = 0 To n - 1
                            buf(i) = dest(i).Item1
                        Next
                        _normalized = New String(buf, 0, n)
                    Finally
                        ArrayPool(Of Char).Shared.Return(buf)
                    End Try
                    _alignments = _emptyAlignments
                    InvalidateNormalizedCaches()
                    Return
                End If
                Throw New OffsetTrackingRequiredException("Partial transform on a no-track NormalizedString.")
            End If

            Dim nRange As (Integer, Integer)
            If Not range.IsOriginal Then
                nRange = range.IntoFullRange(Utf8Helpers.Utf8Length(_normalized))
            Else
                Dim converted = ConvertOffsets(range)
                If Not converted.HasValue Then Return
                nRange = converted.Value
            End If

            Dim netStart As Integer = Utf8Helpers.ByteToNetIndex(_normalized, nRange.Item1)
            Dim netEnd As Integer = Utf8Helpers.ByteToNetIndex(_normalized, nRange.Item2)

            ' Mirrors the Rust `(&mut iter).take(initial_offset)` call: the first
            ' `initial_offset` characters of the replaced range are dropped before the loop, so
            ' the on-demand scalar cursor is positioned past them and their byte length is
            ' accumulated into the initial byte offset. The scalar count in range is only needed
            ' when initialOffset > 0 (the common whole-range ByteLevel transform uses
            ' initialOffset = 0, so the scan is skipped).
            Dim replacedNet As Integer = netStart
            Dim initialRemoved As Integer = 0
            Dim toSkip As Integer = 0
            If initialOffset > 0 Then
                Dim scanNet As Integer = netStart
                Dim scalarCountInRange As Integer = 0
                While scanNet < netEnd
                    scanNet += Utf8Helpers.NetLengthOfCodePoint(UnicodePredicates.ScalarCodePoint(_normalized, scanNet))
                    scalarCountInRange += 1
                End While
                toSkip = Math.Min(initialOffset, scalarCountInRange)
                For k As Integer = 0 To toSkip - 1
                    Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                    initialRemoved += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                    replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                Next
            End If

            Dim offset As Integer = initialRemoved + nRange.Item1

            ' Pre-size the alignment list and the normalized-text builder so the hot Char
            ' transform does not pay List/StringBuilder growth doubling. The alignment list has
            ' one entry per UTF-8 byte of the emitted text; the normalized string is exactly one
            ' UTF-16 char per stream item.
            Dim destByteLen As Integer = 0
            For i As Integer = 0 To dest.Count - 1
                destByteLen += Utf8Helpers.Utf8LengthOfCodePoint(AscW(dest(i).Item1))
            Next
            Dim normalizedBuilder As New StringBuilder(dest.Count)

            ' Splice range validation.
            Dim spliceStart As Integer = nRange.Item1
            Dim spliceEnd As Integer = nRange.Item2
            If spliceStart < 0 OrElse spliceEnd > _alignments.Count OrElse spliceStart > spliceEnd Then
                Throw New InvalidOperationException("NormalizedString bad transform range.")
            End If

            ' Build ONE pre-sized alignment list for the result. A whole-range transform (the
            ' common ByteLevel hot path) allocates only this list and publishes it directly,
            ' avoiding the separate newAlignments list plus the internal temporary array that
            ' List.InsertRange allocates (each ~the full output size). A partial transform
            ' pre-copies the prefix into the same list and appends the suffix afterwards.
            Dim oldAlignments As List(Of (Integer, Integer)) = _alignments
            Dim isWholeRange As Boolean = (spliceStart = 0) AndAlso (spliceEnd = oldAlignments.Count)
            Dim suffixCount As Integer = oldAlignments.Count - spliceEnd
            Dim target As List(Of (Integer, Integer))
            If isWholeRange Then
                target = New List(Of (Integer, Integer))(destByteLen)
            Else
                target = New List(Of (Integer, Integer))(spliceStart + destByteLen + suffixCount)
                For i As Integer = 0 To spliceStart - 1
                    target.Add(oldAlignments(i))
                Next
            End If

            For i As Integer = 0 To dest.Count - 1
                Dim item As (Char, Integer) = dest(i)
                Dim c As Char = item.Item1
                Dim changes As Integer = item.Item2

                Dim idx As Integer = offset
                Dim align As (Integer, Integer)
                If changes > 0 Then
                    If idx < 1 Then
                        align = (0, 0)
                    Else
                        align = oldAlignments(idx - 1)
                    End If
                Else
                    align = oldAlignments(idx)
                End If

                ' If we are replacing a character, find it and compute the change in size.
                Dim replacedCharSize As Integer = 0
                If changes <= 0 Then
                    If replacedNet < netEnd Then
                        Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                        replacedCharSize = Utf8Helpers.Utf8LengthOfCodePoint(cp)
                        replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                    End If
                End If

                ' If we are removing some characters, find them too.
                Dim totalBytesToRemove As Integer = 0
                If changes < 0 Then
                    For k As Integer = 0 To (-changes) - 1
                        If replacedNet < netEnd Then
                            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                            totalBytesToRemove += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                            replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                        End If
                    Next
                End If

                ' Keep track of the changes for next offsets.
                offset += replacedCharSize + totalBytesToRemove

                ' New normalized alignment entries: one per UTF-8 byte the emitted char
                ' occupies. For a Char this is pure arithmetic (a lone surrogate counts as the
                ' 3-byte U+FFFD replacement, matching Utf8Helpers.Utf8Length of the same char).
                Dim cLen As Integer = Utf8Helpers.Utf8LengthOfCodePoint(AscW(c))
                For k As Integer = 0 To cLen - 1
                    target.Add(align)
                Next
                normalizedBuilder.Append(c)
            Next

            ' Publish the spliced alignments.
            If Not isWholeRange Then
                For i As Integer = spliceEnd To oldAlignments.Count - 1
                    target.Add(oldAlignments(i))
                Next
            End If
            _alignments = target

            ' Splice normalized string without intermediate String.Concat: a whole-range
            ' transform uses the builder directly; a partial transform appends the untouched
            ' prefix/suffix ranges of _normalized into one pre-sized StringBuilder (no
            ' SliceByUtf8 substring copies).
            If isWholeRange Then
                _normalized = normalizedBuilder.ToString()
            Else
                Dim prefixLen As Integer = netStart
                Dim suffixNetStart As Integer = netEnd
                Dim builder As New StringBuilder(prefixLen + normalizedBuilder.Length + (_normalized.Length - suffixNetStart))
                builder.Append(_normalized, 0, prefixLen)
                builder.Append(normalizedBuilder.ToString())
                builder.Append(_normalized, suffixNetStart, _normalized.Length - suffixNetStart)
                _normalized = builder.ToString()
            End If
            InvalidateNormalizedCaches()
        End Sub

        ''' <summary>
        ''' Appends the ByteLevel byte-transform stream of this piece's normalized scalars to
        ''' <paramref name="dest"/> (one (Char, Integer) item per UTF-8 byte). For a lazy no-track
        ''' view this iterates the source's scalar range directly (never materializing the piece's
        ''' normalized substring); otherwise it iterates the materialized normalized string. Both
        ''' paths emit byte-identical streams to iterating <c>Get</c> with
        ''' <c>Utf8Helpers.EnumerateScalars</c>.
        ''' </summary>
        Friend Sub AppendByteTransform(dest As List(Of (Char, Integer)))
            If _normalized Is Nothing AndAlso _root IsNot Nothing Then
                ' Lazy view: the source's scalar-boundary index is already cached (FuseIsolatedSplits
                ' built it to find the piece boundaries), so the byte->net conversion is an O(log n)
                ' binary search. No substring is allocated.
                Dim rootIdx As ScalarBoundaryIndex = _root.NormalizedIndex()
                Dim netA As Integer = ByteToNetCached(rootIdx, _root._normalized, _viewNormStart)
                Dim netB As Integer = ByteToNetCached(rootIdx, _root._normalized, _viewNormEnd)
                Dim net As Integer = netA
                While net < netB
                    Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_root._normalized, net)
                    BytesToUnicodeTable.AppendByteTransform(dest, cp)
                    net += Utf8Helpers.NetLengthOfCodePoint(cp)
                End While
            Else
                For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                    BytesToUnicodeTable.AppendByteTransform(dest, sc.CodePoint)
                Next
            End If
        End Sub

        ''' <summary>
        ''' Applies filtering over the normalized characters.
        ''' NOTE: the <paramref name="keep"/> predicate receives a single <see cref="Char"/> (the
        ''' first UTF-16 code unit of each scalar). Surrogate pairs are still treated as one scalar
        ''' for alignment/byte accounting, but the predicate itself only sees the high surrogate.
        ''' This is a known minor deviation from Rust, where the predicate operates on the full
        ''' Unicode scalar value.
        ''' </summary>
        Public Function Filter(keep As Func(Of Char, Boolean)) As NormalizedString
            MaterializeNormalized()
            Dim removed As Integer = 0
            Dim removedStart As Integer = 0

            Dim transforms As New List(Of (String, Integer))()
            Dim lastC As String = Nothing
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                If keep(Utf8Helpers.ScalarFirstChar(sc.CodePoint)) Then
                    If lastC Is Nothing Then
                        removedStart = removed
                    Else
                        transforms.Add((lastC, -removed))
                    End If
                    lastC = Utf8Helpers.ScalarToString(sc.CodePoint)
                    removed = 0
                Else
                    removed += 1
                End If
            Next
            If lastC IsNot Nothing Then
                transforms.Add((lastC, -removed))
            End If

            Me.Transform(transforms, removedStart)
            Return Me
        End Function

        ''' <summary>
        ''' Maps the normalized characters.
        ''' NOTE: <paramref name="func"/> receives a single <see cref="Char"/> (the first UTF-16
        ''' code unit of each scalar). Supplementary scalars (surrogate pairs) are passed through
        ''' unchanged, since a <see cref="Char"/>-based function cannot map a two-code-unit value.
        ''' This is a known minor deviation from Rust, where the mapping operates on the full
        ''' Unicode scalar value.
        ''' </summary>
        Public Function Map(func As Func(Of Char, Char)) As NormalizedString
            MaterializeNormalized()
            Dim transforms As New List(Of (String, Integer))()
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                If sc.NetLen = 1 Then
                    transforms.Add((func(Utf8Helpers.ScalarFirstChar(sc.CodePoint)).ToString(), 0))
                Else
                    transforms.Add((Utf8Helpers.ScalarToString(sc.CodePoint), 0))
                End If
            Next
            Me.Transform(transforms, 0)
            Return Me
        End Function

        ''' <summary>Lowercases the normalized string.</summary>
        Public Function Lowercase() As NormalizedString
            MaterializeNormalized()
            Dim newChars As New List(Of (String, Integer))()
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                Dim lowered As String = Utf8Helpers.ScalarToString(sc.CodePoint).ToLowerInvariant()
                Dim first As Boolean = True
                For Each lc In Utf8Helpers.EnumerateScalars(lowered)
                    newChars.Add((Utf8Helpers.ScalarToString(lc.CodePoint), If(first, 0, 1)))
                    first = False
                Next
            Next
            Me.Transform(newChars, 0)
            Return Me
        End Function

        ''' <summary>Uppercases the normalized string.</summary>
        Public Function Uppercase() As NormalizedString
            MaterializeNormalized()
            Dim newChars As New List(Of (String, Integer))()
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                Dim upper As String = Utf8Helpers.ScalarToString(sc.CodePoint).ToUpperInvariant()
                Dim first As Boolean = True
                For Each uc In Utf8Helpers.EnumerateScalars(upper)
                    newChars.Add((Utf8Helpers.ScalarToString(uc.CodePoint), If(first, 0, 1)))
                    first = False
                Next
            Next
            Me.Transform(newChars, 0)
            Return Me
        End Function

        ''' <summary>Prepends the given string to the normalized string.</summary>
        Public Function Prepend(s As String) As NormalizedString
            MaterializeNormalized()
            Dim firstScalar As ScalarInfo? = Nothing
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                firstScalar = sc
                Exit For
            Next
            If firstScalar.HasValue Then
                Dim fs As ScalarInfo = firstScalar.Value
                Dim transformations As New List(Of (String, Integer))()
                Dim first As Boolean = True
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    transformations.Add((Utf8Helpers.ScalarToString(sc.CodePoint), If(first, 0, 1)))
                    first = False
                Next
                transformations.Add((Utf8Helpers.ScalarToString(fs.CodePoint), 1))
                Me.TransformRange(New OffsetRange(False, 0, fs.Utf8Len), transformations, 0)
            End If
            Return Me
        End Function

        ''' <summary>Appends the given string to the normalized string.</summary>
        Public Function Append(s As String) As NormalizedString
            MaterializeNormalized()
            Dim lastScalar As ScalarInfo? = Nothing
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                lastScalar = sc
            Next
            If lastScalar.HasValue Then
                Dim ls As ScalarInfo = lastScalar.Value
                Dim transformations As New List(Of (String, Integer))()
                transformations.Add((Utf8Helpers.ScalarToString(ls.CodePoint), 0))
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    transformations.Add((Utf8Helpers.ScalarToString(sc.CodePoint), 1))
                Next
                Me.TransformRange(New OffsetRange(False, ls.Utf8Start, -1), transformations, 0)
            Else
                Dim transformations As New List(Of (String, Integer))()
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    transformations.Add((Utf8Helpers.ScalarToString(sc.CodePoint), 1))
                Next
                Me.TransformRange(OffsetRange.WholeNormalized(), transformations, 0)
            End If
            Return Me
        End Function

        ''' <summary>Clears the normalized part of the string.</summary>
        Public Function Clear() As Integer
            MaterializeNormalized()
            Dim len As Integer = Utf8Helpers.Utf8Length(_normalized)
            Me.Transform(New List(Of (String, Integer))(), len)
            Return len
        End Function

        ''' <summary>Replaces anything that matches the pattern with the given content.</summary>
        Public Function Replace(pattern As Pattern, content As String) As NormalizedString
            MaterializeNormalized()
            Dim newNormalized As New StringBuilder()
            ' Pre-size to the current alignment count: a typical replace keeps the text size
            ' roughly stable, so the common case never pays List growth doubling.
            Dim newAlignments As New List(Of (Integer, Integer))(_alignments.Count)
            Dim lastEnd As Integer = 0

            For Each m In pattern.FindMatches(_normalized)
                If m.IsMatch Then
                    Dim start As Integer = m.Start
                    Dim [end] As Integer = m.End

                    ' Copy the part of the string before the match.
                    newNormalized.Append(Utf8Helpers.SliceByUtf8(_normalized, lastEnd, start))
                    For k As Integer = lastEnd To start - 1
                        newAlignments.Add(_alignments(k))
                    Next

                    ' Compute the replacement, mirroring the optimized Rust replace().
                    Dim replacedBytes As New List(Of Integer)()
                    Dim netStart As Integer = Utf8Helpers.ByteToNetIndex(_normalized, start)
                    Dim netEnd As Integer = Utf8Helpers.ByteToNetIndex(_normalized, [end])
                    Dim scanNet As Integer = netStart
                    While scanNet < netEnd
                        Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, scanNet)
                        replacedBytes.Add(Utf8Helpers.Utf8LengthOfCodePoint(cp))
                        scanNet += Utf8Helpers.NetLengthOfCodePoint(cp)
                    End While
                    Dim removedChars As Integer = replacedBytes.Count
                    Dim initialRemoved As Integer = 0
                    For k As Integer = 0 To removedChars - 1
                        initialRemoved += replacedBytes(k)
                    Next
                    Dim offset As Integer = initialRemoved + start

                    For Each sc In Utf8Helpers.EnumerateScalars(content)
                        Dim idx As Integer = offset
                        Dim align As (Integer, Integer)
                        If idx < 1 Then
                            align = (0, 0)
                        Else
                            align = _alignments(idx - 1)
                        End If
                        For k As Integer = 0 To sc.Utf8Len - 1
                            newAlignments.Add(align)
                        Next
                        newNormalized.Append(Utf8Helpers.ScalarToString(sc.CodePoint))
                    Next

                    lastEnd = [end]
                End If
            Next

            ' Copy the remaining part of the input.
            newNormalized.Append(Utf8Helpers.SliceByUtf8(_normalized, lastEnd, Utf8Helpers.Utf8Length(_normalized)))
            For k As Integer = lastEnd To _alignments.Count - 1
                newAlignments.Add(_alignments(k))
            Next

            _normalized = newNormalized.ToString()
            _alignments = newAlignments
            InvalidateNormalizedCaches()
            Return Me
        End Function

        ''' <summary>Remove any leading space(s) of the normalized string.</summary>
        Public Function LStrip() As NormalizedString
            Return Lrstrip(True, False)
        End Function

        ''' <summary>Remove any trailing space(s) of the normalized string.</summary>
        Public Function RStrip() As NormalizedString
            Return Lrstrip(False, True)
        End Function

        ''' <summary>Remove any leading and trailing space(s) of the normalized string.</summary>
        Public Function Strip() As NormalizedString
            Return Lrstrip(True, True)
        End Function

        Private Function Lrstrip(left As Boolean, right As Boolean) As NormalizedString
            MaterializeNormalized()
            Dim leadingSpaces As Integer = 0
            If left Then
                For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                    If Not IsWhitespaceScalar(sc.CodePoint) Then Exit For
                    leadingSpaces += 1
                Next
            End If

            Dim trailingSpaces As Integer = 0
            If right Then
                Dim currentTrailing As Integer = 0
                For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                    If IsWhitespaceScalar(sc.CodePoint) Then
                        currentTrailing += 1
                    Else
                        currentTrailing = 0
                    End If
                Next
                trailingSpaces = currentTrailing
            End If

            If leadingSpaces > 0 OrElse trailingSpaces > 0 Then
                Dim count As Integer = Utf8Helpers.ScalarCount(_normalized)
                Dim transformation As New List(Of (String, Integer))()
                Dim i As Integer = 0
                For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                    If i < leadingSpaces OrElse i >= count - trailingSpaces Then
                        ' Dropped char.
                    ElseIf i = Utf8Helpers.Utf8Length(_normalized) - trailingSpaces - 1 Then
                        transformation.Add((Utf8Helpers.ScalarToString(sc.CodePoint), -trailingSpaces))
                    Else
                        transformation.Add((Utf8Helpers.ScalarToString(sc.CodePoint), 0))
                    End If
                    i += 1
                Next
                Me.Transform(transformation, leadingSpaces)
            End If
            Return Me
        End Function

        ''' <summary>Whether the scalar is whitespace (a supplementary scalar is never whitespace).</summary>
        Private Shared Function IsWhitespaceScalar(cp As Integer) As Boolean
            Return cp < &H10000 AndAlso Char.IsWhiteSpace(ChrW(cp))
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Unicode normalization"
        ' ------------------------------------------------------------------

        ''' <summary>Applies NFD normalization.</summary>
        Public Function Nfd() As NormalizedString
            MaterializeNormalized()
            Dim stream As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=False)
            Me.Transform(stream.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        ''' <summary>Applies NFKD normalization.</summary>
        Public Function Nfkd() As NormalizedString
            MaterializeNormalized()
            Dim stream As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=True)
            Me.Transform(stream.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        ''' <summary>Applies NFC normalization.</summary>
        Public Function Nfc() As NormalizedString
            MaterializeNormalized()
            Dim nfd As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=False)
            Dim composed As List(Of NormChar) = UnicodeNormalizer.Compose(nfd)
            Me.Transform(composed.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        ''' <summary>Applies NFKC normalization.</summary>
        Public Function Nfkc() As NormalizedString
            MaterializeNormalized()
            Dim nfd As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=True)
            Dim composed As List(Of NormChar) = UnicodeNormalizer.Compose(nfd)
            Me.Transform(composed.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Splitting"
        ' ------------------------------------------------------------------

        Private Structure SplitEntry
            Public Offsets As MatchInfo
            Public Remove As Boolean
            Public Sub New(offsets As MatchInfo, remove As Boolean)
                Me.Offsets = offsets
                Me.Remove = remove
            End Sub
        End Structure

        ''' <summary>
        ''' Splits the current string in many subparts. Specify what to do with the delimiter via
        ''' <paramref name="behavior"/>.
        ''' </summary>
        Public Function Split(pattern As Pattern, behavior As SplitDelimiterBehavior) As List(Of NormalizedString)
            MaterializeNormalized()
            Dim matches As List(Of MatchInfo) = pattern.FindMatches(_normalized)

            ' Isolated (the pre-tokenizer hot path): every match becomes a piece and no
            ' delimiter handling is applied, so slice directly from the matches list (pre-sized,
            ' no intermediate SplitEntry list).
            If behavior = SplitDelimiterBehavior.Isolated Then
                Dim isolatedResult As New List(Of NormalizedString)(matches.Count)
                For Each m In matches
                    isolatedResult.Add(Me.Slice(New OffsetRange(False, m.Start, m.End)))
                Next
                Return isolatedResult
            End If

            Dim splits As New List(Of SplitEntry)()

            Select Case behavior
                Case SplitDelimiterBehavior.Removed
                    For Each m In matches
                        splits.Add(New SplitEntry(m, m.IsMatch))
                    Next

                Case SplitDelimiterBehavior.Contiguous
                    Dim previousMatch As Boolean = False
                    For Each m In matches
                        If m.IsMatch = previousMatch Then
                            If splits.Count > 0 Then
                                Dim entry As SplitEntry = splits(splits.Count - 1)
                                entry.Offsets = New MatchInfo(entry.Offsets.Start, m.End, entry.Offsets.IsMatch)
                                splits(splits.Count - 1) = entry
                            Else
                                splits.Add(New SplitEntry(m, False))
                            End If
                        Else
                            splits.Add(New SplitEntry(m, False))
                        End If
                        previousMatch = m.IsMatch
                    Next

                Case SplitDelimiterBehavior.MergedWithPrevious
                    Dim previousMatch As Boolean = False
                    For Each m In matches
                        If m.IsMatch AndAlso Not previousMatch Then
                            If splits.Count > 0 Then
                                Dim entry As SplitEntry = splits(splits.Count - 1)
                                entry.Offsets = New MatchInfo(entry.Offsets.Start, m.End, entry.Offsets.IsMatch)
                                splits(splits.Count - 1) = entry
                            Else
                                splits.Add(New SplitEntry(m, False))
                            End If
                        Else
                            splits.Add(New SplitEntry(m, False))
                        End If
                        previousMatch = m.IsMatch
                    Next

                Case SplitDelimiterBehavior.MergedWithNext
                    Dim previousMatch As Boolean = False
                    Dim reversedMatches As List(Of MatchInfo) = matches.ToList()
                    reversedMatches.Reverse()
                    Dim temp As New List(Of SplitEntry)()
                    For Each m In reversedMatches
                        If m.IsMatch AndAlso Not previousMatch Then
                            If temp.Count > 0 Then
                                Dim entry As SplitEntry = temp(temp.Count - 1)
                                entry.Offsets = New MatchInfo(m.Start, entry.Offsets.End, entry.Offsets.IsMatch)
                                temp(temp.Count - 1) = entry
                            Else
                                temp.Add(New SplitEntry(m, False))
                            End If
                        Else
                            temp.Add(New SplitEntry(m, False))
                        End If
                        previousMatch = m.IsMatch
                    Next
                    temp.Reverse()
                    splits = temp
            End Select

            Dim result As New List(Of NormalizedString)()
            For Each entry In splits
                If Not entry.Remove Then
                    result.Add(Me.Slice(New OffsetRange(False, entry.Offsets.Start, entry.Offsets.End)))
                End If
            Next
            Return result
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Alignment helpers (tests)"
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Recalculates original alignments: for each byte of the original string, the
        ''' corresponding (offset, length) range into the normalized string.
        ''' </summary>
        Public Function AlignmentsOriginal() As List(Of (Integer, Integer))
            MaterializeOriginal()
            ' Pre-size to the original byte length: the output has exactly one entry per original
            ' byte, so the common case never pays List growth doubling.
            Dim result As New List(Of (Integer, Integer))(Utf8Helpers.Utf8Length(_original))
            If _alignments.Count = 0 Then Return result

            ' Eventual gap before the first group.
            Dim firstStart As Integer = _alignments(0).Item1
            If firstStart <> 0 Then
                For k As Integer = 0 To firstStart - 1
                    result.Add((0, 0))
                Next
            End If

            Dim lastStart As Integer = _alignments(0).Item1
            Dim lastEnd As Integer = _alignments(0).Item2
            Dim offset As Integer = 0
            Dim length As Integer = 0
            For Each alignment In _alignments
                Dim start As Integer = alignment.Item1
                Dim [end] As Integer = alignment.Item2
                If lastStart = start AndAlso lastEnd = [end] Then
                    length += 1
                Else
                    If start < lastEnd Then
                        Throw New InvalidOperationException("We can't have overlapping ranges.")
                    End If
                    ' Add the old group.
                    For k As Integer = 0 To lastEnd - lastStart - 1
                        result.Add((offset, offset + length))
                    Next
                    offset += length
                    length = 1
                    ' Eventual gap between the two groups.
                    For k As Integer = 0 To start - lastEnd - 1
                        result.Add((offset, offset))
                    Next
                End If
                lastStart = start
                lastEnd = [end]
            Next
            ' Add the last group.
            For k As Integer = 0 To lastEnd - lastStart - 1
                result.Add((offset, offset + length))
            Next
            ' Add the eventual last gap.
            offset += length
            Dim remaining As Integer = Utf8Helpers.Utf8Length(_original) - result.Count
            For k As Integer = 0 To remaining - 1
                result.Add((offset, offset))
            Next
            Return result
        End Function

        #End Region

        Public Overrides Function ToString() As String
            MaterializeNormalized()
            Return _normalized
        End Function
    End Class

End Namespace
