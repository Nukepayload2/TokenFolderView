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

        <TestMethod>
        Public Sub LargeCapacityPreallocatesWithoutError()
            ' #34: the internal Dictionary/Queue must accept the requested capacity up front
            ' (including the 32K sweep value) without error.
            Dim c As New Cache(Of String, Integer)(capacity:=32000)
            Assert.AreEqual(32000, c.Capacity)
            For i As Integer = 0 To 999
                c.Insert(i.ToString(), i)
            Next
            Assert.AreEqual(1000, c.Count)
        End Sub

        <TestMethod>
        Public Sub StatsDisabledByDefault_CountersStayZero()
            Dim c As New Cache(Of String, Integer)(capacity:=10)
            Assert.IsFalse(c.StatsEnabled)
            c.GetValue("a")      ' would be a miss
            c.Insert("a", 1)     ' an insert
            c.GetValue("a")      ' would be a hit
            c.RecordSkip()       ' would be a skip
            Dim s As CacheStats = c.GetStats()
            Assert.AreEqual(0, s.Hits + s.Misses + s.Skips + s.Evictions)
        End Sub

        <TestMethod>
        Public Sub StatsEnabled_CountsHitsMissesAndEvictions()
            Dim c As New Cache(Of String, Integer)(capacity:=3, enableStats:=True)
            c.Insert("k1", 1)
            c.Insert("k2", 2)
            c.Insert("k3", 3)
            c.Insert("k4", 4) ' at capacity -> evicts k1

            c.GetValue("k2")  ' hit
            c.GetValue("zz")  ' miss

            Dim s As CacheStats = c.GetStats()
            Assert.AreEqual(1, s.Hits)
            Assert.AreEqual(1, s.Misses)
            Assert.AreEqual(1, s.Evictions)
            Assert.AreEqual(0, s.Skips)
        End Sub

        <TestMethod>
        Public Sub RecordSkip_CountsOnlyWhenEnabled()
            Dim c As New Cache(Of String, Integer)(capacity:=10, enableStats:=True)
            c.RecordSkip()
            c.RecordSkip()
            Assert.AreEqual(2, c.GetStats().Skips)

            c.ResetStats()
            Assert.AreEqual(0, c.GetStats().Skips)
            Assert.AreEqual(0, c.GetStats().Hits + c.GetStats().Misses + c.GetStats().Evictions)
        End Sub

    End Class

End Namespace
