Imports System.Text.Json.Nodes

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>Nmt</c> normalizer (normalizers/unicode.rs <c>do_nmt</c>).
    ''' Removes ASCII control characters and maps a set of whitespace-like code points to ' '.
    ''' </summary>
    Public NotInheritable Class NmtNormalizer
        Implements INormalizer

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            normalized.Filter(Function(c) Not IsRemovedControl(c))
            normalized.Map(Function(c) If(IsSpaceReplacement(c), " "c, c))
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Nmt"
            Return o
        End Function

        ''' <summary>ASCII control characters to remove entirely.</summary>
        Private Shared Function IsRemovedControl(c As Char) As Boolean
            Dim cp As Integer = AscW(c)
            Return (cp >= &H1 AndAlso cp <= &H8) OrElse
                   cp = &HB OrElse
                   (cp >= &HE AndAlso cp <= &H1F) OrElse
                   cp = &H7F OrElse
                   cp = &H8F OrElse
                   cp = &H9F
        End Function

        ''' <summary>Code points considered as whitespace and mapped to ' '.</summary>
        Private Shared Function IsSpaceReplacement(c As Char) As Boolean
            Dim cp As Integer = AscW(c)
            Return cp = &H9 OrElse cp = &HA OrElse cp = &HC OrElse cp = &HD OrElse
                   cp = &H1680 OrElse
                   (cp >= &H200B AndAlso cp <= &H200F) OrElse
                   cp = &H2028 OrElse cp = &H2029 OrElse
                   cp = &H2581 OrElse
                   cp = &HFEFF OrElse
                   cp = &HFFFD
        End Function

    End Class

End Namespace
