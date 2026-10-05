using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CodeJanitor.UnitTests.Cleaning;

/// <summary>
/// Executes the shared transformation fixture corpus from shared/tests/transformations, the same
/// JSON files the VS Code extension runs in test/sharedTransformationCorpus.test.ts. Fixtures
/// express cross-platform behavior; a failing case here means the two implementations diverged.
/// </summary>
[TestClass]
public sealed class SharedTransformationCorpusTests
{
    private string _corpusDirectory;

    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
        Settings.Default.Cleaning_AiXmlDocumentationEnabled = false;
        _corpusDirectory = LocateCorpusDirectory();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Settings.Default.Reset();
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void SharedFixtures_ProduceExpectedBehavior()
    {
        var fixtureFiles = Directory.GetFiles(_corpusDirectory, "*.json");
        Assert.IsTrue(fixtureFiles.Length > 0, "No shared transformation fixtures found in " + _corpusDirectory);

        var failures = new List<string>();

        foreach (var fixtureFile in fixtureFiles)
        {
            var fixture = ParseFixture(fixtureFile);
            var fileName = Path.GetFileNameWithoutExtension(fixtureFile);
            var settings = GetSection(fixture, "settings");
            var sampleFile = WriteFixtureFile(fixture, settings, fileName);

            try
            {
                // The written .codejanitor policy drives the pipeline settings for this file.
                var output = CodeCleanupManager.ApplyHeadlessCSharpTransformations(GetInput(fixture, settings), sampleFile);

                foreach (var expected in GetStringList(fixture, "mustContain"))
                {
                    if (!output.Contains(expected))
                    {
                        failures.Add($"{fileName}: expected output to contain '{Abbreviate(expected)}'.");
                    }
                }

                foreach (var forbidden in GetStringList(fixture, "mustNotContain"))
                {
                    if (output.Contains(forbidden))
                    {
                        failures.Add($"{fileName}: expected output NOT to contain '{Abbreviate(forbidden)}'.");
                    }
                }
            }
            finally
            {
                DeleteFixtureDirectory(sampleFile);
            }
        }

        Assert.AreEqual(0, failures.Count, fixtureFiles.Length + " fixtures executed, failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Locates the shared corpus by walking up from the test assembly directory.
    /// </summary>

    private static string LocateCorpusDirectory()
    {
        var directory = Path.GetDirectoryName(typeof(SharedTransformationCorpusTests).Assembly.Location);

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, "shared", "tests", "transformations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        Assert.Fail("Could not locate the shared transformation corpus directory.");
        return null;
    }

    /// <summary>
    /// Parses a fixture file into a dictionary, failing with the file name on invalid JSON.
    /// </summary>

    private static Dictionary<string, object> ParseFixture(string fixtureFile)
    {
        try
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(fixtureFile));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Fixture '{Path.GetFileName(fixtureFile)}' is not valid JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes the fixture input into an isolated directory together with a .codejanitor policy when
    /// the fixture defines settings, so the pipeline sees the same configuration as the harness.
    /// </summary>

    private static string WriteFixtureFile(Dictionary<string, object> fixture, Dictionary<string, object> settings, string fileName)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "CodeJanitor.SharedCorpus", Guid.NewGuid().ToString("N")));

        if (settings.Count > 0)
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, RepositoryCleanupSettings.PrimaryConfigFileName),
                new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    ["cleanup"] = new Dictionary<string, object>(settings, StringComparer.Ordinal),
                }));
        }

        var filePath = Path.Combine(directory.FullName, fileName + ".cs");
        File.WriteAllText(filePath, GetInput(fixture, settings));
        return filePath;
    }

    /// <summary>
    /// Deletes the temporary fixture directory.
    /// </summary>

    private static void DeleteFixtureDirectory(string sampleFile)
    {
        try
        {
            var directory = Path.GetDirectoryName(sampleFile);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary files are best-effort cleanup; do not fail the assertion path.
        }
    }

    /// <summary>
    /// Gets the fixture input, normalizing newlines to LF when the fixture requests it.
    /// </summary>

    private static string GetInput(Dictionary<string, object> fixture, IReadOnlyDictionary<string, object> settings)
    {
        var input = fixture.TryGetValue("input", out var value) ? (string)value : string.Empty;
        return IsLf(settings) ? input.Replace("\r\n", "\n") : input;
    }

    /// <summary>
    /// Gets the fixture's mustContain/mustNotContain lists with newline normalization applied.
    /// </summary>

    private static List<string> GetStringList(Dictionary<string, object> fixture, string key)
    {
        var result = new List<string>();
        var lf = IsLf(GetSection(fixture, "settings"));

        if (fixture.TryGetValue(key, out var value) && value is object[] items)
        {
            foreach (var item in items)
            {
                var text = (string)item;
                result.Add(lf ? text.Replace("\r\n", "\n") : text);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets a fixture section as a dictionary.
    /// </summary>

    private static Dictionary<string, object> GetSection(Dictionary<string, object> fixture, string key)
    {
        return fixture.TryGetValue(key, out var value) && value is Dictionary<string, object> section
            ? section
            : new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Determines whether the fixture requests LF newlines.
    /// </summary>

    private static bool IsLf(IReadOnlyDictionary<string, object> settings)
    {
        return settings.TryGetValue("newlines", out var value) && string.Equals(value as string, "lf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Shortens a substring for readable assertion messages.
    /// </summary>

    private static string Abbreviate(string value)
    {
        var escaped = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return escaped.Length > 60 ? escaped.Substring(0, 60) + "..." : escaped;
    }
}
