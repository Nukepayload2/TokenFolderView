Imports System.Collections.Generic
Imports System.Linq

Namespace Internal

    ''' <summary>
    ''' A single trie node. Mirrors the Rust <c>Node { is_leaf, children }</c> from
    ''' <c>models/unigram/trie.rs</c>.
    ''' </summary>
    Public NotInheritable Class TrieNode(Of TLabel)
        Public IsLeaf As Boolean
        Public Children As Dictionary(Of TLabel, TrieNode(Of TLabel))

        Public Sub New()
            IsLeaf = False
            Children = New Dictionary(Of TLabel, TrieNode(Of TLabel))()
        End Sub
    End Class

    ''' <summary>
    ''' A generic trie keyed by labels. Faithful port of the Rust <c>Trie</c> /
    ''' <c>TrieBuilder</c> in <c>models/unigram/trie.rs</c>:
    ''' <see cref="Push"/> inserts a sequence, <see cref="CommonPrefixSearch"/> returns every
    ''' progressively-longer stored prefix of the input, and <see cref="FindLongestPrefix"/>
    ''' returns the end index of the longest stored prefix at an offset (0 if none).
    ''' </summary>
    Public Class Trie(Of TLabel)

        Private ReadOnly _root As New TrieNode(Of TLabel)()

        ''' <summary>The root node. Internal: used by <see cref="CharTrie"/> to walk the trie over a
        ''' <c>String</c> without copying it to a list first.</summary>
        Friend ReadOnly Property Root As TrieNode(Of TLabel)
            Get
                Return _root
            End Get
        End Property

        ''' <summary>Inserts a sequence of labels into the trie, marking the final node as a leaf.</summary>
        Public Sub Push(element As IEnumerable(Of TLabel))
            Dim node As TrieNode(Of TLabel) = _root
            For Each label In element
                Dim child As TrieNode(Of TLabel) = Nothing
                If Not node.Children.TryGetValue(label, child) Then
                    child = New TrieNode(Of TLabel)()
                    node.Children(label) = child
                End If
                node = child
            Next
            node.IsLeaf = True
        End Sub

        ''' <summary>
        ''' Mirrors the Rust <c>TrieIterator</c>: walks the input consuming labels and yields the
        ''' accumulated prefix each time a leaf node is reached, stopping as soon as the input
        ''' diverges from the trie (or the input runs out).
        ''' </summary>
        Public Function CommonPrefixSearch(input As IReadOnlyList(Of TLabel)) As List(Of List(Of TLabel))
            Dim result As New List(Of List(Of TLabel))()
            Dim node As TrieNode(Of TLabel) = _root
            Dim prefix As New List(Of TLabel)()
            For Each label In input
                Dim child As TrieNode(Of TLabel) = Nothing
                If Not node.Children.TryGetValue(label, child) Then Exit For
                node = child
                prefix.Add(label)
                If node.IsLeaf Then
                    result.Add(New List(Of TLabel)(prefix))
                End If
            Next
            Return result
        End Function

        ''' <summary>
        ''' Returns the end index (exclusive) into <paramref name="input"/> of the longest stored
        ''' prefix starting at <paramref name="start"/>, or 0 if there is no match. Mirrors the
        ''' common-prefix search used by the added-token matcher and unigram <c>common_prefix_search</c>.
        ''' </summary>
        Public Function FindLongestPrefix(input As IReadOnlyList(Of TLabel), start As Integer) As Integer
            If start < 0 OrElse start > input.Count Then Return 0
            Dim node As TrieNode(Of TLabel) = _root
            Dim lastMatchEnd As Integer = 0
            Dim i As Integer = start
            While i < input.Count
                Dim child As TrieNode(Of TLabel) = Nothing
                If Not node.Children.TryGetValue(input(i), child) Then Exit While
                node = child
                i += 1
                If node.IsLeaf Then lastMatchEnd = i
            End While
            Return lastMatchEnd
        End Function

        ''' <summary>Overload searching from the start of the input.</summary>
        Public Function FindLongestPrefix(input As IReadOnlyList(Of TLabel)) As Integer
            Return FindLongestPrefix(input, 0)
        End Function
    End Class

    ''' <summary>
    ''' Convenience trie over <c>Char</c> labels, used by the added-vocabulary leftmost-longest
    ''' matcher. Operates on UTF-16 code units (each <c>Char</c> is one label); use
    ''' <see cref="Trie(Of TLabel)"/> with <see cref="System.Text.Rune"/> labels for scalar-based
    ''' matching.
    ''' </summary>
    Public NotInheritable Class CharTrie

        Private ReadOnly _trie As New Trie(Of Char)()

        Public Sub Push(word As String)
            If word Is Nothing Then Return
            _trie.Push(word)
        End Sub

        Public Function CommonPrefixSearch(word As String) As List(Of String)
            Dim labels As List(Of Char) = If(word Is Nothing, New List(Of Char)(), word.ToList())
            Return _trie.CommonPrefixSearch(labels).
                Select(Function(ls) New String(ls.ToArray())).
                ToList()
        End Function

        ''' <summary>Returns the .NET end index (exclusive) of the longest match at <paramref name="startNetIndex"/>, 0 if none.</summary>
        Public Function FindLongestPrefix(word As String, startNetIndex As Integer) As Integer
            If word Is Nothing Then Return 0
            ' Walk the trie over the String directly (String has Length + an indexer but does
            ' NOT implement IReadOnlyList(Of Char)). The previous implementation converted the
            ' WHOLE string to a List(Of Char) on every call, which made the added-token matcher
            ' O(n^2) -- FindMatches calls this once per scalar position.
            Dim node As TrieNode(Of Char) = _trie.Root
            Dim lastMatchEnd As Integer = 0
            Dim i As Integer = startNetIndex
            While i < word.Length
                Dim child As TrieNode(Of Char) = Nothing
                If Not node.Children.TryGetValue(word(i), child) Then Exit While
                node = child
                i += 1
                If node.IsLeaf Then lastMatchEnd = i
            End While
            Return lastMatchEnd
        End Function

        ''' <summary>Alias for <see cref="FindLongestPrefix"/> used by the added-vocabulary matcher.</summary>
        Public Function LongestPrefixAt(text As String, startIndex As Integer) As Integer
            Return FindLongestPrefix(text, startIndex)
        End Function
    End Class

End Namespace
