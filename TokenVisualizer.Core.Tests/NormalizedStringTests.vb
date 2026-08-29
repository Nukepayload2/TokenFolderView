Imports System.Text
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class NormalizedStringTests

        ' ------------------------------------------------------------------
        ' ByteLevel normalization (port of normalizers/byte_level.rs test_byte_level_normalize)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteLevel_Normalize()
            Dim original As String = "Hello 我今天能为你做什么"
            Dim expectedNormalized As String = "HelloĠæĪĳä»Ĭå¤©èĥ½ä¸ºä½łåģļä»Ģä¹Ī"
            Assert.AreNotEqual(expectedNormalized, original)

            Dim n As NormalizedString = NormalizedString.FromString(original)
            n.Transform(TestHelpers.ByteLevelTransform(original), 0)

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

        ' ------------------------------------------------------------------
        ' NFKC ligature (port of normalizers/unicode.rs test_nfkc)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Nfkc_Ligature()
            Dim original As String = "ﬁ"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            n.Nfkc()
            Assert.AreEqual("fi", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(0, 3), (0, 3)}
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {(0, 2), (0, 2), (0, 2)}
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        ' ------------------------------------------------------------------
        ' Prepend (port of normalizers/prepend.rs test_prepend)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Prepend_Exact()
            Dim original As String = "Hello"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            n.Prepend("▁")
            Assert.AreEqual("▁Hello", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 1), (0, 1), (0, 1), (0, 1), (1, 2), (2, 3), (3, 4), (4, 5)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {(0, 4), (4, 5), (5, 6), (6, 7), (7, 8)}
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        ' ------------------------------------------------------------------
        ' Strip accents (port of normalizers/strip.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub StripAccents_Simple()
            Dim n As NormalizedString = NormalizedString.FromString("Me llamó")
            n.Nfkd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c))
            Assert.AreEqual("Me llamo", n.Get)

            Dim n2 As NormalizedString = NormalizedString.FromString("Me llamo")
            n2.Nfkd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c))
            Assert.AreEqual("Me llamo", n2.Get)

            Dim n3 As NormalizedString = NormalizedString.FromString("这很简单")
            n3.Nfkd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c))
            Assert.AreEqual("这很简单", n3.Get)
        End Sub

        <TestMethod>
        Public Sub StripAccents_VietnameseBug()
            Dim n As NormalizedString = NormalizedString.FromString("ậ…")
            n.Nfkd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c))
            Assert.AreEqual("a...", n.Get)
            n.Lowercase()
            Assert.AreEqual("a...", n.Get)

            Dim original As String = "Cụ thể, bạn sẽ tham gia một nhóm các giám đốc điều hành tổ chức, các nhà lãnh đạo doanh nghiệp, các học giả, chuyên gia phát triển và tình nguyện viên riêng biệt trong lĩnh vực phi lợi nhuận…"
            Dim expected As String = "cu the, ban se tham gia mot nhom cac giam đoc đieu hanh to chuc, cac nha lanh đao doanh nghiep, cac hoc gia, chuyen gia phat trien va tinh nguyen vien rieng biet trong linh vuc phi loi nhuan..."
            Dim n2 As NormalizedString = NormalizedString.FromString(original)
            n2.Nfkd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c)).Lowercase()
            Assert.AreEqual(expected, n2.Get)
        End Sub

        <TestMethod>
        Public Sub StripAccents_ThaiBug()
            Dim original As String = "ำน" & ChrW(&H0E49) & "ำ3ลำ"
            Dim expected As String = "านา3ลา"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            n.Nfkd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c)).Lowercase()
            Assert.AreEqual(expected, n.Get)
        End Sub

        <TestMethod>
        Public Sub StripAccents_Multiple()
            Dim original As String = "e" & ChrW(&H304) & ChrW(&H304) & ChrW(&H304) & "o"
            Dim n As NormalizedString = NormalizedString.FromString(original)
            n.Filter(Function(c) Not TestHelpers.IsCombiningMark(c))
            Assert.AreEqual("eo", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(0, 1), (7, 8)}
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 1), (1, 1), (1, 1), (1, 1), (1, 1), (1, 1), (1, 2)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        ' ------------------------------------------------------------------
        ' Replace (port of normalizers/replace.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Replace_Simple()
            Dim n As NormalizedString = NormalizedString.FromString("This is a ''test''")
            n.Replace(New StringPattern("''"), """")
            Assert.AreEqual("This is a ""test""", n.Get)
        End Sub

        <TestMethod>
        Public Sub Replace_Regex()
            Dim n As NormalizedString = NormalizedString.FromString("This     is   a         test")
            n.Replace(New RegexPattern("\s+"), " ")
            Assert.AreEqual("This is a test", n.Get)
        End Sub

        <TestMethod>
        Public Sub Replace_FromNormalizerTests()
            Dim n As NormalizedString = NormalizedString.FromString(" Hello   friend ")
            n.Replace(New StringPattern(" "), "_")
            Assert.AreEqual("_Hello___friend_", n.Get)

            Dim n2 As NormalizedString = NormalizedString.FromString("aaaab")
            n2.Replace(New StringPattern("a"), "b")
            Assert.AreEqual("bbbbb", n2.Get)

            Dim n3 As NormalizedString = NormalizedString.FromString("aaaab")
            n3.Replace(New StringPattern("aaa"), "b")
            Assert.AreEqual("bab", n3.Get)

            Dim n4 As NormalizedString = NormalizedString.FromString(" Hello   friend ")
            n4.Replace(New RegexPattern("\s+"), "_")
            Assert.AreEqual("_Hello_friend_", n4.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Filter / NFD alignment vectors (port of normalizer.rs tests)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Nfd_AddsNewChars()
            Dim n As NormalizedString = NormalizedString.FromString("élégant")
            n.Nfd()
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 2), (0, 2), (0, 2), (2, 3), (3, 5), (3, 5), (3, 5), (5, 6), (6, 7), (7, 8), (8, 9)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 3), (0, 3), (3, 4), (4, 7), (4, 7), (7, 8), (8, 9), (9, 10), (10, 11)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        <TestMethod>
        Public Sub RemoveCharsAddedByNfd()
            Dim n As NormalizedString = NormalizedString.FromString("élégant")
            n.Nfd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c))
            Assert.AreEqual("elegant", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 2), (2, 3), (3, 5), (5, 6), (6, 7), (7, 8), (8, 9)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 1), (0, 1), (1, 2), (2, 3), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        <TestMethod>
        Public Sub RemoveChars()
            Dim n As NormalizedString = NormalizedString.FromString("élégant")
            n.Filter(Function(c) c <> "n"c)
            Assert.AreEqual("élégat", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 2), (0, 2), (2, 3), (3, 5), (3, 5), (5, 6), (6, 7), (8, 9)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 2), (0, 2), (2, 3), (3, 5), (3, 5), (5, 6), (6, 7), (7, 7), (7, 8)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        <TestMethod>
        Public Sub MixedAdditionAndRemoval()
            Dim n As NormalizedString = NormalizedString.FromString("élégant")
            n.Nfd().Filter(Function(c) Not TestHelpers.IsCombiningMark(c) AndAlso c <> "n"c)
            Assert.AreEqual("elegat", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 2), (2, 3), (3, 5), (5, 6), (6, 7), (8, 9)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
        End Sub

        <TestMethod>
        Public Sub Filter_BasicMapLowercase()
            ' filter + lowercase through a whitespace-heavy input
            Dim n As NormalizedString = NormalizedString.FromString("    __Hello__   ")
            n.Filter(Function(c) Not Char.IsWhiteSpace(c)).Lowercase()
            Assert.AreEqual("__hello__", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' transform_range change-value contract (port of transform_range_single_bytes)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub TransformRange_SingleBytes()
            Dim s As NormalizedString = NormalizedString.FromString("Hello friend")

            ' Removing at the beginning
            Dim current As NormalizedString = NormalizedString.FromString("Hello friend")
            current.TransformRange(New OffsetRange(True, 0, 4), New List(Of (String, Integer)) From {("Y", 0)}, 3)
            Dim expected1 As New List(Of (Integer, Integer)) From {
                (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9), (9, 10), (10, 11), (11, 12)
            }
            TestHelpers.AssertNormalized(current, "Hello friend", "Yo friend", expected1)

            ' Removing in the middle
            Dim current2 As NormalizedString = NormalizedString.FromString("Hello friend")
            current2.TransformRange(New OffsetRange(True, 3, 10),
                                    New List(Of (String, Integer)) From {("_", 0), ("F", 0), ("R", -2)}, 2)
            Dim expected2 As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (5, 6), (6, 7), (7, 8), (10, 11), (11, 12)
            }
            TestHelpers.AssertNormalized(current2, "Hello friend", "Hel_FRnd", expected2)

            ' Removing at the end
            Dim current3 As NormalizedString = NormalizedString.FromString("Hello friend")
            current3.TransformRange(New OffsetRange(True, 5, -1),
                                    New List(Of (String, Integer)) From {("_", 0), ("F", -5)}, 0)
            Dim expected3 As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7)
            }
            TestHelpers.AssertNormalized(current3, "Hello friend", "Hello_F", expected3)

            ' Adding at the beginning
            Dim current4 As NormalizedString = NormalizedString.FromString("Hello friend")
            current4.TransformRange(New OffsetRange(True, 0, 1),
                                    New List(Of (String, Integer)) From {("H", 1), ("H", 0)}, 0)
            Dim expected4 As New List(Of (Integer, Integer)) From {
                (0, 0), (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9), (9, 10), (10, 11), (11, 12)
            }
            TestHelpers.AssertNormalized(current4, "Hello friend", "HHello friend", expected4)

            ' Adding in the middle
            Dim current5 As NormalizedString = NormalizedString.FromString("Hello friend")
            current5.TransformRange(New OffsetRange(True, 5, 6),
                                    New List(Of (String, Integer)) From {("_", 0), ("m", 1), ("y", 1), ("_", 1)}, 0)
            Dim expected5 As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (5, 6), (5, 6), (5, 6),
                (6, 7), (7, 8), (8, 9), (9, 10), (10, 11), (11, 12)
            }
            TestHelpers.AssertNormalized(current5, "Hello friend", "Hello_my_friend", expected5)

            ' Adding at the end
            Dim current6 As NormalizedString = NormalizedString.FromString("Hello friend")
            current6.TransformRange(New OffsetRange(True, 11, -1),
                                    New List(Of (String, Integer)) From {("d", 0), ("_", 1), ("!", 1)}, 0)
            Dim expected6 As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9), (9, 10),
                (10, 11), (11, 12), (11, 12), (11, 12)
            }
            TestHelpers.AssertNormalized(current6, "Hello friend", "Hello friend_!", expected6)
        End Sub

        <TestMethod>
        Public Sub TransformRange_MultipleBytes()
            ' Removing at the beginning on surrogate pairs
            Dim current As NormalizedString = NormalizedString.FromString("𝔾𝕠𝕠𝕕")
            current.TransformRange(New OffsetRange(True, 0, 8), New List(Of (String, Integer)) From {("G", -1)}, 0)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 4), (8, 12), (8, 12), (8, 12), (8, 12), (12, 16), (12, 16), (12, 16), (12, 16)
            }
            TestHelpers.AssertNormalized(current, "𝔾𝕠𝕠𝕕", "G𝕠𝕕", expected)

            ' Adding at the end
            Dim current2 As NormalizedString = NormalizedString.FromString("𝔾𝕠𝕠𝕕")
            current2.TransformRange(New OffsetRange(True, 16, -1), New List(Of (String, Integer)) From {("!", 1)}, 0)
            Dim expected2 As New List(Of (Integer, Integer)) From {
                (0, 4), (0, 4), (0, 4), (0, 4), (4, 8), (4, 8), (4, 8), (4, 8),
                (8, 12), (8, 12), (8, 12), (8, 12), (12, 16), (12, 16), (12, 16), (12, 16), (12, 16)
            }
            TestHelpers.AssertNormalized(current2, "𝔾𝕠𝕠𝕕", "𝔾𝕠𝕠𝕕!", expected2)
        End Sub

        <TestMethod>
        Public Sub AddedCharactersAlignment()
            Dim n As NormalizedString = NormalizedString.FromString("野口 No")
            n.Transform(
                n.Get.ToCharArray().Select(
                    Function(c)
                        If AscW(c) > &H4E00 Then
                            Return New List(Of (String, Integer)) From {(" ", 0), (c.ToString(), 1), (" ", 1)}
                        Else
                            Return New List(Of (String, Integer)) From {(c.ToString(), 0)}
                        End If
                    End Function).SelectMany(Function(x) x),
                0)

            Assert.AreEqual(" 野  口  No", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 3), (0, 3), (0, 3), (0, 3), (0, 3), (3, 6), (3, 6), (3, 6), (3, 6), (3, 6),
                (6, 7), (7, 8), (8, 9)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Dim expectedOriginal As New List(Of (Integer, Integer)) From {
                (0, 5), (0, 5), (0, 5), (5, 10), (5, 10), (5, 10), (10, 11), (11, 12), (12, 13)
            }
            CollectionAssert.AreEqual(expectedOriginal, n.AlignmentsOriginal())
        End Sub

        <TestMethod>
        Public Sub AddedAroundEdges()
            Dim n As NormalizedString = NormalizedString.FromString("Hello")
            n.Transform(
                New List(Of (String, Integer)) From {
                    (" ", 1), ("H", 0), ("e", 0), ("l", 0), ("l", 0), ("o", 0), (" ", 1)
                },
                0)
            Assert.AreEqual(" Hello ", n.Get)
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(False, 1, 6)))
        End Sub

        ' ------------------------------------------------------------------
        ' Strip / LStrip / RStrip
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub LStrip()
            Dim n As NormalizedString = NormalizedString.FromString("  This is an example  ")
            n.LStrip()
            Assert.AreEqual("This is an example  ", n.Get)
            Assert.AreEqual("This is an example  ", n.GetRangeOriginal(New OffsetRange(False, 0, 20)))
        End Sub

        <TestMethod>
        Public Sub RStrip()
            Dim n As NormalizedString = NormalizedString.FromString("  This is an example  ")
            n.RStrip()
            Assert.AreEqual("  This is an example", n.Get)
            Assert.AreEqual("  This is an example", n.GetRangeOriginal(New OffsetRange(False, 0, n.Len())))
        End Sub

        <TestMethod>
        Public Sub Strip()
            Dim n As NormalizedString = NormalizedString.FromString("  This is an example  ")
            n.Strip()
            Assert.AreEqual("This is an example", n.Get)
            Assert.AreEqual("This is an example", n.GetRangeOriginal(New OffsetRange(False, 0, n.Len())))
        End Sub

        <TestMethod>
        Public Sub StripUnicode()
            Dim n As NormalizedString = NormalizedString.FromString("  你好asa " & vbLf)
            n.Strip()
            Assert.AreEqual("你好asa", n.Get)
            Assert.AreEqual("你好asa", n.GetRangeOriginal(New OffsetRange(False, 0, Utf8Helpers.Utf8Length("你好asa"))))
        End Sub

        ' ------------------------------------------------------------------
        ' Prepend / Append exact alignments (port of normalizer.rs prepend/append tests)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Prepend_String()
            Dim n As NormalizedString = NormalizedString.FromString("there")
            n.Prepend("Hey ")
            Assert.AreEqual("Hey there", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 1), (0, 1), (0, 1), (0, 1), (0, 1), (1, 2), (2, 3), (3, 4), (4, 5)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Assert.AreEqual((0, 1), n.ConvertOffsets(New OffsetRange(False, 0, 4)))
        End Sub

        <TestMethod>
        Public Sub Append_String()
            Dim n As NormalizedString = NormalizedString.FromString("Hey")
            n.Append(" there")
            Assert.AreEqual("Hey there", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {
                (0, 1), (1, 2), (2, 3), (2, 3), (2, 3), (2, 3), (2, 3), (2, 3), (2, 3)
            }
            CollectionAssert.AreEqual(expected, n.Alignments)
            Assert.AreEqual((2, 3), n.ConvertOffsets(New OffsetRange(False, 3, Utf8Helpers.Utf8Length(" there") + 3)))
        End Sub

        <TestMethod>
        Public Sub AppendAfterClear()
            Dim n As NormalizedString = NormalizedString.FromString("Hello")
            Assert.AreEqual("Hello", n.Get)
            n.Clear()
            Assert.AreEqual("", n.Get)
            n.Append(" World")
            Assert.AreEqual(" World", n.Get)
            Assert.AreEqual(5, n.LenOriginal())
            Assert.AreEqual(6, n.Len())
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(True, 0, 5)))
            Assert.AreEqual("", n.GetRangeOriginal(New OffsetRange(False, 0, 6)))
            Assert.AreEqual(" World", n.GetRange(New OffsetRange(False, 0, 6)))
        End Sub

        <TestMethod>
        Public Sub ClearTest()
            Dim n As NormalizedString = NormalizedString.FromString("Hello")
            Dim len As Integer = n.Clear()
            Assert.AreEqual(5, len)
            Assert.AreEqual("", n.Get)
        End Sub

        ' ------------------------------------------------------------------
        ' Slice
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Slice_Nfkc()
            Dim s As NormalizedString = NormalizedString.FromString("𝔾𝕠𝕠𝕕 𝕞𝕠𝕣𝕟𝕚𝕟𝕘")
            s.Nfkc()

            Dim originalSlice As NormalizedString = s.Slice(New OffsetRange(True, 0, 4))
            Assert.AreEqual("G", originalSlice.Get)
            Assert.AreEqual("𝔾", originalSlice.Original)

            Dim normalizedSlice As NormalizedString = s.Slice(New OffsetRange(False, 0, 4))
            Assert.AreEqual("Good", normalizedSlice.Get)
            Assert.AreEqual("𝔾𝕠𝕠𝕕", normalizedSlice.Original)
        End Sub

        <TestMethod>
        Public Sub Slice_AfterStrip()
            Dim s As NormalizedString = NormalizedString.FromString("   Good Morning!   ")
            s.Strip()

            Dim slice As NormalizedString = s.Slice(New OffsetRange(True, 0, -1))
            Assert.AreEqual("Good", slice.GetRangeOriginal(New OffsetRange(False, 0, 4)))
            slice = s.Slice(New OffsetRange(False, 0, -1))
            Assert.AreEqual("Good", slice.GetRangeOriginal(New OffsetRange(False, 0, 4)))
            slice = s.Slice(New OffsetRange(True, 4, 15))
            Assert.AreEqual("ood", slice.GetRangeOriginal(New OffsetRange(False, 0, 3)))
            slice = s.Slice(New OffsetRange(True, 3, 16))
            Assert.AreEqual("Good", slice.GetRangeOriginal(New OffsetRange(False, 0, 4)))
        End Sub

        ' ------------------------------------------------------------------
        ' convert_offsets / range conversion
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub RangeConversion()
            Dim n As NormalizedString = NormalizedString.FromString("    __Hello__   ")
            n.Filter(Function(c) Not Char.IsWhiteSpace(c)).Lowercase()

            Dim helloN As (Integer, Integer)? = n.ConvertOffsets(New OffsetRange(True, 6, 11))
            Assert.AreEqual((2, 7), helloN)
            Assert.AreEqual("hello", n.GetRange(New OffsetRange(False, 2, 7)))
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(False, 2, 7)))
            Assert.AreEqual("hello", n.GetRange(New OffsetRange(True, 6, 11)))
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(True, 6, 11)))

            Assert.AreEqual((0, 0), n.ConvertOffsets(New OffsetRange(True, 0, 0)))
            Assert.AreEqual((3, 3), n.ConvertOffsets(New OffsetRange(True, 3, 3)))
            Assert.AreEqual((9, 9), n.ConvertOffsets(New OffsetRange(True, 15, -1)))
            Assert.AreEqual((16, 16), n.ConvertOffsets(New OffsetRange(True, 16, -1)))
            Assert.IsNull(n.ConvertOffsets(New OffsetRange(True, 17, -1)))
            Assert.AreEqual((0, 0), n.ConvertOffsets(New OffsetRange(False, 0, 0)))
            Assert.AreEqual((3, 3), n.ConvertOffsets(New OffsetRange(False, 3, 3)))
            Assert.AreEqual((9, 9), n.ConvertOffsets(New OffsetRange(False, 9, -1)))
            Assert.IsNull(n.ConvertOffsets(New OffsetRange(False, 10, -1)))
        End Sub

        <TestMethod>
        Public Sub OriginalRange()
            Dim n As NormalizedString = NormalizedString.FromString("Hello_______ World!")
            n.Filter(Function(c) c <> "_"c).Lowercase()

            Dim worldN As String = n.GetRange(New OffsetRange(False, 6, 11))
            Dim worldO As String = n.GetRangeOriginal(New OffsetRange(False, 6, 11))
            Assert.AreEqual("world", worldN)
            Assert.AreEqual("World", worldO)

            Dim originalRange As (Integer, Integer) = n.ConvertOffsets(New OffsetRange(False, 6, 11)).Value
            Assert.AreEqual("world", n.GetRange(New OffsetRange(True, originalRange.Item1, originalRange.Item2)))
            Assert.AreEqual("World", n.GetRangeOriginal(New OffsetRange(True, originalRange.Item1, originalRange.Item2)))
            Assert.AreEqual((13, 18), New OffsetRange(True, originalRange.Item1, originalRange.Item2).IntoFullRange(n.LenOriginal()))
        End Sub

        <TestMethod>
        Public Sub RemoveAtBeginning()
            Dim n As NormalizedString = NormalizedString.FromString("     Hello")
            n.Filter(Function(c) Not Char.IsWhiteSpace(c))
            Assert.AreEqual("Hello", n.Get)
            Assert.AreEqual("ello", n.GetRangeOriginal(New OffsetRange(False, 1, 5)))
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(False, 0, 5)))
        End Sub

        <TestMethod>
        Public Sub RemoveAtEnd()
            Dim n As NormalizedString = NormalizedString.FromString("Hello    ")
            n.Filter(Function(c) Not Char.IsWhiteSpace(c))
            Assert.AreEqual("Hell", n.GetRangeOriginal(New OffsetRange(False, 0, 4)))
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(False, 0, 5)))
        End Sub

        <TestMethod>
        Public Sub RemovedAroundBothEdges()
            Dim n As NormalizedString = NormalizedString.FromString("  Hello  ")
            n.Filter(Function(c) Not Char.IsWhiteSpace(c))
            Assert.AreEqual("Hello", n.Get)
            Assert.AreEqual("Hello", n.GetRangeOriginal(New OffsetRange(False, 0, 5)))
            Assert.AreEqual("ell", n.GetRangeOriginal(New OffsetRange(False, 1, 4)))
        End Sub

        <TestMethod>
        Public Sub Filter_RemovesLeadingMultiByteChar()
            ' Regression: TransformRange must advance the replaced-character iterator past the
            ' initial_offset dropped chars (mirrors Rust `(&mut iter).take(initial_offset)`).
            Dim n As NormalizedString = NormalizedString.FromString("あab")
            n.Filter(Function(c) c <> "あ"c)
            Assert.AreEqual("ab", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(3, 4), (4, 5)}
            CollectionAssert.AreEqual(expected, n.Alignments)
            Assert.AreEqual("ab", n.GetRangeOriginal(New OffsetRange(False, 0, 2)))
        End Sub

        <TestMethod>
        Public Sub Strip_IdeographicSpace()
            ' Regression: U+3000 is a whitespace char that is 3 UTF-8 bytes; stripping it used to
            ' double-count its bytes and crash with ArgumentOutOfRangeException.
            Dim n As NormalizedString = NormalizedString.FromString(ChrW(&H3000) & "Hello")
            n.Strip()
            Assert.AreEqual("Hello", n.Get)
            Dim expected As New List(Of (Integer, Integer)) From {(3, 4), (4, 5), (5, 6), (6, 7), (7, 8)}
            CollectionAssert.AreEqual(expected, n.Alignments)
        End Sub

        <TestMethod>
        Public Sub TransformCheck()
            Dim s As NormalizedString = NormalizedString.FromString("abc…")
            s.Nfkd()
            s.Transform(New List(Of (String, Integer)) From {("a", -2), (".", 0), (".", 0), (".", 0)}, 0)
            s.Lowercase()
            Assert.AreEqual("a...", s.Get)
        End Sub

    End Class

End Namespace
