using Catch2OpenFolder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UT_OpenFolder
{
    [TestClass]
    public class TestDeclarationLocatorTests
    {
        [TestMethod]
        public void TwoLineTagsAnchorToMacroRatherThanReportedClosingLine()
        {
            const string source = "// header\n  TEST_CASE(\"a test\",\n            \"[tag]\")\n{ }";
            Assert.AreEqual(source.IndexOf("TEST_CASE"), TestDeclarationLocator.FindAnchor(source, 3));
        }

        [DataTestMethod]
        [DataRow("TEST_CASE")]
        [DataRow("TEST_CASE_METHOD")]
        [DataRow("SCENARIO")]
        [DataRow("CATCH_TEST_CASE")]
        [DataRow("CATCH_SCENARIO_METHOD")]
        [DataRow("TEMPLATE_TEST_CASE")]
        [DataRow("TEMPLATE_TEST_CASE_METHOD_SIG")]
        [DataRow("TEMPLATE_PRODUCT_TEST_CASE_METHOD")]
        [DataRow("TEMPLATE_LIST_TEST_CASE")]
        public void KnownTestMacrosCanSpanSeveralLines(string macro)
        {
            var source = "\r\n" + macro + "\r\n(\r\n    \"test\",\r\n    \"[tags]\"\r\n)\r\n{}";
            Assert.AreEqual(2, TestDeclarationLocator.FindAnchor(source, 6));
            Assert.AreEqual(2, TestDeclarationLocator.FindAnchor(source, 2));
        }

        [TestMethod]
        public void ParenthesesInStringsCommentsAndCharacterLiteralsAreIgnored()
        {
            const string source = "TEST_CASE(\"test ) \\\" (\", /* ) */\n MakeTag(')', '('))\n{}";
            Assert.AreEqual(0, TestDeclarationLocator.FindAnchor(source, 2));
        }

        [DataTestMethod]
        [DataRow("R")]
        [DataRow("u8R")]
        [DataRow("LR")]
        public void RawStringContentsDoNotEndTheMacro(string prefix)
        {
            var source = "TEST_CASE(" + prefix + "\"tag(a ) \" TEST_CASE( fake )tag\",\n \"[tag]\")\n{}";
            Assert.AreEqual(0, TestDeclarationLocator.FindAnchor(source, 2));
        }

        [TestMethod]
        public void CommentsAndStringContentsAreNotTestDeclarations()
        {
            const string source = "/* TEST_CASE(\n fake ) */\nconst char* s = R\"(TEST_CASE(\n fake ))\";\nCUSTOM_TEST(\"actual\",\n \"[tag]\")\n{}";
            Assert.AreEqual(source.LastIndexOf("\"[tag]\""), TestDeclarationLocator.FindAnchor(source, 6));
        }

        [TestMethod]
        public void DigitSeparatorsDoNotHideFollowingDeclarations()
        {
            const string source = "constexpr auto x = 1'000 + 0xFF'AB;\nTEST_CASE(\"test\",\n\"[tags]\")\n{}";
            Assert.AreEqual(source.IndexOf("TEST_CASE"), TestDeclarationLocator.FindAnchor(source, 3));
        }

        [TestMethod]
        public void DoesNotMoveAReportedBodyLineToAnEarlierDeclaration()
        {
            const string source = "TEST_CASE(\"first\",\n \"[tag]\")\n{\n  custom_registration();\n}";
            Assert.AreEqual(source.IndexOf("custom_registration"), TestDeclarationLocator.FindAnchor(source, 4));
        }

        [TestMethod]
        public void SelectsTheCorrectDeclarationAmongSeveralTests()
        {
            const string source = "TEST_CASE(\"first\",\n \"[tag]\") {}\n\nTEST_CASE(\"second\",\n \"[tag]\") {}";
            Assert.AreEqual(source.LastIndexOf("TEST_CASE"), TestDeclarationLocator.FindAnchor(source, 5));
        }

        [TestMethod]
        public void IncompleteMacroUsesReportedLineWithoutGuessing()
        {
            const string source = "TEST_CASE(\"editing\",\n \"[tag]\"\n";
            Assert.AreEqual(source.IndexOf("\"[tag]\""), TestDeclarationLocator.FindAnchor(source, 2));
        }

        [TestMethod]
        public void SingleLineDeclarationKeepsItsAnchor()
        {
            const string source = "\r\n    TEST_CASE(\"test\", \"[tag]\") {}";
            Assert.AreEqual(6, TestDeclarationLocator.FindAnchor(source, 2));
        }

        [DataTestMethod]
        [DataRow(null, 1)]
        [DataRow("", 1)]
        [DataRow("code", 0)]
        [DataRow("code", 2)]
        [DataRow("code\n  \n", 2)]
        public void MissingOrInvalidLocationsHaveNoAnchor(string source, int line)
        {
            Assert.AreEqual(-1, TestDeclarationLocator.FindAnchor(source, line));
        }
    }
}
