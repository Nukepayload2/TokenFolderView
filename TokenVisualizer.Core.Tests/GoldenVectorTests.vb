Imports System.Collections.Generic
Imports System.Linq
Imports Tokenizers
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' The DEFINITIVE Rust-parity gate. For every pipeline in <see cref="GoldenVectors"/> we load
    ''' the exact tokenizer.json recorded from the Python <c>tokenizers</c> library (the Rust core
    ''' binding) via <see cref="Tokenizer.FromJson"/>, then assert our engine reproduces the
    ''' reference ids (all pipelines), the byte offsets (pipeline 1) and the decoded strings.
    '''
    ''' The deepseek pipeline is exercised in a single explicit integration method that reads the
    ''' real tokenizer.json from disk, so the rest of the suite never touches the file system.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class GoldenVectorTests

        Private Const DeepSeekPath As String =
            "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"

        ''' <summary>
        ''' Byte-offset alignment gates: gpt2 (the task's pipeline 1) plus the real DeepSeek config.
        ''' The Python encode() binding reports char offsets, so tests/performance/gen_golden.py converts
        ''' them to UTF-8 byte offsets; the conversion is exact for these pipelines (no add_prefix_space
        ''' trimming of multi-byte tokens).
        ''' of multi-byte tokens).
        ''' </summary>
        Private Shared ReadOnly ByteOffsetPipelines As HashSet(Of String) =
            New HashSet(Of String) From {"gpt2", "deepseek"}

        <TestMethod>
        Public Sub AllPipelines_MatchPythonReference()
            ' The deepseek pipeline has an empty ConfigJson (it is loaded from the real file in a
            ' single dedicated integration test), so skip it here.
            Dim inlinePipelines = GoldenVectors.Pipelines.Where(
                Function(p) p.ConfigJson.Length > 0).ToList()

            Assert.IsGreaterThanOrEqualTo(inlinePipelines.Count, 7, "Expected at least the 7 inline pipelines")

            For Each pipeline In inlinePipelines
                Dim tokenizer As Tokenizer = Tokenizer.FromJson(pipeline.ConfigJson)
                Dim checkOffsets As Boolean = ByteOffsetPipelines.Contains(pipeline.Name)

                Assert.AreEqual(
                    pipeline.VocabSize, tokenizer.GetVocabSize(),
                    $"{pipeline.Name}: vocabulary size mismatch with the Python reference")

                For Each v As TextVector In pipeline.Vectors
                    Dim label As String = $"{pipeline.Name} | text={v.Text}"

                    Dim enc As Encoding = tokenizer.Encode(v.Text, False)
                    CollectionAssert.AreEqual(v.Ids, enc.Ids, $"{label}: ids mismatch with Python")

                    If checkOffsets Then
                        CollectionAssert.AreEqual(
                            v.ByteOffsets.ToList(), enc.Offsets,
                            $"{label}: byte offsets mismatch with Python")
                    End If

                    ' The decoded reference is recorded from Python with skip_special_tokens=False.
                    Assert.AreEqual(
                        v.Decoded, tokenizer.Decode(v.Ids, False),
                        $"{label}: decode mismatch with Python")

                    ' Pipelines with a special-adding post-processor also record the ids produced
                    ' with add_special_tokens=True, so we verify the post-processor path too.
                    If v.IdsWithSpecials IsNot Nothing Then
                        Dim encFull As Encoding = tokenizer.Encode(v.Text, True)
                        CollectionAssert.AreEqual(
                            v.IdsWithSpecials, encFull.Ids,
                            $"{label}: ids with add_special_tokens=True mismatch with Python")
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' Explicit integration test for the real DeepSeek tokenizer: loads the 6 MB tokenizer.json
        ''' from disk and asserts the ids/decoded match the Python reference. This is the ONLY test
        ''' that touches the file system.
        ''' </summary>
        <TestMethod>
        Public Sub DeepSeek_MatchPythonReference_FromRealFile()
            If Not IO.File.Exists(DeepSeekPath) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If

            Dim pipeline = GoldenVectors.Pipelines.First(Function(p) p.Name = "deepseek")
            Dim tokenizer As Tokenizer = Tokenizer.FromFile(DeepSeekPath)

            Assert.AreEqual(pipeline.VocabSize, tokenizer.GetVocabSize(),
                            "deepseek: vocabulary size mismatch with the Python reference")

            For Each v As TextVector In pipeline.Vectors
                Dim label As String = $"deepseek | text={v.Text}"
                Dim enc As Encoding = tokenizer.Encode(v.Text, False)
                CollectionAssert.AreEqual(v.Ids, enc.Ids, $"{label}: ids mismatch with Python")
                CollectionAssert.AreEqual(v.ByteOffsets.ToList(), enc.Offsets,
                                          $"{label}: byte offsets mismatch with Python")
                Assert.AreEqual(v.Decoded, tokenizer.Decode(v.Ids, False),
                                $"{label}: decode mismatch with Python")
            Next
        End Sub

    End Class

End Namespace
