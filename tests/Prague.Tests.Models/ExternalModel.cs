namespace Prague.Tests.Models;

using MessagePack;

[MessagePackObject]
public class ExternalModel {
	[Key(0)] public int Id { get; set; }

	[Key(1)] public string Name { get; set; }

	[Key(2)] public int Age { get; set; }

	//
	[Key(3)] public int Heihgt { get; set; }

	[Key(4)] public int Whatever { get; set; }
}