Imports Tokenizers.Internal
Imports Tokenizers.Normalizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports of the Rust normalizer unit tests (tokenizers/src/normalizers/*.rs).
    ''' </summary>
    <TestClass>
    Public Class NormalizersTests

        ' ------------------------------------------------------------------
        ' ByteLevel (port of byte_level.rs test_byte_level_normalize)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteLevel_Normalize()
            Dim original As String = "Hello 我今天能为你做什么"
            Dim expectedNormalized As String = "HelloĠæĪĳä»Ĭå¤©èĥ½ä¸ºä½łåģļä»Ģä¹Ī"
            Assert.AreNotEqual(expectedNormalized, original)

            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim normalizer As New ByteLevelNormalizer()
            normalizer.Normalize(n)

            Assert.AreEqual(expectedNormalized, n.Get)

            Dim expectedAlignments As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (5, 6),
                (6, 9), (6, 9), (6, 9), (6, 9), (6, 9), (6, 9),
                (9, 12), (9, 12), (9, 12), (9, 12), (9, 12), (9, 12),
                (12, 15), (12, 15), (12, 15), (12, 15), (12, 15), (12, 15),
                (15, 18), (15, 18), (15, 18), (15, 18), (15, 18), (15, 18),
                (18, 21), (18, 21), (18, 21), (18, 21), (18, 21), (18, 21),
                (21, 24), (21, 24), (21, 24), (21, 24), (21, 24), (21, 24),
                (24, 27), (24, 27), (24, 27), (24, 27), (24, 27), (24, 27),
                (27, 30), (27, 30), (27, 30), (27, 30), (27, 30), (27, 30),
                (30, 33), (30, 33), (30, 33), (30, 33), (30, 33), (30, 33)
            }
            CollectionAssert.AreEqual(expectedAlignments, n.Alignments)

            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 7),
                (7, 13), (7, 13), (7, 13),
                (13, 19), (13, 19), (13, 19),
                (19, 25), (19, 25), (19, 25),
                (25, 31), (25, 31), (25, 31),
                (31, 37), (31, 37), (31, 37),
                (37, 43), (37, 43), (37, 43),
                (43, 49), (43, 49), (43, 49),
                (49, 55), (49, 55), (49, 55),
                (55, 61), (55, 61), (55, 61)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        <TestMethod>
        Public Sub ByteLevel_Empty_NoOp()
            Dim n As NormalizedString = NormalizedString.FromString("")
            Dim normalizer As New ByteLevelNormalizer()
            normalizer.Normalize(n)
            Assert.AreEqual("", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' NFKC (port of unicode.rs test_nfkc)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Nfkc_Ligature()
            Dim original As String = "ﬁ"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim normalizer As New NfkcNormalizer()
            normalizer.Normalize(n)

            Assert.AreEqual("fi", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(0, 3), (0, 3)}
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {(0, 2), (0, 2), (0, 2)}
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        ' ------------------------------------------------------------------
        ' Prepend (port of prepend.rs test_prepend)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Prepend_Exact()
            Dim original As String = "Hello"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim normalizer As New PrependNormalizer("▁")
            normalizer.Normalize(n)

            Assert.AreEqual("▁Hello", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 1), (0, 1), (0, 1), (0, 1), (1, 2), (2, 3), (3, 4), (4, 5)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {(0, 4), (4, 5), (5, 6), (6, 7), (7, 8)}
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        <TestMethod>
        Public Sub Prepend_EmptyString_NoOp()
            ' Rust guards on is_empty; an empty prepend string is a textual no-op on a non-empty string.
            Dim n As NormalizedString = NormalizedString.FromString("Hello")
            Dim normalizer As New PrependNormalizer("")
            normalizer.Normalize(n)
            Assert.AreEqual("Hello", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Replace (port of replace.rs test_replace / test_replace_regex)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Replace_String()
            Dim n As NormalizedString = NormalizedString.FromString("This is a ''test''")
            Dim normalizer As New ReplaceNormalizer("String", "''", """")
            normalizer.Normalize(n)
            Assert.AreEqual("This is a ""test""", n.Get)
        End Sub

        <TestMethod>
        Public Sub Replace_Regex()
            Dim n As NormalizedString = NormalizedString.FromString("This     is   a         test")
            Dim normalizer As New ReplaceNormalizer("Regex", "\s+", " ")
            normalizer.Normalize(n)
            Assert.AreEqual("This is a test", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' StripAccents (port of strip.rs test_strip_accents and friends)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub StripAccents_Simple()
            ' Unicode combining char (NFD first, as the Rust test does).
            Dim n As NormalizedString = NormalizedString.FromString("Me llamó")
            n.Nfkd()
            Dim normalizer As New StripAccentsNormalizer()
            normalizer.Normalize(n)
            Assert.AreEqual("Me llamo", n.Get)

            ' Ignores regular ascii.
            Dim n2 As NormalizedString = NormalizedString.FromString("Me llamo")
            normalizer.Normalize(n2)
            Assert.AreEqual("Me llamo", n2.Get)

            ' Does not change chinese.
            Dim n3 As NormalizedString = NormalizedString.FromString("这很简单")
            n3.Nfkd()
            normalizer.Normalize(n3)
            Assert.AreEqual("这很简单", n3.Get)
        End Sub

        <TestMethod>
        Public Sub StripAccents_VietnameseBug()
            ' ậ…  --NFKD-->  a ̂...  --StripAccents-->  a...  --Lowercase-->  a...
            Dim n As NormalizedString = NormalizedString.FromString("ậ…")
            Dim sequence As New NormalizerSequence(
                {New NfkdNormalizer(), New StripAccentsNormalizer(), New LowercaseNormalizer()})
            sequence.Normalize(n)
            Assert.AreEqual("a...", n.Get)
        End Sub

        <TestMethod>
        Public Sub StripAccents_ThaiBug()
            Dim original As String = "ำน" & ChrW(&H0E49) & "ำ3ลำ"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim sequence As New NormalizerSequence(
                {New NfkdNormalizer(), New StripAccentsNormalizer(), New LowercaseNormalizer()})
            sequence.Normalize(n)
            Assert.AreEqual("านา3ลา", n.Get)
        End Sub

        <TestMethod>
        Public Sub StripAccents_Multiple()
            Dim original As String = "e" & ChrW(&H304) & ChrW(&H304) & ChrW(&H304) & "o"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim normalizer As New StripAccentsNormalizer()
            normalizer.Normalize(n)

            Assert.AreEqual("eo", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(0, 1), (7, 8)}
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 1), (1, 1), (1, 1), (1, 1), (1, 1), (1, 1), (1, 2)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        ' ------------------------------------------------------------------
        ' Nmt (port of unicode.rs do_nmt)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Nmt_RemovesControl_MapsWhitespace()
            Dim original As String =
                "a" & ChrW(&H1) & "b" & ChrW(&HB) & "c" & ControlChars.Tab & "d" & ChrW(&H200B) &
                "e" & ChrW(&HFEFF) & "f" & ChrW(&HFFFD) & "g" & " " & "h"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim normalizer As New NmtNormalizer()
            normalizer.Normalize(n)
            Assert.AreEqual("abc d e f g h", n.Get)
        End Sub

        <TestMethod>
        Public Sub Nmt_MapsNewlineAndCarriageReturn()
            Dim n As NormalizedString = NormalizedString.FromString("a" & ControlChars.Lf & "b" & ControlChars.Cr & "c")
            Dim normalizer As New NmtNormalizer()
            normalizer.Normalize(n)
            Assert.AreEqual("a b c", n.Get)
        End Sub

        <TestMethod>
        Public Sub Nmt_RemovesControlsOnly()
            Dim original As String =
                ChrW(&H1) & ChrW(&H2) & ChrW(&H8) & ChrW(&HB) & ChrW(&HE) & ChrW(&H1F) &
                ChrW(&H7F) & ChrW(&H8F) & ChrW(&H9F)
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim normalizer As New NmtNormalizer()
            normalizer.Normalize(n)
            Assert.AreEqual("", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Bert (port of bert.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Bert_CleanText()
            Dim original As String =
                "Hello" & ChrW(0) & ChrW(&HFFFD) & "World" & ControlChars.Tab & "test" &
                ControlChars.Lf & ControlChars.Cr
            Dim n As NormalizedString = NormalizedString.FromString(original)
            Dim bert As New BertNormalizer(cleanText:=True, handleChineseChars:=False, stripAccents:=False, lowercase:=False)
            bert.Normalize(n)
            Assert.AreEqual("HelloWorld test  ", n.Get)
        End Sub

        <TestMethod>
        Public Sub Bert_ChineseChars()
            ' NOTE: adjacent Chinese chars yield TWO spaces between them. The transform's offset
            ' only advances over replaced/removed chars, not inserted ones, so each char emits a
            ' trailing AND a leading space. This matches the reference transform semantics
            ' (see NormalizedStringTests.AddedCharactersAlignment which asserts " 野  口  No").
            Dim n As NormalizedString = NormalizedString.FromString("野口里佳")
            Dim bert As New BertNormalizer() ' defaults: clean, chinese, strip=>lowercase, lowercase
            bert.Normalize(n)
            Assert.AreEqual(" 野  口  里  佳 ", n.Get)
        End Sub

        <TestMethod>
        Public Sub Bert_Lowercase_ResolvesStripAccents()
            ' strip_accents = None resolves to lowercase = True, so accents are stripped.
            Dim n As NormalizedString = NormalizedString.FromString("Héllo")
            Dim bert As New BertNormalizer(cleanText:=False, handleChineseChars:=False, stripAccents:=Nothing, lowercase:=True)
            bert.Normalize(n)
            Assert.AreEqual("hello", n.Get)
        End Sub

        <TestMethod>
        Public Sub Bert_NoStripAccents()
            ' strip_accents = False keeps the accents even though lowercase is True.
            Dim n As NormalizedString = NormalizedString.FromString("Héllo")
            Dim bert As New BertNormalizer(cleanText:=False, handleChineseChars:=False, stripAccents:=False, lowercase:=True)
            bert.Normalize(n)
            Assert.AreEqual("héllo", n.Get)
        End Sub

        <TestMethod>
        Public Sub Bert_StripAccentsOnly()
            Dim n As NormalizedString = NormalizedString.FromString("Héllo")
            Dim bert As New BertNormalizer(cleanText:=False, handleChineseChars:=False, stripAccents:=True, lowercase:=False)
            bert.Normalize(n)
            Assert.AreEqual("Hello", n.Get)
        End Sub

        <TestMethod>
        Public Sub Bert_Order_CleanThenChinese()
            Dim n As NormalizedString = NormalizedString.FromString("h" & ChrW(0) & "i野")
            Dim bert As New BertNormalizer() ' defaults
            bert.Normalize(n)
            Assert.AreEqual("hi 野 ", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Strip (port of strip.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Strip_Both()
            Dim n As NormalizedString = NormalizedString.FromString("  Hello  ")
            Dim normalizer As New StripNormalizer(stripLeft:=True, stripRight:=True)
            normalizer.Normalize(n)
            Assert.AreEqual("Hello", n.Get)
        End Sub

        <TestMethod>
        Public Sub Strip_LeftOnly()
            Dim n As NormalizedString = NormalizedString.FromString("  Hello  ")
            Dim normalizer As New StripNormalizer(stripLeft:=True, stripRight:=False)
            normalizer.Normalize(n)
            Assert.AreEqual("Hello  ", n.Get)
        End Sub

        <TestMethod>
        Public Sub Strip_RightOnly()
            Dim n As NormalizedString = NormalizedString.FromString("  Hello  ")
            Dim normalizer As New StripNormalizer(stripLeft:=False, stripRight:=True)
            normalizer.Normalize(n)
            Assert.AreEqual("  Hello", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Precompiled (port of precompiled.rs expansion_followed_by_removal)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Precompiled_ExpansionFollowedByRemoval()
            Dim original As String = "™" & ChrW(&H1E) & "g"
            Dim n As NormalizedString = NormalizedString.FromString(original)

            Dim precompiled As New PrecompiledNormalizer(New Byte() {1})
            precompiled.SetMapping("™", "TM")
            precompiled.SetMapping(ChrW(&H1E), "")
            precompiled.Normalize(n)

            Assert.AreEqual("TMg", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(0, 3), (3, 4), (4, 5)}
            CollectionAssert.AreEqual(expected, n.Alignments)
        End Sub

        <TestMethod>
        Public Sub Precompiled_EmptyBlob_NoOp()
            Dim n As NormalizedString = NormalizedString.FromString("™g")
            Dim precompiled As New PrecompiledNormalizer(Array.Empty(Of Byte)())
            precompiled.SetMapping("™", "TM")
            precompiled.Normalize(n)
            Assert.AreEqual("™g", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Sequence (port of utils.rs Sequence)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Sequence_RunsInOrder()
            Dim n As NormalizedString = NormalizedString.FromString("  HELLO  ")
            Dim sequence As New NormalizerSequence(
                {New StripNormalizer(stripLeft:=True, stripRight:=True), New LowercaseNormalizer()})
            sequence.Normalize(n)
            Assert.AreEqual("hello", n.Get)
        End Sub

        <TestMethod>
        Public Sub Sequence_NormalizersRunAgainstSameNormalizedString()
            ' Composition that depends on the previous step's output (NFKD -> StripAccents).
            Dim n As NormalizedString = NormalizedString.FromString("élégant")
            Dim sequence As New NormalizerSequence(
                {New NfdNormalizer(), New StripAccentsNormalizer()})
            sequence.Normalize(n)
            Assert.AreEqual("elegant", n.Get)
        End Sub

    End Class

End Namespace
