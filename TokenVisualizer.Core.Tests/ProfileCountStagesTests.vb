Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.IO
Imports Tokenizers

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public NotInheritable Class ProfileCountStagesTests

        Private Const DeepSeekPath As String =
            "C:\Users\james\Projects\TokenVisualizer\deepseek-v4-flash\tokenizer.json"

        <TestMethod>
        Public Sub ProfileCountStages_AgreesWithEncodeCount()
            ' The diagnostic profile method mirrors EncodeCountCore; its token count must match the
            ' real EncodeCount so the two never drift apart. All read-only (tokenizer.json + in-memory).
            If Not File.Exists(DeepSeekPath) Then
                Assert.Inconclusive("deepseek tokenizer.json not present")
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
