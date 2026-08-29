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
        End Sub

        ''' <summary>Returns the byte-to-char mapping for all 256 byte values.</summary>
        Public Function GetBytesToChar() As IReadOnlyDictionary(Of Byte, Char)
            Return BytesToChar
        End Function

        ''' <summary>Returns the char-to-byte inverse mapping.</summary>
        Public Function GetCharToBytes() As IReadOnlyDictionary(Of Char, Byte)
            Return CharToBytes
        End Function
    End Module

End Namespace
