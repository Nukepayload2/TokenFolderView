Imports System

Namespace Scanning

    ''' <summary>
    ''' Turns the persisted "文件编码" choice into a <see cref="Global.System.Text.Encoding"/> and applies
    ''' that encoding when file bytes become text. <see cref="Resolve"/> is called once per scan (the
    ''' result lives on <see cref="ScanOptions.TextEncoding"/>), so the per-file cost is the single
    ''' reference comparison inside <see cref="Decode"/>.
    ''' </summary>
    Public NotInheritable Class FileEncoding

        ''' <summary>
        ''' Cached because it reaches for the culture and the code-page provider; a scan must not pay
        ''' that per file.
        ''' </summary>
        Private Shared ReadOnly _ansi As New Lazy(Of Global.System.Text.Encoding)(AddressOf ResolveAnsi)

        ''' <summary>
        ''' Maps a persisted choice to an encoding. Only "ANSI" selects anything else: Nothing and any
        ''' unrecognised value (a hand-edited or older settings file) mean the documented default
        ''' UTF-8, and always the shared <see cref="Global.System.Text.Encoding.UTF8"/> instance, which
        ''' <see cref="Decode"/> recognises by reference.
        ''' </summary>
        Public Shared Function Resolve(choice As String) As Global.System.Text.Encoding
            If choice = "ANSI" Then Return _ansi.Value
            Return Global.System.Text.Encoding.UTF8
        End Function

        ''' <summary>
        ''' Decodes the first <paramref name="length"/> bytes of <paramref name="buffer"/>.
        ''' The setting is authoritative (D4): apart from a leading UTF-8 BOM, which is the single
        ''' content-based exception and always decodes as UTF-8, the bytes are read with
        ''' <paramref name="encoding"/> even when they are valid UTF-8 - which is how choosing ANSI over
        ''' a UTF-8 Chinese file yields counted mojibake instead of an error. How often that is what
        ''' actually gets counted is decided by <see cref="ScanOptions.CheckBinary"/> (the
        ''' "跳过二进制文件" switch, on by default), because the binary verdict follows the chosen
        ''' encoding (see <see cref="BinaryDetector"/>). With the switch on, most UTF-8 Chinese files
        ''' never reach here at all - they are skipped as binary and silently missing from the totals,
        ''' which is why the end-to-end effect of choosing ANSI is a large under-count, and only the
        ''' handful whose byte pairs happen to be legal code points get this far and are over-counted.
        ''' With the switch off nothing is skipped, so nearly all of them arrive here as mojibake
        ''' instead. That over-count is the 1.81x-2.08x in total, and up to 3.5x for a single file,
        ''' inflation measured at the component level by decoding a UTF-8 Chinese corpus with the
        ''' verdict gate bypassed - it is deliberately not an end-to-end figure, the two go in
        ''' opposite directions. Pure ASCII is identical in both encodings and unaffected either way.
        ''' The BOM itself is not stripped, because removing it would change the token counts.
        ''' </summary>
        Public Shared Function Decode(buffer As Byte(),
                                      length As Integer,
                                      encoding As Global.System.Text.Encoding) As String
            If encoding Is Global.System.Text.Encoding.UTF8 Then
                Return Global.System.Text.Encoding.UTF8.GetString(buffer, 0, length)
            End If
            If length >= 3 AndAlso buffer(0) = &HEF AndAlso buffer(1) = &HBB AndAlso buffer(2) = &HBF Then
                Return Global.System.Text.Encoding.UTF8.GetString(buffer, 0, length)
            End If
            Return encoding.GetString(buffer, 0, length)
        End Function

        ''' <summary>
        ''' ANSI means the system ANSI code page (D1), never <c>Encoding.Default</c> - that is UTF-8 on
        ''' .NET Core and would silently make the setting a no-op. Without the code-pages provider the
        ''' non-built-in pages throw on lookup, and a platform that reports no ANSI page at all (0)
        ''' leaves nothing to follow, so both cases fall back to UTF-8.
        ''' </summary>
        Private Shared Function ResolveAnsi() As Global.System.Text.Encoding
            Try
                Global.System.Text.Encoding.RegisterProvider(Global.System.Text.CodePagesEncodingProvider.Instance)
                Dim codePage As Integer = Global.System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage
                If codePage <= 0 Then Return Global.System.Text.Encoding.UTF8
                Return Global.System.Text.Encoding.GetEncoding(codePage)
            Catch ex As Exception
                Return Global.System.Text.Encoding.UTF8
            End Try
        End Function

    End Class
End Namespace
