Imports System.Linq
Imports System.Text

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
        ' O(log n) binary searches, built once per NormalizedString.
        Private _originalScalars As List(Of ScalarInfo)
        Private _normalizedScalars As List(Of ScalarInfo)
        Private _originalUtf8Len As Integer?
        Private _normalizedUtf8Len As Integer?

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
            n._alignments = New List(Of (Integer, Integer))()
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

        ''' <summary>Lazily materialized scalar info for the original string.</summary>
        Private Function OriginalScalars() As List(Of ScalarInfo)
            If _originalScalars Is Nothing Then
                _originalScalars = Utf8Helpers.EnumerateScalars(_original).ToList()
            End If
            Return _originalScalars
        End Function

        ''' <summary>Lazily materialized scalar info for the normalized string.</summary>
        Private Function NormalizedScalars() As List(Of ScalarInfo)
            If _normalizedScalars Is Nothing Then
                _normalizedScalars = Utf8Helpers.EnumerateScalars(_normalized).ToList()
            End If
            Return _normalizedScalars
        End Function

        Private Function OriginalUtf8LenCached() As Integer
            If Not _originalUtf8Len.HasValue Then
                _originalUtf8Len = Utf8Helpers.Utf8Length(_original)
            End If
            Return _originalUtf8Len.Value
        End Function

        Private Function NormalizedUtf8LenCached() As Integer
            If Not _normalizedUtf8Len.HasValue Then
                _normalizedUtf8Len = Utf8Helpers.Utf8Length(_normalized)
            End If
            Return _normalizedUtf8Len.Value
        End Function

        ''' <summary>Invalidates the normalized-side caches (the original side never mutates).</summary>
        Private Sub InvalidateNormalizedCaches()
            _normalizedScalars = Nothing
            _normalizedUtf8Len = Nothing
        End Sub

        ''' <summary>
        ''' Converts a UTF-8 byte offset to a .NET string index using a cached scalar list.
        ''' Semantics match <see cref="Utf8Helpers.ByteToNetIndex"/>.
        ''' </summary>
        Private Shared Function ByteToNetCached(scalars As List(Of ScalarInfo), utf8Len As Integer, byteOffset As Integer) As Integer
            If byteOffset <= 0 Then Return 0
            If byteOffset >= utf8Len Then Return scalars(scalars.Count - 1).NetEnd
            Dim lo As Integer = 0
            Dim hi As Integer = scalars.Count - 1
            While lo <= hi
                Dim mid As Integer = (lo + hi) \ 2
                Dim sc As ScalarInfo = scalars(mid)
                If byteOffset < sc.Utf8Start Then
                    hi = mid - 1
                ElseIf byteOffset >= sc.Utf8Start + sc.Utf8Len Then
                    lo = mid + 1
                Else
                    Return sc.NetStart
                End If
            End While
            Return 0
        End Function

        ''' <summary>
        ''' Whether the given UTF-8 byte offset lies on a scalar boundary, using a cached scalar list.
        ''' Semantics match <see cref="Utf8Helpers.IsUtf8CharBoundary"/>.
        ''' </summary>
        Private Shared Function IsBoundaryCached(scalars As List(Of ScalarInfo), utf8Len As Integer, byteOffset As Integer) As Boolean
            If byteOffset <= 0 OrElse byteOffset >= utf8Len Then Return True
            Dim lo As Integer = 0
            Dim hi As Integer = scalars.Count - 1
            While lo <= hi
                Dim mid As Integer = (lo + hi) \ 2
                Dim start As Integer = scalars(mid).Utf8Start
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
        ''' Slices the string by a UTF-8 byte range using a cached scalar list.
        ''' Semantics match <see cref="Utf8Helpers.SliceByUtf8"/>.
        ''' </summary>
        Private Shared Function SliceByUtf8Cached(scalars As List(Of ScalarInfo), utf8Len As Integer, s As String, startByte As Integer, endByte As Integer) As String
            Dim startNet As Integer = ByteToNetCached(scalars, utf8Len, startByte)
            Dim endNet As Integer = ByteToNetCached(scalars, utf8Len, endByte)
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
                If Not IsBoundaryCached(OriginalScalars(), len, r.Item1) OrElse
                   Not IsBoundaryCached(OriginalScalars(), len, r.Item2) Then
                    Return Nothing
                End If
                Return New OffsetRange(True, r.Item1, r.Item2)
            Else
                Dim len As Integer = NormalizedUtf8LenCached()
                Dim r = range.IntoFullRange(len)
                If Not IsBoundaryCached(NormalizedScalars(), len, r.Item1) OrElse
                   Not IsBoundaryCached(NormalizedScalars(), len, r.Item2) Then
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
            result._original = SliceByUtf8Cached(OriginalScalars(), OriginalUtf8LenCached(), _original, originalRange.Item1, originalRange.Item2)
            result._normalized = SliceByUtf8Cached(NormalizedScalars(), NormalizedUtf8LenCached(), _normalized, normalizedRange.Item1, normalizedRange.Item2)
            result._alignments = New List(Of (Integer, Integer))()
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

            ' Retrieve the original characters that are being replaced.
            Dim replacedNormalized As List(Of String) =
                Utf8Helpers.EnumerateScalars(Utf8Helpers.SliceByUtf8(_normalized, nRange.Item1, nRange.Item2)).
                Select(Function(sc) sc.Value).ToList()
            Dim replacedIdx As Integer = 0

            Dim initialRemoved As Integer = 0
            For k As Integer = 0 To initialOffset - 1
                If k < replacedNormalized.Count Then
                    initialRemoved += Utf8Helpers.Utf8Length(replacedNormalized(k))
                End If
            Next
            ' Mirrors the Rust `(&mut iter).take(initial_offset)` call: the first
            ' `initial_offset` characters of the replaced range are dropped before the loop,
            ' so the per-character iterator must be positioned past them.
            replacedIdx = Math.Min(initialOffset, replacedNormalized.Count)

            Dim offset As Integer = initialRemoved + nRange.Item1
            Dim newAlignments As New List(Of (Integer, Integer))()
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
                        align = _alignments(idx - 1)
                    End If
                Else
                    align = _alignments(idx)
                End If

                ' If we are replacing a character, find it and compute the change in size.
                Dim replacedChar As String = Nothing
                If changes <= 0 Then
                    If replacedIdx < replacedNormalized.Count Then
                        replacedChar = replacedNormalized(replacedIdx)
                        replacedIdx += 1
                    End If
                End If
                Dim replacedCharSize As Integer = If(replacedChar Is Nothing, 0, Utf8Helpers.Utf8Length(replacedChar))

                ' If we are removing some characters, find them too.
                Dim totalBytesToRemove As Integer = 0
                If changes < 0 Then
                    For k As Integer = 0 To (-changes) - 1
                        If replacedIdx < replacedNormalized.Count Then
                            totalBytesToRemove += Utf8Helpers.Utf8Length(replacedNormalized(replacedIdx))
                            replacedIdx += 1
                        End If
                    Next
                End If

                ' Keep track of the changes for next offsets.
                offset += replacedCharSize + totalBytesToRemove

                ' New normalized alignment entries.
                Dim cLen As Integer = Utf8Helpers.Utf8Length(c)
                For k As Integer = 0 To cLen - 1
                    newAlignments.Add(align)
                Next
                normalizedBuilder.Append(c)
            Next

            ' Splice alignments.
            Dim spliceStart As Integer = nRange.Item1
            Dim spliceEnd As Integer = nRange.Item2
            If spliceStart < 0 OrElse spliceEnd > _alignments.Count OrElse spliceStart > spliceEnd Then
                Throw New InvalidOperationException("NormalizedString bad transform range.")
            End If
            _alignments.RemoveRange(spliceStart, spliceEnd - spliceStart)
            _alignments.InsertRange(spliceStart, newAlignments)

            ' Splice normalized string.
            Dim prefix As String = Utf8Helpers.SliceByUtf8(_normalized, 0, spliceStart)
            Dim suffix As String = Utf8Helpers.SliceByUtf8(_normalized, spliceEnd, Utf8Helpers.Utf8Length(_normalized))
            _normalized = prefix & normalizedBuilder.ToString() & suffix
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
                If keep(sc.Value(0)) Then
                    If lastC Is Nothing Then
                        removedStart = removed
                    Else
                        transforms.Add((lastC, -removed))
                    End If
                    lastC = sc.Value
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
                    transforms.Add((func(sc.Value(0)).ToString(), 0))
                Else
                    transforms.Add((sc.Value, 0))
                End If
            Next
            Me.Transform(transforms, 0)
            Return Me
        End Function

        ''' <summary>Lowercases the normalized string.</summary>
        Public Function Lowercase() As NormalizedString
            Dim newChars As New List(Of (String, Integer))()
            For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                Dim lowered As String = sc.Value.ToLowerInvariant()
                Dim first As Boolean = True
                For Each lc In Utf8Helpers.EnumerateScalars(lowered)
                    newChars.Add((lc.Value, If(first, 0, 1)))
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
                Dim upper As String = sc.Value.ToUpperInvariant()
                Dim first As Boolean = True
                For Each uc In Utf8Helpers.EnumerateScalars(upper)
                    newChars.Add((uc.Value, If(first, 0, 1)))
                    first = False
                Next
            Next
            Me.Transform(newChars, 0)
            Return Me
        End Function

        ''' <summary>Prepends the given string to the normalized string.</summary>
        Public Function Prepend(s As String) As NormalizedString
            Dim firstScalar As ScalarInfo = Utf8Helpers.EnumerateScalars(_normalized).FirstOrDefault()
            If firstScalar.Value IsNot Nothing Then
                Dim transformations As New List(Of (String, Integer))()
                Dim first As Boolean = True
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    transformations.Add((sc.Value, If(first, 0, 1)))
                    first = False
                Next
                transformations.Add((firstScalar.Value, 1))
                Me.TransformRange(New OffsetRange(False, 0, firstScalar.Utf8Len), transformations, 0)
            End If
            Return Me
        End Function

        ''' <summary>Appends the given string to the normalized string.</summary>
        Public Function Append(s As String) As NormalizedString
            Dim lastScalar As ScalarInfo = Utf8Helpers.EnumerateScalars(_normalized).LastOrDefault()
            If lastScalar.Value IsNot Nothing Then
                Dim transformations As New List(Of (String, Integer))()
                transformations.Add((lastScalar.Value, 0))
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    transformations.Add((sc.Value, 1))
                Next
                Me.TransformRange(New OffsetRange(False, lastScalar.Utf8Start, -1), transformations, 0)
            Else
                Dim transformations As New List(Of (String, Integer))()
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    transformations.Add((sc.Value, 1))
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
            Dim newAlignments As New List(Of (Integer, Integer))()
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
                    Dim replacedNormalized As List(Of String) =
                        Utf8Helpers.EnumerateScalars(Utf8Helpers.SliceByUtf8(_normalized, start, [end])).
                        Select(Function(sc) sc.Value).ToList()
                    Dim removedChars As Integer = replacedNormalized.Count
                    Dim initialRemoved As Integer = 0
                    For k As Integer = 0 To removedChars - 1
                        initialRemoved += Utf8Helpers.Utf8Length(replacedNormalized(k))
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
                        newNormalized.Append(sc.Value)
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
                    If Not Char.IsWhiteSpace(sc.Value(0)) Then Exit For
                    leadingSpaces += 1
                Next
            End If

            Dim trailingSpaces As Integer = 0
            If right Then
                Dim scalars As List(Of ScalarInfo) = Utf8Helpers.EnumerateScalars(_normalized).ToList()
                For i As Integer = scalars.Count - 1 To 0 Step -1
                    If Not Char.IsWhiteSpace(scalars(i).Value(0)) Then Exit For
                    trailingSpaces += 1
                Next
            End If

            If leadingSpaces > 0 OrElse trailingSpaces > 0 Then
                Dim count As Integer = Utf8Helpers.ScalarCount(_normalized)
                Dim transformation As New List(Of (String, Integer))()
                Dim i As Integer = 0
                For Each sc In Utf8Helpers.EnumerateScalars(_normalized)
                    If i < leadingSpaces OrElse i >= count - trailingSpaces Then
                        ' Dropped char.
                    ElseIf i = Utf8Helpers.Utf8Length(_normalized) - trailingSpaces - 1 Then
                        transformation.Add((sc.Value, -trailingSpaces))
                    Else
                        transformation.Add((sc.Value, 0))
                    End If
                    i += 1
                Next
                Me.Transform(transformation, leadingSpaces)
            End If
            Return Me
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
            Dim splits As New List(Of SplitEntry)()

            Select Case behavior
                Case SplitDelimiterBehavior.Isolated
                    For Each m In matches
                        splits.Add(New SplitEntry(m, False))
                    Next

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
            Dim result As New List(Of (Integer, Integer))()
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
