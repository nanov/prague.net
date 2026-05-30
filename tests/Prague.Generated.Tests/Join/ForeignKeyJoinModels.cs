namespace Prague.Generated.Tests.Join;

using Prague.Core;

// Parent entity - the one being referenced
[DataCache]
public partial class Author {
	[DataCacheKey] public int Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string Country { get; set; } = string.Empty;
}

// Child entity with OneToOne relationship - each book has one author
// The AuthorId is a foreign key to Author.Id
[DataCache]
public partial class Book {
	[DataCacheKey] public int Id { get; set; }

	public string Title { get; set; } = string.Empty;

	// This foreign key marks a OneToMany relationship FROM Author's perspective
	// (one author has many books), but from Book's perspective it's ManyToOne
	// The JoinType specifies how THIS entity relates: OneToMany means multiple Books can have the same AuthorId
	[DataCacheForeignKey<Author>(DataCacheJoinType.OneToMany)]
	public int AuthorId { get; set; }

	public int Year { get; set; }
}

// Entity with OneToOne relationship - each author has one profile
[DataCache]
public partial class AuthorProfile {
	[DataCacheKey] public int Id { get; set; }

	// OneToOne: Each author has exactly one profile, and each profile belongs to exactly one author
	[DataCacheForeignKey<Author>(DataCacheJoinType.OneToOne)]
	public int AuthorId { get; set; }

	public string Bio { get; set; } = string.Empty;

	public string Website { get; set; } = string.Empty;
}

// Another child entity - reviews for books
[DataCache]
public partial class BookReview {
	[DataCacheKey] public int Id { get; set; }

	// OneToMany from Book's perspective: one book can have many reviews
	[DataCacheForeignKey<Book>(DataCacheJoinType.OneToMany)]
	public int BookId { get; set; }

	public int Rating { get; set; }

	public string Comment { get; set; } = string.Empty;
}

// ==========================================
// Additional entities for 5-level join tests
// All entities have FKs pointing to Author (the root entity)
// This supports chained joins where all joins reference the LEFT entity
// ==========================================

// Author's awards - one author can have many awards
[DataCache]
public partial class AuthorAward {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<Author>(DataCacheJoinType.OneToMany)]
	public int AuthorId { get; set; }

	public string AwardName { get; set; } = string.Empty;

	public int Year { get; set; }
}

// Author's publisher - one author has one publisher
[DataCache]
public partial class AuthorPublisher {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<Author>(DataCacheJoinType.OneToOne)]
	public int AuthorId { get; set; }

	public string PublisherName { get; set; } = string.Empty;

	public string Country { get; set; } = string.Empty;
}

// Author's events - one author can have many events
[DataCache]
public partial class AuthorEvent {
	[DataCacheKey] public int Id { get; set; }

	[DataCacheForeignKey<Author>(DataCacheJoinType.OneToMany)]
	public int AuthorId { get; set; }

	public string EventName { get; set; } = string.Empty;

	public string Location { get; set; } = string.Empty;
}

// ==========================================
// Test case: Key property that is also a foreign key
// This tests that only ONE With{PropertyName} method is generated
// ==========================================

// Parent entity for the key+FK test
[DataCache]
public partial class ProductEvent {
	[DataCacheKey] public long Id { get; set; }

	public string Name { get; set; } = string.Empty;
}

// Entity where the key is also a foreign key (e.g., a 1:1 extension table)
// EventId is both the primary key AND a foreign key to ProductEvent
[DataCache]
public partial class ProductEventExtension {
	[DataCacheKey]
	[DataCacheForeignKey<ProductEvent>(DataCacheJoinType.OneToOne)]
	public long EventId { get; set; }

	public string ExtraData { get; set; } = string.Empty;

	public int Priority { get; set; }
}
