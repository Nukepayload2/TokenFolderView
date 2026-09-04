Imports System.Linq
Imports System.Text.Json
Imports Tokenizers.Decoders
Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers
Imports Tokenizers.PreTokenizers
Imports Tokenizers.Processors

Namespace Serialization

    ''' <summary>
    ''' The serde-equivalent dispatcher: reconstructs every component (model, normalizer,
    ''' pre-tokenizer, post-processor, decoder) from a <c>JsonElement</c>. Mirrors the Rust
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

        ''' <summary>
        ''' Reconstructs a model from its JSON node, or <c>Nothing</c> when nothing matches.
        ''' <paramref name="cacheCapacity"/> / <paramref name="cacheMaxWord"/> are optional
        ''' overrides threaded through to the BPE model's word cache (used by the dev benchmark to
        ''' sweep capacity and the max-word-length cache-eligibility limit). <c>Nothing</c> keeps the
        ''' model defaults.
        ''' </summary>
        Public Shared Function FromModel(prop As JsonElement?,
                                         Optional cacheCapacity As Integer? = Nothing,
                                         Optional cacheMaxWord As Integer? = Nothing,
                                         Optional sharedCacheCapacity As Integer? = Nothing) As Object
            If Not prop.HasValue Then Return Nothing
            Dim obj As JsonElement = prop.Value
            If obj.ValueKind <> JsonValueKind.Object Then Return Nothing

            Dim tag As String = SerializationHelpers.GetString(obj, "type")
            If tag IsNot Nothing Then
                Select Case tag
                    Case "BPE"
                        Return BuildBpe(obj, cacheCapacity, cacheMaxWord, sharedCacheCapacity)
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
                Return BuildBpe(obj, cacheCapacity, cacheMaxWord)
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

        Private Shared Function BuildBpe(obj As JsonElement,
                                         Optional cacheCapacity As Integer? = Nothing,
                                         Optional cacheMaxWord As Integer? = Nothing,
                                         Optional sharedCacheCapacity As Integer? = Nothing) As BpeModel
            Dim vocab As Dictionary(Of String, Integer) = ParseVocab(SerializationHelpers.GetProperty(obj, "vocab"))
            Dim merges As List(Of String) = ParseMerges(SerializationHelpers.GetProperty(obj, "merges"))
            Dim dropout As Double? = SerializationHelpers.GetDouble(obj, "dropout")
            Dim unk As String = SerializationHelpers.GetString(obj, "unk_token")
            Dim prefix As String = SerializationHelpers.GetString(obj, "continuing_subword_prefix")
            Dim eow As String = SerializationHelpers.GetString(obj, "end_of_word_suffix")
            Dim fuseUnk As Boolean = SerializationHelpers.GetBool(obj, "fuse_unk").GetValueOrDefault(False)
            Dim byteFallback As Boolean = SerializationHelpers.GetBool(obj, "byte_fallback").GetValueOrDefault(False)
            Dim ignoreMerges As Boolean = SerializationHelpers.GetBool(obj, "ignore_merges").GetValueOrDefault(False)
            Return New BpeModel(vocab, merges, prefix, eow, unk, fuseUnk, byteFallback, dropout, ignoreMerges,
                                cacheCapacity:=If(cacheCapacity.HasValue, cacheCapacity.Value, Models.BpeModel.DefaultCacheCapacity),
                                maxWordLength:=cacheMaxWord,
                                sharedCacheCapacity:=If(sharedCacheCapacity.HasValue, sharedCacheCapacity.Value, Models.BpeModel.SharedCacheCapacity))
        End Function

        Private Shared Function BuildWordPiece(obj As JsonElement) As WordPieceModel
            Dim vocab As Dictionary(Of String, Integer) = ParseVocab(SerializationHelpers.GetProperty(obj, "vocab"))
            Dim unk As String = SerializationHelpers.GetString(obj, "unk_token")
            If unk Is Nothing Then unk = "[UNK]"
            Dim prefix As String = SerializationHelpers.GetString(obj, "continuing_subword_prefix")
            If prefix Is Nothing Then prefix = "##"
            Dim maxChars As Integer = SerializationHelpers.GetInt(obj, "max_input_chars_per_word").GetValueOrDefault(100)
            Return New WordPieceModel(vocab, unk, prefix, maxChars)
        End Function

        Private Shared Function BuildWordLevel(obj As JsonElement) As WordLevelModel
            Dim vocab As Dictionary(Of String, Integer) = ParseVocab(SerializationHelpers.GetProperty(obj, "vocab"))
            Dim unk As String = SerializationHelpers.GetString(obj, "unk_token")
            If unk Is Nothing Then unk = "<unk>"
            Return New WordLevelModel(vocab, unk)
        End Function

        Private Shared Function BuildUnigram(obj As JsonElement) As UnigramModel
            Dim vocab As New List(Of (String, Double))()
            Dim vocabNode As JsonElement? = SerializationHelpers.GetProperty(obj, "vocab")
            If vocabNode.HasValue AndAlso vocabNode.Value.ValueKind = JsonValueKind.Array Then
                For Each entry As JsonElement In vocabNode.Value.EnumerateArray()
                    If entry.ValueKind = JsonValueKind.Array AndAlso entry.GetArrayLength() >= 2 Then
                        Dim piece As String = entry(0).GetString()
                        Dim score As Double = entry(1).GetDouble()
                        vocab.Add((piece, score))
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
        Public Shared Function FromNormalizer(prop As JsonElement?) As INormalizer
            If Not prop.HasValue Then Return Nothing
            Dim obj As JsonElement = prop.Value
            If obj.ValueKind <> JsonValueKind.Object Then Return Nothing

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

        Private Shared Function BuildBertNormalizer(obj As JsonElement) As BertNormalizer
            Dim cleanText As Boolean = SerializationHelpers.GetBool(obj, "clean_text").GetValueOrDefault(True)
            Dim handleChinese As Boolean = SerializationHelpers.GetBool(obj, "handle_chinese_chars").GetValueOrDefault(True)
            Dim stripAccents As Boolean? = SerializationHelpers.GetBool(obj, "strip_accents")
            Dim lowercase As Boolean = SerializationHelpers.GetBool(obj, "lowercase").GetValueOrDefault(True)
            Return New BertNormalizer(cleanText, handleChinese, stripAccents, lowercase)
        End Function

        Private Shared Function BuildPrecompiledNormalizer(obj As JsonElement) As PrecompiledNormalizer
            Dim node As JsonElement? = SerializationHelpers.GetProperty(obj, "precompiled_charsmap")
            Dim bytes As Byte() = Array.Empty(Of Byte)()
            If node.HasValue Then
                Dim s As String = Nothing
                If node.Value.ValueKind = JsonValueKind.String Then
                    s = node.Value.GetString()
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

        Private Shared Function BuildReplaceNormalizer(obj As JsonElement) As ReplaceNormalizer
            Dim patternNode As JsonElement? = SerializationHelpers.GetProperty(obj, "pattern")
            Dim kind As String = "String"
            Dim pattern As String = ""
            If patternNode.HasValue AndAlso patternNode.Value.ValueKind = JsonValueKind.Object Then
                Dim pObj As JsonElement = patternNode.Value
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

        Private Shared Function BuildNormalizerSequence(obj As JsonElement) As NormalizerSequence
            Dim arrNode As JsonElement? = SerializationHelpers.GetProperty(obj, "normalizers")
            If Not arrNode.HasValue OrElse arrNode.Value.ValueKind <> JsonValueKind.Array Then
                Throw New ArgumentException("missing field `normalizers`")
            End If
            Dim items As New List(Of INormalizer)()
            For Each item As JsonElement In arrNode.Value.EnumerateArray()
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
        Public Shared Function FromPreTokenizer(prop As JsonElement?) As IPreTokenizer
            If Not prop.HasValue Then Return Nothing
            Dim obj As JsonElement = prop.Value
            If obj.ValueKind <> JsonValueKind.Object Then Return Nothing

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

        Private Shared Function BuildByteLevelPreTokenizer(obj As JsonElement) As ByteLevelPreTokenizer
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim useRegex As Boolean = SerializationHelpers.GetBool(obj, "use_regex").GetValueOrDefault(True)
            Return New ByteLevelPreTokenizer(addPrefix, trimOffsets, useRegex)
        End Function

        Private Shared Function ParseDelimiterChar(obj As JsonElement) As Char
            Dim s As String = SerializationHelpers.GetString(obj, "delimiter")
            If String.IsNullOrEmpty(s) Then Throw New ArgumentException("missing field `delimiter`")
            Return s(0)
        End Function

        Private Shared Function BuildMetaspacePreTokenizer(obj As JsonElement) As MetaspacePreTokenizer
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

        Private Shared Function ParseBehavior(obj As JsonElement) As SplitDelimiterBehavior
            Dim s As String = SerializationHelpers.GetString(obj, "behavior")
            If s Is Nothing Then Throw New ArgumentException("missing field `behavior`")
            Return SerializationHelpers.ParseSplitDelimiterBehavior(s)
        End Function

        Private Shared Function TryParseBehavior(obj As JsonElement) As SplitDelimiterBehavior?
            Dim s As String = SerializationHelpers.GetString(obj, "behavior")
            If s Is Nothing Then Return Nothing
            Try
                Return SerializationHelpers.ParseSplitDelimiterBehavior(s)
            Catch ex As ArgumentException
                Return Nothing
            End Try
        End Function

        Private Shared Function BuildPreTokenizerSequence(obj As JsonElement) As PreTokenizerSequence
            Dim arrNode As JsonElement? = SerializationHelpers.GetProperty(obj, "pretokenizers")
            If Not arrNode.HasValue OrElse arrNode.Value.ValueKind <> JsonValueKind.Array Then
                Throw New ArgumentException("missing field `pretokenizers`")
            End If
            Dim items As New List(Of IPreTokenizer)()
            For Each item As JsonElement In arrNode.Value.EnumerateArray()
                Dim pt As IPreTokenizer = FromPreTokenizer(item)
                If pt Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum PreTokenizerUntagged")
                End If
                items.Add(pt)
            Next
            Return New PreTokenizerSequence(items)
        End Function

        Private Shared Function BuildSplitPreTokenizer(obj As JsonElement) As SplitPreTokenizer
            Dim patternNode As JsonElement? = SerializationHelpers.GetProperty(obj, "pattern")
            Dim kind As String = "String"
            Dim pattern As String = ""
            If patternNode.HasValue AndAlso patternNode.Value.ValueKind = JsonValueKind.Object Then
                Dim pObj As JsonElement = patternNode.Value
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
        Public Shared Function FromPostProcessor(prop As JsonElement?) As IPostProcessor
            If Not prop.HasValue Then Return Nothing
            Dim obj As JsonElement = prop.Value
            If obj.ValueKind <> JsonValueKind.Object Then Return Nothing

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

        Private Shared Function ParseTokenPair(obj As JsonElement, key As String) As (String, Integer)
            Dim node As JsonElement? = SerializationHelpers.GetProperty(obj, key)
            If Not node.HasValue OrElse node.Value.ValueKind <> JsonValueKind.Array Then Throw New ArgumentException($"missing field `{key}`")
            Dim arr As JsonElement = node.Value
            If arr.GetArrayLength() < 2 Then Throw New ArgumentException($"invalid `{key}`")
            Return (arr(0).GetString(), arr(1).GetInt32())
        End Function

        Private Shared Function BuildBertProcessing(obj As JsonElement) As BertProcessing
            Dim sep As (String, Integer) = ParseTokenPair(obj, "sep")
            Dim cls As (String, Integer) = ParseTokenPair(obj, "cls")
            Return New BertProcessing(sep, cls)
        End Function

        Private Shared Function BuildRobertaProcessing(obj As JsonElement) As RobertaProcessing
            Dim sep As (String, Integer) = ParseTokenPair(obj, "sep")
            Dim cls As (String, Integer) = ParseTokenPair(obj, "cls")
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Return New RobertaProcessing(sep, cls, trimOffsets, addPrefix)
        End Function

        Private Shared Function BuildByteLevelProcessing(obj As JsonElement) As ByteLevelProcessing
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim useRegex As Boolean = SerializationHelpers.GetBool(obj, "use_regex").GetValueOrDefault(True)
            Return New ByteLevelProcessing(addPrefix, trimOffsets, useRegex)
        End Function

        Private Shared Function BuildProcessorSequence(obj As JsonElement) As ProcessorSequence
            Dim arrNode As JsonElement? = SerializationHelpers.GetProperty(obj, "processors")
            If Not arrNode.HasValue OrElse arrNode.Value.ValueKind <> JsonValueKind.Array Then
                Throw New ArgumentException("missing field `processors`")
            End If
            Dim items As New List(Of IPostProcessor)()
            For Each item As JsonElement In arrNode.Value.EnumerateArray()
                Dim pp As IPostProcessor = FromPostProcessor(item)
                If pp Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum PostProcessorWrapper")
                End If
                items.Add(pp)
            Next
            Return New ProcessorSequence(items)
        End Function

        Private Shared Function BuildTemplateProcessing(obj As JsonElement) As TemplateProcessing
            Dim singleStr As String = TemplateToString(SerializationHelpers.GetProperty(obj, "single"))
            Dim pairStr As String = TemplateToString(SerializationHelpers.GetProperty(obj, "pair"))

            Dim special As New Dictionary(Of String, (List(Of Integer), List(Of String)))()
            Dim specialNode As JsonElement? = SerializationHelpers.GetProperty(obj, "special_tokens")
            If specialNode.HasValue AndAlso specialNode.Value.ValueKind = JsonValueKind.Object Then
                Dim sObj As JsonElement = specialNode.Value
                For Each kv As JsonProperty In sObj.EnumerateObject()
                    Dim entry As JsonElement = kv.Value
                    If entry.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim ids As New List(Of Integer)()
                    Dim idsNode As JsonElement? = SerializationHelpers.GetProperty(entry, "ids")
                    If idsNode.HasValue AndAlso idsNode.Value.ValueKind = JsonValueKind.Array Then
                        For Each idNode As JsonElement In idsNode.Value.EnumerateArray()
                            ids.Add(idNode.GetInt32())
                        Next
                    End If
                    Dim tokens As New List(Of String)()
                    Dim tokensNode As JsonElement? = SerializationHelpers.GetProperty(entry, "tokens")
                    If tokensNode.HasValue AndAlso tokensNode.Value.ValueKind = JsonValueKind.Array Then
                        For Each tNode As JsonElement In tokensNode.Value.EnumerateArray()
                            tokens.Add(tNode.GetString())
                        Next
                    End If
                    special(kv.Name) = (ids, tokens)
                Next
            End If
            Return New TemplateProcessing(singleStr, pairStr, special)
        End Function

        ''' <summary>Reconstructs a template string ("$A [SEP] $B:1 ...") from its JSON piece array.</summary>
        Private Shared Function TemplateToString(prop As JsonElement?) As String
            If Not prop.HasValue OrElse prop.Value.ValueKind <> JsonValueKind.Array Then
                Throw New ArgumentException("missing field `template`")
            End If
            Dim parts As New List(Of String)()
            For Each piece As JsonElement In prop.Value.EnumerateArray()
                If piece.ValueKind <> JsonValueKind.Object Then Continue For
                If IsPresent(piece, "Sequence") Then
                    Dim seqNode As JsonElement? = SerializationHelpers.GetProperty(piece, "Sequence")
                    If seqNode.HasValue AndAlso seqNode.Value.ValueKind = JsonValueKind.Object Then
                        Dim seq As JsonElement = seqNode.Value
                        Dim id As String = SerializationHelpers.GetString(seq, "id")
                        Dim typeId As Integer = SerializationHelpers.GetInt(seq, "type_id").GetValueOrDefault(0)
                        parts.Add(If(typeId = 0, "$" & id, "$" & id & ":" & typeId))
                    End If
                ElseIf IsPresent(piece, "SpecialToken") Then
                    Dim spNode As JsonElement? = SerializationHelpers.GetProperty(piece, "SpecialToken")
                    If spNode.HasValue AndAlso spNode.Value.ValueKind = JsonValueKind.Object Then
                        Dim sp As JsonElement = spNode.Value
                        Dim id As String = SerializationHelpers.GetString(sp, "id")
                        Dim typeId As Integer = SerializationHelpers.GetInt(sp, "type_id").GetValueOrDefault(0)
                        parts.Add(If(typeId = 0, id, id & ":" & typeId))
                    End If
                End If
            Next
            Return String.Join(" ", parts)
        End Function

        ' ------------------------------------------------------------------
        ' Decoders
        ' ------------------------------------------------------------------

        ''' <summary>Reconstructs a decoder from its JSON node, or <c>Nothing</c> when nothing matches.</summary>
        Public Shared Function FromDecoder(prop As JsonElement?) As IDecoder
            If Not prop.HasValue Then Return Nothing
            Dim obj As JsonElement = prop.Value
            If obj.ValueKind <> JsonValueKind.Object Then Return Nothing

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

        Private Shared Function BuildByteLevelDecoder(obj As JsonElement) As ByteLevelDecoder
            Dim addPrefix As Boolean = SerializationHelpers.GetBool(obj, "add_prefix_space").GetValueOrDefault(True)
            Dim trimOffsets As Boolean = SerializationHelpers.GetBool(obj, "trim_offsets").GetValueOrDefault(True)
            Dim useRegex As Boolean = SerializationHelpers.GetBool(obj, "use_regex").GetValueOrDefault(True)
            Return New ByteLevelDecoder(addPrefix, trimOffsets, useRegex)
        End Function

        Private Shared Function BuildMetaspaceDecoder(obj As JsonElement) As MetaspaceDecoder
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

        Private Shared Function BuildDecoderSequence(obj As JsonElement) As DecoderSequence
            Dim arrNode As JsonElement? = SerializationHelpers.GetProperty(obj, "decoders")
            If Not arrNode.HasValue OrElse arrNode.Value.ValueKind <> JsonValueKind.Array Then
                Throw New ArgumentException("missing field `decoders`")
            End If
            Dim items As New List(Of IDecoder)()
            For Each item As JsonElement In arrNode.Value.EnumerateArray()
                Dim dec As IDecoder = FromDecoder(item)
                If dec Is Nothing Then
                    Throw New ArgumentException("data did not match any variant of untagged enum DecoderUntagged")
                End If
                items.Add(dec)
            Next
            Return New DecoderSequence(items)
        End Function

        Private Shared Function BuildReplaceDecoder(obj As JsonElement) As ReplaceDecoder
            Dim patternNode As JsonElement? = SerializationHelpers.GetProperty(obj, "pattern")
            Dim kind As String = "String"
            Dim pattern As String = ""
            If patternNode.HasValue AndAlso patternNode.Value.ValueKind = JsonValueKind.Object Then
                Dim pObj As JsonElement = patternNode.Value
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

        Private Shared Function ParseCharContent(obj As JsonElement) As Char
            Dim s As String = SerializationHelpers.GetString(obj, "content")
            If String.IsNullOrEmpty(s) Then Return " "c
            Return s(0)
        End Function

        ' ------------------------------------------------------------------
        ' Helpers
        ' ------------------------------------------------------------------

        Private Shared Function IsPresent(obj As JsonElement, key As String) As Boolean
            Dim n As JsonElement
            Return obj.ValueKind = JsonValueKind.Object AndAlso obj.TryGetProperty(key, n) AndAlso n.ValueKind <> JsonValueKind.Null
        End Function

        ''' <summary>Parses a vocab object (token string to id).</summary>
        Public Shared Function ParseVocab(prop As JsonElement?) As Dictionary(Of String, Integer)
            Dim result As New Dictionary(Of String, Integer)()
            If Not prop.HasValue Then Return result
            Dim obj As JsonElement = prop.Value
            If obj.ValueKind <> JsonValueKind.Object Then Return result
            For Each kv As JsonProperty In obj.EnumerateObject()
                Dim id As Integer
                If kv.Value.ValueKind = JsonValueKind.Number Then
                    If kv.Value.TryGetInt32(id) Then
                        result(kv.Name) = id
                        Continue For
                    End If
                    Dim l As Long
                    If kv.Value.TryGetInt64(l) Then
                        result(kv.Name) = CInt(l)
                        Continue For
                    End If
                End If
            Next
            Return result
        End Function

        ''' <summary>
        ''' Parses a merges array, accepting both space-joined strings (<c>["a b", ...]</c>) and
        ''' nested arrays (<c>[["a","b"], ...]</c>).
        ''' </summary>
        Public Shared Function ParseMerges(prop As JsonElement?) As List(Of String)
            Dim result As New List(Of String)()
            If Not prop.HasValue Then Return result
            Dim arr As JsonElement = prop.Value
            If arr.ValueKind <> JsonValueKind.Array Then Return result
            For Each item As JsonElement In arr.EnumerateArray()
                If item.ValueKind = JsonValueKind.Array Then
                    If item.GetArrayLength() >= 2 Then
                        result.Add(item(0).GetString() & " " & item(1).GetString())
                    End If
                ElseIf item.ValueKind = JsonValueKind.String Then
                    result.Add(item.GetString())
                End If
            Next
            Return result
        End Function

    End Class

End Namespace
