Imports System.Linq
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class TrieTests

        <TestMethod>
        Public Sub CommonPrefixSearch_ReturnsProgressivelyLongerPrefixes()
            Dim trie As New Trie(Of Char)()
            trie.Push("a")
            trie.Push("ab")
            trie.Push("abc")

            Dim results = trie.CommonPrefixSearch("abcd".ToList())
            Assert.HasCount(3, results)
            Assert.AreEqual("a", New String(results(0).ToArray()))
            Assert.AreEqual("ab", New String(results(1).ToArray()))
            Assert.AreEqual("abc", New String(results(2).ToArray()))
        End Sub

        <TestMethod>
        Public Sub CommonPrefixSearch_StopsWhenInputDiverges()
            Dim trie As New Trie(Of Char)()
            trie.Push("a")
            trie.Push("ab")
            trie.Push("abc")

            Dim results = trie.CommonPrefixSearch("abd".ToList())
            Assert.HasCount(2, results)
            Assert.AreEqual("a", New String(results(0).ToArray()))
            Assert.AreEqual("ab", New String(results(1).ToArray()))
        End Sub

        <TestMethod>
        Public Sub CommonPrefixSearch_EmptyInputYieldsNothing()
            Dim trie As New Trie(Of Char)()
            trie.Push("abc")
            Assert.HasCount(0, trie.CommonPrefixSearch(New List(Of Char)()))
        End Sub

        <TestMethod>
        Public Sub CommonPrefixSearch_NoMatchYieldsNothing()
            Dim trie As New Trie(Of Char)()
            trie.Push("b")
            Assert.HasCount(0, trie.CommonPrefixSearch("abc".ToList()))
        End Sub

        <TestMethod>
        Public Sub FindLongestPrefix_AtOffset()
            Dim trie As New Trie(Of Char)()
            trie.Push("a")
            trie.Push("ab")
            trie.Push("abc")

            Dim input As List(Of Char) = "xabcd".ToList()
            Assert.AreEqual(4, trie.FindLongestPrefix(input, 1))
            Assert.AreEqual(0, trie.FindLongestPrefix(input, 0))
            Assert.AreEqual(0, trie.FindLongestPrefix(input, 99))
        End Sub

        <TestMethod>
        Public Sub FindLongestPrefix_ShorterStoredPrefixWinsWhenLongerDiverges()
            Dim trie As New Trie(Of Char)()
            trie.Push("ab")

            ' "abc": 'c' diverges; longest stored prefix is "ab".
            Assert.AreEqual(2, trie.FindLongestPrefix("abc".ToList(), 0))
        End Sub

        <TestMethod>
        Public Sub CharTrie_CommonPrefixSearch()
            Dim ct As New CharTrie()
            ct.Push("hello")
            ct.Push("hi")
            ct.Push("hey")

            Dim results = ct.CommonPrefixSearch("hello world")
            Assert.HasCount(1, results)
            Assert.AreEqual("hello", results(0))

            Dim none = ct.CommonPrefixSearch("zzz")
            Assert.HasCount(0, none)
        End Sub

        <TestMethod>
        Public Sub CharTrie_LongestPrefixAt()
            Dim ct As New CharTrie()
            ct.Push("a")
            ct.Push("ab")
            ct.Push("abc")

            Assert.AreEqual(3, ct.LongestPrefixAt("abc", 0))
            Assert.AreEqual(4, ct.LongestPrefixAt("xabc", 1))
            Assert.AreEqual(0, ct.LongestPrefixAt("z", 0))
            Assert.AreEqual(0, ct.LongestPrefixAt("zz", 1))
            Assert.AreEqual(0, ct.LongestPrefixAt("abc", 99))
        End Sub

        <TestMethod>
        Public Sub TrieDistinguishesLeafFromPrefix()
            ' Inserting "ab" must not make "a" a leaf.
            Dim trie As New Trie(Of Char)()
            trie.Push("ab")
            Assert.AreEqual(0, trie.FindLongestPrefix("a".ToList(), 0))
            Assert.AreEqual(2, trie.FindLongestPrefix("ab".ToList(), 0))
        End Sub

    End Class

End Namespace
