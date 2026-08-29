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
            Public ReadOnly Utf8Starts As Integer()
            Public ReadOnly NetStarts As Integer()
            Public ReadOnly Utf8Len As Integer

            Public Sub New(utf8Starts As Integer(), netStarts As Integer(), utf8Len As Integer)
                Me.Utf8Starts = utf8Starts
                Me.NetStarts = netStarts
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

        ' ------------------------------------------------------------------
        #Region "Basic accessors"
        ' ------------------------------------------------------------------

        ''' <summary>Returns the normalized string.</summary>
        Public ReadOnly Property [Get]() As String
            Get
                Return _normalized
            End Get
        End Property

        ''' <summary>Returns the original string.</summary>
        Public ReadOnly Property Original() As String
            Get
                Return _original
            End Get
        End Property

        ''' <summary>Returns the original offsets of this NormalizedString.</summary>
        Public Function OffsetsOriginal() As (Integer, Integer)
            Return (_originalShift, _originalShift + Utf8Helpers.Utf8Length(_original))
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
            Return Utf8Helpers.Utf8Length(_normalized)
        End Function

        ''' <summary>Length of the original string in UTF-8 bytes.</summary>
        Public Function LenOriginal() As Integer
            Return Utf8Helpers.Utf8Length(_original)
        End Function

        ''' <summary>Whether the normalized string is empty.</summary>
        Public Function IsEmpty() As Boolean
            Return _normalized.Length = 0
        End Function

        #End Region

        ' ------------------------------------------------------------------
        #Region "Cached byte<->net conversions (hot paths)"
        ' ------------------------------------------------------------------

        ''' <summary>Builds a scalar boundary index (byte and net offsets of each scalar start) for <paramref name="s"/>.</summary>
        Private Shared Function BuildScalarIndex(s As String) As ScalarBoundaryIndex
            If s Is Nothing OrElse s.Length = 0 Then
                Return New ScalarBoundaryIndex(Array.Empty(Of Integer)(), Array.Empty(Of Integer)(), 0)
            End If
            Dim count As Integer = Utf8Helpers.ScalarCount(s)
            Dim utf8Starts(count - 1) As Integer
            Dim netStarts(count - 1) As Integer
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            Dim idx As Integer = 0
            While net < s.Length
                utf8Starts(idx) = byteOff
                netStarts(idx) = net
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                byteOff += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                net += Utf8Helpers.NetLengthOfCodePoint(cp)
                idx += 1
            End While
            Return New ScalarBoundaryIndex(utf8Starts, netStarts, byteOff)
        End Function

        ''' <summary>Lazily built scalar boundary index for the original string (never mutates).</summary>
        Private Function OriginalIndex() As ScalarBoundaryIndex
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
            Dim v As Integer = Volatile.Read(_originalUtf8Len)
            If v < 0 Then
                v = Utf8Helpers.Utf8Length(_original)
                Volatile.Write(_originalUtf8Len, v)
            End If
            Return v
        End Function

        Private Function NormalizedUtf8LenCached() As Integer
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
        ''' Converts a UTF-8 byte offset to a .NET string index using a cached boundary index.
        ''' Semantics match <see cref="Utf8Helpers.ByteToNetIndex"/>.
        ''' </summary>
        Private Shared Function ByteToNetCached(index As ScalarBoundaryIndex, s As String, byteOffset As Integer) As Integer
            If byteOffset <= 0 Then Return 0
            If byteOffset >= index.Utf8Len Then Return s.Length
            Dim lo As Integer = 0
            Dim hi As Integer = index.Utf8Starts.Length - 1
            Dim result As Integer = 0
            While lo <= hi
                Dim mid As Integer = (lo + hi) \ 2
                If index.Utf8Starts(mid) <= byteOffset Then
                    result = index.NetStarts(mid)
                    lo = mid + 1
                Else
                    hi = mid - 1
                End If
            End While
            Return result
        End Function

        ''' <summary>
        ''' Whether the given UTF-8 byte offset lies on a scalar boundary, using a cached boundary index.
        ''' Semantics match <see cref="Utf8Helpers.IsUtf8CharBoundary"/>.
        ''' </summary>
        Private Shared Function IsBoundaryCached(index As ScalarBoundaryIndex, byteOffset As Integer) As Boolean
            If byteOffset <= 0 OrElse byteOffset >= index.Utf8Len Then Return True
            Dim lo As Integer = 0
            Dim hi As Integer = index.Utf8Starts.Length - 1
            While lo <= hi
                Dim mid As Integer = (lo + hi) \ 2
                Dim start As Integer = index.Utf8Starts(mid)
                If start < byteOffset Then
                    lo = mid + 1
                ElseIf start > byteOffset Then
                    hi = mid - 1
                Else
                    Return True
                End If
            End While
            Return False
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
            Dim lenOriginal As Integer = OriginalUtf8LenCached()
            Dim lenNormalized As Integer = NormalizedUtf8LenCached()

            Dim target As (Integer, Integer)
            Dim original As Boolean
            If range.IsOriginal Then
                target = range.IntoFullRange(lenOriginal)
                original = True
            Else
                target = range.IntoFullRange(lenNormalized)
                original = False
            End If

            ' If we target an empty range, let's return the same
            If target.Item1 = target.Item2 Then Return target
            ' If the target goes reverse, return Nothing
            If target.Item1 > target.Item2 Then Return Nothing

            ' If we target 0..0 on an empty string, we want to expand to the entire equivalent
            If original AndAlso _original.Length = 0 AndAlso target.Item1 = 0 AndAlso target.Item2 = 0 Then
                Return (0, lenNormalized)
            End If
            If Not original AndAlso _normalized.Length = 0 AndAlso target.Item1 = 0 AndAlso target.Item2 = 0 Then
                Return (0, lenOriginal)
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
                    Throw New InvalidOperationException("NormalizedString bad slice: cannot convert offsets.")
                End If
                normalizedRange = converted.Value
                originalRange = fullRange.IntoFullRange(OriginalUtf8LenCached())
            Else
                normalizedRange = fullRange.IntoFullRange(NormalizedUtf8LenCached())
                Dim converted = ConvertOffsets(fullRange)
                If Not converted.HasValue Then
                    Throw New InvalidOperationException("NormalizedString bad slice: cannot convert offsets.")
                End If
                originalRange = converted.Value
            End If

            Dim nShift As Integer = originalRange.Item1

            Dim result As New NormalizedString()
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
            result._originalShift = _originalShift + originalRange.Item1
            Return result
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

            ' The replaced range's scalars are scanned ON DEMAND as the stream is consumed, so no
            ' replaced-bytes list is materialized.
            Dim scanNet As Integer = netStart
            Dim scalarCountInRange As Integer = 0
            While scanNet < netEnd
                scanNet += Utf8Helpers.NetLengthOfCodePoint(UnicodePredicates.ScalarCodePoint(_normalized, scanNet))
                scalarCountInRange += 1
            End While

            ' Mirrors the Rust `(&mut iter).take(initial_offset)` call: the first
            ' `initial_offset` characters of the replaced range are dropped before the loop.
            Dim replacedNet As Integer = netStart
            Dim initialRemoved As Integer = 0
            Dim toSkip As Integer = Math.Min(initialOffset, scalarCountInRange)
            For k As Integer = 0 To toSkip - 1
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                initialRemoved += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
            Next

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

            ' The replaced range's scalars are scanned ON DEMAND as the stream is consumed, so no
            ' replaced-bytes list is materialized (saves a list + its population pass). Count the
            ' scalars in the range up front so the initial-offset skip can be bounded.
            Dim scanNet As Integer = netStart
            Dim scalarCountInRange As Integer = 0
            While scanNet < netEnd
                scanNet += Utf8Helpers.NetLengthOfCodePoint(UnicodePredicates.ScalarCodePoint(_normalized, scanNet))
                scalarCountInRange += 1
            End While

            ' Mirrors the Rust `(&mut iter).take(initial_offset)` call: the first
            ' `initial_offset` characters of the replaced range are dropped before the loop, so
            ' the on-demand scalar cursor is positioned past them and their byte length is
            ' accumulated into the initial byte offset.
            Dim replacedNet As Integer = netStart
            Dim initialRemoved As Integer = 0
            Dim toSkip As Integer = Math.Min(initialOffset, scalarCountInRange)
            For k As Integer = 0 To toSkip - 1
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_normalized, replacedNet)
                initialRemoved += Utf8Helpers.Utf8LengthOfCodePoint(cp)
                replacedNet += Utf8Helpers.NetLengthOfCodePoint(cp)
            Next

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
        ''' Applies filtering over the normalized characters.
        ''' NOTE: the <paramref name="keep"/> predicate receives a single <see cref="Char"/> (the
        ''' first UTF-16 code unit of each scalar). Surrogate pairs are still treated as one scalar
        ''' for alignment/byte accounting, but the predicate itself only sees the high surrogate.
        ''' This is a known minor deviation from Rust, where the predicate operates on the full
        ''' Unicode scalar value.
        ''' </summary>
        Public Function Filter(keep As Func(Of Char, Boolean)) As NormalizedString
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
            Dim len As Integer = Utf8Helpers.Utf8Length(_normalized)
            Me.Transform(New List(Of (String, Integer))(), len)
            Return len
        End Function

        ''' <summary>Replaces anything that matches the pattern with the given content.</summary>
        Public Function Replace(pattern As Pattern, content As String) As NormalizedString
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
            Dim stream As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=False)
            Me.Transform(stream.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        ''' <summary>Applies NFKD normalization.</summary>
        Public Function Nfkd() As NormalizedString
            Dim stream As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=True)
            Me.Transform(stream.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        ''' <summary>Applies NFC normalization.</summary>
        Public Function Nfc() As NormalizedString
            Dim nfd As List(Of NormChar) = UnicodeNormalizer.Decompose(_normalized, compat:=False)
            Dim composed As List(Of NormChar) = UnicodeNormalizer.Compose(nfd)
            Me.Transform(composed.Select(Function(x) (x.Ch, x.Change)), 0)
            Return Me
        End Function

        ''' <summary>Applies NFKC normalization.</summary>
        Public Function Nfkc() As NormalizedString
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
            Return _normalized
        End Function
    End Class

End Namespace
