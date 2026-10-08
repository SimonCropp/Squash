using System.Text.RegularExpressions;

/// <summary>
/// The documentation file, trimmed to match the assembly it describes. The first half is the
/// editing of the file on its own; the second is the task and the linker against a fixture whose
/// every member says whether it is expected to stay.
/// </summary>
public class DocumentationTests
{
    const string sample =
        """
        <?xml version="1.0"?>
        <doc>
            <assembly>
                <name>Lib</name>
            </assembly>
            <members>
                <member name="T:Lib.Kept">
                    <summary>
                    Kept, with <see cref="T:Lib.Gone"/> and &amp; in it.
                    </summary>
                </member>
                <member name="T:Lib.Gone">
                    <summary>Gone.</summary>
                </member>
                <member name="M:Lib.Kept.Method(System.Int32)">
                    <summary>Kept.</summary>
                    <param name="value">Kept.</param>
                </member>
                <member name="M:Lib.Kept.Method(System.String)">
                    <summary>Gone.</summary>
                    <param name="value">Gone.</param>
                </member>
            </members>
        </doc>

        """;

    static byte[] Bytes(string text) =>
        Encoding.UTF8.GetBytes(text);

    static string Trim(string xml, params string[] removed) =>
        Encoding.UTF8.GetString(Documentation.Trim(Bytes(xml), [.. removed]).Content);

    [Test]
    public async Task TheNamedEntriesGoAndTheRestIsUntouched()
    {
        var trimmed = Documentation.Trim(Bytes(sample), ["T:Lib.Gone", "M:Lib.Kept.Method(System.String)", "T:Lib.NeverThere"]);

        await Assert.That(trimmed.Removed).IsEqualTo(2);
        await Assert.That(Encoding.UTF8.GetString(trimmed.Content)).IsEqualTo(
            """
            <?xml version="1.0"?>
            <doc>
                <assembly>
                    <name>Lib</name>
                </assembly>
                <members>
                    <member name="T:Lib.Kept">
                        <summary>
                        Kept, with <see cref="T:Lib.Gone"/> and &amp; in it.
                        </summary>
                    </member>
                    <member name="M:Lib.Kept.Method(System.Int32)">
                        <summary>Kept.</summary>
                        <param name="value">Kept.</param>
                    </member>
                </members>
            </doc>

            """);
    }

    [Test]
    public async Task NothingToTakeOutGivesBackWhatWentIn()
    {
        var xml = Bytes(sample);
        var trimmed = Documentation.Trim(xml, ["T:Lib.NeverThere"]);

        await Assert.That(trimmed.Removed).IsEqualTo(0);
        await Assert.That(ReferenceEquals(trimmed.Content, xml)).IsTrue();
    }

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task LineEndingsStayAsTheyWere(string newline)
    {
        var xml = sample.ReplaceLineEndings(newline);
        var expected = Trim(sample.ReplaceLineEndings("\n"), "T:Lib.Gone").ReplaceLineEndings(newline);

        await Assert.That(Trim(xml, "T:Lib.Gone")).IsEqualTo(expected);
    }

    [Test]
    public async Task AByteOrderMarkStays()
    {
        byte[] xml = [0xEF, 0xBB, 0xBF, .. Bytes(sample)];
        var trimmed = Documentation.Trim(xml, ["T:Lib.Gone"]);

        await Assert.That(trimmed.Removed).IsEqualTo(1);
        await Assert.That(trimmed.Content.Take(4).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'<' })).IsTrue();
    }

    [Test]
    public async Task EveryMemberCanGo()
    {
        var trimmed = Trim(
            sample,
            "T:Lib.Kept",
            "T:Lib.Gone",
            "M:Lib.Kept.Method(System.Int32)",
            "M:Lib.Kept.Method(System.String)");

        await Assert.That(trimmed).IsEqualTo(
            """
            <?xml version="1.0"?>
            <doc>
                <assembly>
                    <name>Lib</name>
                </assembly>
                <members>
                </members>
            </doc>

            """);
    }

    [Test]
    public async Task WhatLooksLikeMarkupInsideAnEntryIsContent()
    {
        // Each would end the entry early, or start another, if it were read as markup.
        var xml =
            """
            <doc>
                <members>
                    <member name="T:A">
                        <summary><![CDATA[ </member> <member name="T:B"> ]]></summary>
                        <!-- </member> -->
                        <example title="a > b">
                            <member name="T:B">Not an entry: it is inside one.</member>
                        </example>
                    </member>
                    <member name="T:B">
                        <summary>The entry.</summary>
                    </member>
                </members>
            </doc>

            """;

        await Assert.That(Trim(xml, "T:B")).IsEqualTo(
            """
            <doc>
                <members>
                    <member name="T:A">
                        <summary><![CDATA[ </member> <member name="T:B"> ]]></summary>
                        <!-- </member> -->
                        <example title="a > b">
                            <member name="T:B">Not an entry: it is inside one.</member>
                        </example>
                    </member>
                </members>
            </doc>

            """);
        await Assert.That(Trim(xml, "T:A")).IsEqualTo(
            """
            <doc>
                <members>
                    <member name="T:B">
                        <summary>The entry.</summary>
                    </member>
                </members>
            </doc>

            """);
    }

    [Test]
    public async Task AMemberOutsideMembersIsNotAnEntry()
    {
        var xml =
            """
            <doc>
                <member name="T:A"/>
                <members>
                    <group>
                        <member name="T:A"/>
                    </group>
                </members>
            </doc>

            """;

        await Assert.That(Documentation.Trim(Bytes(xml), ["T:A"]).Removed).IsEqualTo(0);
    }

    [Test]
    public async Task ANameIsMatchedAsItWasBeforeItWasEscaped()
    {
        // How the compiler names the types behind an extension block.
        var xml =
            """
            <doc>
                <members>
                    <member name="T:Lib.Extensions.&lt;G&gt;$34505F.&lt;M&gt;$1ECAAA">
                        <summary>Gone.</summary>
                    </member>
                    <member name='M:Lib.A&#x26;B.&#67;(&quot;&apos;)'>
                        <summary>Gone.</summary>
                    </member>
                    <member name="T:Lib.&nonsense;">
                        <summary>Kept: no name is spelled that way.</summary>
                    </member>
                </members>
            </doc>

            """;

        var trimmed = Documentation.Trim(Bytes(xml), ["T:Lib.Extensions.<G>$34505F.<M>$1ECAAA", "M:Lib.A&B.C(\"')", "T:Lib.&nonsense;", "T:Lib."]);

        await Assert.That(trimmed.Removed).IsEqualTo(2);
        await Assert.That(Encoding.UTF8.GetString(trimmed.Content)).Contains("Kept: no name is spelled that way.");
    }

    [Test]
    public async Task AnEntryThatSharesItsLinesIsCutOutAlone()
    {
        var xml = "<doc><members><member name=\"T:A\"/><member name=\"T:B\">b</member> <member name=\"T:C\"/></members></doc>";

        await Assert.That(Trim(xml, "T:B")).IsEqualTo("<doc><members><member name=\"T:A\"/> <member name=\"T:C\"/></members></doc>");
        await Assert.That(Trim(xml, "T:A", "T:C")).IsEqualTo("<doc><members><member name=\"T:B\">b</member> </members></doc>");
    }

    [Test]
    [Arguments("<doc><members><member name=\"T:A\"></members></doc>")]
    [Arguments("<doc><members><member name=\"T:A\">")]
    [Arguments("<doc><members><member name=\"T:A></member></members></doc>")]
    [Arguments("<doc><members><member name=T:A></member></members></doc>")]
    [Arguments("<doc><members><!-- <member name=\"T:A\"/></members></doc>")]
    [Arguments("<!DOCTYPE doc><doc><members><member name=\"T:A\"/></members></doc>")]
    [Arguments("</doc>")]
    public async Task XmlThatCannotBeFollowedIsRefused(string xml) =>
        await Assert.That(() => Documentation.Trim(Bytes(xml), ["T:A"])).Throws<InvalidDataException>();

    [Test]
    public async Task AWiderEncodingIsRefused()
    {
        // Its markup is not single bytes, so none of it would be found, and nothing would be said.
        await Assert.That(() => Documentation.Trim(Encoding.Unicode.GetBytes(sample), ["T:Lib.Gone"])).Throws<InvalidDataException>();
        await Assert.That(() => Documentation.Trim([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(sample)], ["T:Lib.Gone"])).Throws<InvalidDataException>();
        await Assert.That(() => Documentation.Trim([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(sample)], ["T:Lib.Gone"])).Throws<InvalidDataException>();
    }

    static string[] frameworks = Trims.Frameworks;

    public static IEnumerable<string> Frameworks() =>
        frameworks;

    sealed record Entry(string Name, string Summary);

    /// <summary>
    /// Read by a real XML parser, which the code under test is not. The method the compiler makes
    /// from a member of an extension block has no summary of its own: it points at the member.
    /// </summary>
    static List<Entry> Entries(string path)
    {
        var members = XDocument.Load(path)
            .Root!
            .Element("members")!
            .Elements("member")
            .ToList();
        var summaries = members
            .Where(_ => _.Element("summary") != null)
            .ToDictionary(_ => (string)_.Attribute("name")!, _ => _.Element("summary")!.Value.Trim());
        return members
            .Select(_ => new Entry((string)_.Attribute("name")!, Summary(_)))
            .ToList();

        string Summary(XElement member)
        {
            var inherited = member.Element("inheritdoc");
            if (inherited == null)
            {
                return summaries[(string)member.Attribute("name")!];
            }

            return summaries.GetValueOrDefault((string)inherited.Attribute("cref")!, "");
        }
    }

    [Test]
    [MethodDataSource(nameof(Frameworks))]
    public async Task OnlyWhatIsLeftIsStillDocumented(string framework)
    {
        var trimmed = Trims.Documented(framework);
        await Assert.That(trimmed.Succeeded).IsTrue();

        // Each summary in the fixture says whether its member is expected to survive. An entry
        // that outlives its member is a name the step did not spell as the compiler does; an entry
        // that goes while its member stays is the failure this feature must never have.
        var before = Entries(trimmed.OriginalDocumentation);
        var expected = before
            .Where(_ => _.Summary.StartsWith("Kept.", StringComparison.Ordinal))
            .Select(_ => _.Name)
            .ToList();
        var after = Entries(trimmed.Documentation)
            .Select(_ => _.Name)
            .ToList();

        await Assert.That(before.Count(_ => _.Summary.StartsWith("Removed.", StringComparison.Ordinal))).IsEqualTo(before.Count - expected.Count);
        await Assert.That(expected.Count).IsGreaterThan(40);
        await Assert.That(before.Count - expected.Count).IsGreaterThan(80);
        await Assert.That(after.Except(expected)).IsEmpty();
        await Assert.That(expected.Except(after)).IsEmpty();
        await Assert.That(after.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task TheTrimmedFile()
    {
        var trimmed = Trims.Documented("netstandard2.0");

        await Verify(await File.ReadAllTextAsync(trimmed.Documentation), extension: "xml");
    }

    [Test]
    [MethodDataSource(nameof(Frameworks))]
    public async Task WhatIsLeftIsExactlyAsTheCompilerWroteIt(string framework)
    {
        var trimmed = Trims.Documented(framework);
        var removed = (await File.ReadAllLinesAsync(trimmed.RemovedDocumentationFile)).ToHashSet();

        // Done again a different way. The compiler gives each entry lines of its own, eight spaces
        // in, and indents what is inside further, so an entry runs to the next line that closes one
        // at that depth.
        var entry = new Regex("^ {8}<member name=\"([^\"]*)\">.*?^ {8}</member>\r?\n", RegexOptions.Singleline | RegexOptions.Multiline);
        var entries = 0;
        var expected = entry.Replace(await File.ReadAllTextAsync(trimmed.OriginalDocumentation), Keep);

        await Assert.That(await File.ReadAllTextAsync(trimmed.Documentation)).IsEqualTo(expected);
        await Assert.That(entries).IsEqualTo(Entries(trimmed.OriginalDocumentation).Count);

        string Keep(Match match)
        {
            entries++;
            if (removed.Contains(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)))
            {
                return "";
            }

            return match.Value;
        }
    }

    [Test]
    public async Task WhatTheCompilerWroteIsKeptBesideTheOtherOriginals()
    {
        var trimmed = Trims.Documented("netstandard2.0");
        var kept = await File.ReadAllBytesAsync(Path.Combine(trimmed.Directory, "in", "Documented.xml"));
        var original = await File.ReadAllBytesAsync(trimmed.OriginalDocumentation);

        await Assert.That(kept.SequenceEqual(original)).IsTrue();
        await Assert.That(new FileInfo(trimmed.Documentation).Length).IsLessThan(kept.Length);
    }

    [Test]
    public async Task TheSizesAreReported()
    {
        var trimmed = Trims.Documented("netstandard2.0");
        var removed = Entries(trimmed.OriginalDocumentation).Count - Entries(trimmed.Documentation).Count;
        var message = trimmed.Engine.Messages.Single(_ => _.Message!.StartsWith("Squash: Documented.xml ", StringComparison.Ordinal));

        await Assert.That(message.Message!).Contains($" bytes, {removed} entries removed.");
    }

    [Test]
    [MethodDataSource(nameof(Frameworks))]
    public async Task TheSameInputGivesTheSameDocumentation(string framework)
    {
        using var again = Trimmed.Run("Documented", framework);

        var first = await File.ReadAllBytesAsync(Trims.Documented(framework).Documentation);
        var second = await File.ReadAllBytesAsync(again.Documentation);

        await Assert.That(first.SequenceEqual(second)).IsTrue();
    }

    [Test]
    public async Task TurnedOffTheFileIsNotTouched()
    {
        using var trimmed = Trimmed.Run("Documented", "netstandard2.0", _ => _.TrimDocumentation = "false");

        await Assert.That(trimmed.Succeeded).IsTrue();
        var after = await File.ReadAllBytesAsync(trimmed.Documentation);
        var original = await File.ReadAllBytesAsync(trimmed.OriginalDocumentation);
        await Assert.That(after.SequenceEqual(original)).IsTrue();

        // The linker is not asked for a list nobody will read.
        await Assert.That(File.Exists(trimmed.RemovedDocumentationFile)).IsFalse();
        await Assert.That(trimmed.ResponseFile).DoesNotContain("RemovedDocumentation");
        await Assert.That(new FileInfo(trimmed.Assembly).Length).IsEqualTo(new FileInfo(Trims.Documented("netstandard2.0").Assembly).Length);
    }

    [Test]
    public async Task AnAssemblyWithNoDocumentationIsNotAskedAbout()
    {
        var trimmed = Trims.Scenarios("netstandard2.0");

        await Assert.That(File.Exists(trimmed.RemovedDocumentationFile)).IsFalse();
        await Assert.That(trimmed.ResponseFile).DoesNotContain("RemovedDocumentation");
    }

    [Test]
    public async Task AFileThatIsNotTheCompilersXmlIsLeftWithAWarning()
    {
        const string broken = "<doc><members><member name=\"T:Documented.Outer`1\">";
        using var trimmed = Trimmed.Run("Documented", "netstandard2.0", _ => File.WriteAllText(_.DocumentationFile, broken));

        // The assembly is trimmed all the same: the documentation is not worth failing a build for.
        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(trimmed.WarningCodes).Contains(Diagnostics.DocumentationNotTrimmed);
        await Assert.That(trimmed.Engine.Warnings.Single(_ => _.Code == Diagnostics.DocumentationNotTrimmed).Message!).Contains("'<member>' is never closed");
        await Assert.That(await File.ReadAllTextAsync(trimmed.Documentation)).IsEqualTo(broken);
        await Assert.That(new FileInfo(trimmed.Assembly).Length).IsLessThan(new FileInfo(trimmed.Original).Length);
    }

    [Test]
    public async Task AFailedTrimLeavesTheDocumentationAsItWas()
    {
        using var trimmed = Trimmed.Run(
            "Documented",
            "netstandard2.0",
            _ => _.RootDescriptors = [new TaskItem(Path.Combine(Path.GetTempPath(), "no-such-descriptor.xml"))]);

        await Assert.That(trimmed.Succeeded).IsFalse();
        var after = await File.ReadAllBytesAsync(trimmed.Documentation);
        var original = await File.ReadAllBytesAsync(trimmed.OriginalDocumentation);
        await Assert.That(after.SequenceEqual(original)).IsTrue();
    }
}
