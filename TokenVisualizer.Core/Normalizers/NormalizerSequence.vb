Imports System.Linq
Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>Sequence</c> normalizer (normalizers/utils.rs).
    ''' Runs all the normalizers in the given order against the same NormalizedString.
    ''' </summary>
    Public NotInheritable Class NormalizerSequence
        Implements INormalizer

        Private ReadOnly _normalizers As List(Of INormalizer)

        Public Sub New(normalizers As IEnumerable(Of INormalizer))
            If normalizers Is Nothing Then
                _normalizers = New List(Of INormalizer)()
            Else
                _normalizers = normalizers.ToList()
            End If
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            For Each normalizer In _normalizers
                normalizer.Normalize(normalized)
            Next
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Sequence"
            Dim arr As New JsonArray()
            For Each normalizer In _normalizers
                arr.Add(normalizer.ToJson())
            Next
            o("normalizers") = arr
            Return o
        End Function
    End Class

End Namespace
