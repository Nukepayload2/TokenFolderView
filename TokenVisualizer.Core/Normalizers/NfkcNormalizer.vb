Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>Port of the Rust <c>NFKC</c> normalizer (normalizers/unicode.rs).</summary>
    Public NotInheritable Class NfkcNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Nfkc()
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "NFKC"
            Return o
        End Function
    End Class

End Namespace
