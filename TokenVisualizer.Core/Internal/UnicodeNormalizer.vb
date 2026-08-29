Imports System.Collections.Concurrent
Imports System.Globalization
Imports System.Linq
Imports System.Text

Namespace Internal

    ''' <summary>
    ''' A character of a normalized (decomposed/composed) stream, carrying the change value
    ''' used by <c>NormalizedString.Transform</c> (0 = replace current char, positive = insert,
    ''' negative = replace + remove following chars) together with the UTF-8 byte span of the
    ''' original input character(s) it derives from.
    ''' </summary>
    Public Structure NormChar
        Public Ch As String
        Public Change As Integer
        Public OrigStart As Integer
        Public OrigEnd As Integer

        Public Sub New(ch As String, change As Integer, origStart As Integer, origEnd As Integer)
            Me.Ch = ch
            Me.Change = change
            Me.OrigStart = origStart
            Me.OrigEnd = origEnd
        End Sub
    End Structure

    ''' <summary>
    ''' Implements Unicode normalization (NFD/NFKD/NFC/NFKC) with alignment tracking.
    ''' Decomposition is per-scalar via the single-character normalization trick; composition
    ''' follows UAX#15 over the decomposed sequence, discovering composition pairs by checking
    ''' whether a candidate base+mark pair normalizes to a single character under FormC.
    ''' </summary>
    Public Module UnicodeNormalizer

        ' ------------------------------------------------------------------
        ' Canonical combining class support.
        ' The exact ccc of a character is discovered at runtime by comparing its NFD
        ' reordering against probe characters of known class, then binary searching over the
        ' distinct classes. Results are cached per code point.
        ' ------------------------------------------------------------------

        Private ReadOnly CccClasses As Integer() = {
            0, 1, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26,
            27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 84, 91, 103, 107, 118, 122, 129, 130, 132,
            202, 214, 216, 218, 220, 222, 224, 226, 228, 230, 232, 233, 234, 240
        }

        ' The class-0 probe must itself be a combining mark (not a starter like "A"), so that it
        ' participates in NFD canonical reordering. U+034F (COMBINING GRAPHEME JOINER) has ccc 0
        ' and reorders ahead of any positive-class mark, which is what lets the binary search
        ' distinguish ccc 0 from ccc 1.
        Private ReadOnly CccProbes As String() = {
            ChrW(&H34F), ChrW(&H334), Char.ConvertFromUtf32(&H16FF0), ChrW(&H93C), ChrW(&H3099), ChrW(&H94D),
            ChrW(&H5B0), ChrW(&H5B1), ChrW(&H5B2), ChrW(&H5B3), ChrW(&H5B4), ChrW(&H5B5), ChrW(&H5B6),
            ChrW(&H5B7), ChrW(&H5B8), ChrW(&H5B9), ChrW(&H5BB), ChrW(&H5BC), ChrW(&H5BD), ChrW(&H5BF),
            ChrW(&H5C1), ChrW(&H5C2), ChrW(&HFB1E), ChrW(&H64B), ChrW(&H64C), ChrW(&H64D), ChrW(&H618),
            ChrW(&H619), ChrW(&H61A), ChrW(&H651), ChrW(&H652), ChrW(&H670), ChrW(&H711), ChrW(&HC55),
            ChrW(&HC56), ChrW(&HE38), ChrW(&HE48), ChrW(&HEB8), ChrW(&HEC8), ChrW(&HF71), ChrW(&HF72),
            ChrW(&HF74), ChrW(&H321), ChrW(&H1DCE), ChrW(&H31B), Char.ConvertFromUtf32(&H1DFA), ChrW(&H316),
            ChrW(&H59A), ChrW(&H302E), Char.ConvertFromUtf32(&H1D16D), ChrW(&H5AE), ChrW(&H300), ChrW(&H315),
            ChrW(&H35C), ChrW(&H35D), ChrW(&H345)
        }

        Private ReadOnly CccCache As New ConcurrentDictionary(Of Integer, Integer)()

        ''' <summary>Returns the canonical combining class of a scalar value.</summary>
        Public Function CombiningClass(scalar As String) As Integer
            If String.IsNullOrEmpty(scalar) Then Return 0

            ' The only ccc-1 characters in Unicode are the overlay marks U+0334..U+0338.
            ' Special-case them: the probe comparison against a ccc-0 mark is the tightest
            ' possible check, and keeping these as a hardcoded fast path guards against any
            ' edge case in the NFD reordering used by the probe.
            Dim cp As Integer = GetScalarValue(scalar)
            If cp >= &H334 AndAlso cp <= &H338 Then Return 1

            Dim cached As Integer
            If CccCache.TryGetValue(cp, cached) Then Return cached
            Dim ccc As Integer = ComputeCccScalar(scalar)
            CccCache.TryAdd(cp, ccc)
            Return ccc
        End Function

        Private Function ComputeCccScalar(scalar As String) As Integer
            Dim lo As Integer = 0
            Dim hi As Integer = CccClasses.Length - 1
            While lo < hi
                Dim mid As Integer = (lo + hi) \ 2
                If CccLessOrEqual(scalar, CccClasses(mid)) Then
                    hi = mid
                Else
                    lo = mid + 1
                End If
            End While
            Return CccClasses(lo)
        End Function

        ''' <summary>
        ''' True if ccc(scalar) &lt;= probeClass, determined via NFD reordering: the probe mark
        ''' and the scalar are both placed after a starter base and the NFD output reveals which
        ''' one sorts first. The scalar may be a supplementary code point (surrogate pair).
        ''' </summary>
        Private Function CccLessOrEqual(scalar As String, probeClass As Integer) As Boolean
            Dim probe As String = CccProbes(Array.IndexOf(CccClasses, probeClass))
            Dim s As String = "A" & scalar & probe
            Dim nfd As String = s.Normalize(NormalizationForm.FormD)
            Return nfd.StartsWith("A" & scalar, StringComparison.Ordinal)
        End Function

        ' ------------------------------------------------------------------
        ' Decomposition.
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Decomposes the given text into a stream of (char, change, origStart, origEnd) items in
        ''' canonical order. <paramref name="compat"/> selects NFKD-style compatibility
        ''' decomposition, otherwise NFD-style canonical decomposition.
        ''' </summary>
        Public Function Decompose(text As String, compat As Boolean) As List(Of NormChar)
            Dim result As New List(Of NormChar)()
            Dim pending As New List(Of NormChar)()

            For Each sc In Utf8Helpers.EnumerateScalars(text)
                Dim raw As List(Of String) = GetRawDecomposition(sc.Value, compat)
                For i As Integer = 0 To raw.Count - 1
                    Dim d As String = raw(i)
                    Dim change As Integer = If(i = 0, 0, 1)
                    Dim ccc As Integer = CombiningClass(d)
                    Dim item As New NormChar(d, change, sc.Utf8Start, sc.Utf8Start + sc.Utf8Len)
                    If ccc = 0 Then
                        If pending.Count > 0 Then
                            result.AddRange(pending.OrderBy(Function(p) CombiningClass(p.Ch)))
                            pending.Clear()
                        End If
                        result.Add(item)
                    Else
                        pending.Add(item)
                    End If
                Next
            Next
            If pending.Count > 0 Then
                result.AddRange(pending.OrderBy(Function(p) CombiningClass(p.Ch)))
            End If
            Return result
        End Function

        ''' <summary>
        ''' Computes the decomposition of a single scalar value using the single-character
        ''' normalization trick: normalizing " " + scalar + " " and extracting the middle segment.
        ''' </summary>
        Private Function GetRawDecomposition(scalar As String, compat As Boolean) As List(Of String)
            Dim form As NormalizationForm = If(compat, NormalizationForm.FormKD, NormalizationForm.FormD)
            Dim probe As String = " " & scalar & " "
            Dim norm As String = probe.Normalize(form)
            Dim middle As String = norm.Substring(1, norm.Length - 2)
            If middle = scalar Then
                Return New List(Of String) From {scalar}
            End If
            Return Utf8Helpers.EnumerateScalars(middle).Select(Function(m) m.Value).ToList()
        End Function

        ' ------------------------------------------------------------------
        ' Canonical composition (UAX#15).
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Composes a decomposed stream (from <see cref="Decompose"/>) following UAX#15
        ''' canonical composition. When two characters compose, the composed character's change
        ''' value is k.change + ch.change - 1 and its byte span is the merge of both spans.
        ''' </summary>
        Public Function Compose(nfd As List(Of NormChar)) As List(Of NormChar)
            Dim result As New List(Of NormChar)()
            Dim buffer As New List(Of NormChar)()
            Dim composee As NormChar? = Nothing
            Dim lastCcc As Integer? = Nothing

            For Each item In nfd
                Dim ch As String = item.Ch
                Dim chClass As Integer = CombiningClass(ch)

                If Not composee.HasValue Then
                    If chClass <> 0 Then
                        result.Add(item)
                        Continue For
                    End If
                    composee = item
                    Continue For
                End If

                Dim k As NormChar = composee.Value

                If Not lastCcc.HasValue Then
                    Dim r As String = ComposePair(k.Ch, ch)
                    If r IsNot Nothing Then
                        composee = New NormChar(r, k.Change + item.Change - 1,
                                                Math.Min(k.OrigStart, item.OrigStart),
                                                Math.Max(k.OrigEnd, item.OrigEnd))
                        Continue For
                    End If
                    If chClass = 0 Then
                        result.Add(k)
                        composee = item
                        Continue For
                    End If
                    buffer.Add(item)
                    lastCcc = chClass
                Else
                    Dim lClass As Integer = lastCcc.Value
                    If lClass >= chClass Then
                        ' ch is blocked from composee
                        If chClass = 0 Then
                            result.Add(k)
                            result.AddRange(buffer)
                            buffer.Clear()
                            composee = item
                            lastCcc = Nothing
                            Continue For
                        End If
                        buffer.Add(item)
                        lastCcc = chClass
                        Continue For
                    End If
                    Dim r2 As String = ComposePair(k.Ch, ch)
                    If r2 IsNot Nothing Then
                        composee = New NormChar(r2, k.Change + item.Change - 1,
                                                Math.Min(k.OrigStart, item.OrigStart),
                                                Math.Max(k.OrigEnd, item.OrigEnd))
                        Continue For
                    End If
                    buffer.Add(item)
                    lastCcc = chClass
                End If
            Next

            If composee.HasValue Then
                result.Add(composee.Value)
            End If
            result.AddRange(buffer)
            Return result
        End Function

        Private ReadOnly ComposeCache As New ConcurrentDictionary(Of ULong, String)()

        ''' <summary>
        ''' Returns the canonical composition of two characters, or Nothing when they do not
        ''' compose. Hangul Jamo composition uses the standard arithmetic; general compositions
        ''' are discovered by normalizing the candidate pair under FormC.
        ''' </summary>
        Public Function ComposePair(a As String, b As String) As String
            Dim aCp As Integer = GetScalarValue(a)
            Dim bCp As Integer = GetScalarValue(b)

            Dim hangul As Integer = ComposeHangul(aCp, bCp)
            If hangul >= 0 Then Return Char.ConvertFromUtf32(hangul)

            Dim key As ULong = (CULng(aCp) << 32) Or CULng(bCp)
            Dim cached As String = Nothing
            If ComposeCache.TryGetValue(key, cached) Then
                Return cached
            End If

            Dim s As String = a & b
            Dim c As String = s.Normalize(NormalizationForm.FormC)
            Dim result As String = Nothing
            If Utf8Helpers.ScalarCount(c) = 1 AndAlso c <> a Then
                result = c
            End If
            ComposeCache.TryAdd(key, result)
            Return result
        End Function

        Private Function GetScalarValue(scalar As String) As Integer
            If scalar.Length = 2 Then
                Dim hi As Integer = AscW(scalar(0))
                Dim lo As Integer = AscW(scalar(1))
                Return &H10000 + ((hi - &HD800) << 10) + (lo - &HDC00)
            End If
            Return AscW(scalar(0))
        End Function

        ' Hangul constants from Unicode 9.0.0 Section 3.12.
        Private Const SBase As Integer = &HAC00
        Private Const LBase As Integer = &H1100
        Private Const VBase As Integer = &H1161
        Private Const TBase As Integer = &H11A7
        Private Const LCount As Integer = 19
        Private Const VCount As Integer = 21
        Private Const TCount As Integer = 28
        Private Const NCount As Integer = VCount * TCount
        Private Const SCount As Integer = LCount * NCount
        Private Const TFirst As Integer = TBase + 1

        Private Function ComposeHangul(a As Integer, b As Integer) As Integer
            If a >= LBase AndAlso a < LBase + LCount AndAlso b >= VBase AndAlso b < VBase + VCount Then
                Dim lIndex As Integer = a - LBase
                Dim vIndex As Integer = b - VBase
                Dim lvIndex As Integer = lIndex * NCount + vIndex * TCount
                Return SBase + lvIndex
            End If
            If a >= SBase AndAlso a < SBase + SCount AndAlso b >= TFirst AndAlso b < TBase + TCount Then
                If (a - SBase) Mod TCount = 0 Then
                    Return a + (b - TBase)
                End If
            End If
            Return -1
        End Function

    End Module

End Namespace
