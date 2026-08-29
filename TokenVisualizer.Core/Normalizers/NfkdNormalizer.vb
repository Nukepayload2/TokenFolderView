Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>Port of the Rust <c>NFKD</c> normalizer (normalizers/unicode.rs).</summary>
    Public NotInheritable Class NfkdNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Nfkd()
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "NFKD"
            Return o
        End Function
    End Class

End Namespace
