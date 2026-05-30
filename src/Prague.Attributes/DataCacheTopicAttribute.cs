// ReSharper disable once CheckNamespace
namespace Prague.Core;

/// <summary>
///   Marks a class to generate a cache topic constant.
///   When applied, generates a readonly const string property with the cache topic name.
///   If used without parameters, the topic will be "Cache.{ClassName}".
///   If a custom name is provided, that name will be used instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DataCacheTopicAttribute : Attribute {
	/// <summary>
	///   Initializes a new instance of the <see cref="DataCacheTopicAttribute" /> class.
	///   The topic name will default to "Cache.{ClassName}".
	/// </summary>
	public DataCacheTopicAttribute() {
	}

	/// <summary>
	///   Initializes a new instance of the <see cref="DataCacheTopicAttribute" /> class with a custom topic name template.
	/// </summary>
	/// <param name="topicNameTemplate">The custom cache topic name template to use.</param>
	public DataCacheTopicAttribute(string topicNameTemplate) {
		TopicNameTemplate = topicNameTemplate;
	}

	/// <summary>
	///   Gets the custom cache topic name template, if provided.
	/// </summary>
	public string? TopicNameTemplate { get; }
}