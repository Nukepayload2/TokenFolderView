Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class SplitBehaviorTests

        Private Shared Function SplitStrings(s As NormalizedString, pattern As Pattern, behavior As SplitDelimiterBehavior) As List(Of String)
            Dim result As New List(Of String)()
            For Each part In s.Split(pattern, behavior)
                result.Add(part.Get)
            Next
            Return result
        End Function

        <TestMethod>
        Public Sub Split_Removed()
            Dim s As NormalizedString = NormalizedString.FromString("The-final--countdown")
            Dim actual As List(Of String) = SplitStrings(s, New StringPattern("-"), SplitDelimiterBehavior.Removed)
            CollectionAssert.AreEqual(New String() {"The", "final", "countdown"}, actual)
        End Sub

        <TestMethod>
        Public Sub Split_Isolated()
            Dim s As NormalizedString = NormalizedString.FromString("The-final--countdown")
            Dim actual As List(Of String) = SplitStrings(s, New StringPattern("-"), SplitDelimiterBehavior.Isolated)
            CollectionAssert.AreEqual(New String() {"The", "-", "final", "-", "-", "countdown"}, actual)
        End Sub

        <TestMethod>
        Public Sub Split_MergedWithPrevious()
            Dim s As NormalizedString = NormalizedString.FromString("The-final--countdown")
            Dim actual As List(Of String) = SplitStrings(s, New StringPattern("-"), SplitDelimiterBehavior.MergedWithPrevious)
            CollectionAssert.AreEqual(New String() {"The-", "final-", "-", "countdown"}, actual)
        End Sub

        <TestMethod>
        Public Sub Split_MergedWithNext()
            Dim s As NormalizedString = NormalizedString.FromString("The-final--countdown")
            Dim actual As List(Of String) = SplitStrings(s, New StringPattern("-"), SplitDelimiterBehavior.MergedWithNext)
            CollectionAssert.AreEqual(New String() {"The", "-final", "-", "-countdown"}, actual)
        End Sub

        <TestMethod>
        Public Sub Split_Contiguous()
            Dim s As NormalizedString = NormalizedString.FromString("The-final--countdown")
            Dim actual As List(Of String) = SplitStrings(s, New StringPattern("-"), SplitDelimiterBehavior.Contiguous)
            CollectionAssert.AreEqual(New String() {"The", "-", "final", "--", "countdown"}, actual)
        End Sub

        <TestMethod>
        Public Sub Split_KeepsOriginalAlignment()
            ' After splitting, each slice maps back to the correct original text.
            Dim s As NormalizedString = NormalizedString.FromString("The-final--countdown")
            Dim parts As List(Of NormalizedString) = s.Split(New StringPattern("-"), SplitDelimiterBehavior.Removed)
            Assert.HasCount(3, parts)
            Assert.AreEqual("The", parts(0).Original)
            Assert.AreEqual("final", parts(1).Original)
            Assert.AreEqual("countdown", parts(2).Original)
        End Sub

    End Class

End Namespace
