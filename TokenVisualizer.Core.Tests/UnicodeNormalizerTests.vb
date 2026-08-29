Imports System.Globalization
Imports System.Text
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class UnicodeNormalizerTests

        Private Shared ReadOnly Battery As String() = {
            "Hello World",
            "ASCII test 123 !@#",
            "é à ü ñ å",
            "ậ",
            "ậ…",
            "Me llamó",
            "ำน" & ChrW(&H0E49) & "ำ3ลำ",
            "中文测试",
            "こんにちは",
            "안녕하세요",
            "각",
            "가",
            "각가 한글",
            "ﬁ",
            "ﬁ ﬃ ﬄ ﬅ",
            "emoji 👋 🌟 😀",
            "zero-width " & ChrW(&H200D) & " joiner"
        }

        ''' <summary>
        ''' Asserts that applying the given normalization produces text identical to
        ''' <c>String.Normalize</c>, and that the full normalized range converts back to the full
        ''' original byte range (alignment round-trip).
        ''' </summary>
        Private Sub AssertNormalizedRoundTrip(s As String, form As NormalizationForm, apply As Action(Of NormalizedString))
            Dim n As NormalizedString = NormalizedString.FromString(s)
            apply(n)

            Dim expected As String = s.Normalize(form)
            Assert.AreEqual(expected, n.Get)

            Dim originalLen As Integer = Utf8Helpers.Utf8Length(s)
            Dim normLen As Integer = Utf8Helpers.Utf8Length(n.Get)
            Dim converted As (Integer, Integer)? = n.ConvertOffsets(New OffsetRange(False, 0, normLen))
            Assert.AreEqual((0, originalLen), converted, $"Round-trip failed for input [{s}] with form {form}")
        End Sub

        <TestMethod>
        Public Sub TextAndAlignment_AllForms()
            For Each s In Battery
                AssertNormalizedRoundTrip(s, NormalizationForm.FormC, Sub(n) n.Nfc())
                AssertNormalizedRoundTrip(s, NormalizationForm.FormKC, Sub(n) n.Nfkc())
                AssertNormalizedRoundTrip(s, NormalizationForm.FormD, Sub(n) n.Nfd())
                AssertNormalizedRoundTrip(s, NormalizationForm.FormKD, Sub(n) n.Nfkd())
            Next
        End Sub

        <TestMethod>
        Public Sub Hangul_Jamo()
            ' NFD of a precomposed syllable produces jamo.
            Dim n As NormalizedString = NormalizedString.FromString("각")
            n.Nfd()
            Assert.AreEqual("각", n.Get)
            Assert.AreEqual("각", "각".Normalize(NormalizationForm.FormD))
            Assert.AreEqual((0, 3), n.ConvertOffsets(New OffsetRange(False, 0, 9)))

            ' NFC of a precomposed syllable stays itself and keeps full alignment.
            Dim n2 As NormalizedString = NormalizedString.FromString("각")
            n2.Nfc()
            Assert.AreEqual("각", n2.Get)
            Assert.AreEqual((0, 3), n2.ConvertOffsets(New OffsetRange(False, 0, 3)))

            ' A decomposed jamo input composes to a single syllable (text only).
            Dim n3 As NormalizedString = NormalizedString.FromString("각")
            n3.Nfc()
            Assert.AreEqual("각", n3.Get)
        End Sub

        <TestMethod>
        Public Sub Nfkc_LigatureText()
            Assert.AreEqual("fi", "ﬁ".Normalize(NormalizationForm.FormKC))
            Dim n As NormalizedString = NormalizedString.FromString("ﬁ")
            n.Nfkc()
            Assert.AreEqual("fi", n.Get)
        End Sub

        <TestMethod>
        Public Sub Nfd_OverlayMarks_ReorderCorrectly()
            ' U+0334 (ccc 1) must sort before U+0304 (ccc 230).
            Dim s1 As String = "A" & ChrW(&H334) & ChrW(&H304)
            Assert.AreEqual(s1.Normalize(NormalizationForm.FormD), NormalizedString.FromString(s1).Nfd().Get)

            ' U+0338 (ccc 1) must sort before U+0E38 (ccc 103).
            Dim s2 As String = ChrW(&H0E38) & ChrW(&H338)
            Assert.AreEqual(s2.Normalize(NormalizationForm.FormD), NormalizedString.FromString(s2).Nfd().Get)
        End Sub

        <TestMethod>
        Public Sub OverlayMarks_AllForms()
            ' Fixed battery covering the ccc-1 overlay marks U+0334..U+0338 co-occurring with
            ' other combining marks, across all four normalization forms.
            Dim battery As String() = {
                "A" & ChrW(&H334) & ChrW(&H304),
                "B" & ChrW(&H335) & ChrW(&H323),
                "C" & ChrW(&H336) & ChrW(&H301),
                "D" & ChrW(&H337) & ChrW(&H308),
                "E" & ChrW(&H338) & ChrW(&H323) & ChrW(&H301),
                ChrW(&H0E38) & ChrW(&H338),
                ChrW(&H0E48) & ChrW(&H334),
                "e" & ChrW(&H334) & ChrW(&H304) & ChrW(&H323),
                "a" & ChrW(&H335) & ChrW(&H301) & ChrW(&H323)
            }
            For Each s In battery
                Assert.AreEqual(s.Normalize(NormalizationForm.FormD), NormalizedString.FromString(s).Nfd().Get, $"NFD failed for [{s}]")
                Assert.AreEqual(s.Normalize(NormalizationForm.FormKD), NormalizedString.FromString(s).Nfkd().Get, $"NFKD failed for [{s}]")
                Assert.AreEqual(s.Normalize(NormalizationForm.FormC), NormalizedString.FromString(s).Nfc().Get, $"NFC failed for [{s}]")
                Assert.AreEqual(s.Normalize(NormalizationForm.FormKC), NormalizedString.FromString(s).Nfkc().Get, $"NFKC failed for [{s}]")
            Next
        End Sub

        <TestMethod>
        Public Sub SupplementaryCombiningMark()
            ' U+1D16D (MUSICAL SYMBOL COMBINING AUGMENTATION DOT) is a supplementary scalar
            ' with ccc 226; it must sort after U+0E38 (ccc 103), not be treated as a starter.
            Dim s As String = Char.ConvertFromUtf32(&H1D16D) & ChrW(&H0E38)
            Assert.AreEqual(s.Normalize(NormalizationForm.FormD), NormalizedString.FromString(s).Nfd().Get)
            Assert.AreEqual(s.Normalize(NormalizationForm.FormC), NormalizedString.FromString(s).Nfc().Get)
            Assert.AreEqual(s.Normalize(NormalizationForm.FormKC), NormalizedString.FromString(s).Nfkc().Get)
        End Sub

        <TestMethod>
        Public Sub ComposePair_Hangul()
            Assert.AreEqual("가", UnicodeNormalizer.ComposePair("ᄀ", "ᅡ"))
            Assert.AreEqual("각", UnicodeNormalizer.ComposePair("가", "ᆨ"))
            Assert.IsNull(UnicodeNormalizer.ComposePair("a", "b"))
            Assert.AreEqual("á", UnicodeNormalizer.ComposePair("a", ChrW(&H301).ToString()))
        End Sub

    End Class

End Namespace
