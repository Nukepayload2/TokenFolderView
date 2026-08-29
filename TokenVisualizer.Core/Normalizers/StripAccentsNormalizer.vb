Imports System.Globalization
Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>StripAccents</c> normalizer (normalizers/strip.rs).
    ''' Removes combining marks (General_Category = Mn/Mc/Me) WITHOUT applying any
    ''' normalization first.
    ''' </summary>
    Public NotInheritable Class StripAccentsNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Filter(Function(c) Not IsCombiningMark(c))
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "StripAccents"
            Return o
        End Function

        ''' <summary>Whether the char is a combining mark (Mn/Mc/Me).</summary>
        Private Shared Function IsCombiningMark(c As Char) As Boolean
            Dim cat As UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c)
            Return cat = UnicodeCategory.NonSpacingMark OrElse
                   cat = UnicodeCategory.SpacingCombiningMark OrElse
                   cat = UnicodeCategory.EnclosingMark
        End Function

    End Class

End Namespace
