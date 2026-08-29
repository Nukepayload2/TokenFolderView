Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>UnicodeScripts</c> pre-tokenizer
    ''' (pre_tokenizers/unicode_scripts/pre_tokenizer.rs). Splits on transitions between Unicode
    ''' script categories, with spaces belonging to every script.
    ''' </summary>
    Public NotInheritable Class UnicodeScriptsPreTokenizer
        Implements IPreTokenizer

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitByFunction(
                Function(i As Integer, normalized As NormalizedString) As IEnumerable(Of NormalizedString)
                    Dim text As String = normalized.Get
                    Dim lastScript As Script? = Nothing
                    Dim offset As Integer = 0
                    Dim ranges As New List(Of Integer)()

                    For Each sc In Utf8Helpers.EnumerateScalars(text)
                        Dim cp As Integer = sc.CodePoint
                        Dim script As Script = UnicodeScripts.FixedScript(cp)
                        If script <> Script.Any AndAlso
                           (Not lastScript.HasValue OrElse
                            (lastScript.Value <> Script.Any AndAlso lastScript.Value <> script)) Then
                            ranges.Add(offset)
                        End If
                        offset += sc.Utf8Len
                        If script <> Script.Any Then
                            lastScript = script
                        End If
                    Next
                    ranges.Add(Utf8Helpers.Utf8Length(text))

                    Dim result As New List(Of NormalizedString)()
                    For k As Integer = 0 To ranges.Count - 2
                        result.Add(normalized.Slice(New OffsetRange(False, ranges(k), ranges(k + 1))))
                    Next
                    Return result
                End Function)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "UnicodeScripts"
            Return o
        End Function
    End Class

End Namespace
