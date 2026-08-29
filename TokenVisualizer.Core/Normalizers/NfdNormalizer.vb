Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>Port of the Rust <c>NFD</c> normalizer (normalizers/unicode.rs).</summary>
    Public NotInheritable Class NfdNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Nfd()
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "NFD"
            Return o
        End Function
    End Class

End Namespace
