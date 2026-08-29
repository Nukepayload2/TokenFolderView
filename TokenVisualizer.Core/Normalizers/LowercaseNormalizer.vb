Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>Port of the Rust <c>Lowercase</c> normalizer (normalizers/utils.rs).</summary>
    Public NotInheritable Class LowercaseNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Lowercase()
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Lowercase"
            Return o
        End Function
    End Class

End Namespace
