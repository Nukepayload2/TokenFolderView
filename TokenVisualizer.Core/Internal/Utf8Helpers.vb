Imports System.Text

Namespace Internal

    ''' <summary>
    ''' Information about a single Unicode scalar value inside a .NET string,
    ''' including its position in the UTF-16 (.NET) string and its position/length
    ''' in the UTF-8 encoding of the same text. No per-instance string is stored:
    ''' <see cref="CodePoint"/> carries the scalar value, and a string is built on
    ''' demand (via the ASCII cache or <c>Char.ConvertFromUtf32</c>) only where one is
    ''' actually needed.
    ''' </summary>
    Public Structure ScalarInfo
        ''' <summary>.NET string index of the first UTF-16 code unit of this scalar.</summary>
        Public NetStart As Integer
        ''' <summary>Number of UTF-16 code units (1 or 2).</summary>
        Public NetLen As Integer
        ''' <summary>Byte offset of this scalar in the UTF-8 encoding.</summary>
        Public Utf8Start As Integer
        ''' <summary>Number of UTF-8 bytes (1..4).</summary>
        Public Utf8Len As Integer
        ''' <summary>The Unicode scalar value (code point) of this scalar.</summary>
        Public CodePoint As Integer

        Public Sub New(netStart As Integer, netLen As Integer, utf8Start As Integer, utf8Len As Integer, codePoint As Integer)
            Me.NetStart = netStart
            Me.NetLen = netLen
            Me.Utf8Start = utf8Start
            Me.Utf8Len = utf8Len
            Me.CodePoint = codePoint
        End Sub

        Public ReadOnly Property NetEnd As Integer
            Get
                Return NetStart + NetLen
            End Get
        End Property
    End Structure

    ''' <summary>
    ''' Zero-allocation enumerable over the Unicode scalars of a string. A <c>For Each</c>
    ''' over <see cref="Utf8Helpers.EnumerateScalars"/> calls <see cref="GetEnumerator"/> and
    ''' iterates with a value-type enumerator, so no state machine and no per-scalar heap
    ''' allocation occur. Not an <c>IEnumerable</c>, by design: any LINQ materialization on it
    ''' is a compile-time error, which keeps the hot paths honest.
    ''' </summary>
    Public Structure ScalarEnumerable
        Private _text As String

        Public Sub New(text As String)
            _text = text
        End Sub

        Public Function GetEnumerator() As ScalarEnumerator
            Return New ScalarEnumerator(_text)
        End Function
    End Structure

    ''' <summary>
    ''' Value-type enumerator backing <see cref="ScalarEnumerable"/>. Tracks the UTF-16 index
    ''' and the running UTF-8 byte offset while walking scalar by scalar.
    ''' </summary>
    Public Structure ScalarEnumerator
        Private _text As String
        Private _net As Integer
        Private _byteOff As Integer
        Private _current As ScalarInfo

        Public Sub New(text As String)
            _text = text
            _net = 0
            _byteOff = 0
            _current = Nothing
        End Sub

        Public ReadOnly Property Current As ScalarInfo
            Get
                Return _current
            End Get
        End Property

        Public Function MoveNext() As Boolean
            If _text Is Nothing Then Return False
            If _net >= _text.Length Then Return False
            Dim cp As Integer = UnicodePredicates.ScalarCodePoint(_text, _net)
            Dim netLen As Integer = NetLengthOfCodePoint(cp)
            Dim utf8Len As Integer = Utf8LengthOfCodePoint(cp)
            _current = New ScalarInfo(_net, netLen, _byteOff, utf8Len, cp)
            _net += netLen
            _byteOff += utf8Len
            Return True
        End Function
    End Structure

    ''' <summary>
    ''' Helpers to bridge between .NET UTF-16 strings and the UTF-8 byte offsets that the
    ''' Rust implementation uses for its alignments.
    '''
    ''' All hot-path helpers here scan by .NET index (no <c>Iterator</c>/<c>Yield</c>, no
    ''' per-scalar string) so they can be called from <c>Parallel.ForEach</c> consumers without
    ''' shared mutable state or allocations.
    ''' </summary>
    Public Module Utf8Helpers

        ' ------------------------------------------------------------------
        ' 256-entry ASCII char -> single-char String cache, built once.
        ' ------------------------------------------------------------------
        Private ReadOnly AsciiStrings As String() = BuildAsciiCache()

        Private Function BuildAsciiCache() As String()
            Dim arr As String() = New String(255) {}
            For i As Integer = 0 To 255
                arr(i) = New String(ChrW(i), 1)
            Next
            Return arr
        End Function

        ''' <summary>Number of UTF-16 code units used by a scalar with the given code point (1 or 2).</summary>
        Public Function NetLengthOfCodePoint(cp As Integer) As Integer
            If cp >= &H10000 Then Return 2
            Return 1
        End Function

        ''' <summary>Number of UTF-8 bytes used by a scalar with the given code point (1..4). A lone surrogate encodes as U+FFFD (3 bytes).</summary>
        Public Function Utf8LengthOfCodePoint(cp As Integer) As Integer
            If cp < &H80 Then Return 1
            If cp < &H800 Then Return 2
            If cp >= &HD800 AndAlso cp <= &HDFFF Then Return 3
            If cp < &H10000 Then Return 3
            Return 4
        End Function

        ''' <summary>
        ''' Returns the single-char string for a code point without allocating for ASCII:
        ''' code points 0..255 come from the static cache; supplementary values use
        ''' <c>Char.ConvertFromUtf32</c>; other BMP values (including lone surrogates) are
        ''' built from a single <see cref="Char"/>.
        ''' </summary>
        Public Function ScalarToString(cp As Integer) As String
            If cp >= 0 AndAlso cp < 256 Then Return AsciiStrings(cp)
            If cp >= &H10000 Then Return Char.ConvertFromUtf32(cp)
            Return New String(ChrW(cp), 1)
        End Function

        ''' <summary>
        ''' First UTF-16 code unit of a scalar with the given code point (for BMP it is the
        ''' code point itself; for a supplementary value it is the high surrogate).
        ''' </summary>
        Public Function ScalarFirstChar(cp As Integer) As Char
            If cp >= &H10000 Then
                Dim v As Integer = cp - &H10000
                Return ChrW(&HD800 + (v >> 10))
            End If
            Return ChrW(cp)
        End Function

        ''' <summary>Number of UTF-8 bytes needed to encode the given .NET string.</summary>
        Public Function Utf8Length(s As String) As Integer
            If s Is Nothing OrElse s.Length = 0 Then Return 0
            Return Global.System.Text.Encoding.UTF8.GetByteCount(s)
        End Function

        ''' <summary>Number of Unicode scalar values in the string.</summary>
        Public Function ScalarCount(s As String) As Integer
            If s Is Nothing OrElse s.Length = 0 Then Return 0
            Dim count As Integer = 0
            Dim net As Integer = 0
            While net < s.Length
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                net += NetLengthOfCodePoint(cp)
                count += 1
            End While
            Return count
        End Function

        ''' <summary>
        ''' Enumerates the Unicode scalar values of a string, along with their UTF-16 and
        ''' UTF-8 positions. Surrogate pairs are treated as a single scalar value. Returns a
        ''' value-type enumerable so the enumeration itself allocates nothing.
        ''' </summary>
        Public Function EnumerateScalars(s As String) As ScalarEnumerable
            Return New ScalarEnumerable(s)
        End Function

        ''' <summary>
        ''' Converts a UTF-8 byte offset to a .NET string index. The byte offset is expected to
        ''' fall on a scalar boundary; for non-boundary offsets the start of the containing scalar
        ''' is returned.
        ''' </summary>
        Public Function ByteToNetIndex(s As String, byteOffset As Integer) As Integer
            If s Is Nothing OrElse s.Length = 0 Then Return 0
            Dim total As Integer = Utf8Length(s)
            If byteOffset <= 0 Then Return 0
            If byteOffset >= total Then Return s.Length
            Dim byteOff As Integer = 0
            Dim net As Integer = 0
            While net < s.Length
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                Dim utf8Len As Integer = Utf8LengthOfCodePoint(cp)
                Dim netLen As Integer = NetLengthOfCodePoint(cp)
                Dim newByteOff As Integer = byteOff + utf8Len
                If newByteOff = byteOffset Then Return net + netLen
                If newByteOff > byteOffset Then Return net
                byteOff = newByteOff
                net += netLen
            End While
            Return s.Length
        End Function

        ''' <summary>
        ''' Converts a .NET string index (UTF-16 code-unit offset) to a UTF-8 byte offset.
        ''' </summary>
        Public Function NetIndexToUtf8(s As String, netIndex As Integer) As Integer
            If s Is Nothing OrElse s.Length = 0 Then Return 0
            If netIndex <= 0 Then Return 0
            Dim byteOff As Integer = 0
            Dim net As Integer = 0
            While net < s.Length
                If net >= netIndex Then Return byteOff
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                byteOff += Utf8LengthOfCodePoint(cp)
                net += NetLengthOfCodePoint(cp)
            End While
            Return byteOff
        End Function

        ''' <summary>Returns the substring covering the UTF-8 byte range [startByte, endByte).</summary>
        Public Function SliceByUtf8(s As String, startByte As Integer, endByte As Integer) As String
            If s Is Nothing OrElse s.Length = 0 Then Return String.Empty
            If startByte = endByte Then Return String.Empty
            Dim startNet As Integer = ByteToNetIndex(s, startByte)
            Dim endNet As Integer = ByteToNetIndex(s, endByte)
            If endNet <= startNet Then Return String.Empty
            Return s.Substring(startNet, endNet - startNet)
        End Function

        ''' <summary>Whether the given UTF-8 byte offset lies on a scalar boundary.</summary>
        Public Function IsUtf8CharBoundary(s As String, byteOffset As Integer) As Boolean
            If s Is Nothing OrElse s.Length = 0 Then Return byteOffset = 0
            Dim total As Integer = Utf8Length(s)
            If byteOffset = 0 OrElse byteOffset = total Then Return True
            If byteOffset < 0 OrElse byteOffset > total Then Return False
            Dim byteOff As Integer = 0
            Dim net As Integer = 0
            While net < s.Length
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, net)
                Dim utf8Len As Integer = Utf8LengthOfCodePoint(cp)
                byteOff += utf8Len
                If byteOff = byteOffset Then Return True
                If byteOff > byteOffset Then Return False
                net += NetLengthOfCodePoint(cp)
            End While
            Return False
        End Function

    End Module

End Namespace
