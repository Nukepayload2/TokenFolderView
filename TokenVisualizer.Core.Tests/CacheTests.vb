Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class CacheTests

        <TestMethod>
        Public Sub DefaultCapacityIs10000()
            Dim c As New Cache(Of String, Integer)()
            Assert.AreEqual(10000, c.Capacity)
            Assert.AreEqual(0, c.Count)
        End Sub

        <TestMethod>
        Public Sub InsertAndGetValue()
            Dim c As New Cache(Of String, Integer)(capacity:=10)
            c.Insert("a", 1)
            c.Insert("b", 2)

            Assert.AreEqual(1, c.GetValue("a"))
            Assert.AreEqual(2, c.GetValue("b"))
            Assert.IsTrue(c.Contains("a"))
            Assert.IsFalse(c.Contains("z"))
            Assert.AreEqual(2, c.Count)
        End Sub

        <TestMethod>
        Public Sub GetWithCreateFnCachesResult()
            Dim c As New Cache(Of String, Integer)(capacity:=10)
            Dim calls As Integer = 0
            Dim fn As Func(Of String, Integer) = Function(k)
                                                     calls += 1
                                                     Return 42
                                                 End Function

            Assert.AreEqual(42, c.Get("a", fn))
            Assert.AreEqual(42, c.Get("a", fn))
            Assert.AreEqual(1, calls, "createFn must be called only once")
        End Sub

        <TestMethod>
        Public Sub CapacityEviction_EvictsOldestInsert()
            Dim c As New Cache(Of String, Integer)(capacity:=3)
            c.Insert("k1", 1)
            c.Insert("k2", 2)
            c.Insert("k3", 3)

            c.Insert("k4", 4) ' at capacity -> evicts oldest (k1)

            Assert.IsFalse(c.Contains("k1"), "k1 should be evicted")
            Assert.AreEqual(2, c.GetValue("k2"))
            Assert.AreEqual(3, c.GetValue("k3"))
            Assert.AreEqual(4, c.GetValue("k4"))
            Assert.AreEqual(3, c.Count)
        End Sub

        <TestMethod>
        Public Sub ReInsertingExistingKeyUpdatesValueInPlace()
            Dim c As New Cache(Of String, Integer)(capacity:=3)
            c.Insert("k1", 1)
            c.Insert("k2", 2)
            c.Insert("k3", 3)

            ' Re-inserting an existing key updates its value without changing its eviction order.
            c.Insert("k1", 10)
            Assert.AreEqual(10, c.GetValue("k1"))

            ' The next insert still evicts k1: it remains the oldest insertion.
            c.Insert("k4", 4)
            Assert.IsFalse(c.Contains("k1"), "k1 (oldest) should be evicted")
            Assert.AreEqual(2, c.GetValue("k2"))
            Assert.AreEqual(3, c.GetValue("k3"))
            Assert.AreEqual(4, c.GetValue("k4"))
        End Sub

        <TestMethod>
        Public Sub ClearRemovesEverything()
            Dim c As New Cache(Of String, Integer)(capacity:=10)
            c.Insert("a", 1)
            c.Insert("b", 2)
            c.Clear()

            Assert.AreEqual(0, c.Count)
            Assert.IsFalse(c.Contains("a"))
            Assert.AreEqual(0, c.GetValue("a"))
        End Sub

        <TestMethod>
        Public Sub ZeroCapacityInsertsNothing()
            Dim c As New Cache(Of String, Integer)(capacity:=0)
            c.Insert("a", 1)
            Assert.AreEqual(0, c.Count)
            Assert.IsFalse(c.Contains("a"))
        End Sub

    End Class

End Namespace
