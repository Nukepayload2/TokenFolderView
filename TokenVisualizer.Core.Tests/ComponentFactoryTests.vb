Imports System.Text.Json
Imports Tokenizers
Imports Tokenizers.Decoders
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors
Imports Tokenizers.Serialization

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the <see cref="ComponentFactory"/> dispatcher and the per-component
    ''' <c>ToJson</c> serializers: byte-exact round-trips for every component type, legacy
    ''' untagged dispatch and error behavior. Ported from the Rust
    ''' <c>tests/serialization.rs</c> and the component <c>serde</c> tests.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class ComponentFactoryTests

        ' JsonDocument documents backing the JsonElement roots fed to ComponentFactory.From*.
        ' Held per instance so each test's buffers live until its own cleanup and are then
        ' returned to the pool deterministically.
        Private _openDocs As New List(Of JsonDocument)()

        Private Function OpenJson(json As String) As JsonElement?
            Dim doc As JsonDocument = JsonDocument.Parse(json)
            _openDocs.Add(doc)
            Return doc.RootElement
        End Function

        <TestCleanup>
        Public Sub DisposeOpenDocs()
            For Each doc As JsonDocument In _openDocs
                doc.Dispose()
            Next
            _openDocs.Clear()
        End Sub

        ' ------------------------------------------------------------------
        ' Models
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub BpeModel_RoundTrips_ByteExact()
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"<unk>", 0}, {"a", 1}, {"b", 2}, {"ab", 3}
            }
            Dim model As New BpeModel(vocab, New List(Of String) From {"a b"},
                                      unkToken:="<unk>", ignoreMerges:=True)
            Dim json As String = SerializeJson(model.ToJson())
            Assert.AreEqual(
                "{" & _
                """type"":""BPE"",""dropout"":null,""unk_token"":""<unk>""," & _
                """continuing_subword_prefix"":null,""end_of_word_suffix"":null,""fuse_unk"":false," & _
                """byte_fallback"":false,""ignore_merges"":true," & _
                """vocab"":{""<unk>"":0,""a"":1,""b"":2,""ab"":3},""merges"":[""a b""]" & _
                "}",
                json)

            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Assert.IsInstanceOfType(recon, GetType(BpeModel))
            Assert.AreEqual(json, SerializeJson(DirectCast(recon, IModel).ToJson()))
        End Sub

        <TestMethod>
        Public Sub BpeModel_LegacyUntagged_Dispatch()
            Dim json As String = "{""dropout"":null,""unk_token"":null,""continuing_subword_prefix"":null," & _
                """end_of_word_suffix"":null,""fuse_unk"":false,""byte_fallback"":false," & _
                """vocab"":{""a"":0,""b"":1},""merges"":[""a b""]}"
            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Assert.IsInstanceOfType(recon, GetType(BpeModel))
        End Sub

        <TestMethod>
        Public Sub BpeModel_AcceptsNestedArrayMerges()
            Dim json As String = "{""type"":""BPE"",""vocab"":{""a"":0,""b"":1,""ab"":2}," & _
                """merges"":[[""a"",""b""]]}"
            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Dim bpe As BpeModel = DirectCast(recon, BpeModel)
            Assert.AreEqual(3, bpe.VocabSize)
            Dim tokens As List(Of Token) = bpe.Tokenize("ab")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(2, tokens(0).Id)
        End Sub

        <TestMethod>
        Public Sub WordPieceModel_RoundTrips_ByteExact()
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"[UNK]", 0}, {"hello", 1}, {"hello##world", 2}
            }
            Dim model As New WordPieceModel(vocab, "[UNK]", "##", 100)
            Dim json As String = SerializeJson(model.ToJson())
            Assert.AreEqual(
                "{" & _
                """type"":""WordPiece"",""unk_token"":""[UNK]"",""continuing_subword_prefix"":""##""," & _
                """max_input_chars_per_word"":100,""vocab"":{""[UNK]"":0,""hello"":1,""hello##world"":2}" & _
                "}",
                json)
            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Assert.IsInstanceOfType(recon, GetType(WordPieceModel))
            Assert.AreEqual(json, SerializeJson(DirectCast(recon, IModel).ToJson()))
        End Sub

        <TestMethod>
        Public Sub WordLevelModel_RoundTrips_ByteExact()
            Dim vocab As New Dictionary(Of String, Integer) From {
                {"a", 0}, {"b", 1}
            }
            Dim model As New WordLevelModel(vocab, "<unk>")
            Dim json As String = SerializeJson(model.ToJson())
            Assert.AreEqual(
                "{""type"":""WordLevel"",""vocab"":{""a"":0,""b"":1},""unk_token"":""<unk>""}",
                json)
            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Assert.IsInstanceOfType(recon, GetType(WordLevelModel))
            Assert.AreEqual(json, SerializeJson(DirectCast(recon, IModel).ToJson()))
        End Sub

        <TestMethod>
        Public Sub UnigramModel_RoundTrips_ByteExact()
            Dim vocab As New List(Of (String, Double)) From {
                ("<unk>", 0.0), ("a", -0.5), ("b", -0.7)
            }
            Dim model As New UnigramModel(vocab, 0, False)
            Dim json As String = SerializeJson(model.ToJson())
            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Assert.IsInstanceOfType(recon, GetType(UnigramModel))
            Assert.AreEqual(json, SerializeJson(DirectCast(recon, IModel).ToJson()))
            ' Vocab order is preserved.
            Dim unigram As UnigramModel = DirectCast(recon, UnigramModel)
            Assert.AreEqual(0, unigram.TokenToId("<unk>"))
            Assert.AreEqual(1, unigram.TokenToId("a"))
            Assert.AreEqual(2, unigram.TokenToId("b"))
        End Sub

        <TestMethod>
        Public Sub UnigramModel_WithoutUnkId_SerializesNull()
            Dim vocab As New List(Of (String, Double)) From {("a", -0.5)}
            Dim model As New UnigramModel(vocab, Nothing, False)
            Dim json As String = SerializeJson(model.ToJson())
            Assert.Contains("""unk_id"":null", json)
            Dim recon As Object = ComponentFactory.FromModel(OpenJson(json))
            Assert.AreEqual(json, SerializeJson(DirectCast(recon, IModel).ToJson()))
        End Sub

        ' ------------------------------------------------------------------
        ' Normalizers
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Normalizers_RoundTrip_ByteExact()
            Dim normalizers As New List(Of INormalizer) From {
                New BertNormalizer(True, True, Nothing, True),
                New BertNormalizer(False, False, True, False),
                New ByteLevelNormalizer(),
                New LowercaseNormalizer(),
                New NfcNormalizer(),
                New NfdNormalizer(),
                New NfkcNormalizer(),
                New NfkdNormalizer(),
                New NmtNormalizer(),
                New PrependNormalizer("▁"),
                New ReplaceNormalizer("String", "Hello", "Hey"),
                New ReplaceNormalizer("Regex", "\s+", " "),
                New StripNormalizer(True, False),
                New StripAccentsNormalizer(),
                New NormalizerSequence(New List(Of INormalizer) From {New NfcNormalizer(), New LowercaseNormalizer()})
            }

            For Each n As INormalizer In normalizers
                Dim json As String = SerializeJson(n.ToJson())
                Dim recon As INormalizer = ComponentFactory.FromNormalizer(OpenJson(json))
                Assert.IsNotNull(recon, $"Normalizer not dispatched: {json}")
                Assert.AreEqual(json, SerializeJson(recon.ToJson()), $"Round-trip failed for {json}")
            Next
        End Sub

        <TestMethod>
        Public Sub BertNormalizer_ExactJsonShape()
            Dim n As New BertNormalizer()
            Assert.AreEqual(
                "{""type"":""BertNormalizer"",""clean_text"":true,""handle_chinese_chars"":true," & _
                """strip_accents"":null,""lowercase"":true}",
                SerializeJson(n.ToJson()))
        End Sub

        <TestMethod>
        Public Sub Normalizer_LegacyUntagged_Dispatch()
            ' {"strip_left":false,"strip_right":true} -> Strip
            Dim strip As INormalizer = ComponentFactory.FromNormalizer(
                OpenJson("{""strip_left"":false,""strip_right"":true}"))
            Assert.IsInstanceOfType(strip, GetType(StripNormalizer))

            ' {"prepend":"a"} -> Prepend
            Dim prepend As INormalizer = ComponentFactory.FromNormalizer(
                OpenJson("{""prepend"":""a""}"))
            Assert.IsInstanceOfType(prepend, GetType(PrependNormalizer))
        End Sub

        <TestMethod>
        Public Sub Normalizer_BertAndBertNormalizer_Tags()
            Assert.IsInstanceOfType(
                ComponentFactory.FromNormalizer(OpenJson("{""type"":""Bert"",""clean_text"":true,""handle_chinese_chars"":true,""strip_accents"":null,""lowercase"":true}")),
                GetType(BertNormalizer))
            Assert.IsInstanceOfType(
                ComponentFactory.FromNormalizer(OpenJson("{""type"":""BertNormalizer"",""clean_text"":true,""handle_chinese_chars"":true,""strip_accents"":null,""lowercase"":true}")),
                GetType(BertNormalizer))
        End Sub

        <TestMethod>
        Public Sub Normalizer_MissingField_Errors()
            ' Sequence with no normalizers field.
            Assert.ThrowsExactly(Of ArgumentException)(
                Function() ComponentFactory.FromNormalizer(OpenJson("{""type"":""Sequence"",""prepend_scheme"":""always""}")),
                "missing field `normalizers`")
            ' Empty element cannot be dispatched.
            Assert.ThrowsExactly(Of ArgumentException)(
                Function() ComponentFactory.FromNormalizer(OpenJson("{""type"":""Sequence"",""normalizers"":[{}]}")),
                "data did not match any variant of untagged enum NormalizerUntagged")
            ' Metaspace-shaped object is not a normalizer.
            Assert.IsNull(ComponentFactory.FromNormalizer(OpenJson("{""replacement"":""▁"",""prepend_scheme"":""always""}")))
        End Sub

        <TestMethod>
        Public Sub PrecompiledNormalizer_RoundTrip()
            Dim bytes As Byte() = {&H0, &H1, &H2, &HFF}
            Dim n As New PrecompiledNormalizer(bytes)
            Dim json As String = SerializeJson(n.ToJson())
            Assert.StartsWith("{""type"":""Precompiled"",""precompiled_charsmap"":""", json)
            Dim recon As INormalizer = ComponentFactory.FromNormalizer(OpenJson(json))
            Assert.AreEqual(json, SerializeJson(recon.ToJson()))
        End Sub

        ' ------------------------------------------------------------------
        ' Pre-tokenizers
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub PreTokenizers_RoundTrip_ByteExact()
            Dim pretokenizers As New List(Of IPreTokenizer) From {
                New BertPreTokenizer(),
                New ByteLevelPreTokenizer(True, True, True),
                New CharDelimiterSplit("-"c),
                New DigitsPreTokenizer(False),
                New DigitsPreTokenizer(True),
                New FixedLengthPreTokenizer(5),
                New MetaspacePreTokenizer("▁"c, PrependScheme.Always, True),
                New MetaspacePreTokenizer("_"c, PrependScheme.Never, False),
                New PunctuationPreTokenizer(SplitDelimiterBehavior.Isolated),
                New SplitPreTokenizer("String", "[SEP]", SplitDelimiterBehavior.Isolated, False),
                New SplitPreTokenizer("Regex", "[SEP]", SplitDelimiterBehavior.MergedWithNext, True),
                New UnicodeScriptsPreTokenizer(),
                New WhitespacePreTokenizer(),
                New WhitespaceSplitPreTokenizer(),
                New PreTokenizerSequence(New List(Of IPreTokenizer) From {
                    New WhitespaceSplitPreTokenizer(),
                    New MetaspacePreTokenizer("▁"c, PrependScheme.Always, True)
                })
            }

            For Each pt As IPreTokenizer In pretokenizers
                Dim json As String = SerializeJson(pt.ToJson())
                Dim recon As IPreTokenizer = ComponentFactory.FromPreTokenizer(OpenJson(json))
                Assert.IsNotNull(recon, $"Pre-tokenizer not dispatched: {json}")
                Assert.AreEqual(json, SerializeJson(recon.ToJson()), $"Round-trip failed for {json}")
            Next
        End Sub

        <TestMethod>
        Public Sub PreTokenizer_LegacyUntagged_Dispatch()
            ' {"delimiter":"-"} -> CharDelimiterSplit
            Assert.IsInstanceOfType(
                ComponentFactory.FromPreTokenizer(OpenJson("{""delimiter"":""-""}")),
                GetType(CharDelimiterSplit))
            ' {"replacement":"▁","add_prefix_space":true} -> Metaspace
            Dim ms As IPreTokenizer = ComponentFactory.FromPreTokenizer(
                OpenJson("{""replacement"":""▁"",""add_prefix_space"":true}"))
            Assert.IsInstanceOfType(ms, GetType(MetaspacePreTokenizer))
        End Sub

        <TestMethod>
        Public Sub PreTokenizer_DelimiterVsCharDelimiterSplit_Tags()
            Assert.IsInstanceOfType(
                ComponentFactory.FromPreTokenizer(OpenJson("{""type"":""Delimiter"",""delimiter"":""-""}")),
                GetType(CharDelimiterSplit))
            Assert.IsInstanceOfType(
                ComponentFactory.FromPreTokenizer(OpenJson("{""type"":""CharDelimiterSplit"",""delimiter"":""-""}")),
                GetType(CharDelimiterSplit))
        End Sub

        <TestMethod>
        Public Sub SplitPreTokenizer_ExactJsonShape()
            Dim pt As New SplitPreTokenizer("String", "[SEP]", SplitDelimiterBehavior.Isolated, False)
            Assert.AreEqual(
                "{""type"":""Split"",""pattern"":{""String"":""[SEP]""},""behavior"":""Isolated"",""invert"":false}",
                SerializeJson(pt.ToJson()))

            Dim pt2 As New SplitPreTokenizer("Regex", "[SEP]", SplitDelimiterBehavior.Isolated, False)
            Assert.AreEqual(
                "{""type"":""Split"",""pattern"":{""Regex"":""[SEP]""},""behavior"":""Isolated"",""invert"":false}",
                SerializeJson(pt2.ToJson()))
        End Sub

        <TestMethod>
        Public Sub MetaspacePreTokenizer_ExactJsonShape()
            Dim ms As New MetaspacePreTokenizer("_"c, PrependScheme.Always, True)
            Assert.AreEqual(
                "{""type"":""Metaspace"",""replacement"":""_"",""prepend_scheme"":""always"",""split"":true}",
                SerializeJson(ms.ToJson()))
        End Sub

        <TestMethod>
        Public Sub PreTokenizer_MissingField_Errors()
            Assert.ThrowsExactly(Of ArgumentException)(
                Function() ComponentFactory.FromPreTokenizer(OpenJson("{""type"":""Sequence"",""prepend_scheme"":""always""}")),
                "missing field `pretokenizers`")
            Assert.IsNull(ComponentFactory.FromPreTokenizer(OpenJson("{""behavior"":""default_split""}")))
        End Sub

        ' ------------------------------------------------------------------
        ' Post-processors
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Processors_RoundTrip_ByteExact()
            Dim processors As New List(Of IPostProcessor) From {
                New BertProcessing(("[SEP]", 102), ("[CLS]", 101)),
                New RobertaProcessing(("</s>", 2), ("<s>", 0), True, True),
                New ByteLevelProcessing(True, True, True),
                New TemplateProcessing("[CLS] $A [SEP]",
                                       "[CLS] $A [SEP] $B:1 [SEP]:1",
                                       New Dictionary(Of String, (List(Of Integer), List(Of String))) From {
                                           {"[CLS]", (New List(Of Integer) From {1}, New List(Of String) From {"[CLS]"})},
                                           {"[SEP]", (New List(Of Integer) From {0}, New List(Of String) From {"[SEP]"})}
                                       }),
                New ProcessorSequence(New List(Of IPostProcessor) From {
                    New BertProcessing(("[SEP]", 102), ("[CLS]", 101)),
                    New ByteLevelProcessing(True, True, True)
                })
            }

            For Each pp As IPostProcessor In processors
                Dim json As String = SerializeJson(pp.ToJson())
                Dim recon As IPostProcessor = ComponentFactory.FromPostProcessor(OpenJson(json))
                Assert.IsNotNull(recon, $"Post-processor not dispatched: {json}")
                Assert.AreEqual(json, SerializeJson(recon.ToJson()), $"Round-trip failed for {json}")
            Next
        End Sub

        <TestMethod>
        Public Sub BertProcessing_ExactJsonShape()
            Dim bert As New BertProcessing(("[SEP]", 102), ("[CLS]", 101))
            Assert.AreEqual(
                "{""type"":""BertProcessing"",""sep"":[""[SEP]"",102],""cls"":[""[CLS]"",101]}",
                SerializeJson(bert.ToJson()))
        End Sub

        <TestMethod>
        Public Sub Processor_LegacyUntagged_Dispatch()
            ' {"sep":["[SEP]",102],"cls":["[CLS]",101]} -> Bert
            Dim bert As IPostProcessor = ComponentFactory.FromPostProcessor(
                OpenJson("{""sep"":[""[SEP]"",102],""cls"":[""[CLS]"",101]}"))
            Assert.IsInstanceOfType(bert, GetType(BertProcessing))
            ' Roberta form -> Roberta
            Dim roberta As IPostProcessor = ComponentFactory.FromPostProcessor(
                OpenJson("{""sep"":[""</s>"",2],""cls"":[""<s>"",0],""trim_offsets"":true,""add_prefix_space"":true}"))
            Assert.IsInstanceOfType(roberta, GetType(RobertaProcessing))
        End Sub

        <TestMethod>
        Public Sub TemplateProcessing_ExactJsonShape()
            Dim t As New TemplateProcessing("[CLS] $A [SEP]",
                                            "[CLS] $A [SEP] $B:1 [SEP]:1",
                                            New Dictionary(Of String, (List(Of Integer), List(Of String))) From {
                                                {"[CLS]", (New List(Of Integer) From {1}, New List(Of String) From {"[CLS]"})},
                                                {"[SEP]", (New List(Of Integer) From {0}, New List(Of String) From {"[SEP]"})}
                                            })
            Assert.AreEqual(
                "{" & _
                """type"":""TemplateProcessing""," & _
                """single"":[{""SpecialToken"":{""id"":""[CLS]"",""type_id"":0}},{""Sequence"":{""id"":""A"",""type_id"":0}},{""SpecialToken"":{""id"":""[SEP]"",""type_id"":0}}]," & _
                """pair"":[{""SpecialToken"":{""id"":""[CLS]"",""type_id"":0}},{""Sequence"":{""id"":""A"",""type_id"":0}},{""SpecialToken"":{""id"":""[SEP]"",""type_id"":0}},{""Sequence"":{""id"":""B"",""type_id"":1}},{""SpecialToken"":{""id"":""[SEP]"",""type_id"":1}}]," & _
                """special_tokens"":{""[CLS]"":{""id"":""[CLS]"",""ids"":[1],""tokens"":[""[CLS]""]},""[SEP]"":{""id"":""[SEP]"",""ids"":[0],""tokens"":[""[SEP]""]}}}",
                SerializeJson(t.ToJson()))
        End Sub

        <TestMethod>
        Public Sub Processor_MissingField_Errors()
            Assert.ThrowsExactly(Of ArgumentException)(
                Function() ComponentFactory.FromPostProcessor(OpenJson("{""type"":""Sequence"",""prepend_scheme"":""always""}")),
                "missing field `processors`")
            ' A ByteLevel-shaped object dispatches to ByteLevelProcessing in the legacy path.
            Assert.IsInstanceOfType(
                ComponentFactory.FromPostProcessor(
                    OpenJson("{""add_prefix_space"":true,""trim_offsets"":false,""use_regex"":false}")),
                GetType(ByteLevelProcessing))
        End Sub

        ' ------------------------------------------------------------------
        ' Decoders
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Decoders_RoundTrip_ByteExact()
            Dim decoders As New List(Of IDecoder) From {
                New BpeDecoder("</w>"),
                New ByteFallbackDecoder(),
                New ByteLevelDecoder(True, True, True),
                New WordPieceDecoder("##", True),
                New MetaspaceDecoder("▁"c, PrependScheme.Always, True),
                New CtcDecoder("<pad>", "|", True),
                New ReplaceDecoder("String", "Hello", "Hey"),
                New FuseDecoder(),
                New StripDecoder("H"c, 1, 0),
                New DecoderSequence(New List(Of IDecoder) From {
                    New ByteFallbackDecoder(),
                    New MetaspaceDecoder("▁"c, PrependScheme.Always, True)
                })
            }

            For Each d As IDecoder In decoders
                Dim json As String = SerializeJson(d.ToJson())
                Dim recon As IDecoder = ComponentFactory.FromDecoder(OpenJson(json))
                Assert.IsNotNull(recon, $"Decoder not dispatched: {json}")
                Assert.AreEqual(json, SerializeJson(recon.ToJson()), $"Round-trip failed for {json}")
            Next
        End Sub

        <TestMethod>
        Public Sub Decoder_LegacyUntagged_Dispatch()
            Assert.IsInstanceOfType(
                ComponentFactory.FromDecoder(OpenJson("{""suffix"":""</w>""}")),
                GetType(BpeDecoder))
            Assert.IsInstanceOfType(
                ComponentFactory.FromDecoder(OpenJson("{""prefix"":""##"",""cleanup"":true}")),
                GetType(WordPieceDecoder))
        End Sub

        <TestMethod>
        Public Sub Decoder_MissingField_Errors()
            Assert.ThrowsExactly(Of ArgumentException)(
                Function() ComponentFactory.FromDecoder(OpenJson("{""type"":""Sequence"",""prepend_scheme"":""always""}")),
                "missing field `decoders`")
            ' A Metaspace-shaped object dispatches to MetaspaceDecoder in the legacy path.
            Assert.IsInstanceOfType(
                ComponentFactory.FromDecoder(OpenJson("{""replacement"":""▁"",""prepend_scheme"":""always""}")),
                GetType(MetaspaceDecoder))
        End Sub

        <TestMethod>
        Public Sub DecoderSequence_ExactJsonShape()
            Dim d As New DecoderSequence(New List(Of IDecoder) From {
                New ByteFallbackDecoder(),
                New MetaspaceDecoder("▁"c, PrependScheme.Always, True)
            })
            Assert.AreEqual(
                "{""type"":""Sequence"",""decoders"":[{""type"":""ByteFallback""},{""type"":""Metaspace"",""replacement"":""▁"",""prepend_scheme"":""always"",""split"":true}]}",
                SerializeJson(d.ToJson()))
        End Sub

        ' ------------------------------------------------------------------
        ' Serialization helpers
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub TruncationAndPadding_EnumSerialization()
            Assert.AreEqual("left", SerializationHelpers.TruncationDirectionToString(TruncationDirection.Left))
            Assert.AreEqual("right", SerializationHelpers.TruncationDirectionToString(TruncationDirection.Right))
            Assert.AreEqual("LongestFirst", SerializationHelpers.TruncationStrategyToString(TruncationStrategy.LongestFirst))
            Assert.AreEqual("OnlyFirst", SerializationHelpers.TruncationStrategyToString(TruncationStrategy.OnlyFirst))
            Assert.AreEqual("OnlySecond", SerializationHelpers.TruncationStrategyToString(TruncationStrategy.OnlySecond))
            Assert.AreEqual(TruncationStrategy.OnlySecond, SerializationHelpers.ParseTruncationStrategy("only_second"))
            Assert.AreEqual(TruncationStrategy.OnlyFirst, SerializationHelpers.ParseTruncationStrategy("OnlyFirst"))
        End Sub

        <TestMethod>
        Public Sub ParseMerges_AcceptsBothForms()
            Dim spaceJoined As List(Of String) = ComponentFactory.ParseMerges(
                OpenJson("[""a b"",""c d""]"))
            CollectionAssert.AreEqual(New List(Of String) From {"a b", "c d"}, spaceJoined)

            Dim nested As List(Of String) = ComponentFactory.ParseMerges(
                OpenJson("[[""a"",""b""],[""c"",""d""]]"))
            CollectionAssert.AreEqual(New List(Of String) From {"a b", "c d"}, nested)
        End Sub

    End Class

End Namespace
