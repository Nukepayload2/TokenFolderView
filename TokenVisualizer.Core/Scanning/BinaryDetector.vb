Imports System
Imports System.Buffers
Imports System.Text

Namespace Scanning

    ''' <summary>
    ''' Detects binary content with a strict, non-flushed UTF-8 decode. Callers normally inspect
    ''' only the first <c>min(4096, length)</c> bytes of a file.
    ''' </summary>
    Public NotInheritable Class BinaryDetector

        ''' <summary>
        ''' True when the byte content cannot be decoded as UTF-8. The decode is run with
        ''' <c>flush:=False</c>, so a valid multi-byte character truncated at the 4 KiB inspection
        ''' boundary is treated as text rather than binary (a <see cref="DecoderFallbackException"/>
        ''' is only raised for genuinely invalid sequences). Empty content is text.
        ''' </summary>
        ''' <remarks>
        ''' The parameter is <see cref="ReadOnlyMemory(Of Byte)"/> (not
        ''' <see cref="ReadOnlySpan(Of Byte)"/>) because the Visual Basic compiler does not support
        ''' ByRef-like types in method signatures; a <c>Byte()</c> or <c>Memory(Of Byte)</c> converts
        ''' to it implicitly.
        ''' </remarks>
        Public Shared Function IsBinary(content As ReadOnlyMemory(Of Byte)) As Boolean
            If content.Length = 0 Then Return False

            Dim decoder As Decoder = New UTF8Encoding(False, True).GetDecoder()
            ' UTF-8 decoding never produces more than one UTF-16 code unit per byte; x4 is a safe
            ' upper bound that also satisfies the span-based Convert's buffer requirement.
            Dim charCount As Integer = CInt(Math.Min(content.Length * 4L, Integer.MaxValue))
            Dim charBuffer As Char() = ArrayPool(Of Char).Shared.Rent(charCount)
            Try
                Try
                    Dim bytesUsed As Integer
                    Dim charsUsed As Integer
                    Dim completed As Boolean
                    decoder.Convert(content.Span, charBuffer.AsSpan(0, charCount), False, bytesUsed, charsUsed, completed)
                    Return False
                Catch ex As DecoderFallbackException
                    Return True
                End Try
            Finally
                ArrayPool(Of Char).Shared.Return(charBuffer)
            End Try
        End Function

    End Class
End Namespace
