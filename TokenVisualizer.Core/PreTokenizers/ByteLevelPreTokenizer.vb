Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace PreTokenizers

    ''' <summary>
    ''' Port of the Rust <c>ByteLevel</c> pre-tokenizer (pre_tokenizers/byte_level.rs). Optionally
    ''' adds a leading space, splits on the GPT-2 regex (unless disabled), then maps every UTF-8
    ''' byte of each split through the GPT-2 byte-to-char table.
    ''' </summary>
    Public NotInheritable Class ByteLevelPreTokenizer
        Implements IPreTokenizer

        Private ReadOnly _addPrefixSpace As Boolean
        Private ReadOnly _trimOffsets As Boolean
        Private ReadOnly _useRegex As Boolean

        Public Sub New(addPrefixSpace As Boolean, trimOffsets As Boolean, useRegex As Boolean)
            _addPrefixSpace = addPrefixSpace
            _trimOffsets = trimOffsets
            _useRegex = useRegex
        End Sub

        Public Sub New()
            Me.New(True, True, True)
        End Sub

        Public Sub PreTokenize(pretokenized As PreTokenizedString) Implements IPreTokenizer.PreTokenize
            pretokenized.SplitByFunction(
                Function(i As Integer, normalized As NormalizedString) As IEnumerable(Of NormalizedString)
                    If _addPrefixSpace AndAlso Not normalized.Get.StartsWith(" "c) Then
                        normalized.Prepend(" ")
                    End If
                    If _useRegex Then
                        Dim pattern As Pattern = ManualPatternFactory.TryCreate(Gpt2ByteLevelPattern.Canonical)
                        If pattern Is Nothing Then pattern = New RegexPattern(Gpt2ByteLevelPattern.Canonical)
                        Return normalized.Split(pattern, SplitDelimiterBehavior.Isolated)
                    Else
                        Return New List(Of NormalizedString) From {normalized}
                    End If
                End Function)

            pretokenized.Normalize(
                Sub(normalized As NormalizedString)
                    Dim s As String = normalized.Get
                    Dim transformations As New List(Of (String, Integer))()
                    For Each sc In Utf8Helpers.EnumerateScalars(s)
                        Dim bytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(sc.Value)
                        For j As Integer = 0 To bytes.Length - 1
                            Dim ch As Char = BytesToUnicodeTable.GetBytesToChar()(bytes(j))
                            transformations.Add((ch.ToString(), If(j > 0, 1, 0)))
                        Next
                    Next
                    normalized.Transform(transformations, 0)
                End Sub)
        End Sub

        Public Function ToJson() As JsonObject Implements IPreTokenizer.ToJson
            Dim o As New JsonObject()
            o("type") = "ByteLevel"
            o("add_prefix_space") = _addPrefixSpace
            o("trim_offsets") = _trimOffsets
            o("use_regex") = _useRegex
            Return o
        End Function
    End Class

End Namespace
