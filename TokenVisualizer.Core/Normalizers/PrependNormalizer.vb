Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>Prepend</c> normalizer (normalizers/prepend.rs).
    ''' Prepends a string to the (non-empty) normalized string.
    ''' </summary>
    Public NotInheritable Class PrependNormalizer
        Implements INormalizer

        Private ReadOnly _prepend As String

        Public Sub New(prepend As String)
            _prepend = If(prepend Is Nothing, String.Empty, prepend)
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            If Not normalized.IsEmpty() Then
                normalized.Prepend(_prepend)
            End If
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Prepend"
            o("prepend") = _prepend
            Return o
        End Function
    End Class

End Namespace
