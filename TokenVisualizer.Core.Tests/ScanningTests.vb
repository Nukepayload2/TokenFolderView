Imports System.Buffers
Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports Tokenizers.Scanning

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' In-memory tests for the folder scanner: filter decisions, binary detection and pure tree
    ''' construction. No file I/O is performed.
    ''' </summary>
    <TestClass>
    Public Class ScanFilterTests

        <TestMethod>
        Public Sub BlacklistIsCaseInsensitive()
            Dim blacklist As IReadOnlyList(Of String) = New List(Of String) From {"bin", "OBJ", "node_modules"}
            Assert.IsTrue(ScanFilter.ShouldSkipFolder("Bin", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipFolder("obj", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipFolder("NODE_MODULES", blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipFolder("src", blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipFolder("binary", blacklist))
        End Sub

        <TestMethod>
        Public Sub BlacklistMatchesNameAtAnyDepth()
            Dim blacklist As IReadOnlyList(Of String) = ScanOptions.[Default].FolderBlacklist
            Assert.IsTrue(ScanFilter.ShouldSkipFolder("bin", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipFolder(".git", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipFolder("node_modules", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipFolder("target", blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipFolder("mytarget", blacklist))
        End Sub

        <TestMethod>
        Public Sub SizeThresholdBoundaryIsInclusive()
            Dim max As Long = 10 * 1024 * 1024
            Assert.IsFalse(ScanFilter.ShouldSkipFileSize(max, max), "== max must not be skipped")
            Assert.IsTrue(ScanFilter.ShouldSkipFileSize(max + 1, max), "> max must be skipped")
            Assert.IsFalse(ScanFilter.ShouldSkipFileSize(0, max))
        End Sub

        <TestMethod>
        Public Sub ShouldSkipFileChecksPathSegmentsAndSize()
            Dim options As New ScanOptions()
            Assert.IsTrue(ScanFilter.ShouldSkipFile("src/bin/gen.cs", 100, options))
            Assert.IsTrue(ScanFilter.ShouldSkipFile("src\obj\gen.cs", 100, options))
            Assert.IsTrue(ScanFilter.ShouldSkipFile("a/.git/config", 100, options))
            Assert.IsTrue(ScanFilter.ShouldSkipFile("node_modules/pkg/index.js", 100, options))
            Assert.IsTrue(ScanFilter.ShouldSkipFile("gen.cs", 20 * 1024 * 1024, options))
            Assert.IsFalse(ScanFilter.ShouldSkipFile("src/gen.cs", 100, options))
            Assert.IsFalse(ScanFilter.ShouldSkipFile("binaries/b.dat", 100, options), "folder 'binaries' must not match 'bin'")
        End Sub

        <TestMethod>
        Public Sub ShouldSkipPathChecksEverySegmentIncludingTheLast()
            Dim blacklist As IReadOnlyList(Of String) = New List(Of String) From {"bin", "OBJ", ".git"}
            Assert.IsTrue(ScanFilter.ShouldSkipPath("bin", blacklist), "a single segment is a folder name for a path")
            Assert.IsTrue(ScanFilter.ShouldSkipPath("src/bin", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipPath("src\bin", blacklist), "backslashes separate segments too")
            Assert.IsTrue(ScanFilter.ShouldSkipPath("src/.git/objects", blacklist))
            Assert.IsTrue(ScanFilter.ShouldSkipPath("src/OBJ/debug", blacklist), "case-insensitive")
            Assert.IsFalse(ScanFilter.ShouldSkipPath("src", blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipPath("binaries", blacklist), "a name is matched whole")
        End Sub

        <TestMethod>
        Public Sub ShouldSkipPathSparsNothingUnlikeShouldSkipFile()
            ' ShouldSkipFile treats the last segment as the file name and spares it.
            Dim options As New ScanOptions()
            Assert.IsFalse(ScanFilter.ShouldSkipFile("bin", 100, options))
            Assert.IsTrue(ScanFilter.ShouldSkipPath("bin", options.FolderBlacklist))
        End Sub

        <TestMethod>
        Public Sub ShouldSkipPathHandlesEmptyPathsAndBlacklists()
            Dim blacklist As IReadOnlyList(Of String) = New List(Of String) From {"bin"}
            Assert.IsFalse(ScanFilter.ShouldSkipPath("", blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipPath(Nothing, blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipPath("//", blacklist))
            Assert.IsFalse(ScanFilter.ShouldSkipPath("src/bin", New List(Of String)()))
            Assert.IsFalse(ScanFilter.ShouldSkipPath("src/bin", Nothing))
        End Sub

        <TestMethod>
        Public Sub DefaultOptionsCarryExpectedValues()
            Dim options As ScanOptions = ScanOptions.[Default]
            Assert.IsNotNull(options)
            CollectionAssert.Contains(options.FolderBlacklist, "bin")
            CollectionAssert.Contains(options.FolderBlacklist, "obj")
            CollectionAssert.Contains(options.FolderBlacklist, "node_modules")
            CollectionAssert.Contains(options.FolderBlacklist, ".vs")
            CollectionAssert.Contains(options.FolderBlacklist, ".git")
            CollectionAssert.Contains(options.FolderBlacklist, "dist")
            CollectionAssert.Contains(options.FolderBlacklist, "target")
            Assert.AreEqual(10 * 1024 * 1024L, options.MaxFileSizeBytes)
            Assert.IsTrue(options.CheckBinary)
        End Sub

    End Class

    <TestClass>
    Public Class BinaryDetectorTests

        <TestMethod>
        Public Sub AsciiIsText()
            Dim bytes As Byte() = Encoding.UTF8.GetBytes("hello world")
            Assert.IsFalse(BinaryDetector.IsBinary(bytes))
        End Sub

        <TestMethod>
        Public Sub ValidUtf8IsText()
            Dim bytes As Byte() = Encoding.UTF8.GetBytes("héllo wörld 日本語")
            Assert.IsFalse(BinaryDetector.IsBinary(bytes))
        End Sub

        <TestMethod>
        Public Sub InvalidByteSequencesAreBinary()
            Assert.IsTrue(BinaryDetector.IsBinary(New Byte() {&HFF, &HFE}), "UTF-16 BOM pattern must be binary")
            Assert.IsTrue(BinaryDetector.IsBinary(New Byte() {&HC0, &HAF}), "overlong encoding must be binary")
            Assert.IsTrue(BinaryDetector.IsBinary(New Byte() {AscW("a"c), &HC0}), "trailing overlong lead byte must be binary")
        End Sub

        <TestMethod>
        Public Sub EmbeddedNulIsStillText()
            Dim bytes As New List(Of Byte)()
            bytes.AddRange(Encoding.UTF8.GetBytes("a"))
            bytes.Add(0)
            bytes.AddRange(Encoding.UTF8.GetBytes("b"))
            Assert.IsFalse(BinaryDetector.IsBinary(bytes.ToArray()))
        End Sub

        <TestMethod>
        Public Sub EmptyIsText()
            Assert.IsFalse(BinaryDetector.IsBinary(Array.Empty(Of Byte)()))
        End Sub

        <TestMethod>
        Public Sub TruncatedTrailingMultibyteAtBoundaryIsNotBinary()
            ' 4095 ASCII bytes followed by the first byte of a 3-byte UTF-8 sequence (&HE4).
            ' With flush:=False the incomplete trailing sequence is buffered, not rejected.
            Dim bytes(4095) As Byte
            For i As Integer = 0 To 4094
                bytes(i) = AscW("a"c)
            Next
            bytes(4095) = &HE4
            Assert.IsFalse(BinaryDetector.IsBinary(bytes))
        End Sub

        <TestMethod>
        Public Sub MatchesStrictFlushFalseDecoder()
            ' The Rune-based detector must agree with the previous strict flush:=False decoder on
            ' every input: exhaustive 1- and 2-byte inputs, a targeted 3-byte sample (overlong /
            ' surrogate / truncated leading bytes) and random buffers with a deterministic seed.
            Dim cases As New List(Of Byte())()
            For b As Integer = 0 To 255
                cases.Add(New Byte() {CByte(b)})
            Next
            For a As Integer = 0 To 255
                For b As Integer = 0 To 255
                    cases.Add(New Byte() {CByte(a), CByte(b)})
                Next
            Next
            Dim lead3() As Integer = {&H80, &HC0, &HC2, &HE0, &HED, &HEE, &HFF, &HF0, &HF4, &HF5}
            Dim third() As Integer = {&H00, &H41, &H80, &H90, &HA0, &HBF, &HC0}
            For Each l3 In lead3
                For c2 As Integer = 0 To 255
                    For Each t3 In third
                        cases.Add(New Byte() {CByte(l3), CByte(c2), CByte(t3)})
                    Next
                Next
            Next

            Dim rng As New Random(12345)
            For i As Integer = 0 To 20000
                Dim buf(7) As Byte
                rng.NextBytes(buf)
                cases.Add(buf)
            Next

            For Each c As Byte() In cases
                Assert.AreEqual(StrictFlushFalseIsBinary(c), BinaryDetector.IsBinary(c),
                                $"divergence on bytes {BitConverter.ToString(c)}")
            Next
        End Sub

        ''' <summary>Reference: the previous decoder-based implementation (strict, flush:=False).</summary>
        Private Shared Function StrictFlushFalseIsBinary(bytes As Byte()) As Boolean
            If bytes.Length = 0 Then Return False
            Dim decoder As Decoder = New UTF8Encoding(False, True).GetDecoder()
            Dim charCount As Integer = bytes.Length * 4
            Dim charBuffer As Char() = ArrayPool(Of Char).Shared.Rent(charCount)
            Try
                Try
                    Dim bytesUsed As Integer
                    Dim charsUsed As Integer
                    Dim completed As Boolean
                    decoder.Convert(bytes.AsSpan(), charBuffer.AsSpan(0, charCount), False, bytesUsed, charsUsed, completed)
                    Return False
                Catch ex As DecoderFallbackException
                    Return True
                End Try
            Finally
                ArrayPool(Of Char).Shared.Return(charBuffer)
            End Try
        End Function

    End Class

    <TestClass>
    Public Class BuildTreeTests

        <TestMethod>
        Public Sub RootAggregatesSumOfDescendants()
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))() From {
                ("a.txt", 10L, 5),
                ("sub/b.txt", 20L, 7),
                ("sub/deep/c.txt", 30L, 9)
            }

            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)

            Assert.IsTrue(root.IsDirectory)
            Assert.AreEqual("root", root.Name)
            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(21L, root.TokenCount)
            Assert.AreEqual(60L, root.FileSize)

            ' Nested directory aggregates.
            Dim subDir As ScanTreeNode = root.Children.First(Function(c) c.IsDirectory AndAlso c.Name = "sub")
            Assert.IsNotNull(subDir)
            Assert.AreEqual(2L, subDir.FileCount)
            Assert.AreEqual(16L, subDir.TokenCount)
            Assert.AreEqual(50L, subDir.FileSize)

            Dim deepDir As ScanTreeNode = subDir.Children.First(Function(c) c.IsDirectory AndAlso c.Name = "deep")
            Assert.AreEqual(1L, deepDir.FileCount)
            Assert.AreEqual(9L, deepDir.TokenCount)
            Assert.AreEqual(30L, deepDir.FileSize)
        End Sub

        <TestMethod>
        Public Sub FilesAtRootAndNestedDirs()
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))() From {
                ("root.txt", 1L, 1),
                ("dir1/inner.txt", 2L, 2),
                ("dir2/x.txt", 3L, 3)
            }

            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)

            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(6L, root.TokenCount)
            Assert.AreEqual(6L, root.FileSize)
            Assert.HasCount(3, root.Children)

            Dim rootFile As ScanTreeNode = root.Children.First(Function(c) Not c.IsDirectory AndAlso c.Name = "root.txt")
            Assert.IsNotNull(rootFile)
            Assert.AreEqual(1L, rootFile.FileCount)
            Assert.AreEqual(1L, rootFile.TokenCount)
            Assert.AreEqual(1L, rootFile.FileSize)
        End Sub

        <TestMethod>
        Public Sub CountTextFormatsWithGroupSeparators()
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))() From {
                ("a.txt", 1L, 1234)
            }

            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)

            Assert.AreEqual("1,234", root.CountText)
            Assert.AreEqual("1,234", root.Children(0).CountText)
        End Sub

        <TestMethod>
        Public Sub ChildrenAreSortedDeterministically()
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))() From {
                ("z.txt", 1L, 1),
                ("a.txt", 1L, 2),
                ("m.txt", 1L, 3)
            }

            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)

            Assert.AreEqual("a.txt", root.Children(0).Name)
            Assert.AreEqual("m.txt", root.Children(1).Name)
            Assert.AreEqual("z.txt", root.Children(2).Name)
        End Sub

        <TestMethod>
        Public Sub EmptyFileListProducesEmptyRoot()
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))()

            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)

            Assert.IsTrue(root.IsDirectory)
            Assert.AreEqual(0L, root.FileCount)
            Assert.AreEqual(0L, root.TokenCount)
            Assert.AreEqual(0L, root.FileSize)
            Assert.HasCount(0, root.Children)
        End Sub

        <TestMethod>
        Public Sub DirectoryAndFileSiblingOrderIsByName()
            ' 'dir.txt' (file) sorts before 'folder' (directory) under ordinal comparison,
            ' demonstrating children are sorted purely by name.
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))() From {
                ("folder/a.txt", 1L, 1),
                ("dir.txt", 1L, 2)
            }

            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)

            Assert.AreEqual("dir.txt", root.Children(0).Name)
            Assert.AreEqual("folder", root.Children(1).Name)
        End Sub

    End Class

    <TestClass>
    Public Class ScanTreeNodeTests

        <TestMethod>
        Public Sub ChildrenNotifyCollectionChangedOnAddAndRemove()
            Dim root As New ScanTreeNode("root", "C:\root", True)
            Dim actions As New List(Of NotifyCollectionChangedAction)()
            AddHandler root.Children.CollectionChanged,
                Sub(sender As Object, e As NotifyCollectionChangedEventArgs) actions.Add(e.Action)

            Dim fileNode As New ScanTreeNode("a.txt", "C:\root\a.txt", False)
            root.AddChild(fileNode)
            Assert.HasCount(1, actions)
            Assert.AreEqual(NotifyCollectionChangedAction.Add, actions(0))
            Assert.AreEqual(0, root.Children.IndexOf(fileNode))

            root.Children.RemoveAt(0)
            Assert.HasCount(2, actions)
            Assert.AreEqual(NotifyCollectionChangedAction.Remove, actions(1), "removing must not raise a Reset")
            Assert.HasCount(0, root.Children)
        End Sub

        <TestMethod>
        Public Sub BuildTreeOutputIsStableUnderReaggregation()
            Dim files As New List(Of (relativePath As String, length As Long, tokenCount As Integer))() From {
                ("a.txt", 10L, 5),
                ("sub/b.txt", 20L, 7),
                ("sub/deep/c.txt", 30L, 9)
            }
            Dim root As ScanTreeNode = FolderScanner.BuildTree("root", "C:\root", files)
            Dim subDir As ScanTreeNode = root.Children.First(Function(c) c.IsDirectory)

            ' BuildTree already aggregates, so folding again must not move a single number.
            ScanTreeEditor.Reaggregate(root)
            Assert.AreEqual(21L, root.TokenCount)
            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(60L, root.FileSize)
            Assert.AreEqual(16L, subDir.TokenCount)

            ScanTreeEditor.Reaggregate(root)
            Assert.AreEqual(21L, root.TokenCount)
            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(60L, root.FileSize)
            Assert.AreEqual(16L, subDir.TokenCount)
        End Sub

    End Class

    ''' <summary>
    ''' Gate for the "保留缓存" setting: the shared L2 BPE word cache must survive the end of a
    ''' whole-tree scan only when the scan's <see cref="ScanOptions"/> snapshot asks for it, and must
    ''' be dropped (default) so the memory a big dictionary's cache held is released.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class WordCacheRetentionTests

        ''' <summary>
        ''' Common English and Chinese vocabulary, long enough that a scan of it merges many distinct
        ''' words and therefore has to fill the shared cache.
        ''' </summary>
        Private Const SampleText As String =
            "The quick brown fox jumps over the lazy dog. " &
            "Tokenization is the process of splitting text into the units a model can read. " &
            "A large dictionary keeps many words warm, while a small one forgets them quickly. " &
            "These settings control the scanner, the folder blacklist and the maximum file size. " &
            "Reading and writing files, counting tokens per second, measuring megabytes per second. " &
            "缓存保留时，下一次扫描可以直接复用已经合并好的词，因此更快。" &
            "不保留缓存时，扫描结束就释放内存，把词缓存交还给运行时。" &
            "你好世界，这是一段用于扫描测试的中文文本，包含常见词语：机器、学习、分词、设置、文件夹。" &
            vbCrLf

        Private _tempDir As String

        <TestCleanup>
        Public Sub RemoveScanFolder()
            ' Leftover temp files are never a reason to fail a test, so a failed delete is ignored.
            Try
                If _tempDir IsNot Nothing AndAlso Directory.Exists(_tempDir) Then
                    Directory.Delete(_tempDir, True)
                End If
            Catch
            End Try
        End Sub

        <TestMethod>
        Public Sub RetainWordCacheIsOffInBothOptionFactories()
            Dim fresh As New ScanOptions()
            Assert.IsFalse(fresh.RetainWordCache, "New ScanOptions must not retain the shared word cache")
            Assert.IsFalse(ScanOptions.[Default].RetainWordCache, "ScanOptions.Default must carry the same default as New()")
        End Sub

        <TestMethod>
        Public Async Function ScanDropsSharedWordCacheUnlessItIsRetained() As Task
            If Not File.Exists(BundledTokenizerPath) Then
                Assert.Fail("deepseek-v4-flash/tokenizer.json was not copied to the test output directory; check the Content item in TokenVisualizer.Core.Tests.vbproj")
                Return
            End If
            Dim scanDir As String = CreateScanFolder()

            ' Both passes share one tokenizer on purpose, and the retaining pass runs first: the
            ' shared cache belongs to the model instance, so the first pass proves this corpus really
            ' fills this instance's L2 and the second pass can only read 0 because the scan cleared it
            ' (a cold instance would make the 0 assertion pass vacuously). No other test can warm it
            ' in between, because no other test holds this instance.
            Dim tokenizer As Tokenizers.Tokenizer = Tokenizers.Tokenizer.FromFile(BundledTokenizerPath)
            Dim bpe As Tokenizers.Models.BpeModel = DirectCast(tokenizer.Model, Tokenizers.Models.BpeModel)

            Dim retainedOptions As New ScanOptions() With {.RetainWordCache = True}
            Dim retainedRoot As ScanTreeNode = Await New FolderScanner(tokenizer, retainedOptions).ScanAsync(scanDir)
            Assert.AreEqual(1L, retainedRoot.FileCount, "the sample file must be counted, not filtered away")
            Assert.IsGreaterThan(0L, retainedRoot.TokenCount, "the sample file must produce tokens")
            Dim warmed As Integer = bpe.SharedWordCacheEntryCount
            Assert.IsGreaterThan(0, warmed, $"RetainWordCache=True must leave the shared word cache filled, got {warmed}")

            Dim droppingOptions As New ScanOptions() With {.RetainWordCache = False}
            Dim droppedRoot As ScanTreeNode = Await New FolderScanner(tokenizer, droppingOptions).ScanAsync(scanDir)
            Assert.AreEqual(1L, droppedRoot.FileCount, "the second scan must see the same file")
            Assert.IsGreaterThan(0L, droppedRoot.TokenCount, "the second scan must count tokens")
            Assert.AreEqual(0, bpe.SharedWordCacheEntryCount,
                            $"RetainWordCache=False must drop the shared word cache, which held {warmed} entries before the scan")
        End Function

        ''' <summary>Creates a uniquely named folder under the system temp path holding one text file.</summary>
        Private Function CreateScanFolder() As String
            Dim dir As String = Path.Combine(Path.GetTempPath(), "tokenfolderview-wordcache-" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(dir)
            _tempDir = dir
            File.WriteAllText(Path.Combine(dir, "sample.txt"), SampleText & SampleText)
            Return dir
        End Function

    End Class

    ''' <summary>
    ''' Gate for the "文件编码" setting on the pure side: what a fresh <see cref="ScanOptions"/> reads
    ''' files as, which branch <see cref="FileEncoding.Decode"/> takes, and what
    ''' <see cref="FileEncoding.Resolve"/> hands out. The "other encoding" is always UTF-16 or UTF-32 -
    ''' encodings that exist on every platform - so no expectation here depends on this machine's ANSI
    ''' code page.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class FileEncodingChoiceTests

        <TestMethod>
        Public Sub TextEncodingDefaultsToSharedUtf8InBothOptionFactories()
            Dim fresh As New ScanOptions()
            Assert.AreSame(Global.System.Text.Encoding.UTF8, fresh.TextEncoding, "New ScanOptions must read files as UTF-8")
            Assert.AreSame(Global.System.Text.Encoding.UTF8, ScanOptions.[Default].TextEncoding,
                           "ScanOptions.Default must carry the same default as New()")
        End Sub

        <TestMethod>
        Public Sub DecodeWithoutABomFollowsTheChosenEncoding()
            Dim utf16 As Global.System.Text.Encoding = Global.System.Text.Encoding.Unicode
            Dim text As String = "词元 hi"
            Dim utf16Bytes As Byte() = utf16.GetBytes(text)

            Assert.AreEqual(text, FileEncoding.Decode(utf16Bytes, utf16Bytes.Length, utf16),
                            "without a UTF-8 BOM the chosen encoding decides everything (D4)")
            Assert.AreNotEqual(Global.System.Text.Encoding.UTF8.GetString(utf16Bytes, 0, utf16Bytes.Length),
                               FileEncoding.Decode(utf16Bytes, utf16Bytes.Length, utf16),
                               "the UTF-8 branch must not have been taken")
        End Sub

        <TestMethod>
        Public Sub Utf8BomIsDecodedAsUtf8AndKeepsItsBomCharacter()
            Dim text As String = "词元 hi"
            Dim bomFile As Byte() = Concat(New Byte() {&HEF, &HBB, &HBF}, Global.System.Text.Encoding.UTF8.GetBytes(text))
            Dim expected As String = ChrW(&HFEFF) & text

            ' The BOM is the one content-based exception, and it wins over any chosen encoding...
            Assert.AreEqual(expected,
                            FileEncoding.Decode(bomFile, bomFile.Length, Global.System.Text.Encoding.Unicode),
                            "a UTF-8 BOM head must decode as UTF-8 even when UTF-16 was chosen")
            Assert.AreEqual(expected,
                            FileEncoding.Decode(bomFile, bomFile.Length, Global.System.Text.Encoding.UTF32),
                            "same for UTF-32: the exception is not tied to one encoding")
            ' ...and the U+FEFF it produces stays in the string, because dropping it would move counts.
            Assert.AreEqual(Convert.ToChar(&HFEFF),
                            FileEncoding.Decode(bomFile, bomFile.Length, Global.System.Text.Encoding.UTF32)(0),
                            "the BOM must survive as a character, not be stripped")

            Dim utf8Bytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(text & vbCrLf)
            Assert.AreEqual(Global.System.Text.Encoding.UTF8.GetString(utf8Bytes, 0, utf8Bytes.Length),
                            FileEncoding.Decode(utf8Bytes, utf8Bytes.Length, Global.System.Text.Encoding.UTF8),
                            "the default path must return exactly what Encoding.UTF8.GetString returns")
        End Sub

        <TestMethod>
        Public Sub ResolveOnlyLeavesUtf8ForTheAnsiChoice()
            Assert.AreSame(Global.System.Text.Encoding.UTF8, FileEncoding.Resolve("UTF-8"),
                           "the default choice must be the shared UTF-8 instance itself")
            Assert.AreSame(Global.System.Text.Encoding.UTF8, FileEncoding.Resolve(Nothing),
                           "a missing choice must mean the documented default")
            Assert.AreSame(Global.System.Text.Encoding.UTF8, FileEncoding.Resolve("乱写的值"),
                           "an unrecognised choice must mean the documented default")

            Dim ansi As Global.System.Text.Encoding = FileEncoding.Resolve("ANSI")
            Assert.AreSame(ansi, FileEncoding.Resolve("ANSI"), "ANSI must be resolved once and then cached")

            ' Both admissible outcomes of D1 pass, whichever one this machine offers: follow the system
            ' ANSI code page, or fall back to UTF-8 when there is no code page to follow.
            Dim ansiPage As Integer = TryReadSystemAnsiCodePage()
            If ansiPage > 0 AndAlso ansi IsNot Global.System.Text.Encoding.UTF8 Then
                Assert.AreEqual(ansiPage, ansi.CodePage, $"ANSI must follow the system ANSI code page CP{ansiPage} (D1)")
            Else
                Assert.AreSame(Global.System.Text.Encoding.UTF8, ansi,
                               "ANSI must fall back to UTF-8 when no ANSI code page can be used (D1)")
            End If
        End Sub

        ''' <summary>The system ANSI code page, or 0 where the platform reports none / cannot say.</summary>
        Private Shared Function TryReadSystemAnsiCodePage() As Integer
            Try
                Return Global.System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage
            Catch ex As Exception
                Return 0
            End Try
        End Function

        ''' <summary>Returns <paramref name="head"/> followed by <paramref name="tail"/>.</summary>
        Private Shared Function Concat(head As Byte(), tail As Byte()) As Byte()
            Dim result(head.Length + tail.Length - 1) As Byte
            Buffer.BlockCopy(head, 0, result, 0, head.Length)
            Buffer.BlockCopy(tail, 0, result, head.Length, tail.Length)
            Return result
        End Function

    End Class

    ''' <summary>
    ''' Verdict matrix for the encoding-aware <see cref="BinaryDetector"/> overload (D2). Everything is
    ''' measured against an explicit CP936 rather than the machine's ANSI setting, so the expected
    ''' answers are the same on every platform.
    ''' </summary>
    <TestClass>
    Public NotInheritable Class BinaryDetectorEncodingTests

        <TestMethod>
        Public Sub GbkChineseIsTextUnderGbkAndStillBinaryUnderUtf8()
            Dim gbk As Global.System.Text.Encoding = Cp936()
            Dim bytes As Byte() = gbk.GetBytes("缓存保留时，下一次扫描可以直接复用已经合并好的词，因此更快。")

            Assert.IsFalse(BinaryDetector.IsBinary(bytes, gbk), "GBK text must be text when GBK is chosen")
            Assert.IsTrue(BinaryDetector.IsBinary(bytes, Global.System.Text.Encoding.UTF8),
                          "the same bytes must stay invalid UTF-8 under the default choice")
            Assert.IsTrue(BinaryDetector.IsBinary(bytes), "the UTF-8 overload must answer through the Rune loop")
        End Sub

        <TestMethod>
        Public Sub PeStyleHeadIsBinaryEvenUnderGbk()
            Dim gbk As Global.System.Text.Encoding = Cp936()
            ' A PE header opening: 90 is a CP936 lead byte and the NUL behind it is no trail byte, plus
            ' FF, which is not a lead byte at all.
            Dim peHead As Byte() = {&H4D, &H5A, &H90, &H00, &H03, &H00, &H04, &H00, &HFF, &HFF, &HB8, &H00, &H00}

            Assert.IsTrue(BinaryDetector.IsBinary(peHead, gbk), "a lead byte followed by NUL must not decode")
        End Sub

        <TestMethod>
        Public Sub GbkPairSplitAtTheHeadBoundaryIsStillText()
            Dim gbk As Global.System.Text.Encoding = Cp936()
            Dim pair As Byte() = gbk.GetBytes("词")

            ' The probe must not flush, or every 4 KiB head that happens to end inside a double-byte
            ' character would be called binary (P-005).
            Assert.HasCount(2, pair, "the fixture character must be a GBK double-byte pair")
            Assert.IsFalse(BinaryDetector.IsBinary(New Byte() {pair(0)}, gbk),
                           "a pair cut in half by the inspection boundary is buffered, not rejected")
        End Sub

        <TestMethod>
        Public Sub LoneHighByteIsAcceptedUnderGbk()
            Dim gbk As Global.System.Text.Encoding = Cp936()

            ' A known, deliberately accepted hole in the rule: CP936 maps a lone 0xFF to U+F8F5 instead
            ' of failing, so such a head counts as text. Asserting it keeps the hole a decision rather
            ' than an accident (the U+FFFD scan that would "catch" it finds nothing - P-004).
            Assert.IsFalse(BinaryDetector.IsBinary(New Byte() {&HFF}, gbk),
                           "a lone 0xFF decodes as U+F8F5 under CP936 and is therefore text")
        End Sub

        <TestMethod>
        Public Sub Utf8ChoiceKeepsTheExistingVerdicts()
            Dim utf8 As Global.System.Text.Encoding = Global.System.Text.Encoding.UTF8

            Assert.IsFalse(BinaryDetector.IsBinary(New Byte() {AscW("a"c), &H00, AscW("b"c)}, utf8),
                           "embedded NUL is still text under UTF-8, as before")
            Assert.IsTrue(BinaryDetector.IsBinary(New Byte() {&HFF, &HFE}, utf8),
                          "invalid byte sequences are still binary under UTF-8, as before")
            Assert.IsFalse(BinaryDetector.IsBinary(Array.Empty(Of Byte)(), utf8), "empty content is text")
            Assert.IsFalse(BinaryDetector.IsBinary(Array.Empty(Of Byte)(), Cp936()),
                           "empty content is text for every encoding")
        End Sub

    End Class

    ''' <summary>
    ''' End-to-end gate for the "文件编码" setting: one BOM-less GBK file on disk, scanned twice with a
    ''' real tokenizer. It must be skipped as binary while the scan reads UTF-8 and counted once the
    ''' scan reads CP936 - and the counted number must be what the colored view computes from the very
    ''' same bytes, which is the premise behind "tree count == view count".
    ''' </summary>
    <TestClass>
    Public NotInheritable Class FileEncodingScanTests

        Private Const GbkText As String =
            "缓存保留时，下一次扫描可以直接复用已经合并好的词，因此更快。" &
            "你好世界，这是一段以 GBK 写入的中文文本，包含常见词语：机器、学习、分词、设置、文件夹。" & vbCrLf

        Private _tempDir As String

        <TestCleanup>
        Public Sub RemoveScanFolder()
            ' Leftover temp files are never a reason to fail a test, so a failed delete is ignored.
            Try
                If _tempDir IsNot Nothing AndAlso Directory.Exists(_tempDir) Then
                    Directory.Delete(_tempDir, True)
                End If
            Catch
            End Try
        End Sub

        <TestMethod>
        Public Async Function GbkFileIsSkippedAsUtf8AndCountedAsGbk() As Task
            If Not File.Exists(BundledTokenizerPath) Then
                Assert.Fail("deepseek-v4-flash/tokenizer.json was not copied to the test output directory; check the Content item in TokenVisualizer.Core.Tests.vbproj")
                Return
            End If

            ' An explicit code page, never this machine's ANSI setting: CI may well answer 1252.
            Dim gbk As Global.System.Text.Encoding = Cp936()
            Dim filePath As String = CreateScanFolder(gbk)
            Dim bytes As Byte() = File.ReadAllBytes(filePath)
            Assert.IsFalse(BinaryDetector.IsBinary(bytes, gbk), "the fixture must be readable GBK text")
            Assert.IsTrue(BinaryDetector.IsBinary(bytes, Global.System.Text.Encoding.UTF8),
                          "the fixture must be invalid UTF-8, or the first pass would prove nothing")

            Dim tokenizer As Tokenizers.Tokenizer = Tokenizers.Tokenizer.FromFile(BundledTokenizerPath)

            Dim utf8Options As New ScanOptions()
            Dim utf8Scanner As New FolderScanner(tokenizer, utf8Options)
            Dim utf8Root As ScanTreeNode = Await utf8Scanner.ScanAsync(_tempDir)
            Assert.AreEqual(0L, utf8Root.FileCount, "a GBK file must stay binary while the scan reads UTF-8")
            Assert.AreEqual(1L, utf8Scanner.ScanProgress.ReadFilesSkipped(), "it must be skipped as binary, not lost")
            Assert.AreEqual(0L, utf8Scanner.ScanProgress.ReadFilesWithErrors(), "skipping is not an error")

            Dim gbkOptions As New ScanOptions() With {.TextEncoding = gbk}
            Dim gbkRoot As ScanTreeNode = Await New FolderScanner(tokenizer, gbkOptions).ScanAsync(_tempDir)
            Assert.AreEqual(1L, gbkRoot.FileCount, "reading the file as its own code page must count it")
            Assert.IsGreaterThan(0L, gbkRoot.TokenCount, "a counted GBK file must produce tokens")

            ' The colored view decodes the same bytes through the same entry point (ExplorerPage.BuildLines),
            ' so a divergence here is a real break of the tree/view agreement, not a difference of intent.
            Dim viewCount As Integer = tokenizer.EncodeCount(FileEncoding.Decode(bytes, bytes.Length, gbk))
            Assert.AreEqual(CLng(viewCount), gbkRoot.TokenCount,
                            "the tree count must equal what the view computes from the same bytes")
        End Function

        ''' <summary>Creates a uniquely named folder under the system temp path holding one GBK file.</summary>
        Private Function CreateScanFolder(encoding As Global.System.Text.Encoding) As String
            Dim dir As String = Path.Combine(Path.GetTempPath(), "tokenfolderview-encoding-" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(dir)
            _tempDir = dir
            Dim filePath As String = Path.Combine(dir, "gbk-sample.txt")
            ' WriteAllBytes: the bytes are the encoding's own output, so no BOM can sneak in.
            File.WriteAllBytes(filePath, encoding.GetBytes(GbkText))
            Return filePath
        End Function

    End Class

    ''' <summary>Fixtures shared by the encoding tests in this file.</summary>
    Friend Module EncodingFixtures

        ''' <summary>
        ''' CP936, reached for by number after registering the provider (P-003). Deliberately not
        ''' <c>FileEncoding.Resolve("ANSI")</c>: a test must not inherit its expectations from whatever
        ''' code page the machine behind it happens to call ANSI.
        ''' </summary>
        Friend Function Cp936() As Global.System.Text.Encoding
            Global.System.Text.Encoding.RegisterProvider(Global.System.Text.CodePagesEncodingProvider.Instance)
            Return Global.System.Text.Encoding.GetEncoding(936)
        End Function

    End Module
End Namespace
