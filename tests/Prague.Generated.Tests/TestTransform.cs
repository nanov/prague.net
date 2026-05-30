namespace Prague.Generated.Tests;

using Prague.Core;
using Prague.Tests.Models;
using MessagePack;

[DataCacheFrom<ExternalModel>("b0cd3d49-93fd-081f-155d-447195dd3b53")]
public class MyModel {
	[Key(0)] public int Id { get; set; }

	[Key(2)] public int Age { get; set; }

	[Key(3)] public int Heihgt { get; set; }

	// [Key(1)]
	// public string Name { get; set; }
	[Key(4)] public int Whatever { get; set; }
}