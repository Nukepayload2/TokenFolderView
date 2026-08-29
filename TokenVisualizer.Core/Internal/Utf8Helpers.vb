Imports System.Text

Namespace Internal

    ''' <summary>
    ''' Information about a single Unicode scalar value inside a .NET string,
    ''' including its position in the UTF-16 (.NET) string and its position/length
    ''' in the UTF-8 encoding of the same text.
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
        ''' <summary>The scalar value as a .NET string (1 or 2 chars).</summary>
        Public Value As String

        Public Sub New(netStart As Integer, netLen As Integer, utf8Start As Integer, utf8Len As Integer, value As String)
            Me.NetStart = netStart
            Me.NetLen = netLen
            Me.Utf8Start = utf8Start
            Me.Utf8Len = utf8Len
            Me.Value = value
        End Sub

        Public ReadOnly Property NetEnd As Integer
            Get
                Return NetStart + NetLen
            End Get
        End Property
    End Structure

    ''' <summary>
    ''' Helpers to bridge between .NET UTF-16 strings and the UTF-8 byte offsets that the
    ''' Rust implementation uses for its alignments.
    ''' </summary>
    Public Module Utf8Helpers

        ''' <summary>Number of UTF-8 bytes needed to encode the given .NET string.</summary>
        Public Function Utf8Length(s As String) As Integer
            If s Is Nothing OrElse s.Length = 0 Then Return 0
            Return Global.System.Text.Encoding.UTF8.GetByteCount(s)
        End Function

        ''' <summary>Number of Unicode scalar values in the string.</summary>
        Public Function ScalarCount(s As String) As Integer
            If s Is Nothing OrElse s.Length = 0 Then Return 0
            Dim count As Integer = 0
            For Each sc In EnumerateScalars(s)
                count += 1
            Next
            Return count
        End Function

        ''' <summary>
        ''' Enumerates the Unicode scalar values of a string, along with their UTF-16 and
        ''' UTF-8 positions. Surrogate pairs are treated as a single scalar value.
        ''' </summary>
        Public Iterator Function EnumerateScalars(s As String) As IEnumerable(Of ScalarInfo)
            If s Is Nothing Then s = String.Empty
            Dim net As Integer = 0
            Dim byteOff As Integer = 0
            While net < s.Length
                Dim c As Char = s(net)
                Dim cp As Integer = AscW(c)
                If cp >= &HD800 AndAlso cp <= &HDBFF AndAlso net + 1 < s.Length Then
                    Dim lo As Char = s(net + 1)
                    Dim loCp As Integer = AscW(lo)
                    If loCp >= &HDC00 AndAlso loCp <= &HDFFF Then
                        Dim rune As String = c.ToString() & lo.ToString()
                        Yield New ScalarInfo(net, 2, byteOff, 4, rune)
                        net += 2
                        byteOff += 4
                        Continue While
                    End If
                End If
                Dim len As Integer
                If cp < &H80 Then
                    len = 1
                ElseIf cp < &H800 Then
                    len = 2
                ElseIf cp >= &HD800 AndAlso cp <= &HDFFF Then
                    ' Lone surrogate: .NET UTF-8 encodes it as the replacement char (3 bytes).
                    len = 3
                Else
                    len = 3
                End If
                Yield New ScalarInfo(net, 1, byteOff, len, c.ToString())
                net += 1
                byteOff += len
            End While
        End Function

        ''' <summary>
        ''' Converts a UTF-8 byte offset to a .NET string index. The byte offset is expected to
        ''' fall on a scalar boundary; for non-boundary offsets the start of the containing scalar
        ''' is returned.
        ''' </summary>
        Public Function ByteToNetIndex(s As String, byteOffset As Integer) As Integer
            Dim total As Integer = Utf8Length(s)
            If byteOffset <= 0 Then Return 0
            If byteOffset >= total Then Return s.Length
            Dim byteOff As Integer = 0
            For Each sc In EnumerateScalars(s)
                byteOff += sc.Utf8Len
                If byteOff = byteOffset Then Return sc.NetEnd
                If byteOff > byteOffset Then Return sc.NetStart
            Next
            Return s.Length
        End Function

        ''' <summary>
        ''' Converts a .NET string index (UTF-16 code-unit offset) to a UTF-8 byte offset.
        ''' </summary>
        Public Function NetIndexToUtf8(s As String, netIndex As Integer) As Integer
            If netIndex <= 0 Then Return 0
            Dim byteOff As Integer = 0
            For Each sc In EnumerateScalars(s)
                If sc.NetStart >= netIndex Then Return byteOff
                byteOff += sc.Utf8Len
            Next
            Return byteOff
        End Function

        ''' <summary>Returns the substring covering the UTF-8 byte range [startByte, endByte).</summary>
        Public Function SliceByUtf8(s As String, startByte As Integer, endByte As Integer) As String
            If startByte = endByte Then Return String.Empty
            Dim startNet As Integer = ByteToNetIndex(s, startByte)
            Dim endNet As Integer = ByteToNetIndex(s, endByte)
            If endNet <= startNet Then Return String.Empty
            Return s.Substring(startNet, endNet - startNet)
        End Function

        ''' <summary>Whether the given UTF-8 byte offset lies on a scalar boundary.</summary>
        Public Function IsUtf8CharBoundary(s As String, byteOffset As Integer) As Boolean
            Dim total As Integer = Utf8Length(s)
            If byteOffset = 0 OrElse byteOffset = total Then Return True
            If byteOffset < 0 OrElse byteOffset > total Then Return False
            Dim byteOff As Integer = 0
            For Each sc In EnumerateScalars(s)
                byteOff += sc.Utf8Len
                If byteOff = byteOffset Then Return True
                If byteOff > byteOffset Then Return False
            Next
            Return False
        End Function

    End Module

End Namespace
