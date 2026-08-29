Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>Port of the Rust <c>NFC</c> normalizer (normalizers/unicode.rs).</summary>
    Public NotInheritable Class NfcNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Nfc()
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "NFC"
            Return o
        End Function
    End Class

End Namespace
