Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>ByteFallback</c> decoder (decoders/byte_fallback.rs). Converts
    ''' byte tokens of the form <c>&lt;0x61&gt;</c> back into bytes and attempts to make them
    ''' into a string. Inconvertible byte runs produce one <c>�</c> per byte.
    ''' </summary>
    Public NotInheritable Class ByteFallbackDecoder
        Implements IDecoder

        Private Shared ReadOnly StrictUtf8 As New UTF8Encoding(False, True)
        Private Const Replacement As String = "�"

        Public Sub New()
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim newTokens As New List(Of String)()
            Dim byteBuffer As New List(Of Byte)()

            For Each rawToken In If(tokens, Enumerable.Empty(Of String)())
                Dim token As String = If(rawToken, String.Empty)
                Dim parsed As Byte? = Nothing
                If token.Length = 6 AndAlso token.StartsWith("<0x") AndAlso token.EndsWith(">") Then
                    Dim value As Byte = 0
                    If Byte.TryParse(token.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, value) Then
                        parsed = value
                    End If
                End If
                If parsed.HasValue Then
                    byteBuffer.Add(parsed.Value)
                Else
                    FlushByteBuffer(byteBuffer, newTokens)
                    newTokens.Add(token)
                End If
            Next
            FlushByteBuffer(byteBuffer, newTokens)

            Return newTokens
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "ByteFallback"
            Return o
        End Function

        Private Shared Sub FlushByteBuffer(byteBuffer As List(Of Byte), newTokens As List(Of String))
            If byteBuffer.Count = 0 Then Return
            Dim bytes As Byte() = byteBuffer.ToArray()
            Try
                newTokens.Add(StrictUtf8.GetString(bytes))
            Catch ex As DecoderFallbackException
                For i As Integer = 0 To bytes.Length - 1
                    newTokens.Add(Replacement)
                Next
            End Try
            byteBuffer.Clear()
        End Sub
    End Class

End Namespace
