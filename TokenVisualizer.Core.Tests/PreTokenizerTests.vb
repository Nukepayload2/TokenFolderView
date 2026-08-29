Imports Tokenizers
Imports Tokenizers.Internal
Imports Tokenizers.PreTokenizers

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Byte-exact ports of the Rust unit tests for the 12 pre-tokenizers
    ''' (tokenizers/src/pre_tokenizers/*.rs and unicode_scripts/pre_tokenizer.rs).
    ''' </summary>
    <TestClass>
    Public Class PreTokenizerTests

        Private Shared Function SplitsTextOffsets(pts As PreTokenizedString, offsetRef As OffsetReferential) As List(Of (String, (Integer, Integer)))
            Dim result As New List(Of (String, (Integer, Integer)))()
            For Each s In pts.GetSplits(offsetRef, OffsetType.Byte)
                result.Add((s.Text, s.Offsets))
            Next
            Return result
        End Function

        Private Shared Sub AssertSplits(actual As List(Of (String, (Integer, Integer))), expected As List(Of (String, (Integer, Integer))))
            Assert.HasCount(expected.Count, actual)
            For i As Integer = 0 To actual.Count - 1
                Assert.AreEqual(expected(i).Item1, actual(i).Item1, $"text at index {i}")
                Assert.AreEqual(expected(i).Item2, actual(i).Item2, $"offsets at index {i}")
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' Bert (pre_tokenizers/bert.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Bert_Basic()
            Dim pretok As New BertPreTokenizer()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey friend!     How are you?!?")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hey", (0, 3)),
                    ("friend", (4, 10)),
                    ("!", (10, 11)),
                    ("How", (16, 19)),
                    ("are", (20, 23)),
                    ("you", (24, 27)),
                    ("?", (27, 28)),
                    ("!", (28, 29)),
                    ("?", (29, 30))
                })
        End Sub

        <TestMethod>
        Public Sub Bert_ChineseChars()
            Dim n As NormalizedString = NormalizedString.FromString("野口里佳 Noguchi Rika")
            Dim stream As New List(Of (String, Integer))()
            For Each c In n.Get.ToCharArray()
                If AscW(c) > &H4E00 Then
                    stream.Add((" ", 0))
                    stream.Add((c.ToString(), 1))
                    stream.Add((" ", 1))
                Else
                    stream.Add((c.ToString(), 0))
                End If
            Next
            n.Transform(stream, 0)

            Dim pts As PreTokenizedString = PreTokenizedString.FromNormalizedString(n)
            Dim pretok As New BertPreTokenizer()
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("野", (0, 3)),
                    ("口", (3, 6)),
                    ("里", (6, 9)),
                    ("佳", (9, 12)),
                    ("Noguchi", (13, 20)),
                    ("Rika", (21, 25))
                })
        End Sub

        ' ------------------------------------------------------------------
        ' ByteLevel (pre_tokenizers/byte_level.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ByteLevel_PreTokenization()
            Dim bytelevel As New ByteLevelPreTokenizer(False, True, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello my friend, how is your day going?")
            bytelevel.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hello", (0, 5)),
                    ("Ġmy", (5, 8)),
                    ("Ġfriend", (8, 15)),
                    (",", (15, 16)),
                    ("Ġhow", (16, 20)),
                    ("Ġis", (20, 23)),
                    ("Ġyour", (23, 28)),
                    ("Ġday", (28, 32)),
                    ("Ġgoing", (32, 38)),
                    ("?", (38, 39))
                })
        End Sub

        <TestMethod>
        Public Sub ByteLevel_PreTokenizationNoRegex()
            Dim bytelevel As New ByteLevelPreTokenizer(True, True, False)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello my friend, how is your day going?")
            bytelevel.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("ĠHelloĠmyĠfriend,ĠhowĠisĠyourĠdayĠgoing?", (0, 39))
                })
        End Sub

        <TestMethod>
        Public Sub ByteLevel_AddPrefixSpace()
            Dim bytelevel As New ByteLevelPreTokenizer(True, True, True)
            For Each s In {" Hello my friend, how is your day going?", "Hello my friend, how is your day going?"}
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(s)
                bytelevel.PreTokenize(pts)
                AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                    New List(Of (String, (Integer, Integer))) From {
                        ("ĠHello", (0, 7)),
                        ("Ġmy", (7, 11)),
                        ("Ġfriend", (11, 19)),
                        (",", (19, 20)),
                        ("Ġhow", (20, 25)),
                        ("Ġis", (25, 29)),
                        ("Ġyour", (29, 35)),
                        ("Ġday", (35, 40)),
                        ("Ġgoing", (40, 47)),
                        ("?", (47, 48))
                    })
            Next
        End Sub

        <TestMethod>
        Public Sub ByteLevel_HandlingOfNewlines()
            Dim bytelevel As New ByteLevelPreTokenizer(False, True, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello there" & vbLf & "Hello there")
            bytelevel.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hello", (0, 5)),
                    ("Ġthere", (5, 11)),
                    ("Ċ", (11, 12)),
                    ("Hello", (12, 17)),
                    ("Ġthere", (17, 23))
                })
        End Sub

        <TestMethod>
        Public Sub ByteLevel_HandlingOfMultipleWhitespaces()
            Dim bytelevel As New ByteLevelPreTokenizer(False, True, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello there       dear")
            bytelevel.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hello", (0, 5)),
                    ("Ġthere", (5, 11)),
                    ("ĠĠĠĠĠĠ", (11, 17)),
                    ("Ġdear", (17, 22))
                })
        End Sub

        <TestMethod>
        Public Sub ByteLevel_OffsetsWhenCharSplitUp()
            Dim input As String = "i⭢j"
            Dim bytelevel As New ByteLevelPreTokenizer(False, True, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString(input)
            bytelevel.PreTokenize(pts)

            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("i", (0, 1)),
                    ("âŃ¢", (1, 4)),
                    ("j", (4, 5))
                })
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("i", (0, 1)),
                    ("âŃ¢", (1, 7)),
                    ("j", (7, 8))
                })

            ' The original offsets still slice the original input.
            Dim origSplits = SplitsTextOffsets(pts, OffsetReferential.Original)
            Dim slices As New List(Of String)()
            For Each s In origSplits
                slices.Add(Utf8Helpers.SliceByUtf8(input, s.Item2.Item1, s.Item2.Item2))
            Next
            CollectionAssert.AreEqual(New String() {"i", "⭢", "j"}, slices)
        End Sub

        ' ------------------------------------------------------------------
        ' Digits (pre_tokenizers/digits.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Digits_Numbers()
            Dim pretok As New DigitsPreTokenizer(False)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey 123 friend!")
            pretok.PreTokenize(pts)
            Dim expected As New List(Of (String, (Integer, Integer))) From {
                ("Hey ", (0, 4)),
                ("123", (4, 7)),
                (" friend!", (7, 15))
            }
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized), expected)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), expected)
        End Sub

        <TestMethod>
        Public Sub Digits_IndividualDigits()
            Dim pretok As New DigitsPreTokenizer(True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey 123 friend!")
            pretok.PreTokenize(pts)
            Dim expected As New List(Of (String, (Integer, Integer))) From {
                ("Hey ", (0, 4)),
                ("1", (4, 5)),
                ("2", (5, 6)),
                ("3", (6, 7)),
                (" friend!", (7, 15))
            }
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized), expected)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), expected)
        End Sub

        ' ------------------------------------------------------------------
        ' FixedLength (pre_tokenizers/fixed_length.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub FixedLength_Basic()
            Dim pretok As New FixedLengthPreTokenizer(5)
            Dim cases As New List(Of (String, List(Of (String, (Integer, Integer))))) From {
                ("Hello world", New List(Of (String, (Integer, Integer))) From {("Hello", (0, 5)), (" worl", (5, 10)), ("d", (10, 11))}),
                ("Short", New List(Of (String, (Integer, Integer))) From {("Short", (0, 5))}),
                ("", New List(Of (String, (Integer, Integer)))())
            }
            For Each c In cases
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(c.Item1)
                pretok.PreTokenize(pts)
                AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), c.Item2)
            Next
        End Sub

        <TestMethod>
        Public Sub FixedLength_CustomLength()
            Dim pretok As New FixedLengthPreTokenizer(3)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello world")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hel", (0, 3)),
                    ("lo ", (3, 6)),
                    ("wor", (6, 9)),
                    ("ld", (9, 11))
                })
        End Sub

        <TestMethod>
        Public Sub FixedLength_Utf8Characters()
            Dim pretok As New FixedLengthPreTokenizer(3)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hello 👋 world")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hel", (0, 3)),
                    ("lo ", (3, 6)),
                    ("👋 w", (6, 12)),
                    ("orl", (12, 15)),
                    ("d", (15, 16))
                })
        End Sub

        ' ------------------------------------------------------------------
        ' Metaspace (pre_tokenizers/metaspace.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Metaspace_Basic()
            Dim pretok As New MetaspacePreTokenizer("▁"c, PrependScheme.Always, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey friend!")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey", (0, 6)),
                    ("▁friend!", (6, 16))
                })
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey", (0, 3)),
                    ("▁friend!", (3, 11))
                })
        End Sub

        <TestMethod>
        Public Sub Metaspace_MultipleSpaces()
            Dim pretok As New MetaspacePreTokenizer("▁"c, PrependScheme.Always, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey   friend!")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey", (0, 6)),
                    ("▁", (6, 9)),
                    ("▁", (9, 12)),
                    ("▁friend!", (12, 22))
                })
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey", (0, 3)),
                    ("▁", (3, 4)),
                    ("▁", (4, 5)),
                    ("▁friend!", (5, 13))
                })
        End Sub

        <TestMethod>
        Public Sub Metaspace_NonLegacyMetaSpace()
            ' First + split=false, with a regex-pre-split on <s>.
            Dim pretok As New MetaspacePreTokenizer("▁"c, PrependScheme.First, False)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey my friend <s>how▁are you")
            pts.SplitBy(New RegexPattern("(<s>)"), SplitDelimiterBehavior.Isolated)
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey▁my▁friend▁", (0, 23)),
                    ("<s>", (23, 26)),
                    ("how▁are▁you", (26, 41))
                })

            ' Re-running with Always + split=true on the same pre-tokenized string.
            Dim pretokAlways As New MetaspacePreTokenizer("▁"c, PrependScheme.Always, True)
            pretokAlways.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey", (0, 6)),
                    ("▁my", (6, 11)),
                    ("▁friend", (11, 20)),
                    ("▁", (20, 23)),
                    ("▁<s>", (23, 29)),
                    ("▁how", (29, 35)),
                    ("▁are", (35, 41)),
                    ("▁you", (41, 47))
                })

            ' First + split=false with a leading space prefix.
            Dim pretokFirst As New MetaspacePreTokenizer("▁"c, PrependScheme.First, False)
            Dim pts2 As PreTokenizedString = PreTokenizedString.FromString(" Hey <s>how")
            pts2.SplitBy(New RegexPattern("(<s>)"), SplitDelimiterBehavior.Isolated)
            pretokFirst.PreTokenize(pts2)
            AssertSplits(SplitsTextOffsets(pts2, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey▁", (0, 9)),
                    ("<s>", (9, 12)),
                    ("how", (12, 15))
                })

            ' First + split=false with many splits.
            Dim pts3 As PreTokenizedString = PreTokenizedString.FromString(" Hey <s>how <s>are <s> you")
            pts3.SplitBy(New RegexPattern("(<s>)"), SplitDelimiterBehavior.Isolated)
            pretokFirst.PreTokenize(pts3)
            AssertSplits(SplitsTextOffsets(pts3, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("▁Hey▁", (0, 9)),
                    ("<s>", (9, 12)),
                    ("how▁", (12, 18)),
                    ("<s>", (18, 21)),
                    ("are▁", (21, 27)),
                    ("<s>", (27, 30)),
                    ("▁you", (30, 36))
                })
        End Sub

        <TestMethod>
        Public Sub Metaspace_Never_NoPrepend()
            ' PrependScheme.Never: no leading replacement is added, so the splits start with the
            ' original text (the "decode" side would restore the spaces).
            Dim pretok As New MetaspacePreTokenizer("▁"c, PrependScheme.Never, True)
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey friend!")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hey", (0, 3)),
                    ("▁friend!", (3, 13))
                })
        End Sub

        ' ------------------------------------------------------------------
        ' Punctuation (pre_tokenizers/punctuation.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Punctuation_Basic()
            Dim pretok As New PunctuationPreTokenizer()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey friend!     How are you?!?")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hey friend", (0, 10)),
                    ("!", (10, 11)),
                    ("     How are you", (11, 27)),
                    ("?", (27, 28)),
                    ("!", (28, 29)),
                    ("?", (29, 30))
                })
        End Sub

        ' ------------------------------------------------------------------
        ' Split (pre_tokenizers/split.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Split_Basic()
            Dim tests As New List(Of (SplitDelimiterBehavior, List(Of (String, (Integer, Integer))))) From {
                (SplitDelimiterBehavior.Removed,
                 New List(Of (String, (Integer, Integer))) From {("How", (0, 3)), ("are", (4, 7)), ("you", (8, 11)), ("doing", (12, 17)), ("?", (17, 18))}),
                (SplitDelimiterBehavior.Isolated,
                 New List(Of (String, (Integer, Integer))) From {
                    ("How", (0, 3)), (" ", (3, 4)), ("are", (4, 7)), (" ", (7, 8)),
                    ("you", (8, 11)), (" ", (11, 12)), ("doing", (12, 17)), ("?", (17, 18))
                 }),
                (SplitDelimiterBehavior.MergedWithPrevious,
                 New List(Of (String, (Integer, Integer))) From {
                    ("How ", (0, 4)), ("are ", (4, 8)), ("you ", (8, 12)), ("doing", (12, 17)), ("?", (17, 18))
                 }),
                (SplitDelimiterBehavior.MergedWithNext,
                 New List(Of (String, (Integer, Integer))) From {
                    ("How", (0, 3)), (" are", (3, 7)), (" you", (7, 11)), (" doing", (11, 17)), ("?", (17, 18))
                 }),
                (SplitDelimiterBehavior.Contiguous,
                 New List(Of (String, (Integer, Integer))) From {
                    ("How", (0, 3)), (" ", (3, 4)), ("are", (4, 7)), (" ", (7, 8)),
                    ("you", (8, 11)), (" ", (11, 12)), ("doing?", (12, 18))
                 })
            }

            For Each t In tests
                Dim pts As PreTokenizedString = PreTokenizedString.FromString("How are you doing?")
                Dim pretok As New SplitPreTokenizer("Regex", "\w+|[^\w\s]+", t.Item1, True)
                pretok.PreTokenize(pts)
                AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), t.Item2)
            Next
        End Sub

        <TestMethod>
        Public Sub Split_RegexString()
            Dim pts1 As PreTokenizedString = PreTokenizedString.FromString("Hey, man!")
            Dim pretokRegex As New SplitPreTokenizer("Regex", "\s+", SplitDelimiterBehavior.Removed, False)
            pretokRegex.PreTokenize(pts1)

            Dim pts2 As PreTokenizedString = PreTokenizedString.FromString("Hey, man!")
            Dim pretokString As New SplitPreTokenizer("String", " ", SplitDelimiterBehavior.Removed, False)
            pretokString.PreTokenize(pts2)

            AssertSplits(SplitsTextOffsets(pts1, OffsetReferential.Original), SplitsTextOffsets(pts2, OffsetReferential.Original))
        End Sub

        <TestMethod>
        Public Sub Split_Invert()
            Dim pts1 As PreTokenizedString = PreTokenizedString.FromString("Hello Hello Hello")
            Dim pretok As New SplitPreTokenizer("String", " ", SplitDelimiterBehavior.Removed, False)
            pretok.PreTokenize(pts1)

            Dim pts2 As PreTokenizedString = PreTokenizedString.FromString("Hello Hello Hello")
            Dim pretokInvert As New SplitPreTokenizer("String", "Hello", SplitDelimiterBehavior.Removed, True)
            pretokInvert.PreTokenize(pts2)

            AssertSplits(SplitsTextOffsets(pts1, OffsetReferential.Original), SplitsTextOffsets(pts2, OffsetReferential.Original))
        End Sub

        ' ------------------------------------------------------------------
        ' Sequence (pre_tokenizers/sequence.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Sequence_Basic()
            Dim pretok As New PreTokenizerSequence(New IPreTokenizer() {
                New WhitespaceSplitPreTokenizer(),
                New PunctuationPreTokenizer()
            })
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Hey friend!     How are you?!?")
            pretok.PreTokenize(pts)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original),
                New List(Of (String, (Integer, Integer))) From {
                    ("Hey", (0, 3)),
                    ("friend", (4, 10)),
                    ("!", (10, 11)),
                    ("How", (16, 19)),
                    ("are", (20, 23)),
                    ("you", (24, 27)),
                    ("?", (27, 28)),
                    ("!", (28, 29)),
                    ("?", (29, 30))
                })
        End Sub

        ' ------------------------------------------------------------------
        ' Whitespace (pre_tokenizers/whitespace.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub Whitespace_Basic()
            Dim pretok As New WhitespacePreTokenizer()
            Dim tests As New List(Of (String, List(Of (String, (Integer, Integer))))) From {
                ("Hey man!", New List(Of (String, (Integer, Integer))) From {("Hey", (0, 3)), ("man", (4, 7)), ("!", (7, 8))}),
                ("How are you doing?", New List(Of (String, (Integer, Integer))) From {
                    ("How", (0, 3)), ("are", (4, 7)), ("you", (8, 11)), ("doing", (12, 17)), ("?", (17, 18))
                 }),
                (vbLf, New List(Of (String, (Integer, Integer)))())
            }
            For Each t In tests
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(t.Item1)
                pretok.PreTokenize(pts)
                AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), t.Item2)
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' WhitespaceSplit (pre_tokenizers/whitespace.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub WhitespaceSplit_Basic()
            Dim pretok As New WhitespaceSplitPreTokenizer()
            Dim tests As New List(Of (String, List(Of (String, (Integer, Integer))))) From {
                ("Hey man!", New List(Of (String, (Integer, Integer))) From {("Hey", (0, 3)), ("man!", (4, 8))}),
                ("Hey, man, Good?", New List(Of (String, (Integer, Integer))) From {("Hey,", (0, 4)), ("man,", (5, 9)), ("Good?", (10, 15))})
            }
            For Each t In tests
                Dim pts As PreTokenizedString = PreTokenizedString.FromString(t.Item1)
                pretok.PreTokenize(pts)
                AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), t.Item2)
            Next
        End Sub

        ' ------------------------------------------------------------------
        ' UnicodeScripts (pre_tokenizers/unicode_scripts/pre_tokenizer.rs)
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub UnicodeScripts_Basic()
            Dim pretok As New UnicodeScriptsPreTokenizer()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("どこで生れ。Yes")
            pretok.PreTokenize(pts)
            Dim expected As New List(Of (String, (Integer, Integer))) From {
                ("どこで生れ", (0, 15)),
                ("。", (15, 18)),
                ("Yes", (18, 21))
            }
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized), expected)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), expected)
        End Sub

        <TestMethod>
        Public Sub UnicodeScripts_SpacesAreIncludedInEveryScript()
            Dim pretok As New UnicodeScriptsPreTokenizer()
            Dim pts As PreTokenizedString = PreTokenizedString.FromString("Apples are りんご 林檎")
            pretok.PreTokenize(pts)
            Dim expected As New List(Of (String, (Integer, Integer))) From {
                ("Apples are ", (0, 11)),
                ("りんご 林檎", (11, 27))
            }
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Normalized), expected)
            AssertSplits(SplitsTextOffsets(pts, OffsetReferential.Original), expected)
        End Sub

        <TestMethod>
        Public Sub UnicodeScripts_FixedScript()
            Assert.AreEqual(Script.Han, UnicodeScripts.FixedScript(AscW("京"c)))
            Assert.AreEqual(Script.Han, UnicodeScripts.FixedScript(AscW("太"c)))
            Assert.AreEqual(Script.Han, UnicodeScripts.FixedScript(AscW("い"c)))
            Assert.AreEqual(Script.Han, UnicodeScripts.FixedScript(AscW("グ"c)))
            Assert.AreEqual(Script.Han, UnicodeScripts.FixedScript(&H30FC))
            Assert.AreEqual(Script.Latin, UnicodeScripts.FixedScript(AscW("a"c)))
            Assert.AreEqual(Script.Latin, UnicodeScripts.FixedScript(AscW("A"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.FixedScript(AscW("0"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.FixedScript(AscW("$"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.FixedScript(AscW("@"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.FixedScript(AscW("-"c)))
            Assert.AreEqual(Script.Any, UnicodeScripts.FixedScript(AscW(" "c)))
        End Sub

        <TestMethod>
        Public Sub UnicodeScripts_GetScript()
            Assert.AreEqual(Script.Han, UnicodeScripts.GetScript(AscW("京"c)))
            Assert.AreEqual(Script.Han, UnicodeScripts.GetScript(AscW("太"c)))
            Assert.AreEqual(Script.Hiragana, UnicodeScripts.GetScript(AscW("い"c)))
            Assert.AreEqual(Script.Katakana, UnicodeScripts.GetScript(AscW("グ"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(&H30FC))
            Assert.AreEqual(Script.Latin, UnicodeScripts.GetScript(AscW("a"c)))
            Assert.AreEqual(Script.Latin, UnicodeScripts.GetScript(AscW("A"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(AscW("0"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(AscW("$"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(AscW("@"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(AscW("-"c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(AscW(" "c)))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(&HFFFD))

            ' Supplementary-plane ranges (5-hex-digit boundaries).
            Assert.AreEqual(Script.Han, UnicodeScripts.GetScript(&H20000))
            Assert.AreEqual(Script.Han, UnicodeScripts.GetScript(&H2A6D6))
            Assert.AreEqual(Script.Common, UnicodeScripts.GetScript(&H1F44B))
            ' Unassigned codepoints fall back to Any.
            Assert.AreEqual(Script.Any, UnicodeScripts.GetScript(&H378))
            Assert.AreEqual(Script.Any, UnicodeScripts.GetScript(&H10FFFF))
        End Sub

    End Class

End Namespace
