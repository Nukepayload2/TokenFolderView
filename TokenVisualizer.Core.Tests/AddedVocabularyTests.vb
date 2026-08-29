Imports Tokenizers.Internal
Imports Tokenizers.Models
Imports Tokenizers.Normalizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports the Rust <c>tokenizer/added_vocabulary.rs</c> unit tests: id assignment, token
    ''' extraction, the single_word/lstrip/rstrip/normalized/special flags, the two-pass
    ''' extract_and_normalize, and a 32-flag matrix.
    ''' </summary>
    <TestClass>
    Public Class AddedVocabularyTests

        Private Shared Function SimplifyOutput(result As PreTokenizedString) As List(Of (String, List(Of Integer)))
            Dim output As New List(Of (String, List(Of Integer)))()
            For Each s In result.GetSplits(OffsetReferential.Original, OffsetType.Byte)
                Dim ids As List(Of Integer) = Nothing
                If s.Tokens IsNot Nothing Then
                    ids = s.Tokens.Select(Function(t) t.Id).ToList()
                End If
                output.Add((s.Text, ids))
            Next
            Return output
        End Function

        Private Shared Sub AssertSplits(actual As PreTokenizedString, ParamArray expected As (String, Integer())())
            Dim splits As List(Of (String, List(Of Integer))) = SimplifyOutput(actual)
            Assert.HasCount(expected.Length, splits, $"split count for '{actual.Original}'")
            For i As Integer = 0 To expected.Length - 1
                Assert.AreEqual(expected(i).Item1, splits(i).Item1, $"split[{i}] text for '{actual.Original}'")
                If expected(i).Item2 Is Nothing Then
                    Assert.IsNull(splits(i).Item2, $"split[{i}] tokens should be None for '{actual.Original}'")
                Else
                    Assert.IsNotNull(splits(i).Item2, $"split[{i}] tokens should be Some for '{actual.Original}'")
                    CollectionAssert.AreEqual(expected(i).Item2, splits(i).Item2, $"split[{i}] ids for '{actual.Original}'")
                End If
            Next
        End Sub

        <TestMethod>
        Public Sub CanAddTokens()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer) From {{"test", 0}, {"tost", 1}}

            ' Add tokens normally.
            Assert.AreEqual(1, vocab.AddTokens(2, {AddedToken.From("added_token_1", False)}))
            Assert.AreEqual(1, vocab.Count)

            ' Does not add multiple time the same token.
            Assert.AreEqual(1, vocab.AddTokens(2, {AddedToken.From("added_token_2", False), AddedToken.From("added_token_2", False)}))
            Assert.AreEqual(2, vocab.Count)

            ' Also adds tokens already covered by the model (keeps the model id).
            Dim tok As AddedToken = AddedToken.From("test", False)
            Assert.AreEqual(1, vocab.AddTokens(2, {tok}))
            Assert.AreEqual(3, vocab.Count)

            Assert.AreEqual(tok, vocab.AddedTokensDecoder(0))
        End Sub

        <TestMethod>
        Public Sub CanAddSpecialTokens()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer) From {{"test", 0}, {"tost", 1}}

            Assert.AreEqual(1, vocab.AddSpecialTokens(2, {AddedToken.From("added_token_1", True)}))
            Assert.AreEqual(1, vocab.Count)

            ' Does not add multiple time the same token.
            Assert.AreEqual(1, vocab.AddSpecialTokens(2, {AddedToken.From("added_token_2", True), AddedToken.From("added_token_2", True)}))
            Assert.AreEqual(2, vocab.Count)

            ' Can add tokens already covered by the model.
            Assert.AreEqual(1, vocab.AddSpecialTokens(2, {AddedToken.From("test", True)}))
            Assert.AreEqual(3, vocab.Count)
            Assert.IsTrue(vocab.IsSpecialToken("test"))
            Assert.AreEqual(AddedToken.From("test", True), vocab.AddedTokensDecoder(0))
            Assert.AreEqual(AddedToken.From("added_token_1", True), vocab.AddedTokensDecoder(2))
            Assert.AreEqual(AddedToken.From("added_token_2", True), vocab.AddedTokensDecoder(3))

            vocab.AddTokens(2, {AddedToken.From("tost", True), AddedToken.From("another_two", False)})
            Assert.AreEqual(5, vocab.Count)
            Assert.AreEqual(4, vocab.AddedTokensMap("another_two"))

            ' Add an already added token again but change the flags: not ignored, id kept.
            Assert.AreEqual(1, vocab.AddSpecialTokens(2, {AddedToken.From("another_two", True)}))
            Assert.AreEqual(5, vocab.Count)
            Assert.AreEqual(4, vocab.AddedTokensMap("another_two"))
        End Sub

        <TestMethod>
        Public Sub CanExtractAddedTokens()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()

            vocab.AddTokens(0, {AddedToken.From("my", False), AddedToken.From("name", False)})
            vocab.AddSpecialTokens(0, {AddedToken.From("[CLS]", True), AddedToken.From("[SEP]", True)})

            Dim result As PreTokenizedString = vocab.ExtractAndNormalize("[CLS] My name is Anthony [SEP]", Nothing)
            AssertSplits(result,
                ("[CLS]", New Integer() {2}),
                (" My ", Nothing),
                ("name", New Integer() {1}),
                (" is Anthony ", Nothing),
                ("[SEP]", New Integer() {3}))
        End Sub

        <TestMethod>
        Public Sub OptionsUseCases()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New LowercaseNormalizer()

            vocab.AddTokens(0, {
                AddedToken.From("my", False).WithLStrip(True).WithRStrip(True),
                AddedToken.From("name", False),
                AddedToken.From("ony", False).WithSingleWord(True)})
            vocab.AddSpecialTokens(0, {AddedToken.From("[CLS]", True), AddedToken.From("[SEP]", True)})

            Dim result As PreTokenizedString = vocab.ExtractAndNormalize("[CLS] My name is Anthony [SEP]", vocab.Normalizer)
            AssertSplits(result,
                ("[CLS]", New Integer() {3}),
                (" my ", New Integer() {0}),
                ("name", New Integer() {1}),
                (" is anthony ", Nothing),
                ("[SEP]", New Integer() {4}))
        End Sub

        <TestMethod>
        Public Sub EmptyMatches()
            Dim vocab As New AddedVocabulary()
            Dim matches As List(Of (Integer?, (Integer, Integer))) = vocab.FindMatches("", vocab.SplitTrie, vocab.SplitTrieIds)
            Assert.HasCount(1, matches)
            Assert.IsNull(matches(0).Item1)
            Assert.AreEqual((0, 0), matches(0).Item2)
        End Sub

        <TestMethod>
        Public Sub SingleWordIsCorrect()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New LowercaseNormalizer()

            vocab.AddTokens(0, {AddedToken.From("<mask>", False).WithSingleWord(True)})

            Dim result As PreTokenizedString = vocab.ExtractAndNormalize("<mask> My name <mask> A<mask> <mask>ony <mask>", vocab.Normalizer)
            AssertSplits(result,
                ("<mask>", New Integer() {0}),
                (" my name ", Nothing),
                ("<mask>", New Integer() {0}),
                (" a<mask> <mask>ony ", Nothing),
                ("<mask>", New Integer() {0}))
        End Sub

        <TestMethod>
        Public Sub SingleWordIsUnicodeCorrect()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New LowercaseNormalizer()

            vocab.AddTokens(0, {AddedToken.From("<mask>", False).WithSingleWord(True)})

            Dim combiningTilde As String = ChrW(&H330) ' U+0330 COMBINING TILDE BELOW
            Dim result As PreTokenizedString = vocab.ExtractAndNormalize("<mask>, <mask>- " & combiningTilde & "<mask>", vocab.Normalizer)
            AssertSplits(result,
                ("<mask>", New Integer() {0}),
                (", ", Nothing),
                ("<mask>", New Integer() {0}),
                ("- " & combiningTilde & "<mask>", Nothing))
        End Sub

        <TestMethod>
        Public Sub LStripUnicodeSpace()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New LowercaseNormalizer()

            vocab.AddTokens(0, {AddedToken.From("<mask>", False).WithLStrip(True).WithRStrip(True).WithSingleWord(True)})

            Dim input As String = "Hi <mask> there" & ChrW(9) & "<mask>" & ChrW(9) & "<mask>" & ChrW(&H2000)
            Dim result As PreTokenizedString = vocab.ExtractAndNormalize(input, vocab.Normalizer)
            AssertSplits(result,
                ("hi", Nothing),
                (" <mask> ", New Integer() {0}),
                ("there", Nothing),
                (ChrW(9) & "<mask>" & ChrW(9), New Integer() {0}),
                ("<mask>" & ChrW(&H2000), New Integer() {0}))
        End Sub

        <TestMethod>
        Public Sub EncodeSpecialTokens()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New LowercaseNormalizer()

            vocab.AddTokens(0, {
                AddedToken.From("<mask>", True).WithLStrip(True).WithRStrip(True).WithSingleWord(True),
                AddedToken.From("ask>", False),
                AddedToken.From("<pad>", True)})

            Dim input As String = "Hi <mask> there" & ChrW(9) & "<mask>" & ChrW(9) & "<mask>" & ChrW(&H2000) & " <pad> <mask><pad><pad>"

            vocab.SetEncodeSpecialTokens(True)
            Dim result As PreTokenizedString = vocab.ExtractAndNormalize(input, vocab.Normalizer)
            AssertSplits(result,
                ("hi <m", Nothing),
                ("ask>", New Integer() {1}),
                (" there" & ChrW(9) & "<m", Nothing),
                ("ask>", New Integer() {1}),
                (ChrW(9) & "<m", Nothing),
                ("ask>", New Integer() {1}),
                (ChrW(&H2000) & " <pad> <m", Nothing),
                ("ask>", New Integer() {1}),
                ("<pad><pad>", Nothing))

            vocab.SetEncodeSpecialTokens(False)
            result = vocab.ExtractAndNormalize(input, vocab.Normalizer)
            AssertSplits(result,
                ("hi", Nothing),
                (" <mask> ", New Integer() {0}),
                ("there", Nothing),
                (ChrW(9) & "<mask>" & ChrW(9), New Integer() {0}),
                ("<mask>" & ChrW(&H2000) & " ", New Integer() {0}),
                ("<pad>", New Integer() {2}),
                (" <mask>", New Integer() {0}),
                ("<pad>", New Integer() {2}),
                ("<pad>", New Integer() {2}))
        End Sub

        <TestMethod>
        Public Sub ContentPreservedWithNormalizer()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New LowercaseNormalizer()

            vocab.AddTokens(0, {AddedToken.From("Hello", False), AddedToken.From("[CLS]", True)})

            Assert.IsTrue(vocab.AddedTokensDecoder.Values.Any(Function(t) t.Content = "Hello"))
            Assert.IsTrue(vocab.AddedTokensDecoder.Values.Any(Function(t) t.Content = "[CLS]"))

            Dim helloId As Integer = vocab.AddedTokensMap("Hello")
            Dim clsId As Integer = vocab.AddedTokensMap("[CLS]")
            ' normalized = true -> decode returns cached lowercased form.
            Assert.AreEqual("hello", vocab.SimpleIdToToken(helloId))
            ' normalized = false -> decode returns original content.
            Assert.AreEqual("[CLS]", vocab.SimpleIdToToken(clsId))
        End Sub

        <TestMethod>
        Public Sub RefreshNormalizedTokensOnNormalizerChange()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()

            ' Add tokens with NO normalizer first.
            vocab.AddTokens(0, {AddedToken.From("Hello", False)})

            Dim helloId As Integer = vocab.AddedTokensMap("Hello")
            Assert.AreEqual("Hello", vocab.SimpleIdToToken(helloId))

            ' Now attach a normalizer and refresh.
            vocab.RefreshNormalizedTokens(New LowercaseNormalizer())
            Assert.AreEqual("hello", vocab.SimpleIdToToken(helloId))

            ' And the vocabulary should still match correctly (splits use normalized form).
            Dim result As PreTokenizedString = vocab.ExtractAndNormalize("Hello world", New LowercaseNormalizer())
            Dim splits As List(Of (String, List(Of Integer))) = SimplifyOutput(result)
            Assert.AreEqual("hello", splits(0).Item1)
            CollectionAssert.AreEqual(New Integer() {0}, splits(0).Item2)
        End Sub

        <TestMethod>
        Public Sub ByteLevelNormalizer()
            Dim vocab As New AddedVocabulary()
            vocab.ModelVocab = New Dictionary(Of String, Integer)()
            vocab.Normalizer = New ByteLevelNormalizer()

            vocab.AddTokens(0, {AddedToken.From("my", False), AddedToken.From("今", False)})

            Dim result As PreTokenizedString = vocab.ExtractAndNormalize("my今", vocab.Normalizer)
            AssertSplits(result,
                ("my", New Integer() {0}),
                ("ä»Ĭ", New Integer() {1}))
        End Sub

        <TestMethod>
        Public Sub FlagMatrixAddAndExtract()
            For i As Integer = 0 To 31
                Dim singleWord As Boolean = (i And 1) <> 0
                Dim lstrip As Boolean = (i And 2) <> 0
                Dim rstrip As Boolean = (i And 4) <> 0
                Dim normalized As Boolean = (i And 8) <> 0
                Dim special As Boolean = (i And 16) <> 0

                Dim vocab As New AddedVocabulary()
                vocab.ModelVocab = New Dictionary(Of String, Integer)()
                vocab.Normalizer = New LowercaseNormalizer()

                Dim token As AddedToken = AddedToken.From("tok" & i, special)
                token.WithSingleWord(singleWord).WithLStrip(lstrip).WithRStrip(rstrip).WithNormalized(normalized)

                Assert.AreEqual(1, vocab.AddTokens(0, {token}), $"combo {i} add count")
                Assert.AreEqual(1, vocab.Count, $"combo {i} vocab count")

                Dim input As String = "start tok" & i & " end"
                Dim pts As PreTokenizedString = vocab.ExtractAndNormalize(input, vocab.Normalizer)
                Dim splits As List(Of (String, List(Of Integer))) = SimplifyOutput(pts)
                Dim found As Boolean = splits.Any(Function(s) s.Item2 IsNot Nothing AndAlso s.Item2.Contains(0))
                Assert.IsTrue(found, $"combo {i} token id 0 not extracted")
            Next
        End Sub

    End Class

End Namespace
