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
    ''' Dedicated node for <see cref="CharTrie"/>. ASCII children (Char &lt; 128) are indexed by a
    ''' lazily-allocated 128-slot array — a direct array index instead of a Dictionary hash+probe on
    ''' the per-position hot walk — while non-ASCII children fall back to <see cref="Children"/>.
    ''' </summary>
    Friend NotInheritable Class CharTrieNode
        Friend IsLeaf As Boolean
        Friend AsciiChildren As CharTrieNode()
        Friend Children As Dictionary(Of Char, CharTrieNode)

        Public Sub New()
            IsLeaf = False
            AsciiChildren = Nothing
            Children = New Dictionary(Of Char, CharTrieNode)()
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
    '''
    ''' #28 ASCII fast channel: real code is nearly all ASCII, so each node stores its ASCII
    ''' children (Char &lt; 128) in a lazily-allocated 128-slot array indexed directly by the char
    ''' code unit — a single array load instead of a <c>Dictionary</c> hash+probe on the per-position
    ''' hot walk. Non-ASCII children fall back to a <see cref="CharTrieNode.Children"/> dictionary.
    ''' The trie semantics are identical to the generic <see cref="Trie(Of Char)"/>; the two are kept
    ''' in lockstep by the M13B differential test.
    ''' </summary>
    Public NotInheritable Class CharTrie

        Private ReadOnly _root As New CharTrieNode()

        Public Sub Push(word As String)
            If word Is Nothing Then Return
            Dim node As CharTrieNode = _root
            For Each c As Char In word
                If Char.IsAscii(c) Then
                    ' ASCII fast channel: lazy 128-slot array, indexed directly by the code unit.
                    If node.AsciiChildren Is Nothing Then
                        node.AsciiChildren = New CharTrieNode(127) {}
                    End If
                    Dim idx As Integer = AscW(c)
                    Dim child As CharTrieNode = node.AsciiChildren(idx)
                    If child Is Nothing Then
                        child = New CharTrieNode()
                        node.AsciiChildren(idx) = child
                    End If
                    node = child
                Else
                    ' Non-ASCII: fall back to the dictionary.
                    Dim child As CharTrieNode = Nothing
                    If Not node.Children.TryGetValue(c, child) Then
                        child = New CharTrieNode()
                        node.Children(c) = child
                    End If
                    node = child
                End If
            Next
            node.IsLeaf = True
        End Sub

        Public Function CommonPrefixSearch(word As String) As List(Of String)
            Dim result As New List(Of String)()
            If word Is Nothing Then Return result
            Dim node As CharTrieNode = _root
            For i As Integer = 0 To word.Length - 1
                Dim c As Char = word(i)
                Dim child As CharTrieNode = Nothing
                If node.AsciiChildren IsNot Nothing AndAlso Char.IsAscii(c) Then
                    child = node.AsciiChildren(AscW(c))
                Else
                    node.Children.TryGetValue(c, child)
                End If
                If child Is Nothing Then Exit For
                node = child
                If node.IsLeaf Then
                    result.Add(word.Substring(0, i + 1))
                End If
            Next
            Return result
        End Function

        ''' <summary>Returns the .NET end index (exclusive) of the longest match at <paramref name="startNetIndex"/>, 0 if none.</summary>
        Public Function FindLongestPrefix(word As String, startNetIndex As Integer) As Integer
            If word Is Nothing Then Return 0
            If startNetIndex < 0 OrElse startNetIndex > word.Length Then Return 0
            ' Walk the trie over the String directly (String has Length + an indexer but does
            ' NOT implement IReadOnlyList(Of Char)), preferring the ASCII fast channel: each node's
            ' ASCII children are indexed directly by code unit (a plain array load), while
            ' non-ASCII children use the dictionary. Semantically identical to the generic
            ' Trie(Of Char).FindLongestPrefix.
            Dim node As CharTrieNode = _root
            Dim lastMatchEnd As Integer = 0
            Dim i As Integer = startNetIndex
            While i < word.Length
                Dim c As Char = word(i)
                Dim child As CharTrieNode = Nothing
                If node.AsciiChildren IsNot Nothing AndAlso Char.IsAscii(c) Then
                    child = node.AsciiChildren(AscW(c))
                Else
                    node.Children.TryGetValue(c, child)
                End If
                If child Is Nothing Then Exit While
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
