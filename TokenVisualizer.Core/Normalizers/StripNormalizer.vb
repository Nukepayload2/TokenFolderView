Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>Strip</c> normalizer (normalizers/strip.rs).
    ''' Strips leading and/or trailing whitespace.
    ''' </summary>
    Public NotInheritable Class StripNormalizer
        Implements INormalizer

        Private ReadOnly _stripLeft As Boolean
        Private ReadOnly _stripRight As Boolean

        Public Sub New(stripLeft As Boolean, stripRight As Boolean)
            _stripLeft = stripLeft
            _stripRight = stripRight
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            If _stripLeft AndAlso _stripRight Then
                normalized.Strip()
            Else
                If _stripLeft Then normalized.LStrip()
                If _stripRight Then normalized.RStrip()
            End If
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Strip"
            o("strip_left") = _stripLeft
            o("strip_right") = _stripRight
            Return o
        End Function
    End Class

End Namespace
