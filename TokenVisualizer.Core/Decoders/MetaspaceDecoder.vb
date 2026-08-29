Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Serialization

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>Metaspace</c> decoder (pre_tokenizers/metaspace.rs). Replaces every
    ''' occurrence of the replacement character with a space. When the prepend scheme is not
    ''' <c>Never</c>, a leading replacement character on the very first token is dropped.
    ''' </summary>
    Public NotInheritable Class MetaspaceDecoder
        Implements IDecoder

        Private ReadOnly _replacement As Char
        Private ReadOnly _prependScheme As PrependScheme
        Private ReadOnly _split As Boolean

        Public Sub New(Optional replacement As Char = "▁"c,
                       Optional prependScheme As PrependScheme = PrependScheme.Always,
                       Optional split As Boolean = True)
            _replacement = replacement
            _prependScheme = prependScheme
            _split = split
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim result As New List(Of String)()
            Dim i As Integer = 0
            For Each rawToken In If(tokens, Enumerable.Empty(Of String)())
                Dim sb As New StringBuilder()
                If rawToken IsNot Nothing Then
                    For Each c As Char In rawToken
                        If c = _replacement Then
                            If i = 0 AndAlso _prependScheme <> PrependScheme.Never Then
                                ' The leading replacement on the first token is dropped.
                            Else
                                sb.Append(" "c)
                            End If
                        Else
                            sb.Append(c)
                        End If
                    Next
                End If
                result.Add(sb.ToString())
                i += 1
            Next
            Return result
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "Metaspace"
            o("replacement") = _replacement.ToString()
            o("prepend_scheme") = SerializationHelpers.PrependSchemeToString(_prependScheme)
            o("split") = _split
            Return o
        End Function
    End Class

End Namespace
