Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Processors

    ''' <summary>
    ''' Post-processor that adds the BERT <c>[CLS]</c> / <c>[SEP]</c> special tokens. Mirrors the
    ''' Rust <c>BertProcessing</c> (processors/bert.rs).
    ''' </summary>
    Public NotInheritable Class BertProcessing
        Implements IPostProcessor

        Public ReadOnly Sep As (String, Integer)
        Public ReadOnly Cls As (String, Integer)

        Public Sub New(sep As (String, Integer), cls As (String, Integer))
            Me.Sep = sep
            Me.Cls = cls
        End Sub

        Public Sub New()
            Me.New(("[SEP]", 102), ("[CLS]", 101))
        End Sub

        Public Function GetAddedTokens(isPair As Boolean) As Integer Implements IPostProcessor.GetAddedTokens
            Return If(isPair, 3, 2)
        End Function

        Public Function Process(enc As Encoding, pairEnc As Encoding, addSpecialTokens As Boolean) As Encoding
            Return PostProcessorHelpers.DefaultProcess(Me, enc, pairEnc, addSpecialTokens)
        End Function

        Public Function ProcessEncodings(encodings As List(Of Encoding), addSpecialTokens As Boolean) As List(Of Encoding) Implements IPostProcessor.ProcessEncodings
            If Not addSpecialTokens Then
                Return encodings
            End If

            Dim result As New List(Of Encoding)()
            For i As Integer = 0 To encodings.Count - 1
                If i = 0 Then
                    result.Add(BuildFirst(encodings(i)))
                Else
                    result.Add(BuildPair(encodings(i)))
                End If
            Next
            Return result
        End Function

        ''' <summary>Builds the first sequence: prepends CLS and appends SEP, reprocessing overflowings.</summary>
        Private Function BuildFirst(encoding As Encoding) As Encoding
            Return BuildFirstCore(encoding, True)
        End Function

        Private Function BuildFirstCore(encoding As Encoding, includeOverflowing As Boolean) As Encoding
            Dim e As New Encoding()
            e.Ids.Add(Cls.Item2)
            e.Ids.AddRange(encoding.Ids)
            e.Ids.Add(Sep.Item2)

            e.TypeIds.Add(0)
            e.TypeIds.AddRange(encoding.TypeIds)
            e.TypeIds.Add(0)

            e.Tokens.Add(Cls.Item1)
            e.Tokens.AddRange(encoding.Tokens)
            e.Tokens.Add(Sep.Item1)

            e.Words.Add(Nothing)
            e.Words.AddRange(encoding.Words)
            e.Words.Add(Nothing)

            e.Offsets.Add((0, 0))
            e.Offsets.AddRange(encoding.Offsets)
            e.Offsets.Add((0, 0))

            e.SpecialTokensMask.Add(1)
            e.SpecialTokensMask.AddRange(Enumerable.Repeat(0, encoding.Ids.Count))
            e.SpecialTokensMask.Add(1)

            e.AttentionMask.AddRange(Enumerable.Repeat(1, e.Ids.Count))

            ' For compatibility with TemplateProcessing, the sequence_ranges shouldn't contain the special tokens.
            e.SequenceRanges(0) = (1, e.Ids.Count - 1)

            If includeOverflowing Then
                For Each o As Encoding In encoding.Overflowing
                    e.Overflowing.Add(BuildFirstCore(o, False))
                Next
            End If
            Return e
        End Function

        ''' <summary>Builds the pair sequence: appends SEP, reprocessing overflowings.</summary>
        Private Function BuildPair(encoding As Encoding) As Encoding
            Return BuildPairCore(encoding, True)
        End Function

        Private Function BuildPairCore(encoding As Encoding, includeOverflowing As Boolean) As Encoding
            Dim e As New Encoding()
            e.Ids.AddRange(encoding.Ids)
            e.Ids.Add(Sep.Item2)

            e.TypeIds.AddRange(encoding.TypeIds)
            e.TypeIds.Add(1)

            e.Tokens.AddRange(encoding.Tokens)
            e.Tokens.Add(Sep.Item1)

            e.Words.AddRange(encoding.Words)
            e.Words.Add(Nothing)

            e.Offsets.AddRange(encoding.Offsets)
            e.Offsets.Add((0, 0))

            e.SpecialTokensMask.AddRange(Enumerable.Repeat(0, encoding.Ids.Count))
            e.SpecialTokensMask.Add(1)

            e.AttentionMask.AddRange(Enumerable.Repeat(1, e.Ids.Count))

            ' For compatibility with TemplateProcessing, the sequence_ranges shouldn't contain the special tokens.
            e.SequenceRanges(1) = (0, e.Ids.Count - 1)

            If includeOverflowing Then
                For Each o As Encoding In encoding.Overflowing
                    e.Overflowing.Add(BuildPairCore(o, False))
                Next
            End If
            Return e
        End Function

        Public Function ToJson() As JsonObject Implements IPostProcessor.ToJson
            Dim o As New JsonObject()
            o("type") = "BertProcessing"
            Dim sepArr As New JsonArray()
            sepArr.Add(Sep.Item1)
            sepArr.Add(Sep.Item2)
            o("sep") = sepArr
            Dim clsArr As New JsonArray()
            clsArr.Add(Cls.Item1)
            clsArr.Add(Cls.Item2)
            o("cls") = clsArr
            Return o
        End Function
    End Class

End Namespace
