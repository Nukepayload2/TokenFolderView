Imports System.Buffers
Imports System.Collections.Generic
Imports System.Text
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
End Namespace
