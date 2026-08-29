Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal
Imports Tokenizers.Serialization

Namespace PreTokenizers

    ''' <summary>
    ''' Options for the metaspace prepending scheme. Mirrors the Rust <c>PrependScheme</c>
    ''' (serialized as "always"/"first"/"never" later).
    ''' </summary>
    Public Enum PrependScheme
        First
        Never
        Always
    End Enum

    ''' <summary>
    ''' Port of the Rust <c>Metaspace</c> pre-tokenizer (pre_tokenizers/metaspace.rs). Replaces
    ''' spaces with the replacement character, optionally prepends it, and optionally splits on it.
    ''' </summary>
    Public NotInheritable Class MetaspacePreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _replacement As Char
        Private ReadOnly _replacementString As String
        Private ReadOnly _prependScheme As PrependScheme
        Private ReadOnly _split As Boolean

        Public Sub New(replacement As Char, prependScheme As PrependScheme, split As Boolean)
            _replacement = replacement
            _replacementString = replacement.ToString()
            _prependScheme = prependScheme
            _split = split
        End Sub

        Public Sub New()
            Me.New("▁"c, PrependScheme.Always, True)
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitByFunction(
                Function(i As Integer, normalized As NormalizedString) As IEnumerable(Of NormalizedString)
                    normalized.Replace(New StringPattern(" "), _replacementString)
                    Select Case _prependScheme
                        Case PrependScheme.Always
                            If Not normalized.Get.StartsWith(_replacementString) Then
                                normalized.Prepend(_replacementString)
                            End If
                        Case PrependScheme.First
                            If Not normalized.Get.StartsWith(_replacementString) AndAlso normalized.OffsetsOriginal().Item1 = 0 Then
                                normalized.Prepend(_replacementString)
                            End If
                        Case PrependScheme.Never
                            ' Nothing to do.
                    End Select
                    If _split Then
                        Dim replacementCp As Integer = AscW(_replacement)
                        Return normalized.Split(New PredicatePattern(Function(r As Rune) r.Value = replacementCp), SplitDelimiterBehavior.MergedWithNext)
                    Else
                        Return New List(Of NormalizedString) From {normalized}
                    End If
                End Function)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Metaspace"
            o("replacement") = _replacementString
            o("prepend_scheme") = SerializationHelpers.PrependSchemeToString(_prependScheme)
            o("split") = _split
            Return o
        End Function
    End Class

End Namespace
