Imports System
Imports System.Buffers
Imports System.Text

Namespace Scanning

    ''' <summary>
    ''' Detects binary content by validating the bytes as UTF-8 without throwing. Callers normally
    ''' inspect only the first <c>min(4096, length)</c> bytes of a file.
    ''' </summary>
    Public NotInheritable Class BinaryDetector

        ''' <summary>
        ''' True when the byte content is not well-formed UTF-8. Uses
        ''' <see cref="Rune.DecodeFromUtf8"/> so nothing is thrown and no buffer is allocated:
        ''' <see cref="OperationStatus.InvalidData"/> means binary, while
        ''' <see cref="OperationStatus.NeedMoreData"/> (a valid multi-byte character truncated at the
        ''' inspection boundary) is treated as text, mirroring the previous flush:=False decoder
        ''' semantics. Empty content is text.
        ''' </summary>
        ''' <remarks>
        ''' The parameter is <see cref="ReadOnlyMemory(Of Byte)"/> (not
        ''' <see cref="ReadOnlySpan(Of Byte)"/>) because the Visual Basic compiler does not support
        ''' ByRef-like types in method signatures; a <c>Byte()</c> or <c>Memory(Of Byte)</c> converts
        ''' to it implicitly.
        ''' </remarks>
        Public Shared Function IsBinary(content As ReadOnlyMemory(Of Byte)) As Boolean
            ' Hoisted span local: inferred (no explicit `As ReadOnlySpan(Of Byte)`, which this VB
            ' compiler rejects); the ExtendRestrictedTypes analyzer backstops ref-safety. For an
            ' array-backed Memory the Span is a managed view over the same buffer, no pinning.
            Dim span = content.Span
            Dim idx As Integer = 0
            While idx < span.Length
                Dim rune As Rune
                Dim consumed As Integer
                Select Case Rune.DecodeFromUtf8(span.Slice(idx), rune, consumed)
                    Case OperationStatus.Done
                        idx += consumed
                    Case OperationStatus.NeedMoreData
                        ' A valid multi-byte sequence truncated at the end (e.g. the 4 KiB inspection
                        ' boundary): treat as text, exactly like a flush:=False decode.
                        Return False
                    Case Else
                        Return True
                End Select
            End While
            Return False
        End Function

    End Class
End Namespace
