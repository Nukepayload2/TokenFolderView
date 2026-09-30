Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.IO
Imports Tokenizers

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public NotInheritable Class ProfileCountStagesTests

        Private ReadOnly DeepSeekPath As String = BundledTokenizerPath

        <TestMethod>
        Public Sub ProfileCountStages_AgreesWithEncodeCount()
            ' The diagnostic profile method mirrors EncodeCountCore; its token count must match the
            ' real EncodeCount so the two never drift apart. All read-only (tokenizer.json + in-memory).
            If Not File.Exists(DeepSeekPath) Then
                Assert.Fail("deepseek-v4-flash/tokenizer.json was not copied to the test output directory; check the Content item in TokenVisualizer.Core.Tests.vbproj")
                Return
            End If

            Dim tokenizer As Tokenizer = Tokenizer.FromFile(DeepSeekPath)
            Dim text As String =
                "Hello, 中文 world! 12345 <｜end▁of▁sentence｜> " &
                "The quick brown fox jumps over the lazy dog. 你好世界 こんにちは 🚀"

            Dim profile As EncodeCountStageProfile = tokenizer.ProfileCountStages(text)

            Assert.AreEqual(tokenizer.EncodeCount(text), profile.TokenCount)
            Assert.IsTrue(profile.ExtractTicks >= 0, "ExtractTicks")
            Assert.IsTrue(profile.PretokenizeTicks >= 0, "PretokenizeTicks")
            Assert.IsTrue(profile.ModelTicks >= 0, "ModelTicks")
            Assert.IsTrue(profile.ExtractAllocated >= 0, "ExtractAllocated")
        End Sub

    End Class

End Namespace
