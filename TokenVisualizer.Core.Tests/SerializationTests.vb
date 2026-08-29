Imports System.Text.Json.Nodes
Imports Tokenizers
Imports Tokenizers.Decoders
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tokenizer-level serialization tests: the tokenizer.json round-trip invariant (ported from
    ''' Rust <c>serialization.rs</c>) and loading the real deepseek tokenizer.json.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class SerializationTests

        <TestMethod>
        Public Sub DeserializationSerialization_Invariant()
            ' Ported from the Rust test_deserialization_serialization_invariant.
            Dim json As String = "{" & vbCrLf &
                "  ""version"": ""1.0""," & vbCrLf &
                "  ""truncation"": null," & vbCrLf &
                "  ""padding"": null," & vbCrLf &
                "  ""added_tokens"": [" & vbCrLf &
                "    {""id"": 0, ""content"": ""[SPECIAL_0]"", ""single_word"": false, ""lstrip"": false, ""rstrip"": false, ""normalized"": false, ""special"": true}," & vbCrLf &
                "    {""id"": 1, ""content"": ""[SPECIAL_1]"", ""single_word"": false, ""lstrip"": false, ""rstrip"": false, ""normalized"": true, ""special"": false}," & vbCrLf &
                "    {""id"": 2, ""content"": ""[SPECIAL_2]"", ""single_word"": false, ""lstrip"": false, ""rstrip"": false, ""normalized"": false, ""special"": true}" & vbCrLf &
                "  ]," & vbCrLf &
                "  ""normalizer"": null," & vbCrLf &
                "  ""pre_tokenizer"": null," & vbCrLf &
                "  ""post_processor"": null," & vbCrLf &
                "  ""decoder"": null," & vbCrLf &
                "  ""model"": {""type"": ""WordPiece"", ""unk_token"": ""[UNK]"", ""continuing_subword_prefix"": """", ""max_input_chars_per_word"": 100, ""vocab"": {}}" & vbCrLf &
                "}"

            Dim tokenizer As Tokenizer = Tokenizer.FromJson(json)
            Dim reserialized As String = tokenizer.ToJson()

            ' The reserialized JSON must be structurally identical to the input.
            Assert.IsTrue(NodesEqual(JsonNode.Parse(json), JsonNode.Parse(reserialized)),
                          $"Round-trip mismatch{vbCrLf}INPUT:{vbCrLf}{json}{vbCrLf}OUTPUT:{vbCrLf}{reserialized}")

            ' And loading the reserialized form again is stable.
            Dim tokenizer2 As Tokenizer = Tokenizer.FromJson(reserialized)
            Assert.IsTrue(NodesEqual(JsonNode.Parse(reserialized), JsonNode.Parse(tokenizer2.ToJson())))
        End Sub

        <TestMethod>
        Public Sub Tokenizer_WithAllComponents_RoundTrips()
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"hello", 0}, {"world", 1}, {"[UNK]", 2}
            }
            Dim model As New WordPieceModel(vocab, "[UNK]", "##", 100)

            Dim tokenizer As New Tokenizer(model)
            tokenizer.WithNormalizer(New NormalizerSequence(New List(Of INormalizer) From {
                New NfcNormalizer(), New LowercaseNormalizer()
            }))
            tokenizer.WithPreTokenizer(New BertPreTokenizer())
            tokenizer.WithPostProcessor(New BertProcessing(("[SEP]", 102), ("[CLS]", 101)))
            tokenizer.WithDecoder(New WordPieceDecoder("##", True))
            tokenizer.AddSpecialTokens(New List(Of AddedToken) From {
                AddedToken.From("[CLS]", True),
                AddedToken.From("[SEP]", True)
            })
            tokenizer.SetTruncation(512, 0, TruncationStrategy.LongestFirst, TruncationDirection.Right)
            tokenizer.SetPadding(New PaddingParams())

            Dim json As String = tokenizer.ToJson()
            Dim recon As Tokenizer = Tokenizer.FromJson(json)
            Assert.IsTrue(NodesEqual(JsonNode.Parse(json), JsonNode.Parse(recon.ToJson())),
                          $"Tokenizer round-trip mismatch{vbCrLf}{json}{vbCrLf}{recon.ToJson()}")

            ' The reconstructed tokenizer must still produce the same encoding.
            Dim original As Encoding = tokenizer.Encode("Hello World", True)
            Dim reencoded As Encoding = recon.Encode("Hello World", True)
            CollectionAssert.AreEqual(original.Ids, reencoded.Ids)
            CollectionAssert.AreEqual(original.Tokens, reencoded.Tokens)
        End Sub

        <TestMethod>
        Public Sub Tokenizer_InlineJson_RoundTrips()
            ' A self-contained tokenizer.json string (no file dependency).
            Dim json As String = "{" & _
                """version"":""1.0""," & _
                """truncation"":null,""padding"":null,""added_tokens"":[]," & _
                """normalizer"":{""type"":""Sequence"",""normalizers"":[]}," & _
                """pre_tokenizer"":{""type"":""Sequence"",""pretokenizers"":[{""type"":""Split"",""pattern"":{""String"":"" ""},""behavior"":""Removed"",""invert"":false},{""type"":""ByteLevel"",""add_prefix_space"":false,""trim_offsets"":true,""use_regex"":false}]}," & _
                """post_processor"":{""type"":""ByteLevel"",""add_prefix_space"":true,""trim_offsets"":false,""use_regex"":true}," & _
                """decoder"":{""type"":""ByteLevel"",""add_prefix_space"":true,""trim_offsets"":true,""use_regex"":true}," & _
                """model"":{""type"":""BPE"",""dropout"":null,""unk_token"":null,""continuing_subword_prefix"":null,""end_of_word_suffix"":null,""fuse_unk"":false,""byte_fallback"":false," & _
                """vocab"":{""Ġh"":0,""e"":1,""l"":2,""o"":3,""Ġw"":4,""r"":5,""d"":6,""Ġhe"":7,""Ġwo"":8},""merges"":[""Ġh e"",""Ġw o""]}" & _
                "}"

            Dim tokenizer As Tokenizer = Tokenizer.FromJson(json)
            Assert.AreEqual(9, tokenizer.Model.VocabSize)
            Assert.IsInstanceOfType(tokenizer.Model, GetType(BpeModel))
            Assert.IsInstanceOfType(tokenizer.PreTokenizer, GetType(PreTokenizerSequence))
            Assert.AreEqual(2, DirectCast(tokenizer.Model, BpeModel).MergeCount)

            ' Serialization is stable: loading the reserialized form reproduces the same output.
            Dim reserialized As String = tokenizer.ToJson()
            Dim reloaded As Tokenizer = Tokenizer.FromJson(reserialized)
            Assert.IsTrue(NodesEqual(JsonNode.Parse(reserialized), JsonNode.Parse(reloaded.ToJson())),
                          $"Inline round-trip mismatch{vbCrLf}{reserialized}")

            ' The reserialized form normalizes the BPE model (adds ignore_merges) but keeps the
            ' same vocab, merges and added tokens.
            Dim modelObj As JsonObject = DirectCast(JsonNode.Parse(reserialized), JsonObject)
            Dim modelNode As JsonObject = DirectCast(modelObj("model"), JsonObject)
            Assert.IsFalse(modelNode("ignore_merges").GetValue(Of Boolean)())
            Assert.AreEqual(9, DirectCast(modelNode("vocab"), JsonObject).Count)
            Assert.AreEqual(2, DirectCast(modelNode("merges"), JsonArray).Count)
        End Sub

        <TestMethod
        >
        Public Sub DeepSeek_FromFile_LoadsCorrectCounts()
            ' NOTE: this is an explicit integration test that reads the real tokenizer.json.
            Dim path As String = "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"
            If Not IO.File.Exists(path) Then
                Assert.Inconclusive("deepseek-v4-flash/tokenizer.json not present")
                Return
            End If

            Dim tokenizer As Tokenizer = Tokenizer.FromFile(path)
            Assert.AreEqual(128000, tokenizer.Model.VocabSize)
            Dim bpe As BpeModel = DirectCast(tokenizer.Model, BpeModel)
            Assert.AreEqual(127741, bpe.MergeCount)
            Assert.AreEqual(1283, tokenizer.AddedVocabulary.Count)
            Assert.IsInstanceOfType(tokenizer.PreTokenizer, GetType(PreTokenizerSequence))
            Assert.IsInstanceOfType(tokenizer.PostProcessor, GetType(ByteLevelProcessing))
            Assert.IsInstanceOfType(tokenizer.Decoder, GetType(ByteLevelDecoder))
            Assert.IsInstanceOfType(tokenizer.Normalizer, GetType(NormalizerSequence))

            ' Round-trip serialization is stable.
            Dim json As String = tokenizer.ToJson()
            Dim reloaded As Tokenizer = Tokenizer.FromJson(json)
            Assert.IsTrue(NodesEqual(JsonNode.Parse(json), JsonNode.Parse(reloaded.ToJson())))

            ' Sanity encode: the special "begin of sentence" token is recognized.
            Dim sepId As Integer? = tokenizer.TokenToId("<｜begin▁of▁sentence｜>")
            Assert.IsTrue(sepId.HasValue, "begin-of-sentence added token should have an id")
        End Sub

        Private Shared Function NodesEqual(a As JsonNode, b As JsonNode) As Boolean
            If a Is Nothing AndAlso b Is Nothing Then Return True
            If a Is Nothing OrElse b Is Nothing Then Return False
            If TypeOf a Is JsonValue AndAlso TypeOf b Is JsonValue Then
                Return a.ToJsonString() = b.ToJsonString()
            End If
            If TypeOf a Is JsonObject AndAlso TypeOf b Is JsonObject Then
                Dim ao As JsonObject = DirectCast(a, JsonObject)
                Dim bo As JsonObject = DirectCast(b, JsonObject)
                If ao.Count <> bo.Count Then Return False
                For Each kv As KeyValuePair(Of String, JsonNode) In ao
                    Dim bVal As JsonNode = Nothing
                    If Not bo.TryGetPropertyValue(kv.Key, bVal) Then Return False
                    If Not NodesEqual(kv.Value, bVal) Then Return False
                Next
                Return True
            End If
            If TypeOf a Is JsonArray AndAlso TypeOf b Is JsonArray Then
                Dim aa As JsonArray = DirectCast(a, JsonArray)
                Dim ba As JsonArray = DirectCast(b, JsonArray)
                If aa.Count <> ba.Count Then Return False
                For i As Integer = 0 To aa.Count - 1
                    If Not NodesEqual(aa(i), ba(i)) Then Return False
                Next
                Return True
            End If
            Return False
        End Function

    End Class

End Namespace
