Imports System
Imports System.Buffers
Imports System.Collections.Concurrent
Imports System.Text

Namespace Scanning

    ''' <summary>
    ''' Detects binary content by validating the bytes as UTF-8 without throwing. Callers normally
    ''' inspect only the first <c>min(4096, length)</c> bytes of a file. The overload that takes an
    ''' encoding asks the same question about that encoding instead, by probing it with a strict
    ''' decoder.
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

        ''' <summary>
        ''' True when <paramref name="content"/> cannot be read with <paramref name="encoding"/>, so the
        ''' binary verdict follows the encoding the user picked (D2) instead of a fixed UTF-8 rule.
        ''' The UTF-8 case hands straight back to <see cref="IsBinary(ReadOnlyMemory(Of Byte))"/>, which
        ''' keeps the existing Rune loop serving the default path unchanged.
        ''' </summary>
        ''' <remarks>
        ''' Any other encoding is probed with a strict (throwing) decoder, because that is the only way
        ''' to tell "this code page has no mapping for these bytes" apart from "it mapped them to
        ''' something odd" - scanning the output for U+FFFD finds nothing (CP936 answers 0xFF with
        ''' U+F8F5 and a truncated pair with "?"). When no strict twin can be built for a code page the
        ''' content is reported as text on purpose: over-counting a few files is survivable, silently
        ''' declaring a whole folder binary is not. Empty content is text.
        ''' </remarks>
        Public Shared Function IsBinary(content As ReadOnlyMemory(Of Byte),
                                        encoding As Global.System.Text.Encoding) As Boolean
            If encoding Is Global.System.Text.Encoding.UTF8 Then Return IsBinary(content)
            If content.Length = 0 Then Return False

            Dim strict As Global.System.Text.Encoding = GetStrictTwin(encoding)
            If strict Is Nothing Then Return False

            ' Pool the chars: allocating per file would put an allocation into the conversion cost, and
            ' one char per input byte is the most any code page can emit, so the head always fits.
            Dim charBuffer As Char() = ArrayPool(Of Char).Shared.Rent(content.Length)
            Try
                Try
                    ' flush:=False, so a multi-byte pair cut in half by the 4 KiB head boundary is
                    ' buffered rather than rejected - the same tolerance the Rune loop shows.
                    strict.GetDecoder().GetChars(content.Span, charBuffer.AsSpan(0, content.Length), flush:=False)
                    Return False
                Catch ex As DecoderFallbackException
                    Return True
                End Try
            Finally
                ArrayPool(Of Char).Shared.Return(charBuffer)
            End Try
        End Function

        ''' <summary>Strict (throwing) twins, built once per code page because the probe runs per file.</summary>
        Private Shared ReadOnly _strictTwins As New ConcurrentDictionary(Of Integer, Global.System.Text.Encoding)()

        ''' <summary>
        ''' Cache marker for "no strict twin exists for this code page"; a dictionary value cannot be
        ''' Nothing here, and a real lookup never returns this private instance.
        ''' </summary>
        Private Shared ReadOnly _noStrictTwin As Global.System.Text.Encoding = New UTF8Encoding(False, False)

        Private Shared Function GetStrictTwin(encoding As Global.System.Text.Encoding) As Global.System.Text.Encoding
            Dim codePage As Integer = encoding.CodePage
            Dim twin As Global.System.Text.Encoding = Nothing
            If Not _strictTwins.TryGetValue(codePage, twin) Then
                twin = CreateStrictTwin(codePage)
                If twin Is Nothing Then twin = _noStrictTwin
                twin = _strictTwins.GetOrAdd(codePage, twin)
            End If
            If twin Is _noStrictTwin Then Return Nothing
            Return twin
        End Function

        ''' <summary>
        ''' Same code page, but with the lenient fallbacks swapped for a throwing decoder. The provider
        ''' has to be registered first or the non-built-in pages are not found at all.
        ''' </summary>
        Private Shared Function CreateStrictTwin(codePage As Integer) As Global.System.Text.Encoding
            Try
                Global.System.Text.Encoding.RegisterProvider(Global.System.Text.CodePagesEncodingProvider.Instance)
                Return Global.System.Text.Encoding.GetEncoding(codePage,
                                                              New EncoderReplacementFallback("?"),
                                                              New DecoderExceptionFallback())
            Catch
                Return Nothing
            End Try
        End Function

    End Class
End Namespace
