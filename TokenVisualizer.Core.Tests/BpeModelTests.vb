Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports Tokenizers.Internal
Imports Tokenizers.Models

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Tests for the BPE model port. Fixtures are synthetic and mirror the Rust unit tests in
    ''' models/bpe/model.rs and models/bpe/word.rs.
    ''' </summary>
    <TestClass>
    Public Class BpeModelTests

        <TestMethod>
        Public Sub TestMerge_MergesLettersOfLowest()
            ' GPT-2 style letter vocab plus the merged tokens referenced by the merge lines.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"l", 0}, {"o", 1}, {"w", 2}, {"e", 3}, {"r", 4}, {"s", 5}, {"t", 6}, {"i", 7}, {"d", 8}, {"n", 9}, {"Ġ", 10},
                {"lo", 11}, {"we", 12}, {"er", 13}, {"rs", 14}, {"st", 15}, {"ti", 16}, {"id", 17}, {"dn", 18},
                {"Ġw", 19}, {"Ġl", 20}, {"Ġlow", 21}
            }
            Dim merges As New List(Of String)() From {
                "Ġ w", "l o", "w e", "e r", "r s", "s t", "t i", "i d", "d n", "Ġ l", "Ġ low"
            }
            Dim bpe As New BpeModel(vocab, merges)

            ' merge_all over the letters of "lowest" (l,o,w,e,s,t): "l o"->lo (rank 1),
            ' "w e"->we (rank 2), "s t"->st (rank 5); the remaining merge lines reference r/i/d/n
            ' and never apply. Expected surviving ids: [lo, we, st].
            Dim symbols As New List(Of (Integer, Integer))() From {
                (0, 1), (1, 1), (2, 1), (3, 1), (5, 1), (6, 1)
            }
            Dim ids As List(Of Integer) = bpe.MergeAll(symbols)
            Assert.HasCount(3, ids, "merge_all must leave lo, we, st")
            CollectionAssert.AreEqual({11, 12, 15}, ids)

            ' The full tokenize path must produce the same ids with cumulative byte offsets.
            Dim tokens As List(Of Token) = bpe.Tokenize("lowest")
            Assert.HasCount(3, tokens)
            Assert.AreEqual(New Token(11, "lo", (0, 2)), tokens(0))
            Assert.AreEqual(New Token(12, "we", (2, 4)), tokens(1))
            Assert.AreEqual(New Token(15, "st", (4, 6)), tokens(2))
        End Sub

        <TestMethod>
        Public Sub TestMergeDeterminism_SameResultOnRepeat()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"u", 0}, {"n", 1}, {"r", 2}, {"e", 3}, {"l", 4}, {"a", 5}, {"t", 6}, {"d", 7},
                {"re", 8}, {"at", 9}, {"ed", 10}, {"un", 11}, {"ated", 12}, {"rel", 13}, {"related", 14}, {"unrelated", 15}
            }
            Dim merges As New List(Of String)() From {
                "r e", "a t", "e d", "u n", "at ed", "re l", "rel ated", "un related"
            }
            Dim bpe As New BpeModel(vocab, merges)

            Dim first As List(Of Token) = bpe.Tokenize("unrelated")
            Dim second As List(Of Token) = bpe.Tokenize("unrelated")
            Assert.HasCount(1, first)
            Assert.AreEqual(first(0), second(0), "repeat tokenization must be deterministic")
        End Sub

        <TestMethod>
        Public Sub TestUnkNotFused()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"a", 1}, {"b", 2}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)(), unkToken:="<unk>")

            Dim t1 As List(Of Token) = bpe.Tokenize("c")
            Assert.HasCount(1, t1)
            Assert.AreEqual(New Token(0, "<unk>", (0, 1)), t1(0))

            Dim t2 As List(Of Token) = bpe.Tokenize("cc")
            Assert.HasCount(2, t2)
            Assert.AreEqual(New Token(0, "<unk>", (0, 1)), t2(0))
            Assert.AreEqual(New Token(0, "<unk>", (1, 2)), t2(1))

            Dim t3 As List(Of Token) = bpe.Tokenize("accb")
            Assert.HasCount(4, t3)
            Assert.AreEqual(New Token(1, "a", (0, 1)), t3(0))
            Assert.AreEqual(New Token(0, "<unk>", (1, 2)), t3(1))
            Assert.AreEqual(New Token(0, "<unk>", (2, 3)), t3(2))
            Assert.AreEqual(New Token(2, "b", (3, 4)), t3(3))
        End Sub

        <TestMethod>
        Public Sub TestUnkGetFused()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"a", 1}, {"b", 2}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)(), unkToken:="<unk>", fuseUnk:=True)

            Dim t1 As List(Of Token) = bpe.Tokenize("c")
            Assert.HasCount(1, t1)
            Assert.AreEqual(New Token(0, "<unk>", (0, 1)), t1(0))

            Dim t2 As List(Of Token) = bpe.Tokenize("cc")
            Assert.HasCount(1, t2)
            Assert.AreEqual(New Token(0, "<unk>", (0, 2)), t2(0))

            Dim t3 As List(Of Token) = bpe.Tokenize("accb")
            Assert.HasCount(3, t3)
            Assert.AreEqual(New Token(1, "a", (0, 1)), t3(0))
            Assert.AreEqual(New Token(0, "<unk>", (1, 3)), t3(1))
            Assert.AreEqual(New Token(2, "b", (3, 4)), t3(2))
        End Sub

        <TestMethod>
        Public Sub TestTokenizeWithAndWithoutDropout()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"u", 0}, {"n", 1}, {"r", 2}, {"e", 3}, {"l", 4}, {"a", 5}, {"t", 6}, {"d", 7},
                {"re", 8}, {"at", 9}, {"ed", 10}, {"un", 11}, {"ated", 12}, {"rel", 13}, {"related", 14}, {"unrelated", 15}
            }
            Dim merges As New List(Of String)() From {
                "r e", "a t", "e d", "u n", "at ed", "re l", "rel ated", "un related"
            }

            ' No dropout: everything merges into a single token.
            Dim noDropout As New BpeModel(vocab, merges)
            Dim tokens As List(Of Token) = noDropout.Tokenize("unrelated")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(15, "unrelated", (0, 9)), tokens(0))

            ' dropout = 0.0 is equivalent to none.
            Dim zeroDropout As New BpeModel(vocab, merges, dropout:=0.0)
            tokens = zeroDropout.Tokenize("unrelated")
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(15, "unrelated", (0, 9)), tokens(0))

            ' dropout = 1.0: every merge is dropped, so the word stays as single characters.
            Dim allDrop As New BpeModel(vocab, merges, dropout:=1.0, seededRandom:=New Random(42))
            tokens = allDrop.Tokenize("unrelated")
            Assert.HasCount(9, tokens)
            Dim expectedIds As Integer() = {0, 1, 2, 3, 4, 5, 6, 3, 7}
            Dim expectedValues As String() = {"u", "n", "r", "e", "l", "a", "t", "e", "d"}
            For i As Integer = 0 To 8
                Assert.AreEqual(New Token(expectedIds(i), expectedValues(i), (i, i + 1)), tokens(i), $"token[{i}]")
            Next

            ' dropout = 0.5: between 1 and 9 tokens.
            Dim halfDrop As New BpeModel(vocab, merges, dropout:=0.5, seededRandom:=New Random(1234))
            tokens = halfDrop.Tokenize("unrelated")
            Assert.IsTrue(tokens.Count >= 1 AndAlso tokens.Count <= 9,
                          $"dropout=0.5 must yield between 1 and 9 tokens, got {tokens.Count}")
        End Sub

        <TestMethod>
        Public Sub TestBpeByteFallback()
            ' 0x61 == 'a'
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"<0x61>", 1}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)(), unkToken:="<unk>", byteFallback:=True)

            Dim t1 As List(Of Token) = bpe.Tokenize("c")
            Assert.HasCount(1, t1)
            Assert.AreEqual(New Token(0, "<unk>", (0, 1)), t1(0))

            Dim t2 As List(Of Token) = bpe.Tokenize("a")
            Assert.HasCount(1, t2)
            Assert.AreEqual(New Token(1, "<0x61>", (0, 1)), t2(0))
        End Sub

        <TestMethod>
        Public Sub TestBpeByteFallbackNewline()
            ' 0x0A == '\n'
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<unk>", 0}, {"<0x0A>", 1}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)(), unkToken:="<unk>", byteFallback:=True)

            Dim tokens As List(Of Token) = bpe.Tokenize(vbLf)
            Assert.HasCount(1, tokens)
            Assert.AreEqual(New Token(1, "<0x0A>", (0, 1)), tokens(0))
        End Sub

        <TestMethod>
        Public Sub TestBpeByteFallback_MultiByteCharSplitsIntoByteTokens()
            ' 'é' is 2 UTF-8 bytes (0xC3 0xA9); with both byte tokens in the vocab it becomes
            ' two 1-byte symbols, so the offset lands on the raw char's byte boundary.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"<0xC3>", 0}, {"<0xA9>", 1}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)(), byteFallback:=True)

            Dim tokens As List(Of Token) = bpe.Tokenize("é")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(0, "<0xC3>", (0, 1)), tokens(0))
            Assert.AreEqual(New Token(1, "<0xA9>", (1, 2)), tokens(1))
        End Sub

        <TestMethod>
        Public Sub TestBpeWithContinuingSubwordPrefix()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"##b", 1}, {"##c", 2}, {"ab", 3}, {"abc", 4}
            }
            Dim merges As New List(Of String)() From {
                "a ##b", "ab ##c"
            }
            Dim bpe As New BpeModel(vocab, merges, unkToken:="[UNK]", continuingSubwordPrefix:="##")

            Dim t1 As List(Of Token) = bpe.Tokenize("ab")
            Assert.HasCount(1, t1)
            Assert.AreEqual(New Token(3, "ab", (0, 2)), t1(0))

            Dim t2 As List(Of Token) = bpe.Tokenize("abc")
            Assert.HasCount(1, t2)
            Assert.AreEqual(New Token(4, "abc", (0, 3)), t2(0))
        End Sub

        <TestMethod>
        Public Sub TestIgnoreMerges()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {".:.:", 0}, {"Ġbelirtilen", 1}, {".", 2}, {":", 3},
                {"bel", 4}, {"irtilen", 5}, {"Ġ", 6}, {".:", 7}, {"belirtilen", 8}, {".:.", 9},
                {"be", 10}, {"l", 11}, {"ir", 12}, {"ti", 13}, {"en", 14}, {"irtil", 15},
                {"irti", 16}, {"i", 17}, {"r", 18}, {"t", 19}, {"b", 20}, {"e", 21}, {"n", 22}
            }
            Dim merges As New List(Of String)() From {
                ". :", "b e", "be l", "i r", "t i", "ir ti", "e n", "irti l"
            }

            ' With ignore_merges: whole words in the vocab are emitted directly.
            Dim bpe As New BpeModel(vocab, merges, ignoreMerges:=True)

            Dim t1 As List(Of Token) = bpe.Tokenize(".:.:")
            Assert.HasCount(1, t1)
            Assert.AreEqual(New Token(0, ".:.:", (0, 4)), t1(0))

            Dim t2 As List(Of Token) = bpe.Tokenize("Ġbelirtilen")
            Assert.HasCount(1, t2)
            Assert.AreEqual(New Token(1, "Ġbelirtilen", (0, 12)), t2(0))

            ' Without ignore_merges the same words are merged.
            Dim bpe2 As New BpeModel(vocab, merges, ignoreMerges:=False)

            Dim t3 As List(Of Token) = bpe2.Tokenize(".:.:")
            Assert.HasCount(2, t3)
            Assert.AreEqual(New Token(7, ".:", (0, 2)), t3(0))
            Assert.AreEqual(New Token(7, ".:", (2, 4)), t3(1))

            Dim t4 As List(Of Token) = bpe2.Tokenize("Ġbelirtilen")
            Assert.HasCount(4, t4)
            Assert.AreEqual(New Token(6, "Ġ", (0, 2)), t4(0))
            Assert.AreEqual(New Token(4, "bel", (2, 5)), t4(1))
            Assert.AreEqual(New Token(15, "irtil", (5, 10)), t4(2))
            Assert.AreEqual(New Token(14, "en", (10, 12)), t4(3))
        End Sub

        <TestMethod>
        Public Sub TestByteLevelIntegration_Gpt2Style()
            ' Byte-encode pre-tokens through the GPT-2 byte-to-char table:
            ' "Hello" stays "Hello", " my" becomes "Ġmy".
            Dim byteToChar As IReadOnlyDictionary(Of Byte, Char) = BytesToUnicodeTable.GetBytesToChar()

            Dim helloBytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes("Hello")
            Dim hello As String = New String(helloBytes.Select(Function(b) byteToChar(b)).ToArray())
            Assert.AreEqual("Hello", hello)

            Dim myBytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(" my")
            Dim gpt2My As String = New String(myBytes.Select(Function(b) byteToChar(b)).ToArray())
            Assert.AreEqual("Ġmy", gpt2My)

            ' Small byte-level vocab/merges.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"H", 0}, {"e", 1}, {"l", 2}, {"o", 3},
                {"He", 4}, {"Hel", 5}, {"Hell", 6}, {"Hello", 7},
                {"Ġ", 8}, {"m", 9}, {"y", 10}, {"Ġm", 11}, {"Ġmy", 12}
            }
            Dim merges As New List(Of String)() From {
                "H e", "He l", "Hel l", "Hell o", "Ġ m", "Ġm y"
            }
            Dim bpe As New BpeModel(vocab, merges)

            Dim t1 As List(Of Token) = bpe.Tokenize(hello)
            Assert.HasCount(1, t1)
            Assert.AreEqual(New Token(7, "Hello", (0, 5)), t1(0))

            Dim t2 As List(Of Token) = bpe.Tokenize(gpt2My)
            Assert.HasCount(1, t2)
            Assert.AreEqual(New Token(12, "Ġmy", (0, 4)), t2(0))
        End Sub

        <TestMethod>
        Public Sub TestStaleEntryGuard_PreventsDoubleMerge()
            ' "a bb" (rank 3) is queued after "b b" merges, but by the time it pops, the pair at
            ' position 0 is (a, bbc), not (a, bb). The stale-entry guard must skip it; without
            ' the guard a+bbc would be merged using the wrong replacement id (abb).
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"b", 1}, {"c", 2}, {"bb", 3}, {"bbc", 4}, {"ab", 5}, {"abb", 6}
            }
            Dim merges As New List(Of String)() From {
                "b b", "bb c", "a b", "a bb"
            }
            Dim bpe As New BpeModel(vocab, merges)

            Dim tokens As List(Of Token) = bpe.Tokenize("abbc")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(0, "a", (0, 1)), tokens(0))
            Assert.AreEqual(New Token(4, "bbc", (1, 4)), tokens(1))
        End Sub

        <TestMethod>
        Public Sub TestCacheBestEffort_CacheOnOffIdentical()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"u", 0}, {"n", 1}, {"r", 2}, {"e", 3}, {"l", 4}, {"a", 5}, {"t", 6}, {"d", 7},
                {"re", 8}, {"at", 9}, {"ed", 10}, {"un", 11}, {"ated", 12}, {"rel", 13}, {"related", 14}, {"unrelated", 15}
            }
            Dim merges As New List(Of String)() From {
                "r e", "a t", "e d", "u n", "at ed", "re l", "rel ated", "un related"
            }

            Dim withCache As New BpeModel(vocab, merges, cacheCapacity:=10000)
            Dim withoutCache As New BpeModel(vocab, merges, cacheCapacity:=0)

            For Each word As String In {"unrelated", "un", "ated", "rel"}
                Dim a As List(Of Token) = withCache.Tokenize(word)
                Dim b As List(Of Token) = withoutCache.Tokenize(word)
                Assert.HasCount(a.Count, b, $"token count for '{word}'")
                For i As Integer = 0 To a.Count - 1
                    Assert.AreEqual(a(i), b(i), $"token[{i}] for '{word}'")
                Next
            Next
        End Sub

        <TestMethod>
        Public Sub TestUnknownCharWithoutUnkTokenIsDropped()
            ' Rust merge_word silently omits a char that is not in the vocab when there is no
            ' unk_token and no byteFallback. The token offsets therefore compress past it.
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"b", 1}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)())

            Dim tokens As List(Of Token) = bpe.Tokenize("acb")
            Assert.HasCount(2, tokens)
            Assert.AreEqual(New Token(0, "a", (0, 1)), tokens(0))
            Assert.AreEqual(New Token(1, "b", (1, 2)), tokens(1))
        End Sub

        <TestMethod>
        Public Sub TestAccessors_VocabSizeTokenToIdIdToToken()
            Dim vocab As New Dictionary(Of String, Integer)() From {
                {"a", 0}, {"b", 1}, {"ab", 2}
            }
            Dim bpe As New BpeModel(vocab, New List(Of String)() From {"a b"})

            Assert.AreEqual(3, bpe.VocabSize)
            Assert.AreEqual(0, bpe.TokenToId("a"))
            Assert.AreEqual(1, bpe.TokenToId("b"))
            Assert.AreEqual(2, bpe.TokenToId("ab"))
            Assert.IsFalse(bpe.TokenToId("z").HasValue)
            Assert.AreEqual("a", bpe.IdToToken(0))
            Assert.AreEqual("ab", bpe.IdToToken(2))
            Assert.IsNull(bpe.IdToToken(999))
        End Sub

    End Class

End Namespace
