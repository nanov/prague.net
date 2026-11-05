namespace Prague.Core.Tests;

using System.Text.RegularExpressions;

/// <summary>
///   Shape checks for the agent skills under <c>/skills</c> that ship inside the NuGet packages
///   (agentskills.io SKILL.md). Facts in the skill bodies are not checked here; they are kept in sync by hand.
/// </summary>
[TestFixture]
public partial class AgentSkillsTests {
	// Packed by src/Prague/Prague.csproj only (one package per skill). It packs skills/**/SKILL.md; this list makes adding or renaming a skill a deliberate change.
	private static readonly string[] _expectedSkills = ["prague-handlers", "prague-kafka", "prague-modeling", "prague-queries", "prague-testing"];

	[GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
	private static partial Regex SkillNameRegex();

	[Test]
	public void SkillDirectories_MatchThePackedSet() {
		var names = Directory.GetDirectories(SkillsRoot()).Select(Path.GetFileName).Order().ToArray();

		Assert.That(names, Is.EqualTo(_expectedSkills));
	}

	[TestCaseSource(nameof(_expectedSkills))]
	public void Skill_HasValidFrontmatter(string skill) {
		var path = Path.Combine(SkillsRoot(), skill, "SKILL.md");
		Assert.That(File.Exists(path), Is.True, path);

		var lines = File.ReadAllLines(path);
		Assert.That(lines[0], Is.EqualTo("---"), "frontmatter must open on the first line");

		var close = Array.IndexOf(lines, "---", 1);
		Assert.That(close, Is.GreaterThan(1), "frontmatter is not closed");

		var fields = lines[1..close]
			.Where(l => l.Length > 0 && !char.IsWhiteSpace(l[0]) && l.Contains(':'))
			.ToDictionary(l => l[..l.IndexOf(':')], l => l[(l.IndexOf(':') + 1)..].Trim().Trim('"'));

		Assert.That(fields, Contains.Key("name").And.ContainKey("description"));

		var name = fields["name"];
		Assert.That(name, Is.EqualTo(skill), "name must equal the directory name");
		Assert.That(name.Length, Is.InRange(1, 64));
		Assert.That(SkillNameRegex().IsMatch(name), Is.True, name);

		Assert.That(fields["description"].Length, Is.InRange(1, 1024));
		Assert.That(lines.Length, Is.LessThanOrEqualTo(500), "spec recommends a body under 500 lines");
	}

	private static string SkillsRoot() {
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
			if (File.Exists(Path.Combine(dir.FullName, "Prague.sln")))
				return Path.Combine(dir.FullName, "skills");

		throw new DirectoryNotFoundException("Prague.sln not found above " + AppContext.BaseDirectory);
	}
}
