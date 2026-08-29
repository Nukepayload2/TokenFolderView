Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Decoders
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors

Namespace Serialization

    ''' <summary>
    ''' The serde-equivalent dispatcher: reconstructs every component (model, normalizer,
    ''' pre-tokenizer, post-processor, decoder) from a <c>JsonNode</c>. Mirrors the Rust
    ''' <c>ModelWrapper</c>/<c>NormalizerWrapper</c>/<c>PreTokenizerWrapper</c>/
    ''' <c>PostProcessorWrapper</c>/<c>DecoderWrapper</c> deserialization, including the tagged
    ''' dispatch on <c>"type"</c> and the legacy untagged probing by required fields.
    ''' </summary>
    Public NotInheritable Class ComponentFactory

        Private Sub New()
        End Sub

        ' ------------------------------------------------------------------
        ' Models
        ' ------------------------------------------------------------------

        ''' <summary>Reconstructs a model from its JSON node, or <c>Nothing</c> when nothing matches.</summary>
        Public Shared Function FromModel(node As JsonNode) As Object
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)

            Dim tag As String = SerializationHelpers.GetString(obj, "type")
            If tag IsNot Nothing Then
                Select Case tag
                    Case "BPE"
                        Return BuildBpe(obj)
                    Case "WordPiece"
                        Return BuildWordPiece(obj)
                    Case "WordLevel"
                        Return BuildWordLevel(obj)
                    Case "Unigram"
                        Return BuildUnigram(obj)
                    Case Else
                        Throw New ArgumentException($"Unknown model type '{tag}'")
                End Select
            End If

            ' Legacy untagged: probe in the Rust order BPE -> WordPiece -> WordLevel -> Unigram.
            If IsPresent(obj, "vocab") AndAlso IsPresent(obj, "merges") Then
                Return BuildBpe(obj)
            End If
            If IsPresent(obj, "vocab") AndAlso IsPresent(obj, "unk_token") AndAlso
               IsPresent(obj, "continuing_subword_prefix") AndAlso IsPresent(obj, "max_input_chars_per_word") Then
                Return BuildWordPiece(obj)
            End If
            If IsPresent(obj, "vocab") AndAlso IsPresent(obj, "unk_token") Then
                Return BuildWordLevel(obj)
            End If
            If IsPresent(obj, "vocab") Then
                Return BuildUnigram(obj)
            End If
            Return Nothing
        End Function

        Private Shared Function BuildBpe(obj As JsonObject) As BpeModel
            Dim vocab As Dictionary(Of String, Integer) = ParseVocab(SerializationHelpers.GetNode(obj, "vocab"))
            Dim merges As List(Of String) = ParseMerges(SerializationHelpers.GetNode(obj, "merges"))
            Dim dropout As Double? = SerializationHelpers.GetDouble(obj, "dropout")
            Dim unk As String = SerializationHelpers.GetString(obj, "unk_token")
            Dim prefix As String = SerializationHelpers.GetString(obj, "continuing_subword_prefix")
            Dim eow As String = SerializationHelpers.GetString(obj, "end_of_word_suffix")
            Dim fuseUnk As Boolean = SerializationHelpers.GetBool(obj, "fuse_unk").GetValueOrDefault(False)
            Dim byteFallback As Boolean = SerializationHelpers.GetBool(obj, "byte_fallback").GetValueOrDefault(False)
            Dim ignoreMerges As Boolean = SerializationHelpers.GetBool(obj, "ignore_merges").GetValueOrDefault(False)
            Return New BpeModel(vocab, merges, prefix, eow, unk, fuseUnk, byteFallback, dropout, ignoreMerges)
        End Function

        Private Shared Function BuildWordPiece(obj As JsonObject) As WordPieceModel
            Dim vocab As Dictionary(Of String, Integer) = ParseVocab(SerializationHelpers.GetNode(obj, "vocab"))
            Dim unk As String = SerializationHelpers.GetString(obj, "unk_token")
            If unk Is Nothing Then unk = "[UNK]"
            Dim prefix As String = SerializationHelpers.GetString(obj, "continuing_subword_prefix")
            If prefix Is Nothing Then prefix = "##"
            Dim maxChars As Integer = SerializationHelpers.GetInt(obj, "max_input_chars_per_word").GetValueOrDefault(100)
            Return New WordPieceModel(vocab, unk, prefix, maxChars)
        End Function

        Private Shared Function BuildWordLevel(obj As JsonObject) As WordLevelModel
            Dim vocab As Dictionary(Of String, Integer) = ParseVocab(SerializationHelpers.GetNode(obj, "vocab"))
            Dim unk As String = SerializationHelpers.GetString(obj, "unk_token")
            If unk Is Nothing Then unk = "<unk>"
            Return New WordLevelModel(vocab, unk)
        End Function

        Private Shared Function BuildUnigram(obj As JsonObject) As UnigramModel
            Dim vocab As New List(Of (String, Double))()
            Dim vocabNode As JsonNode = SerializationHelpers.GetNode(obj, "vocab")
            If vocabNode IsNot Nothing AndAlso TypeOf vocabNode Is JsonArray Then
                For Each entry As JsonNode In DirectCast(vocabNode, JsonArray)
                    If entry IsNot Nothing AndAlso TypeOf entry Is JsonArray Then
                        Dim pair As JsonArray = DirectCast(entry, JsonArray)
                        If pair.Count >= 2 Then
                            Dim piece As String = pair(0).GetValue(Of String)()
                            Dim score As Double = pair(1).GetValue(Of Double)()
                            vocab.Add((piece, score))
                        End If
                    End If
                Next
            End If
            Dim unkId As Integer? = SerializationHelpers.GetInt(obj, "unk_id")
            Dim byteFallback As Boolean = SerializationHelpers.GetBool(obj, "byte_fallback").GetValueOrDefault(False)
            Return New UnigramModel(vocab, unkId, byteFallback)
        End Function

        ' ------------------------------------------------------------------
        ' Normalizers
        ' ------------------------------------------------------------------

        ''' <summary>Reconstructs a normalizer from its JSON node, or <c>Nothing</c> when nothing matches.</summary>
        Public Shared Function FromNormalizer(node As JsonNode) As INormalizer
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)

            Dim tag As String = SerializationHelpers.GetString(obj, "type")
            If tag IsNot Nothing Then
                Select Case tag
                    Case "Bert", "BertNormalizer"
                        Return BuildBertNormalizer(obj)
                    Case "ByteLevel"
                        Return New ByteLevelNormalizer()
                    Case "Lowercase"
                        Return New LowercaseNormalizer()
                    Case "NFC"
                        Return New NfcNormalizer()
                    Case "NFD"
                        Return New NfdNormalizer()
                    Case "NFKC"
                        Return New NfkcNormalizer()
                    Case "NFKD"
                        Return New NfkdNormalizer()
                    Case "Nmt"
                        Return New NmtNormalizer()
                    Case "Precompiled"
                        Return BuildPrecompiledNormalizer(obj)
                    Case "Prepend"
                        Return New PrependNormalizer(SerializationHelpers.GetString(obj, "prepend"))
                    Case "Replace"
                        Return BuildReplaceNormalizer(obj)
                    Case "Strip"
                        Return New StripNormalizer(SerializationHelpers.GetBool(obj, "strip_left").GetValueOrDefault(False),
                                                   SerializationHelpers.GetBool(obj, "strip_right").GetValueOrDefault(False))
                    Case "StripAccents"
                        Return New StripAccentsNormalizer()
                    Case "Sequence"
                        Return BuildNormalizerSequence(obj)
                    Case Else
                        Throw New ArgumentException($"Unknown normalizer type '{tag}'")
                End Select
            End If

            ' Legacy untagged probing in the Rust order.
            If IsPresent(obj, "clean_text") AndAlso IsPresent(obj, "handle_chinese_chars") AndAlso
               IsPresent(obj, "strip_accents") AndAlso IsPresent(obj, "lowercase") Then
                Return BuildBertNormalizer(obj)
            End If
            If IsPresent(obj, "strip_left") AndAlso IsPresent(obj, "strip_right") Then
                Return New StripNormalizer(SerializationHelpers.GetBool(obj, "strip_left").GetValueOrDefault(False),
                                           SerializationHelpers.GetBool(obj, "strip_right").GetValueOrDefault(False))
            End If
            If IsPresent(obj, "normalizers") Then
                Return BuildNormalizerSequence(obj)
            End If
            If IsPresent(obj, "precompiled_charsmap") Then
                Return BuildPrecompiledNormalizer(obj)
            End If
            If IsPresent(obj, "pattern") AndAlso IsPresent(obj, "content") Then
                Return BuildReplaceNormalizer(obj)
            End If
            If IsPresent(obj, "prepend") Then
                Return New PrependNormalizer(SerializationHelpers.GetString(obj, "prepend"))
            End If
            Return Nothing
        End Function

        Private Shared Function BuildBertNormalizer(obj As JsonObject) As BertNormalizer
            Dim cleanText As Boolean = SerializationHelpers.GetBool(obj, "clean_text").GetValueOrDefault(True)
            Dim handleChinese As Boolean = SerializationHelpers.GetBool(obj, "handle_chinese_chars").GetValueOrDefault(True)
            Dim stripAccents As Boolean? = SerializationHelpers.GetBool(obj, "strip_accents")
            Dim lowercase As Boolean = SerializationHelpers.GetBool(obj, "lowercase").GetValueOrDefault(True)
            Return New BertNormalizer(cleanText, handleChinese, stripAccents, lowercase)
        End Function

        Private Shared Function BuildPrecompiledNormalizer(obj As JsonObject) As PrecompiledNormalizer
            Dim node As JsonNode = SerializationHelpers.GetNode(obj, "precompiled_charsmap")
            Dim bytes As Byte() = Array.Empty(Of Byte)()
            If node IsNot Nothing Then
                Dim s As String = Nothing
                If TypeOf node Is JsonValue Then
                    Try
                        s = node.GetValue(Of String)()
                    Catch
                        s = Nothing
                    End Try
                End If
                If s IsNot Nothing Then
                    Try
                        bytes = Convert.FromBase64String(s)
                    Catch
                        ' Not base64; treat as raw UTF-8.
                        bytes = Global.System.Text.Encoding.UTF8.GetBytes(s)
                    End Try
                End If
            End If
            Return New PrecompiledNormalizer(bytes)
        End Function

        Private Shared Function BuildReplaceNormalizer(obj As JsonObject) As ReplaceNormalizer
            Dim patternNode As JsonNode = SerializationHelpers.GetNode(obj, "pattern")
            Dim kind As String = "String"
            Dim pattern As String = ""
            If patternNode IsNot Nothing AndAlso TypeOf patternNode Is JsonObject Then
                Dim pObj As JsonObject = DirectCast(patternNode, JsonObject)
                If IsPresent(pObj, "Regex") Then
                    kind = "Regex"
                    pattern = SerializationHelpers.GetString(pObj, "Regex")
                Else
                    kind = "String"
                    pattern = SerializationHelpers.GetString(pObj, "String")
                End If
            End If
            Dim content As String = SerializationHelpers.GetString(obj, "content")
            Return New ReplaceNormalizer(kind, pattern, content)
        End Function

        Private Shared Function BuildNormalizerSequence(obj As JsonObject) As NormalizerSequence
            Dim arrNode As JsonNode = SerializationHelpers.GetNode(obj, "normalizers")
            If arrNode Is Nothing OrElse TypeOf arrNode IsNot JsonArray Then
                Throw New ArgumentException("missing field `normalizers`")
            End If
            Dim items As New List(Of INormalizer)()
            For Each item As JsonNode In DirectCast(arrNode, JsonArray)
                Dim norm As INormalizer = FromNormalizer(item)
                If norm Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum NormalizerUntagged")
                End If
                items.Add(norm)
            Next
            Return New NormalizerSequence(items)
        End Function

        ' ------------------------------------------------------------------
        ' Pre-tokenizers
        ' ------------------------------------------------------------------

        ''' <summary>Reconstructs a pre-tokenizer from its JSON node, or <c>Nothing</c> when nothing matches.</summary>
        Public Shared Function FromPreTokenizer(node As JsonNode) As IPreTokenizer
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)

            Dim tag As String = SerializationHelpers.GetString(obj, "type")
            If tag IsNot Nothing Then
                Select Case tag
                    Case "BertPreTokenizer"
                        Return New BertPreTokenizer()
                    Case "ByteLevel"
                        Return BuildByteLevelPreTokenizer(obj)
                    Case "Delimiter", "CharDelimiterSplit"
                        Return New CharDelimiterSplit(ParseDelimiterChar(obj))
                    Case "Digits"
                        Return New DigitsPreTokenizer(SerializationHelpers.GetBool(obj, "individual_digits").GetValueOrDefault(False))
                    Case "FixedLength"
                        Return New FixedLengthPreTokenizer(SerializationHelpers.GetInt(obj, "length").GetValueOrDefault(5))
                    Case "Metaspace"
                        Return BuildMetaspacePreTokenizer(obj)
                    Case "Punctuation"
                        Return New PunctuationPreTokenizer(ParseBehavior(obj))
                    Case "Sequence"
                        Return BuildPreTokenizerSequence(obj)
                    Case "Split"
                        Return BuildSplitPreTokenizer(obj)
                    Case "UnicodeScripts"
                        Return New UnicodeScriptsPreTokenizer()
                    Case "Whitespace"
                        Return New WhitespacePreTokenizer()
                    Case "WhitespaceSplit"
                        Return New WhitespaceSplitPreTokenizer()
                    Case Else
                        Throw New ArgumentException($"Unknown pre-tokenizer type '{tag}'")
                End Select
            End If

            ' Legacy untagged probing in the Rust order.
            If IsPresent(obj, "add_prefix_space") AndAlso IsPresent(obj, "trim_offsets") Then
                Return BuildByteLevelPreTokenizer(obj)
            End If
            If IsPresent(obj, "delimiter") Then
                Return New CharDelimiterSplit(ParseDelimiterChar(obj))
            End If
            If IsPresent(obj, "replacement") Then
                Return BuildMetaspacePreTokenizer(obj)
            End If
            If IsPresent(obj, "pretokenizers") Then
                Return BuildPreTokenizerSequence(obj)
            End If
            If IsPresent(obj, "pattern") AndAlso IsPresent(obj, "behavior") AndAlso TryParseBehavior(obj).HasValue Then
                Return BuildSplitPreTokenizer(obj)
            End If
            Dim punctBehavior As SplitDelimiterBehavior? = TryParseBehavior(obj)
            If punctBehavior.HasValue Then
                Return New PunctuationPreTokenizer(punctBehavior.Value)
            End If
            If IsPresent(obj, "individual_digits") Then
                Return New DigitsPreTokenizer(SerializationHelpers.GetBool(obj, "individual_digits").GetValueOrDefault(False))
            End If
            If IsPresent(obj, "length") Then
                Return New FixedLengthPreTokenizer(SerializationHelpers.GetInt(obj, "length").GetValueOrDefault(5))
            End If
            Return Nothing
        End Function

        Private Shared Function BuildByteLevelPreTokenizer(obj As JsonObject) As ByteLevelPreTokenizer
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim useRegex As Boolean = SerializationHelpers.GetBool(obj, "use_regex").GetValueOrDefault(True)
            Return New ByteLevelPreTokenizer(addPrefix, trimOffsets, useRegex)
        End Function

        Private Shared Function ParseDelimiterChar(obj As JsonObject) As Char
            Dim s As String = SerializationHelpers.GetString(obj, "delimiter")
            If String.IsNullOrEmpty(s) Then Throw New ArgumentException("missing field `delimiter`")
            Return s(0)
        End Function

        Private Shared Function BuildMetaspacePreTokenizer(obj As JsonObject) As MetaspacePreTokenizer
            Dim replacement As String = SerializationHelpers.GetString(obj, "replacement")
            If String.IsNullOrEmpty(replacement) Then Throw New ArgumentException("missing field `replacement`")
            Dim scheme As PrependScheme = PrependScheme.Always
            Dim schemeStr As String = SerializationHelpers.GetString(obj, "prepend_scheme")
            If schemeStr IsNot Nothing Then
                scheme = SerializationHelpers.ParsePrependScheme(schemeStr)
            Else
                Dim addPrefix As Boolean? = SerializationHelpers.GetBool(obj, "add_prefix_space")
                If addPrefix.HasValue AndAlso Not addPrefix.Value Then
                    scheme = PrependScheme.Never
                End If
            End If
            Dim split As Boolean = SerializationHelpers.GetBool(obj, "split").GetValueOrDefault(True)
            Return New MetaspacePreTokenizer(replacement(0), scheme, split)
        End Function

        Private Shared Function ParseBehavior(obj As JsonObject) As SplitDelimiterBehavior
            Dim s As String = SerializationHelpers.GetString(obj, "behavior")
            If s Is Nothing Then Throw New ArgumentException("missing field `behavior`")
            Return SerializationHelpers.ParseSplitDelimiterBehavior(s)
        End Function

        Private Shared Function TryParseBehavior(obj As JsonObject) As SplitDelimiterBehavior?
            Dim s As String = SerializationHelpers.GetString(obj, "behavior")
            If s Is Nothing Then Return Nothing
            Try
                Return SerializationHelpers.ParseSplitDelimiterBehavior(s)
            Catch ex As ArgumentException
                Return Nothing
            End Try
        End Function

        Private Shared Function BuildPreTokenizerSequence(obj As JsonObject) As PreTokenizerSequence
            Dim arrNode As JsonNode = SerializationHelpers.GetNode(obj, "pretokenizers")
            If arrNode Is Nothing OrElse TypeOf arrNode IsNot JsonArray Then
                Throw New ArgumentException("missing field `pretokenizers`")
            End If
            Dim items As New List(Of IPreTokenizer)()
            For Each item As JsonNode In DirectCast(arrNode, JsonArray)
                Dim pt As IPreTokenizer = FromPreTokenizer(item)
                If pt Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum PreTokenizerUntagged")
                End If
                items.Add(pt)
            Next
            Return New PreTokenizerSequence(items)
        End Function

        Private Shared Function BuildSplitPreTokenizer(obj As JsonObject) As SplitPreTokenizer
            Dim patternNode As JsonNode = SerializationHelpers.GetNode(obj, "pattern")
            Dim kind As String = "String"
            Dim pattern As String = ""
            If patternNode IsNot Nothing AndAlso TypeOf patternNode Is JsonObject Then
                Dim pObj As JsonObject = DirectCast(patternNode, JsonObject)
                If IsPresent(pObj, "Regex") Then
                    kind = "Regex"
                    pattern = SerializationHelpers.GetString(pObj, "Regex")
                Else
                    kind = "String"
                    pattern = SerializationHelpers.GetString(pObj, "String")
                End If
            End If
            Dim behavior As SplitDelimiterBehavior = SerializationHelpers.ParseSplitDelimiterBehavior(
                SerializationHelpers.GetString(obj, "behavior"))
            Dim invert As Boolean = SerializationHelpers.GetBool(obj, "invert").GetValueOrDefault(False)
            Return New SplitPreTokenizer(kind, pattern, behavior, invert)
        End Function

        ' ------------------------------------------------------------------
        ' Post-processors
        ' ------------------------------------------------------------------

        ''' <summary>Reconstructs a post-processor from its JSON node, or <c>Nothing</c> when nothing matches.</summary>
        Public Shared Function FromPostProcessor(node As JsonNode) As IPostProcessor
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)

            Dim tag As String = SerializationHelpers.GetString(obj, "type")
            If tag IsNot Nothing Then
                Select Case tag
                    Case "RobertaProcessing"
                        Return BuildRobertaProcessing(obj)
                    Case "BertProcessing"
                        Return BuildBertProcessing(obj)
                    Case "ByteLevel"
                        Return BuildByteLevelProcessing(obj)
                    Case "TemplateProcessing"
                        Return BuildTemplateProcessing(obj)
                    Case "Sequence"
                        Return BuildProcessorSequence(obj)
                    Case Else
                        Throw New ArgumentException($"Unknown post-processor type '{tag}'")
                End Select
            End If

            ' Legacy untagged probing in the Rust order: Roberta -> Bert -> ByteLevel -> Template -> Sequence.
            If IsPresent(obj, "sep") AndAlso IsPresent(obj, "cls") AndAlso
               IsPresent(obj, "trim_offsets") AndAlso IsPresent(obj, "add_prefix_space") Then
                Return BuildRobertaProcessing(obj)
            End If
            If IsPresent(obj, "sep") AndAlso IsPresent(obj, "cls") Then
                Return BuildBertProcessing(obj)
            End If
            If IsPresent(obj, "add_prefix_space") AndAlso IsPresent(obj, "trim_offsets") Then
                Return BuildByteLevelProcessing(obj)
            End If
            If IsPresent(obj, "single") AndAlso IsPresent(obj, "pair") Then
                Return BuildTemplateProcessing(obj)
            End If
            If IsPresent(obj, "processors") Then
                Return BuildProcessorSequence(obj)
            End If
            Return Nothing
        End Function

        Private Shared Function ParseTokenPair(obj As JsonObject, key As String) As (String, Integer)
            Dim node As JsonNode = SerializationHelpers.GetNode(obj, key)
            If node Is Nothing OrElse TypeOf node IsNot JsonArray Then Throw New ArgumentException($"missing field `{key}`")
            Dim arr As JsonArray = DirectCast(node, JsonArray)
            If arr.Count < 2 Then Throw New ArgumentException($"invalid `{key}`")
            Return (arr(0).GetValue(Of String)(), arr(1).GetValue(Of Integer)())
        End Function

        Private Shared Function BuildBertProcessing(obj As JsonObject) As BertProcessing
            Dim sep As (String, Integer) = ParseTokenPair(obj, "sep")
            Dim cls As (String, Integer) = ParseTokenPair(obj, "cls")
            Return New BertProcessing(sep, cls)
        End Function

        Private Shared Function BuildRobertaProcessing(obj As JsonObject) As RobertaProcessing
            Dim sep As (String, Integer) = ParseTokenPair(obj, "sep")
            Dim cls As (String, Integer) = ParseTokenPair(obj, "cls")
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Return New RobertaProcessing(sep, cls, trimOffsets, addPrefix)
        End Function

        Private Shared Function BuildByteLevelProcessing(obj As JsonObject) As ByteLevelProcessing
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim useRegex As Boolean = SerializationHelpers.GetBool(obj, "use_regex").GetValueOrDefault(True)
            Return New ByteLevelProcessing(addPrefix, trimOffsets, useRegex)
        End Function

        Private Shared Function BuildProcessorSequence(obj As JsonObject) As ProcessorSequence
            Dim arrNode As JsonNode = SerializationHelpers.GetNode(obj, "processors")
            If arrNode Is Nothing OrElse TypeOf arrNode IsNot JsonArray Then
                Throw New ArgumentException("missing field `processors`")
            End If
            Dim items As New List(Of IPostProcessor)()
            For Each item As JsonNode In DirectCast(arrNode, JsonArray)
                Dim pp As IPostProcessor = FromPostProcessor(item)
                If pp Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum PostProcessorWrapper")
                End If
                items.Add(pp)
            Next
            Return New ProcessorSequence(items)
        End Function

        Private Shared Function BuildTemplateProcessing(obj As JsonObject) As TemplateProcessing
            Dim singleStr As String = TemplateToString(SerializationHelpers.GetNode(obj, "single"))
            Dim pairStr As String = TemplateToString(SerializationHelpers.GetNode(obj, "pair"))

            Dim special As New Dictionary(Of String, (List(Of Integer), List(Of String)))()
            Dim specialNode As JsonNode = SerializationHelpers.GetNode(obj, "special_tokens")
            If specialNode IsNot Nothing AndAlso TypeOf specialNode Is JsonObject Then
                Dim sObj As JsonObject = DirectCast(specialNode, JsonObject)
                For Each kv As KeyValuePair(Of String, JsonNode) In sObj
                    Dim entry As JsonObject = TryCast(kv.Value, JsonObject)
                    If entry Is Nothing Then Continue For
                    Dim ids As New List(Of Integer)()
                    Dim idsNode As JsonNode = SerializationHelpers.GetNode(entry, "ids")
                    If idsNode IsNot Nothing AndAlso TypeOf idsNode Is JsonArray Then
                        For Each idNode As JsonNode In DirectCast(idsNode, JsonArray)
                            ids.Add(idNode.GetValue(Of Integer)())
                        Next
                    End If
                    Dim tokens As New List(Of String)()
                    Dim tokensNode As JsonNode = SerializationHelpers.GetNode(entry, "tokens")
                    If tokensNode IsNot Nothing AndAlso TypeOf tokensNode Is JsonArray Then
                        For Each tNode As JsonNode In DirectCast(tokensNode, JsonArray)
                            tokens.Add(tNode.GetValue(Of String)())
                        Next
                    End If
                    special(kv.Key) = (ids, tokens)
                Next
            End If
            Return New TemplateProcessing(singleStr, pairStr, special)
        End Function

        ''' <summary>Reconstructs a template string ("$A [SEP] $B:1 ...") from its JSON piece array.</summary>
        Private Shared Function TemplateToString(node As JsonNode) As String
            If node Is Nothing OrElse TypeOf node IsNot JsonArray Then
                Throw New ArgumentException("missing field `template`")
            End If
            Dim parts As New List(Of String)()
            For Each piece As JsonNode In DirectCast(node, JsonArray)
                Dim pObj As JsonObject = TryCast(piece, JsonObject)
                If pObj Is Nothing Then Continue For
                If IsPresent(pObj, "Sequence") Then
                    Dim seq As JsonObject = TryCast(SerializationHelpers.GetNode(pObj, "Sequence"), JsonObject)
                    Dim id As String = SerializationHelpers.GetString(seq, "id")
                    Dim typeId As Integer = SerializationHelpers.GetInt(seq, "type_id").GetValueOrDefault(0)
                    parts.Add(If(typeId = 0, "$" & id, "$" & id & ":" & typeId))
                ElseIf IsPresent(pObj, "SpecialToken") Then
                    Dim sp As JsonObject = TryCast(SerializationHelpers.GetNode(pObj, "SpecialToken"), JsonObject)
                    Dim id As String = SerializationHelpers.GetString(sp, "id")
                    Dim typeId As Integer = SerializationHelpers.GetInt(sp, "type_id").GetValueOrDefault(0)
                    parts.Add(If(typeId = 0, id, id & ":" & typeId))
                End If
            Next
            Return String.Join(" ", parts)
        End Function

        ' ------------------------------------------------------------------
        ' Decoders
        ' ------------------------------------------------------------------

        ''' <summary>Reconstructs a decoder from its JSON node, or <c>Nothing</c> when nothing matches.</summary>
        Public Shared Function FromDecoder(node As JsonNode) As IDecoder
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return Nothing
            Dim obj As JsonObject = DirectCast(node, JsonObject)

            Dim tag As String = SerializationHelpers.GetString(obj, "type")
            If tag IsNot Nothing Then
                Select Case tag
                    Case "BPEDecoder"
                        Dim suffix As String = SerializationHelpers.GetString(obj, "suffix")
                        If suffix Is Nothing Then suffix = "</w>"
                        Return New BpeDecoder(suffix)
                    Case "ByteLevel"
                        Return BuildByteLevelDecoder(obj)
                    Case "WordPiece"
                        Return New WordPieceDecoder(SerializationHelpers.GetString(obj, "prefix"),
                                                    SerializationHelpers.GetBool(obj, "cleanup").GetValueOrDefault(True))
                    Case "Metaspace"
                        Return BuildMetaspaceDecoder(obj)
                    Case "CTC"
                        Return New CtcDecoder(SerializationHelpers.GetString(obj, "pad_token"),
                                             SerializationHelpers.GetString(obj, "word_delimiter_token"),
                                             SerializationHelpers.GetBool(obj, "cleanup").GetValueOrDefault(True))
                    Case "Sequence"
                        Return BuildDecoderSequence(obj)
                    Case "Replace"
                        Return BuildReplaceDecoder(obj)
                    Case "Fuse"
                        Return New FuseDecoder()
                    Case "Strip"
                        Return New StripDecoder(ParseCharContent(obj), SerializationHelpers.GetInt(obj, "start").GetValueOrDefault(0),
                                                SerializationHelpers.GetInt(obj, "stop").GetValueOrDefault(0))
                    Case "ByteFallback"
                        Return New ByteFallbackDecoder()
                    Case Else
                        Throw New ArgumentException($"Unknown decoder type '{tag}'")
                End Select
            End If

            ' Legacy untagged probing in the Rust order.
            If IsPresent(obj, "suffix") Then
                Return New BpeDecoder(SerializationHelpers.GetString(obj, "suffix"))
            End If
            If IsPresent(obj, "add_prefix_space") AndAlso IsPresent(obj, "trim_offsets") Then
                Return BuildByteLevelDecoder(obj)
            End If
            If IsPresent(obj, "prefix") AndAlso IsPresent(obj, "cleanup") Then
                Return New WordPieceDecoder(SerializationHelpers.GetString(obj, "prefix"),
                                            SerializationHelpers.GetBool(obj, "cleanup").GetValueOrDefault(True))
            End If
            If IsPresent(obj, "replacement") Then
                Return BuildMetaspaceDecoder(obj)
            End If
            If IsPresent(obj, "pad_token") AndAlso IsPresent(obj, "word_delimiter_token") Then
                Return New CtcDecoder(SerializationHelpers.GetString(obj, "pad_token"),
                                      SerializationHelpers.GetString(obj, "word_delimiter_token"),
                                      SerializationHelpers.GetBool(obj, "cleanup").GetValueOrDefault(True))
            End If
            If IsPresent(obj, "decoders") Then
                Return BuildDecoderSequence(obj)
            End If
            If IsPresent(obj, "pattern") AndAlso IsPresent(obj, "content") Then
                Return BuildReplaceDecoder(obj)
            End If
            If IsPresent(obj, "content") AndAlso IsPresent(obj, "start") AndAlso IsPresent(obj, "stop") Then
                Return New StripDecoder(ParseCharContent(obj), SerializationHelpers.GetInt(obj, "start").GetValueOrDefault(0),
                                        SerializationHelpers.GetInt(obj, "stop").GetValueOrDefault(0))
            End If
            Return Nothing
        End Function

        Private Shared Function BuildByteLevelDecoder(obj As JsonObject) As ByteLevelDecoder
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim useRegex As Boolean = SerializationHelpers.GetBool(obj, "use_regex").GetValueOrDefault(True)
            Return New ByteLevelDecoder(addPrefix, trimOffsets, useRegex)
        End Function

        Private Shared Function BuildMetaspaceDecoder(obj As JsonObject) As MetaspaceDecoder
            Dim replacement As String = SerializationHelpers.GetString(obj, "replacement")
            If String.IsNullOrEmpty(replacement) Then Throw New ArgumentException("missing field `replacement`")
            Dim scheme As PrependScheme = PrependScheme.Always
            Dim schemeStr As String = SerializationHelpers.GetString(obj, "prepend_scheme")
            If schemeStr IsNot Nothing Then
                scheme = SerializationHelpers.ParsePrependScheme(schemeStr)
            Else
                Dim addPrefix As Boolean? = SerializationHelpers.GetBool(obj, "add_prefix_space")
                If addPrefix.HasValue AndAlso Not addPrefix.Value Then
                    scheme = PrependScheme.Never
                End If
            End If
            Dim split As Boolean = SerializationHelpers.GetBool(obj, "split").GetValueOrDefault(True)
            Return New MetaspaceDecoder(replacement(0), scheme, split)
        End Function

        Private Shared Function BuildDecoderSequence(obj As JsonObject) As DecoderSequence
            Dim arrNode As JsonNode = SerializationHelpers.GetNode(obj, "decoders")
            If arrNode Is Nothing OrElse TypeOf arrNode IsNot JsonArray Then
                Throw New ArgumentException("missing field `decoders`")
            End If
            Dim items As New List(Of IDecoder)()
            For Each item As JsonNode In DirectCast(arrNode, JsonArray)
                Dim dec As IDecoder = FromDecoder(item)
                If dec Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum DecoderUntagged")
                End If
                items.Add(dec)
            Next
            Return New DecoderSequence(items)
        End Function

        Private Shared Function BuildReplaceDecoder(obj As JsonObject) As ReplaceDecoder
            Dim patternNode As JsonNode = SerializationHelpers.GetNode(obj, "pattern")
            Dim kind As String = "String"
            Dim pattern As String = ""
            If patternNode IsNot Nothing AndAlso TypeOf patternNode Is JsonObject Then
                Dim pObj As JsonObject = DirectCast(patternNode, JsonObject)
                If IsPresent(pObj, "Regex") Then
                    kind = "Regex"
                    pattern = SerializationHelpers.GetString(pObj, "Regex")
                Else
                    kind = "String"
                    pattern = SerializationHelpers.GetString(pObj, "String")
                End If
            End If
            Dim content As String = SerializationHelpers.GetString(obj, "content")
            Return New ReplaceDecoder(kind, pattern, content)
        End Function

        Private Shared Function ParseCharContent(obj As JsonObject) As Char
            Dim s As String = SerializationHelpers.GetString(obj, "content")
            If String.IsNullOrEmpty(s) Then Return " "c
            Return s(0)
        End Function

        ' ------------------------------------------------------------------
        ' Helpers
        ' ------------------------------------------------------------------

        Private Shared Function IsPresent(obj As JsonObject, key As String) As Boolean
            Dim n As JsonNode = Nothing
            Return obj IsNot Nothing AndAlso obj.TryGetPropertyValue(key, n) AndAlso n IsNot Nothing
        End Function

        ''' <summary>Parses a vocab object (token string to id).</summary>
        Public Shared Function ParseVocab(node As JsonNode) As Dictionary(Of String, Integer)
            Dim result As New Dictionary(Of String, Integer)()
            If node Is Nothing OrElse TypeOf node IsNot JsonObject Then Return result
            Dim obj As JsonObject = DirectCast(node, JsonObject)
            For Each kv As KeyValuePair(Of String, JsonNode) In obj
                Dim id As Integer
                If kv.Value IsNot Nothing AndAlso TypeOf kv.Value Is JsonValue Then
                    Try
                        id = kv.Value.GetValue(Of Integer)()
                        result(kv.Key) = id
                        Continue For
                    Catch
                    End Try
                    Try
                        Dim l As Long = kv.Value.GetValue(Of Long)()
                        result(kv.Key) = CInt(l)
                        Continue For
                    Catch
                    End Try
                End If
            Next
            Return result
        End Function

        ''' <summary>
        ''' Parses a merges array, accepting both space-joined strings (<c>["a b", ...]</c>) and
        ''' nested arrays (<c>[["a","b"], ...]</c>).
        ''' </summary>
        Public Shared Function ParseMerges(node As JsonNode) As List(Of String)
            Dim result As New List(Of String)()
            If node Is Nothing OrElse TypeOf node IsNot JsonArray Then Return result
            For Each item As JsonNode In DirectCast(node, JsonArray)
                If item Is Nothing Then Continue For
                If TypeOf item Is JsonArray Then
                    Dim pair As JsonArray = DirectCast(item, JsonArray)
                    If pair.Count >= 2 Then
                        result.Add(pair(0).GetValue(Of String)() & " " & pair(1).GetValue(Of String)())
                    End If
                ElseIf TypeOf item Is JsonValue Then
                    result.Add(item.GetValue(Of String)())
                End If
            Next
            Return result
        End Function

    End Class

End Namespace
