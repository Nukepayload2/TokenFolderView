Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>ByteLevel</c> normalizer (normalizers/byte_level.rs).
    ''' Maps every UTF-8 byte of each char through the GPT-2 byte-to-char table.
    ''' </summary>
    Public NotInheritable Class ByteLevelNormalizer
        Implements INormalizer

        Public Sub New()
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            If Not normalized.IsEmpty() Then
                Dim s As String = normalized.Get
                Dim transformations As New List(Of (String, Integer))()
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    Dim bytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(sc.Value)
                    For i As Integer = 0 To bytes.Length - 1
                        Dim ch As Char = BytesToUnicodeTable.GetBytesToChar()(bytes(i))
                        transformations.Add((ch.ToString(), If(i > 0, 1, 0)))
                    Next
                Next
                normalized.Transform(transformations, 0)
            End If
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "ByteLevel"
            Return o
        End Function
    End Class

End Namespace
