using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Build.Locator;
using Microsoft.VisualBasic.FileIO;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Serilog;

namespace NuExporter.Test
{
    public abstract class BaseFixture
    {
        public string TestClassName => TestContext.CurrentContext.Test.ClassName?.Split('.').Last();
        public string TestMethodName => TestContext.CurrentContext.Test.MethodName;

        private AsyncLocal<string> _testInput = new();
        private AsyncLocal<string> _testOutput = new();

        private bool _validateOutput;
        private bool _copyInputToOutput;

        protected string TestDataInput
        {
            get { return _testInput.Value; }
            set { _testInput.Value = value; }
        }

        protected string TestDataOutput
        {
            get { return _testOutput.Value; }
            set { _testOutput.Value = value; }
        }

        static BaseFixture()
        {
            MSBuildLocator.RegisterDefaults();
            Log.Logger = new LoggerConfiguration()
                .WriteTo.NUnitOutput()
                .CreateLogger();
        }

        public BaseFixture(bool validateOutput = true, bool copyInputToOutput = true)
        {
            _validateOutput = validateOutput;
            _copyInputToOutput = copyInputToOutput;
        }

        internal string GetTestDataRoot()
        {
            return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "TestData",
                TestClassName, TestMethodName);
        }

        [SetUp]
        public void TestSetUp()
        {
            var tmpRoot = Path.GetTempPath();
            TestDataInput = Path.Combine(GetTestDataRoot(), "in");
            if (!Directory.Exists(TestDataInput))
                Assert.Fail($"'{TestDataInput}' doesn't exist.");

            TestDataOutput = Path.Combine(tmpRoot, Assembly.GetExecutingAssembly().GetName().Name,
                TestClassName, TestMethodName);

            if (Directory.Exists(TestDataOutput))
                Directory.Delete(TestDataOutput, recursive: true);

            if (_copyInputToOutput)
                FileSystem.CopyDirectory(TestDataInput, TestDataOutput);

            Log.Information("TestDataInput: {Path}", TestDataInput);
            Log.Information("TestDataOutput: {Path}", TestDataOutput);
        }

        [TearDown]
        public void TestTearDown()
        {
            if (!_validateOutput)
                return;

            if (TestContext.CurrentContext.Result.Outcome != ResultState.Success)
                return;

            var testDataExpectedRoot = Path.Combine(GetTestDataRoot(), "out");

            // Take a snapshot of the file system.
            var expectedFiles = new DirectoryInfo(testDataExpectedRoot)
                .GetFiles("*", System.IO.SearchOption.AllDirectories)
                .ToDictionary(x => Path.GetRelativePath(testDataExpectedRoot, x.FullName));

            var actualFiles = new DirectoryInfo(TestDataOutput)
                .GetFiles("*", System.IO.SearchOption.AllDirectories)
                .ToDictionary(x => Path.GetRelativePath(TestDataOutput, x.FullName));

            Assert.That(actualFiles.Keys, Is.EquivalentTo(expectedFiles.Keys));

            foreach (var (fileKey, _) in actualFiles)
            {
                var actualFileInfo = actualFiles[fileKey];
                var expectedFileInfo = expectedFiles[fileKey];

                DiffFiles(fileKey, expectedFiles[fileKey].FullName, actualFiles[fileKey].FullName);
            }
        }

        internal virtual void DiffFiles(string fileId, string expectedFile, string actualFile)
        {
            string ReadAndSanitize(string path) => string.Join("\r\n",
                File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x)));

            var actualContent = ReadAndSanitize(actualFile);
            var expectedContent = ReadAndSanitize(expectedFile);
            var diff = InlineDiffBuilder.Diff(expectedContent, actualContent);
            if (diff.HasDifferences)
            {
                var lines = diff.Lines.Select(x =>
                {
                    switch (x.Type)
                    {
                        case ChangeType.Inserted:
                            return $"+ {x.Text}";

                        case ChangeType.Deleted:
                            return $"- {x.Text}";

                        default:
                            return $"  {x.Text}";
                    }
                });

                var txt = string.Join(Environment.NewLine, lines);
                Assert.Fail($"Unexpected difference in {fileId}:" + Environment.NewLine + txt);
            }
        }
    }
}
