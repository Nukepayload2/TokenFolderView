Imports System.Collections.Generic
Imports Tokenizers.Scanning

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' In-memory tests for the incremental tree editor. Every file system access is replaced by a
    ''' fake counting / listing delegate, so no file I/O is performed.
    ''' </summary>
    <TestClass>
    Public Class ScanTreeEditorTests

        ' ------------------------------------------------------------------
        ' Fakes
        ' ------------------------------------------------------------------

        ''' <summary>A counting delegate backed by a dictionary; records every path it was asked about.</summary>
        Private NotInheritable Class FakeCounter
            Friend ReadOnly Asked As New List(Of String)()

            Private ReadOnly _results As New Dictionary(Of String, FileCountResult)(StringComparer.OrdinalIgnoreCase)

            ''' <summary>Answers with a counted file of the given size.</summary>
            Friend Sub Counted(relativePath As String, tokenCount As Long, length As Long)
                _results(relativePath) = FileCountResult.Counted(tokenCount, length)
            End Sub

            ''' <summary>Answers with a data-free result (skipped / missing / error).</summary>
            Friend Sub Returns(relativePath As String, result As FileCountResult)
                _results(relativePath) = result
            End Sub

            Friend ReadOnly Property Counter As ScanTreeEditor.FileCounter
                Get
                    Return AddressOf Count
                End Get
            End Property

            ''' <summary>Anything not registered is reported as missing, like a real counter would.</summary>
            Private Function Count(relativePath As String) As FileCountResult
                Asked.Add(relativePath)
                Dim result As FileCountResult = Nothing
                Return If(_results.TryGetValue(relativePath, result), result, FileCountResult.Missing)
            End Function
        End Class

        ' ------------------------------------------------------------------
        ' Helpers
        ' ------------------------------------------------------------------

        ''' <summary>Builds a tree with the production builder, so the fixtures share its ordering.</summary>
        Private Shared Function Tree(ParamArray files As (relativePath As String, length As Long, tokenCount As Integer)()) As ScanTreeNode
            Return FolderScanner.BuildTree("root", "C:\root", files)
        End Function

        Private Shared Function Changes(ParamArray items As PathChange()) As IReadOnlyList(Of PathChange)
            Return items
        End Function

        Private Shared Function FileChange(relativePath As String) As PathChange
            Return New PathChange(relativePath, False, False)
        End Function

        Private Shared Function DirectoryChange(relativePath As String) As PathChange
            Return New PathChange(relativePath, True, False)
        End Function

        Private Shared Function Removal(relativePath As String) As PathChange
            Return New PathChange(relativePath, False, True)
        End Function

        ''' <summary>A listing delegate that returns the same prepared files for any directory.</summary>
        Private Shared Function Listing(ParamArray relativeFiles As String()) As ScanTreeEditor.SubtreeEnumerator
            Return Function(relativeDirPath As String) As IEnumerable(Of String)
                       Return relativeFiles
                   End Function
        End Function

        ''' <summary>A counting delegate that fails the test when it is called at all.</summary>
        Private Shared Function UnexpectedCounter() As ScanTreeEditor.FileCounter
            Return Function(relativePath As String) As FileCountResult
                       Assert.Fail($"counting '{relativePath}' was not expected")
                       Return Nothing
                   End Function
        End Function

        Private Shared Function Child(node As ScanTreeNode, name As String) As ScanTreeNode
            Return node.Children.First(Function(c As ScanTreeNode) c.Name = name)
        End Function

        Private Shared Function Find(node As ScanTreeNode, name As String) As ScanTreeNode
            Return node.Children.FirstOrDefault(Function(c As ScanTreeNode) c.Name = name)
        End Function

        ''' <summary>Asserts that every directory node's counts are the sum of its children's.</summary>
        Private Shared Sub AssertAggregatesMatchChildren(node As ScanTreeNode)
            If Not node.IsDirectory Then Return
            Dim tokens As Long = 0
            Dim files As Long = 0
            Dim size As Long = 0
            For Each child As ScanTreeNode In node.Children
                AssertAggregatesMatchChildren(child)
                tokens += child.TokenCount
                files += child.FileCount
                size += child.FileSize
            Next
            Assert.AreEqual(tokens, node.TokenCount, $"tokens of '{node.Name}'")
            Assert.AreEqual(files, node.FileCount, $"files of '{node.Name}'")
            Assert.AreEqual(size, node.FileSize, $"size of '{node.Name}'")
        End Sub

        ' ------------------------------------------------------------------
        ' Single file changes
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub FileChangeCreatesTheMissingDirectoryChain()
            Dim root As ScanTreeNode = Tree()
            Dim counter As New FakeCounter()
            counter.Counted("new/deep/file.txt", 11, 33)

            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("new/deep/file.txt")), Listing(), counter.Counter)

            Assert.HasCount(1, root.Children)
            Dim newDir As ScanTreeNode = root.Children(0)
            Assert.IsTrue(newDir.IsDirectory)
            Assert.AreEqual("new", newDir.Name)
            Assert.AreEqual("C:\root\new", newDir.FullPath)

            Dim deepDir As ScanTreeNode = newDir.Children(0)
            Assert.IsTrue(deepDir.IsDirectory)
            Assert.AreEqual("C:\root\new\deep", deepDir.FullPath)

            Dim fileNode As ScanTreeNode = deepDir.Children(0)
            Assert.IsFalse(fileNode.IsDirectory)
            Assert.AreEqual("file.txt", fileNode.Name)
            Assert.AreEqual("C:\root\new\deep\file.txt", fileNode.FullPath)
            Assert.AreEqual(11L, fileNode.TokenCount)
            Assert.AreEqual(1L, fileNode.FileCount)
            Assert.AreEqual(33L, fileNode.FileSize)

            Assert.AreEqual(11L, root.TokenCount)
            Assert.AreEqual(1L, root.FileCount)
            Assert.AreEqual(33L, root.FileSize)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub NewNodesAreInsertedInNameOrderAmongDirectoriesAndFiles()
            Dim root As ScanTreeNode = Tree(("dir.txt", 1L, 1), ("folder/a.txt", 1L, 2))
            Assert.AreEqual("dir.txt", root.Children(0).Name)
            Assert.AreEqual("folder", root.Children(1).Name)

            Dim counter As New FakeCounter()
            counter.Counted("b.txt", 30, 300)
            counter.Counted("zz.txt", 40, 400)
            counter.Counted("folder/m.txt", 50, 500)

            ScanTreeEditor.ApplyChanges(root,
                                        Changes(FileChange("b.txt"), FileChange("zz.txt"), FileChange("folder/m.txt")),
                                        Listing(),
                                        counter.Counter)

            ' Ordinal by name, directories and files mixed - the order BuildTree produces.
            Assert.AreEqual("b.txt", root.Children(0).Name)
            Assert.AreEqual("dir.txt", root.Children(1).Name)
            Assert.AreEqual("folder", root.Children(2).Name)
            Assert.AreEqual("zz.txt", root.Children(3).Name)
            Assert.AreEqual("a.txt", Child(root, "folder").Children(0).Name)
            Assert.AreEqual("m.txt", Child(root, "folder").Children(1).Name)
            Assert.AreEqual(123L, root.TokenCount)
            Assert.AreEqual(5L, root.FileCount)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub ExistingFileNodeIsUpdatedInPlace()
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 5), ("b.txt", 1L, 1))
            Dim node As ScanTreeNode = Child(root, "a.txt")

            Dim counter As New FakeCounter()
            counter.Counted("a.txt", 7, 20)

            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("a.txt")), Listing(), counter.Counter)

            ' The very same node instance: removing and re-adding it would drop the TreeView selection.
            Assert.AreSame(node, Child(root, "a.txt"))
            Assert.AreEqual(7L, node.TokenCount)
            Assert.AreEqual(1L, node.FileCount)
            Assert.AreEqual(20L, node.FileSize)
            Assert.AreEqual(8L, root.TokenCount)
            Assert.AreEqual(21L, root.FileSize)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub UncountableResultsRemoveTheNodeInsteadOfZeroingIt()
            Dim root As ScanTreeNode = Tree(("big.txt", 100L, 50), ("pic.bin", 200L, 60), ("gone.txt", 10L, 9), ("keep.txt", 3L, 2))

            Dim counter As New FakeCounter()
            counter.Returns("big.txt", FileCountResult.SkippedSize)
            counter.Returns("pic.bin", FileCountResult.SkippedBinary)
            counter.Returns("gone.txt", FileCountResult.Missing)

            ScanTreeEditor.ApplyChanges(root,
                                        Changes(FileChange("big.txt"), FileChange("pic.bin"), FileChange("gone.txt")),
                                        Listing(),
                                        counter.Counter)

            ' Removed, not zeroed: a zero-count file node would poison every ancestor forever.
            Assert.HasCount(1, root.Children)
            Assert.AreEqual("keep.txt", root.Children(0).Name)
            Assert.AreEqual(2L, root.TokenCount)
            Assert.AreEqual(1L, root.FileCount)
            Assert.AreEqual(3L, root.FileSize)
        End Sub

        <TestMethod>
        Public Sub ErrorResultKeepsTheExistingNodeUntouched()
            Dim root As ScanTreeNode = Tree(("locked.txt", 40L, 7), ("other.txt", 10L, 3))
            Dim node As ScanTreeNode = Child(root, "locked.txt")

            Dim counter As New FakeCounter()
            counter.Returns("locked.txt", FileCountResult.Error)

            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("locked.txt")), Listing(), counter.Counter)

            ' A file that is merely in use right now must not lose its last known counts.
            Assert.AreSame(node, Child(root, "locked.txt"))
            Assert.AreEqual(7L, node.TokenCount)
            Assert.AreEqual(40L, node.FileSize)
            Assert.HasCount(2, root.Children)
            Assert.AreEqual(10L, root.TokenCount)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub ErrorResultForAnUnknownPathCreatesNothing()
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 3))

            Dim counter As New FakeCounter()
            counter.Returns("locked.txt", FileCountResult.Error)

            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("locked.txt")), Listing(), counter.Counter)

            Assert.HasCount(1, root.Children)
            Assert.AreEqual(3L, root.TokenCount)
        End Sub

        <TestMethod>
        Public Sub FileChangeReplacesAFileNodeInTheMiddleOfANewChain()
            Dim root As ScanTreeNode = Tree(("a", 10L, 5))

            Dim counter As New FakeCounter()
            counter.Counted("a/b/c.txt", 1, 11)

            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("a/b/c.txt")), Listing(), counter.Counter)

            ' 'a' is a directory on disk now, so the file node cannot stay a parent.
            Dim aDir As ScanTreeNode = Child(root, "a")
            Assert.IsTrue(aDir.IsDirectory)
            Assert.AreEqual("C:\root\a", aDir.FullPath)
            Assert.AreEqual("C:\root\a\b", Child(aDir, "b").FullPath)
            Assert.AreEqual(1L, root.TokenCount)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub FileChangeReplacesADirectoryNodeAtTheSamePath()
            Dim root As ScanTreeNode = Tree(("thing/inner.txt", 10L, 5))

            Dim counter As New FakeCounter()
            counter.Counted("thing", 2, 22)

            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("thing")), Listing(), counter.Counter)

            Assert.HasCount(1, root.Children)
            Dim node As ScanTreeNode = root.Children(0)
            Assert.IsFalse(node.IsDirectory)
            Assert.AreEqual("thing", node.Name)
            Assert.AreEqual(2L, node.TokenCount)
            Assert.AreEqual(22L, node.FileSize)
            Assert.AreEqual(2L, root.TokenCount)
        End Sub

        ' ------------------------------------------------------------------
        ' Directory changes
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub DirectoryChangeRescansTheSubtree()
            Dim root As ScanTreeNode = Tree(("d/old.txt", 5L, 5), ("d/gone.txt", 6L, 6), ("other.txt", 1L, 1))
            Dim dirNode As ScanTreeNode = Child(root, "d")

            Dim listed As New List(Of String)()
            Dim enumerate As ScanTreeEditor.SubtreeEnumerator =
                Function(relativeDirPath As String) As IEnumerable(Of String)
                    listed.Add(relativeDirPath)
                    Return New String() {"d/new.txt", "d/skip.bin", "d/sub/new2.txt"}
                End Function

            Dim counter As New FakeCounter()
            counter.Counted("d/new.txt", 7, 70)
            counter.Counted("d/sub/new2.txt", 8, 80)
            counter.Returns("d/skip.bin", FileCountResult.SkippedBinary)

            ScanTreeEditor.ApplyChanges(root, Changes(DirectoryChange("d")), enumerate, counter.Counter)

            Assert.HasCount(1, listed)
            Assert.AreEqual("d", listed(0), "the rescan must list the changed directory")
            Assert.HasCount(3, counter.Asked, "every listed file is counted exactly once")
            Assert.AreSame(dirNode, Child(root, "d"), "the directory node itself is kept")
            Assert.AreEqual("C:\root\d", dirNode.FullPath)

            ' Files that disappeared from disk are gone from the tree; skipped ones were never there.
            Assert.IsNull(Find(dirNode, "old.txt"))
            Assert.IsNull(Find(dirNode, "gone.txt"))
            Assert.IsNull(Find(dirNode, "skip.bin"))

            Dim newNode As ScanTreeNode = Child(dirNode, "new.txt")
            Assert.AreEqual(7L, newNode.TokenCount)
            Assert.AreEqual(70L, newNode.FileSize)
            Dim subDir As ScanTreeNode = Child(dirNode, "sub")
            Assert.IsTrue(subDir.IsDirectory)
            Assert.AreEqual("C:\root\d\sub", subDir.FullPath)
            Assert.AreEqual(8L, subDir.Children(0).TokenCount)

            Assert.AreEqual("new.txt", dirNode.Children(0).Name)
            Assert.AreEqual("sub", dirNode.Children(1).Name)
            Assert.AreEqual(15L, dirNode.TokenCount)
            Assert.AreEqual(2L, dirNode.FileCount)
            Assert.AreEqual(150L, dirNode.FileSize)
            Assert.AreEqual(16L, root.TokenCount)
            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(151L, root.FileSize)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub DirectoryRescanOverridesSingleFileChangesOfTheSameBatch()
            ' The file change claims 999 tokens; the rescan of the directory is the truth.
            Dim counterCalls As New List(Of String)()
            Dim count As ScanTreeEditor.FileCounter =
                Function(relativePath As String) As FileCountResult
                    counterCalls.Add(relativePath)
                    If counterCalls.Count = 1 Then Return FileCountResult.Counted(3, 30)
                    Return FileCountResult.Counted(999, 999)
                End Function

            Dim root As ScanTreeNode = Tree(("d/x.txt", 5L, 5))
            ScanTreeEditor.ApplyChanges(root,
                                        Changes(DirectoryChange("d"), FileChange("d/x.txt")),
                                        Listing("d/x.txt"),
                                        count)

            Assert.HasCount(1, counterCalls, "the covered file change must not be counted again")
            Assert.AreEqual(3L, Child(Child(root, "d"), "x.txt").TokenCount)
            Assert.AreEqual(3L, root.TokenCount)

            ' Same batch, the other way round: the phase order decides, not the listing order.
            Dim reversedCalls As New List(Of String)()
            Dim reversedCount As ScanTreeEditor.FileCounter =
                Function(relativePath As String) As FileCountResult
                    reversedCalls.Add(relativePath)
                    If reversedCalls.Count = 1 Then Return FileCountResult.Counted(3, 30)
                    Return FileCountResult.Counted(999, 999)
                End Function

            Dim secondRoot As ScanTreeNode = Tree(("d/x.txt", 5L, 5))
            ScanTreeEditor.ApplyChanges(secondRoot,
                                        Changes(FileChange("d/x.txt"), DirectoryChange("d")),
                                        Listing("d/x.txt"),
                                        reversedCount)

            Assert.HasCount(1, reversedCalls)
            Assert.AreEqual(3L, Child(Child(secondRoot, "d"), "x.txt").TokenCount)
        End Sub

        <TestMethod>
        Public Sub DirectoryChangeIgnoresListedPathsOutsideTheSubtree()
            Dim root As ScanTreeNode = Tree(("d/old.txt", 5L, 5), ("other.txt", 1L, 1))

            Dim counter As New FakeCounter()
            counter.Counted("d/old.txt", 5, 50)
            counter.Counted("outside.txt", 42, 420)

            ' A listing delegate that also reports a file from a different subtree: not this rescan's
            ' business, so it is neither counted nor added.
            ScanTreeEditor.ApplyChanges(root,
                                        Changes(DirectoryChange("d")),
                                        Listing("outside.txt", "d/old.txt"),
                                        counter.Counter)

            Assert.HasCount(1, counter.Asked)
            Assert.AreEqual("d/old.txt", counter.Asked(0))
            Assert.IsNull(Find(root, "outside.txt"))
            Assert.AreEqual(5L, Child(Child(root, "d"), "old.txt").TokenCount)
            Assert.AreEqual(6L, root.TokenCount)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub DirectoryChangeReplacesAFileNodeAtTheSamePath()
            Dim root As ScanTreeNode = Tree(("thing", 10L, 5))

            Dim counter As New FakeCounter()
            counter.Counted("thing/inner.txt", 3, 33)

            ScanTreeEditor.ApplyChanges(root,
                                        Changes(DirectoryChange("thing")),
                                        Listing("thing/inner.txt"),
                                        counter.Counter)

            Assert.HasCount(1, root.Children)
            Dim node As ScanTreeNode = root.Children(0)
            Assert.IsTrue(node.IsDirectory)
            Assert.AreEqual("thing", node.Name)
            Assert.AreEqual(1L, node.FileCount)
            Assert.AreEqual(3L, node.TokenCount)
            Assert.AreEqual(3L, root.TokenCount)
        End Sub

        ' ------------------------------------------------------------------
        ' Removals
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub DirectoryRemovalDropsTheWholeSubtree()
            Dim root As ScanTreeNode = Tree(("sub/a.txt", 10L, 5), ("sub/deep/b.txt", 20L, 7), ("top.txt", 5L, 1))

            ScanTreeEditor.ApplyChanges(root, Changes(Removal("sub")), Listing(), UnexpectedCounter())

            Assert.HasCount(1, root.Children)
            Assert.AreEqual("top.txt", root.Children(0).Name)
            Assert.AreEqual(1L, root.TokenCount)
            Assert.AreEqual(1L, root.FileCount)
            Assert.AreEqual(5L, root.FileSize)
        End Sub

        <TestMethod>
        Public Sub RemovalOfADirectoryReportedAsAFileStillDropsItsSubtree()
            ' A watcher resolves IsDirectory with Directory.Exists *after* the fact, so a deleted
            ' directory usually arrives flagged as a file.
            Dim root As ScanTreeNode = Tree(("sub/a.txt", 10L, 5), ("top.txt", 5L, 1))

            ScanTreeEditor.ApplyChanges(root, Changes(Removal("sub")), Listing(), UnexpectedCounter())

            Assert.HasCount(1, root.Children)
            Assert.IsNull(Find(root, "sub"))
            Assert.AreEqual(1L, root.TokenCount)
        End Sub

        <TestMethod>
        Public Sub EmptyDirectoriesArePrunedButTheRootIsNot()
            Dim root As ScanTreeNode = Tree(("sub/deep/x.txt", 10L, 5), ("keep/y.txt", 1L, 1))

            Dim counter As New FakeCounter()
            counter.Returns("sub/deep/x.txt", FileCountResult.Missing)
            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("sub/deep/x.txt")), Listing(), counter.Counter)

            Assert.HasCount(1, root.Children)
            Assert.AreEqual("keep", root.Children(0).Name)
            Assert.AreEqual(1L, root.TokenCount)
            AssertAggregatesMatchChildren(root)

            ' Removing the last file empties the tree down to the root - which stays.
            Dim lastCounter As New FakeCounter()
            lastCounter.Returns("keep/y.txt", FileCountResult.Missing)
            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("keep/y.txt")), Listing(), lastCounter.Counter)

            Assert.HasCount(0, root.Children)
            Assert.IsTrue(root.IsDirectory)
            Assert.AreEqual("root", root.Name)
            Assert.AreEqual(0L, root.TokenCount)
            Assert.AreEqual(0L, root.FileCount)
            Assert.AreEqual(0L, root.FileSize)
        End Sub

        <TestMethod>
        Public Sub UnknownPathRemovalsAreNoOps()
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 3), ("sub/b.txt", 20L, 4))

            ScanTreeEditor.ApplyChanges(root,
                                        Changes(Removal("nothing.txt"), Removal("no/such/path.txt"), Removal("sub/none.txt")),
                                        Listing(),
                                        UnexpectedCounter())

            Assert.HasCount(2, root.Children)
            Assert.HasCount(1, Child(root, "sub").Children)
            Assert.AreEqual(7L, root.TokenCount)
            Assert.AreEqual(2L, root.FileCount)
            Assert.AreEqual(30L, root.FileSize)
        End Sub

        ' ------------------------------------------------------------------
        ' Batch hygiene
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub DuplicateEntriesOfOneBatchAreAppliedOnce()
            Dim root As ScanTreeNode = Tree(("a.txt", 1L, 1), ("sub/b.txt", 1L, 1))

            Dim counted As New List(Of String)()
            Dim count As ScanTreeEditor.FileCounter =
                Function(relativePath As String) As FileCountResult
                    counted.Add(relativePath)
                    Return FileCountResult.Counted(4, 40)
                End Function

            ScanTreeEditor.ApplyChanges(root,
                                        Changes(FileChange("a.txt"), FileChange("a.txt"), Removal("sub"), Removal("sub")),
                                        Listing(),
                                        count)

            Assert.HasCount(1, counted)
            Assert.AreEqual("a.txt", counted(0))
            Assert.HasCount(1, root.Children)
            Assert.AreEqual("a.txt", root.Children(0).Name)
            Assert.AreEqual(4L, root.TokenCount)
        End Sub

        <TestMethod>
        Public Sub RepeatedAndNestedDirectoryChangesAreRescannedOnce()
            Dim root As ScanTreeNode = Tree(("d/old.txt", 5L, 5), ("d/sub/x.txt", 6L, 6))

            Dim listed As New List(Of String)()
            Dim enumerate As ScanTreeEditor.SubtreeEnumerator =
                Function(relativeDirPath As String) As IEnumerable(Of String)
                    listed.Add(relativeDirPath)
                    Return New String() {"d/sub/x.txt"}
                End Function

            Dim counter As New FakeCounter()
            counter.Counted("d/sub/x.txt", 7, 70)

            ' A repeated path, a path below an already rescanned directory and a repeated path *after*
            ' that nested one: the first rescan of 'd' is a superset of all three.
            ScanTreeEditor.ApplyChanges(root,
                                        Changes(DirectoryChange("d"), DirectoryChange("d/sub"), DirectoryChange("d")),
                                        enumerate,
                                        counter.Counter)

            Assert.HasCount(1, listed, "a repeated or already covered directory must not be rescanned")
            Assert.AreEqual("d", listed(0))
            Assert.HasCount(1, counter.Asked, "every listed file is counted exactly once")
            Assert.IsNull(Find(Child(root, "d"), "old.txt"), "the rescan is the truth for the subtree")
            Assert.AreEqual(7L, Child(Child(Child(root, "d"), "sub"), "x.txt").TokenCount)
            Assert.AreEqual(7L, root.TokenCount)
            Assert.AreEqual(1L, root.FileCount)
            Assert.AreEqual(70L, root.FileSize)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub PathsOutsideTheRootAreIgnored()
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 3))

            Dim counter As New FakeCounter()
            counter.Counted("b.txt", 1, 1)

            ScanTreeEditor.ApplyChanges(root,
                                        Changes(FileChange(""), FileChange("   "), FileChange("../outside.txt"),
                                                FileChange("/abs.txt"), FileChange("C:/abs.txt"),
                                                FileChange("a/../../x.txt"), FileChange("./a.txt"), DirectoryChange("..")),
                                        Listing("../outside.txt"),
                                        counter.Counter)

            Assert.HasCount(0, counter.Asked)
            Assert.HasCount(1, root.Children)
            Assert.AreEqual("a.txt", root.Children(0).Name)
            Assert.AreEqual(3L, root.TokenCount)
        End Sub

        <TestMethod>
        Public Sub EmptyBatchAndMissingBatchAreNoOps()
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 5))

            ScanTreeEditor.ApplyChanges(root, Changes(), Listing(), UnexpectedCounter())
            ScanTreeEditor.ApplyChanges(root, Nothing, Listing(), UnexpectedCounter())

            Assert.HasCount(1, root.Children)
            Assert.AreEqual(5L, root.TokenCount)
        End Sub

        <TestMethod>
        Public Sub NullArgumentsAreRejected()
            Dim root As ScanTreeNode = Tree()

            Assert.ThrowsExactly(Of ArgumentNullException)(
                Sub() ScanTreeEditor.ApplyChanges(Nothing, Changes(), Listing(), UnexpectedCounter()))
            Assert.ThrowsExactly(Of ArgumentNullException)(
                Sub() ScanTreeEditor.ApplyChanges(root, Changes(), Nothing, UnexpectedCounter()))
            Assert.ThrowsExactly(Of ArgumentNullException)(
                Sub() ScanTreeEditor.ApplyChanges(root, Changes(), Listing(), Nothing))
            Assert.ThrowsExactly(Of ArgumentNullException)(
                Sub() ScanTreeEditor.Reaggregate(Nothing))
        End Sub

        ' ------------------------------------------------------------------
        ' Re-aggregation
        ' ------------------------------------------------------------------

        <TestMethod>
        Public Sub ReaggregateIsIdempotentAndSumsTheLeaves()
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 5), ("sub/b.txt", 20L, 7), ("sub/deep/c.txt", 30L, 9))

            ' Corrupt the aggregates; the fold must repair them (it only ever rewrites directories).
            root.TokenCount = 999
            Child(root, "sub").FileSize = -1

            ScanTreeEditor.Reaggregate(root)
            Assert.AreEqual(21L, root.TokenCount)
            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(60L, root.FileSize)
            AssertAggregatesMatchChildren(root)

            ' Idempotent: a second pass leaves the very same numbers behind.
            ScanTreeEditor.Reaggregate(root)
            Assert.AreEqual(21L, root.TokenCount)
            Assert.AreEqual(3L, root.FileCount)
            Assert.AreEqual(60L, root.FileSize)
            AssertAggregatesMatchChildren(root)

            ' Leaves keep their own counts - the fold takes them as they are.
            Dim leaf As ScanTreeNode = root.Children.First(Function(c As ScanTreeNode) Not c.IsDirectory)
            leaf.TokenCount = 42
            ScanTreeEditor.Reaggregate(root)
            Assert.AreEqual(42L, leaf.TokenCount)
            Assert.AreEqual(42L + 7L + 9L, root.TokenCount)
            AssertAggregatesMatchChildren(root)
        End Sub

        <TestMethod>
        Public Sub BatchTotalsAgreeWithAFreshBuildOfTheSameFiles()
            ' The incremental result and a full rebuild of the same file set must agree.
            Dim root As ScanTreeNode = Tree(("a.txt", 10L, 5), ("sub/b.txt", 20L, 7))

            Dim counter As New FakeCounter()
            counter.Counted("a.txt", 100, 1000)
            counter.Counted("new/c.txt", 3, 30)
            ScanTreeEditor.ApplyChanges(root, Changes(FileChange("a.txt"), FileChange("new/c.txt")), Listing(), counter.Counter)

            Dim rebuilt As ScanTreeNode = Tree(("a.txt", 1000L, 100), ("sub/b.txt", 20L, 7), ("new/c.txt", 30L, 3))

            Assert.AreEqual(rebuilt.TokenCount, root.TokenCount)
            Assert.AreEqual(rebuilt.FileCount, root.FileCount)
            Assert.AreEqual(rebuilt.FileSize, root.FileSize)
            AssertAggregatesMatchChildren(root)
        End Sub

    End Class
End Namespace
