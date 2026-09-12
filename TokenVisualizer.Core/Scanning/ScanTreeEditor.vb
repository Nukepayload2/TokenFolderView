Imports System
Imports System.Collections.Generic
Imports System.IO

Namespace Scanning

    ''' <summary>
    ''' Pure, I/O-free incremental updates of a scanned <see cref="ScanTreeNode"/> tree. A batch of
    ''' reported path changes is folded into an existing tree; every read from the file system is
    ''' injected as a delegate, so the whole class is unit-testable without touching disk.
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' Ordering is part of the semantics: all removals are applied first, then directory changes
    ''' (each one a recursive rescan of that subtree, which overrides single-file changes reported for
    ''' the same batch inside it) and finally the remaining single-file changes. The aggregate counts
    ''' are recomputed once per batch, at the end, by <see cref="Reaggregate"/>.
    ''' </para>
    ''' <para>
    ''' The tree is only ever modified with <c>Insert</c> and <c>Remove</c>: an existing file node is
    ''' updated in place so that a bound <c>TreeView</c> keeps the selection, the expanded nodes and
    ''' the item containers. <c>Clear</c> / <c>Move</c> are never used (they would raise a Reset).
    ''' </para>
    ''' <para>
    ''' Rooted / escaping paths (empty, absolute, or containing a <c>..</c> or <c>.</c> segment) are
    ''' ignored, so no change can ever reach outside the scanned root.
    ''' </para>
    ''' </remarks>
    Public NotInheritable Class ScanTreeEditor

        ''' <summary>
        ''' Counts one file, identified by a <c>/</c>-separated path relative to the scanned root.
        ''' Must not throw: report "cannot read right now" as <see cref="FileCountStatus.Error"/>.
        ''' </summary>
        Public Delegate Function FileCounter(relativePath As String) As FileCountResult

        ''' <summary>
        ''' Lists the <c>/</c>-separated relative paths of all files inside a directory subtree
        ''' (recursively), identified by a <c>/</c>-separated path relative to the scanned root. The
        ''' paths must be inside that subtree; anything else is ignored by the editor.
        ''' Must either return the complete list or throw: a partial list is never an acceptable answer.
        ''' </summary>
        ''' <remarks>
        ''' A directory change drops every child of that node first and then rebuilds it from this list,
        ''' so a caller that catches an enumeration failure and returns what it happened to see already
        ''' deletes the whole subtree that the listing missed - silently, and with plausible-looking
        ''' totals, because the aggregates are recomputed from whatever nodes are left.
        ''' </remarks>
        Public Delegate Function SubtreeEnumerator(relativeDirPath As String) As IEnumerable(Of String)

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Applies a batch of changes to <paramref name="root"/>: removals first, then directory
        ''' rescans, then single-file updates, with a single re-aggregation of the counts at the end.
        ''' </summary>
        ''' <param name="root">The root of the tree to patch; never replaced or removed.</param>
        ''' <param name="changes">The batch, as reported by the watcher (may contain duplicates).</param>
        ''' <param name="enumerateFiles">Lists the files of a directory subtree (used for directory changes).</param>
        ''' <param name="countFile">Counts a single file (used for file and directory changes).</param>
        Public Shared Sub ApplyChanges(root As ScanTreeNode,
                                       changes As IReadOnlyList(Of PathChange),
                                       enumerateFiles As SubtreeEnumerator,
                                       countFile As FileCounter)
            If root Is Nothing Then Throw New ArgumentNullException(NameOf(root))
            If enumerateFiles Is Nothing Then Throw New ArgumentNullException(NameOf(enumerateFiles))
            If countFile Is Nothing Then Throw New ArgumentNullException(NameOf(countFile))
            If changes Is Nothing OrElse changes.Count = 0 Then Return

            ApplyRemovals(root, changes)

            ' Directory changes are rescans: they must happen after the removals (so a deleted file
            ' inside a rescanned directory is not resurrected) and before the file changes (so the
            ' rescan result overrides any single-file record of the same batch).
            Dim rescannedDirectories As New List(Of String)()
            For Each change As PathChange In changes
                If change Is Nothing OrElse change.IsRemoval OrElse Not change.IsDirectory Then Continue For
                Dim segments As String() = Nothing
                If Not TryNormalizeRelativePath(change.RelativePath, segments) Then Continue For
                Dim key As String = String.Join("/", segments)
                ' A rescan already covers (and is a superset of) a change of a directory below it.
                If IsCoveredByRescan(rescannedDirectories, key) Then Continue For
                rescannedDirectories.Add(key)
                RescanDirectory(root, segments, enumerateFiles, countFile)
            Next

            Dim applied As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each change As PathChange In changes
                If change Is Nothing OrElse change.IsRemoval OrElse change.IsDirectory Then Continue For
                Dim segments As String() = Nothing
                If Not TryNormalizeRelativePath(change.RelativePath, segments) Then Continue For
                Dim key As String = String.Join("/", segments)
                If Not applied.Add(key) Then Continue For
                ' The rescan of a directory is the freshest word on everything inside it.
                If IsCoveredByRescan(rescannedDirectories, key) Then Continue For
                ApplyFileChange(root, segments, countFile)
            Next

            Reaggregate(root)
        End Sub

        ''' <summary>
        ''' Post-order pass that re-assigns every directory node's counts to the sum of its subtree.
        ''' Leaves keep their own counts, so the pass is idempotent and is the single place where
        ''' aggregate counts are computed.
        ''' </summary>
        Public Shared Sub Reaggregate(root As ScanTreeNode)
            If root Is Nothing Then Throw New ArgumentNullException(NameOf(root))
            Aggregate(root)
        End Sub

        ' ------------------------------------------------------------------
        ' Batch phases
        ' ------------------------------------------------------------------

        ''' <summary>Phase 1: applies every removal (a directory removal drops its whole subtree).</summary>
        Private Shared Sub ApplyRemovals(root As ScanTreeNode, changes As IReadOnlyList(Of PathChange))
            Dim removed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each change As PathChange In changes
                If change Is Nothing OrElse Not change.IsRemoval Then Continue For
                Dim segments As String() = Nothing
                If Not TryNormalizeRelativePath(change.RelativePath, segments) Then Continue For
                ' IsDirectory is deliberately ignored: a deleted directory is usually reported as
                ' "not a directory any more" by the watcher, and the node alone decides what to drop.
                If Not removed.Add(String.Join("/", segments)) Then Continue For
                RemoveAtPath(root, segments, pruneEmptyDirectories:=True)
            Next
        End Sub

        ''' <summary>
        ''' Phase 2: one directory change = one recursive rescan of that subtree. The old contents of
        ''' the directory node are dropped and every file the enumerator lists is counted anew; only
        ''' counted files become nodes (a skipped or missing file was never in the tree).
        ''' </summary>
        Private Shared Sub RescanDirectory(root As ScanTreeNode,
                                           dirSegments As String(),
                                           enumerateFiles As SubtreeEnumerator,
                                           countFile As FileCounter)
            Dim dirNode As ScanTreeNode = EnsureDirectoryNode(root, dirSegments)

            ' The directory node itself is kept (its path still exists, and keeping the instance keeps
            ' the expanded state of a bound TreeView); only its children are dropped, one by one -
            ' Clear would raise a Reset and recreate every container of the whole tree.
            While dirNode.Children.Count > 0
                dirNode.Children.RemoveAt(dirNode.Children.Count - 1)
            End While

            For Each listed As String In enumerateFiles(String.Join("/", dirSegments))
                Dim segments As String() = Nothing
                If Not TryNormalizeRelativePath(listed, segments) Then Continue For
                If Not IsStrictlyInside(dirSegments, segments) Then Continue For
                Dim result As FileCountResult = countFile(String.Join("/", segments))
                If result Is Nothing OrElse result.Status <> FileCountStatus.Counted Then Continue For
                UpsertFile(root, segments, result)
            Next
        End Sub

        ''' <summary>Phase 3: one single-file change.</summary>
        Private Shared Sub ApplyFileChange(root As ScanTreeNode, segments As String(), countFile As FileCounter)
            Dim result As FileCountResult = countFile(String.Join("/", segments))
            If result Is Nothing Then Return

            Select Case result.Status
                Case FileCountStatus.Counted
                    UpsertFile(root, segments, result)
                Case FileCountStatus.SkippedSize, FileCountStatus.SkippedBinary, FileCountStatus.Missing
                    ' The path is gone, or is no longer a file this scan counts: the node must be
                    ' removed. Writing 0 instead would leave a ghost behind and permanently skew the
                    ' totals of every ancestor.
                    RemoveAtPath(root, segments, pruneEmptyDirectories:=True)
                Case FileCountStatus.Error
                    ' An unreadable file is often just locked right now: keep the last known counts.
                Case Else
                    ' Any status a counter must not produce is treated like Error: keep the node.
            End Select
        End Sub

        ' ------------------------------------------------------------------
        ' Tree surgery
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Creates or updates the file node at <paramref name="segments"/>, creating missing parent
        ''' directories on the way. An existing file node is updated in place (never removed and
        ''' re-added), so the bound <c>TreeView</c> keeps its selection; a new node is inserted at its
        ''' ordinal place among the siblings, which is the order <see cref="FolderScanner.BuildTree"/>
        ''' produces (directories and files mixed, compared by name only).
        ''' </summary>
        Private Shared Sub UpsertFile(root As ScanTreeNode, segments As String(), result As FileCountResult)
            Dim parent As ScanTreeNode = EnsureDirectoryNode(root, Take(segments, segments.Length - 1))
            Dim name As String = segments(segments.Length - 1)
            Dim index As Integer = IndexOfChild(parent, name)

            If index >= 0 AndAlso Not parent.Children(index).IsDirectory Then
                Dim existing As ScanTreeNode = parent.Children(index)
                existing.TokenCount = result.TokenCount
                existing.FileCount = 1
                existing.FileSize = result.Length
                Return
            End If

            ' A directory node sits where the file is now: the old subtree cannot describe the file,
            ' so it is dropped (the only case where a node is replaced rather than updated).
            If index >= 0 Then parent.Children.RemoveAt(index)

            Dim node As New ScanTreeNode(name, Path.Combine(root.FullPath, ToNativePath(segments)), False) With {
                .TokenCount = result.TokenCount,
                .FileCount = 1,
                .FileSize = result.Length
            }
            parent.Children.Insert(FindInsertIndex(parent.Children, name), node)
        End Sub

        ''' <summary>
        ''' Walks <paramref name="segments"/> down from <paramref name="root"/>, creating the
        ''' directory nodes that do not exist yet, and returns the deepest one.
        ''' </summary>
        Private Shared Function EnsureDirectoryNode(root As ScanTreeNode, segments As String()) As ScanTreeNode
            Return EnsureDirectoryChain(root, segments)(segments.Length)
        End Function

        ''' <summary>
        ''' Walks <paramref name="segments"/> down from <paramref name="root"/>, creating the missing
        ''' directory nodes, and returns the chain of nodes: <c>chain(0)</c> is always the root and
        ''' <c>chain(i)</c> is the node of <c>segments(i - 1)</c>.
        ''' </summary>
        Private Shared Function EnsureDirectoryChain(root As ScanTreeNode, segments As String()) As List(Of ScanTreeNode)
            Dim chain As New List(Of ScanTreeNode)(segments.Length + 1) From {root}
            For i As Integer = 0 To segments.Length - 1
                Dim parent As ScanTreeNode = chain(chain.Count - 1)
                Dim index As Integer = IndexOfChild(parent, segments(i))
                Dim child As ScanTreeNode = Nothing
                If index >= 0 Then
                    child = parent.Children(index)
                    If Not child.IsDirectory Then
                        ' A file node where a directory is needed (the file was replaced by a
                        ' directory): drop it, the directory node below replaces it.
                        parent.Children.RemoveAt(index)
                        child = Nothing
                    End If
                End If
                If child Is Nothing Then
                    child = New ScanTreeNode(segments(i), Path.Combine(root.FullPath, ToNativePath(Take(segments, i + 1))), True)
                    parent.Children.Insert(FindInsertIndex(parent.Children, segments(i)), child)
                End If
                chain.Add(child)
            Next
            Return chain
        End Function

        ''' <summary>
        ''' Removes the node at <paramref name="segments"/> with its whole subtree (if it exists).
        ''' With <paramref name="pruneEmptyDirectories"/> set, ancestors that just became empty are
        ''' removed as well - except the root, which is never removed (an empty root is a legal tree).
        ''' </summary>
        Private Shared Sub RemoveAtPath(root As ScanTreeNode, segments As String(), pruneEmptyDirectories As Boolean)
            Dim chain As List(Of ScanTreeNode) = FindDirectoryChain(root, Take(segments, segments.Length - 1))
            If chain Is Nothing Then Return

            Dim parent As ScanTreeNode = chain(chain.Count - 1)
            Dim index As Integer = IndexOfChild(parent, segments(segments.Length - 1))
            If index < 0 Then Return
            parent.Children.RemoveAt(index)

            If pruneEmptyDirectories Then PruneEmptyAncestors(chain)
        End Sub

        ''' <summary>Removes the deepest node of <paramref name="chain"/> and its now-empty ancestors.</summary>
        ''' <remarks>
        ''' <paramref name="chain"/> holds root..parent, so index 1 upward is removable and index 0
        ''' (the root) never is. Once a node still has children, all its ancestors do too, so the walk
        ''' can stop there.
        ''' </remarks>
        Private Shared Sub PruneEmptyAncestors(chain As List(Of ScanTreeNode))
            For i As Integer = chain.Count - 1 To 1 Step -1
                Dim node As ScanTreeNode = chain(i)
                If node.Children.Count > 0 Then Exit For
                chain(i - 1).Children.Remove(node)
            Next
        End Sub

        ''' <summary>
        ''' Walks <paramref name="segments"/> down from <paramref name="root"/> over existing
        ''' directory nodes. Returns <c>Nothing</c> when a segment is missing or is not a directory.
        ''' </summary>
        Private Shared Function FindDirectoryChain(root As ScanTreeNode, segments As String()) As List(Of ScanTreeNode)
            Dim chain As New List(Of ScanTreeNode)(segments.Length + 1) From {root}
            For i As Integer = 0 To segments.Length - 1
                Dim parent As ScanTreeNode = chain(chain.Count - 1)
                Dim index As Integer = IndexOfChild(parent, segments(i))
                If index < 0 Then Return Nothing
                Dim child As ScanTreeNode = parent.Children(index)
                If Not child.IsDirectory Then Return Nothing
                chain.Add(child)
            Next
            Return chain
        End Function

        ''' <summary>Index of the child with that name (case-insensitive), or -1.</summary>
        Private Shared Function IndexOfChild(parent As ScanTreeNode, name As String) As Integer
            For i As Integer = 0 To parent.Children.Count - 1
                If String.Equals(parent.Children(i).Name, name, StringComparison.OrdinalIgnoreCase) Then Return i
            Next
            Return -1
        End Function

        ''' <summary>
        ''' Index at which a child named <paramref name="name"/> belongs, keeping the siblings in the
        ''' same ordinal-by-name order <see cref="FolderScanner.BuildTree"/> sorts them into.
        ''' </summary>
        Private Shared Function FindInsertIndex(children As IList(Of ScanTreeNode), name As String) As Integer
            For i As Integer = 0 To children.Count - 1
                If String.CompareOrdinal(children(i).Name, name) > 0 Then Return i
            Next
            Return children.Count
        End Function

        ''' <summary>Post-order fold of the subtree counts into the directory nodes.</summary>
        Private Shared Function Aggregate(node As ScanTreeNode) As (tokens As Long, files As Long, size As Long)
            If Not node.IsDirectory Then
                Return (node.TokenCount, node.FileCount, node.FileSize)
            End If

            Dim tokens As Long = 0
            Dim fileCount As Long = 0
            Dim size As Long = 0
            For Each child As ScanTreeNode In node.Children
                Dim childTotals = Aggregate(child)
                tokens += childTotals.tokens
                fileCount += childTotals.files
                size += childTotals.size
            Next
            node.TokenCount = tokens
            node.FileCount = fileCount
            node.FileSize = size
            Return (tokens, fileCount, size)
        End Function

        ' ------------------------------------------------------------------
        ' Path handling
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' Splits a reported path into segments, or returns False for a path that must be ignored:
        ''' empty, rooted (<c>/x</c> or <c>C:\x</c>) or escaping the root (<c>..</c>) / not a real
        ''' path (<c>.</c>). Both <c>/</c> and <c>\</c> separate segments, empty segments are dropped,
        ''' so <c>a\\b/</c> and <c>a/b</c> normalize to the same thing.
        ''' </summary>
        Private Shared Function TryNormalizeRelativePath(relativePath As String, ByRef segments As String()) As Boolean
            segments = Nothing
            If String.IsNullOrWhiteSpace(relativePath) Then Return False

            Dim normalized As String = relativePath.Replace("\"c, "/"c)
            If normalized.StartsWith("/", StringComparison.Ordinal) Then Return False
            If normalized.Length >= 2 AndAlso normalized(1) = ":"c Then Return False

            Dim parts As String() = normalized.Split("/"c, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Return False
            For Each part As String In parts
                If part = ".." OrElse part = "." Then Return False
            Next
            segments = parts
            Return True
        End Function

        ''' <summary>True when <paramref name="segments"/> is the directory <paramref name="dirSegments"/> or below it.</summary>
        Private Shared Function IsStrictlyInside(dirSegments As String(), segments As String()) As Boolean
            If segments.Length <= dirSegments.Length Then Return False
            For i As Integer = 0 To dirSegments.Length - 1
                If Not String.Equals(dirSegments(i), segments(i), StringComparison.OrdinalIgnoreCase) Then Return False
            Next
            Return True
        End Function

        ''' <summary>
        ''' True when a directory rescan of this batch already covered the normalized path - either
        ''' because it is that very directory or because it lives below it.
        ''' </summary>
        Private Shared Function IsCoveredByRescan(rescannedDirectories As List(Of String), normalizedPath As String) As Boolean
            For Each directoryPath As String In rescannedDirectories
                If normalizedPath.Equals(directoryPath, StringComparison.OrdinalIgnoreCase) Then Return True
                If normalizedPath.StartsWith(directoryPath & "/", StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        End Function

        ''' <summary>The first <paramref name="count"/> segments as a new array.</summary>
        Private Shared Function Take(segments As String(), count As Integer) As String()
            If segments.Length = count Then Return segments
            Dim head(count - 1) As String
            Array.Copy(segments, head, count)
            Return head
        End Function

        ''' <summary>The relative path in the platform's own separator (how <see cref="Path.Combine"/> expects it).</summary>
        Private Shared Function ToNativePath(segments As String()) As String
            Return String.Join("/", segments).Replace("/"c, Path.DirectorySeparatorChar)
        End Function

    End Class
End Namespace
