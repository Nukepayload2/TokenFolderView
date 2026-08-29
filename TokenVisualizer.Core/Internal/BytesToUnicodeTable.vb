Imports System.Collections.Generic

Namespace Internal

    ''' <summary>
    ''' Converts bytes to unicode characters. Mirrors the GPT-2 byte-level scheme used by the
    ''' Rust ByteLevel pre-tokenizer and normalizer.
    ''' See https://github.com/openai/gpt-2/blob/master/src/encoder.py#L9
    ''' </summary>
    Public Module BytesToUnicodeTable

        Private ReadOnly BytesToChar As Dictionary(Of Byte, Char)
        Private ReadOnly CharToBytes As Dictionary(Of Char, Byte)
        Private ReadOnly BytesToCharTable As Char()

        Sub New()
            Dim bs As New List(Of Integer)()
            For b As Integer = &H21 To &H7E
                bs.Add(b)
            Next
            For b As Integer = &HA1 To &HAC
                bs.Add(b)
            Next
            For b As Integer = &HAE To &HFF
                bs.Add(b)
            Next

            Dim cs As New List(Of Integer)()
            For Each b In bs
                cs.Add(b)
            Next

            Dim n As Integer = 0
            For b As Integer = 0 To 255
                If Not bs.Contains(b) Then
                    bs.Add(b)
                    cs.Add(&H100 + n)
                    n += 1
                End If
            Next

            Dim table As New Dictionary(Of Byte, Char)()
            For i As Integer = 0 To bs.Count - 1
                table(CByte(bs(i))) = ChrW(cs(i))
            Next
            BytesToChar = table

            Dim inverse As New Dictionary(Of Char, Byte)()
            For Each kvp In table
                inverse(kvp.Value) = kvp.Key
            Next
            CharToBytes = inverse

            ' Dense 256-entry byte -> Char lookup for the hot transform paths. Building it
            ' after the dictionaries are populated keeps the module initializer order safe.
            Dim arr As Char() = New Char(255) {}
            For i As Integer = 0 To 255
                arr(i) = table(CByte(i))
            Next
            BytesToCharTable = arr
        End Sub

        ''' <summary>Returns the byte-to-char mapping for all 256 byte values.</summary>
        Public Function GetBytesToChar() As IReadOnlyDictionary(Of Byte, Char)
            Return BytesToChar
        End Function

        ''' <summary>Returns the char-to-byte inverse mapping.</summary>
        Public Function GetCharToBytes() As IReadOnlyDictionary(Of Char, Byte)
            Return CharToBytes
        End Function

        ''' <summary>
        ''' Returns a dense 256-entry <see cref="Char"/> array indexed directly by the byte value.
        ''' The returned array is read-only by convention (never mutated after module init), so it
        ''' can be shared freely across concurrent readers.
        ''' </summary>
        Public Function GetBytesToCharArray() As Char()
            Return BytesToCharTable
        End Function

        ''' <summary>
        ''' Appends the byte-level transform items for a Unicode scalar value to
        ''' <paramref name="transformations"/>: the scalar's UTF-8 bytes, each mapped through the
        ''' GPT-2 byte-to-char table as a <c>(Char, Integer)</c> pair (change 0 for the first byte,
        ''' 1 for every following byte). Pure arithmetic is used instead of
        ''' <c>Encoding.UTF8.GetBytes</c>, so no per-scalar String or Byte() is allocated. Lone
        ''' surrogates are encoded as U+FFFD (3 bytes), exactly matching
        ''' <c>Encoding.UTF8.GetBytes</c> replacement behavior. Returns the number of bytes emitted.
        ''' </summary>
        Public Function AppendByteTransform(transformations As List(Of (Char, Integer)), cp As Integer) As Integer
            If cp >= &HD800 AndAlso cp <= &HDFFF Then cp = &HFFFD
            Dim table As Char() = BytesToCharTable
            If cp < &H80 Then
                transformations.Add((table(cp), 0))
                Return 1
            ElseIf cp < &H800 Then
                transformations.Add((table(&HC0 Or (cp >> 6)), 0))
                transformations.Add((table(&H80 Or (cp And &H3F)), 1))
                Return 2
            ElseIf cp < &H10000 Then
                transformations.Add((table(&HE0 Or (cp >> 12)), 0))
                transformations.Add((table(&H80 Or ((cp >> 6) And &H3F)), 1))
                transformations.Add((table(&H80 Or (cp And &H3F)), 1))
                Return 3
            Else
                transformations.Add((table(&HF0 Or (cp >> 18)), 0))
                transformations.Add((table(&H80 Or ((cp >> 12) And &H3F)), 1))
                transformations.Add((table(&H80 Or ((cp >> 6) And &H3F)), 1))
                transformations.Add((table(&H80 Or (cp And &H3F)), 1))
                Return 4
            End If
        End Function
    End Module

End Namespace
