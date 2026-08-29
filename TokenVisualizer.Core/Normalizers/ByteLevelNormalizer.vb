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
                ' Each source scalar produces exactly its UTF-8 byte count of (Char, Integer)
                ' items, so the list is pre-sized to the source's UTF-8 byte length.
                Dim transformations As New List(Of (Char, Integer))(Utf8Helpers.Utf8Length(s))
                For Each sc In Utf8Helpers.EnumerateScalars(s)
                    BytesToUnicodeTable.AppendByteTransform(transformations, sc.CodePoint)
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
