namespace Prague.Generated.Tests.Models;

using Prague.Core;

// Valid topics - these should compile successfully

[DataCache]
[DataCacheTopic("valid.topic-name_123")]
public partial class ValidTopic1 {
	[DataCacheKey] public string Id { get; set; }
}

[DataCache]
[DataCacheTopic("Cache.[v:tenant].Orders")]
public partial class ValidTopicWithVarPlaceholder {
	[DataCacheKey] public string Id { get; set; }
}

[DataCache]
[DataCacheTopic("Cache.[e:environment].Orders")]
public partial class ValidTopicWithEnvPlaceholder {
	[DataCacheKey] public string Id { get; set; }
}

[DataCache]
[DataCacheTopic("[v:tenant].[e:env].Orders")]
public partial class ValidTopicWithMultiplePlaceholders {
	[DataCacheKey] public string Id { get; set; }
}

// Invalid topics - these should cause compile errors
// Uncomment to test validation
/*
[DataCache]
[DataCacheTopic("invalid topic with spaces")]
public partial class InvalidTopicWithSpaces {
	[DataCacheKey]
	public string Id { get; set; }
}
*/
/*
[DataCache]
[DataCacheTopic("invalid@topic")]
public partial class InvalidTopicWithAtSign {
	[DataCacheKey]
	public string Id { get; set; }
}
*/
/*
[DataCache]
[DataCacheTopic("topic[unclosed")]
public partial class InvalidTopicUnclosedBracket {
	[DataCacheKey]
	public string Id { get; set; }
}
*/
/*
[DataCache]
[DataCacheTopic("topic[x:invalid]")]
public partial class InvalidTopicWrongPlaceholderType {
	[DataCacheKey]
	public string Id { get; set; }
}
*/
/*
[DataCache]
[DataCacheTopic("topic[v:]")]
public partial class InvalidTopicEmptyPlaceholderName {
	[DataCacheKey]
	public string Id { get; set; }
}
*/