using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace dndhelper.Models
{
    /// <summary>Top-level folder of an object in the bucket.</summary>
    public enum StorageFolder
    {
        Images,
        Maps,
        Tokens,
        Music,
        Docs,
    }

    /// <summary>
    /// A file kept in Cloudflare R2. The bytes live in the bucket; this record says where and whose.
    /// Entities (character token, item image...) keep this Id and/or Url.
    /// </summary>
    public class StoredFile : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.String)]
        public StorageFolder Folder { get; set; }

        /// <summary>Object key in the bucket: "{folder}/{random}{ext}". Never derived from user input.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Public URL (R2 custom domain + key); null when the bucket has no public domain.</summary>
        public string? Url { get; set; }

        /// <summary>Detected from the bytes, not from what the client claimed.</summary>
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }

        /// <summary>Sanitised display name only; never used in the key.</summary>
        public string? OriginalName { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string OwnerId { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// Bound from the "R2" section. Keep the keys out of appsettings: set R2__AccessKeyId / R2__SecretAccessKey
    /// as environment variables (or user-secrets in dev). Use an R2 API token scoped to this one bucket, Object Read &amp; Write.
    /// </summary>
    public class R2Options
    {
        /// <summary>https://{accountId}.r2.cloudflarestorage.com (or the .eu. jurisdiction endpoint).</summary>
        public string Endpoint { get; set; } = string.Empty;
        public string AccessKeyId { get; set; } = string.Empty;
        public string SecretAccessKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        /// <summary>Custom domain bound to the bucket, e.g. https://files.example.com. Empty = private bucket, use signed URLs.</summary>
        public string? PublicBaseUrl { get; set; }
    }
}
