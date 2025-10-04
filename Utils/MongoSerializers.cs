using dndhelper.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace dndhelper.Serialization
{
    /// <summary>
    /// Common BSON utility methods for serializers
    /// </summary>
    internal static class BsonHelper
    {
        public static string ReadStringOrNumber(IBsonReader reader)
        {
            return reader.GetCurrentBsonType() switch
            {
                BsonType.String => reader.ReadString(),
                BsonType.Int32 => reader.ReadInt32().ToString(),
                BsonType.Int64 => reader.ReadInt64().ToString(),
                BsonType.Double => reader.ReadDouble().ToString(),
                _ => throw new FormatException($"Unsupported BsonType {reader.GetCurrentBsonType()} for string or number")
            };
        }

        public static List<string> NormalizeToStringList(BsonValue value)
        {
            if (value == null || value.IsBsonNull) return null;

            if (value.IsString) return new List<string> { value.AsString };

            if (value.IsBsonArray)
                return value.AsBsonArray.Select(v => v.ToString()).ToList();

            if (value.IsBsonDocument)
                return value.AsBsonDocument.Values.Select(v => v.ToString()).ToList();

            throw new FormatException($"Unexpected BSON type {value.BsonType} for normalization to string list");
        }
    }

    /// <summary>
    /// Serializer for MonsterType, handling string or embedded document forms
    /// </summary>
    public class MonsterTypeSerializer : SerializerBase<MonsterType>
    {
        public override MonsterType Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;

            if (reader.CurrentBsonType == BsonType.String)
            {
                return new MonsterType { Type = reader.ReadString() };
            }
            else if (reader.CurrentBsonType == BsonType.Document)
            {
                var doc = BsonDocumentSerializer.Instance.Deserialize(context);

                return new MonsterType
                {
                    Type = doc.TryGetValue("type", out var typeVal) ? typeVal.ToString() : null,
                    Tags = BsonHelper.NormalizeToStringList(doc.TryGetValue("tags", out var tagsVal) ? tagsVal : null)
                };
            }

            throw new FormatException($"Unexpected BSON type {reader.CurrentBsonType} for MonsterType");
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, MonsterType value)
        {
            var writer = context.Writer;

            if (value.Tags == null || value.Tags.Count == 0)
            {
                writer.WriteString(value.Type ?? "");
            }
            else
            {
                BsonSerializer.Serialize(writer, value);
            }
        }
    }

    /// <summary>
    /// Serializer for speed properties that may be int or document with 'number' field
    /// </summary>
    public class MonsterSpeedSerializer : SerializerBase<int?>
    {
        public override int? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;

            switch (reader.CurrentBsonType)
            {
                case BsonType.Int32:
                    return reader.ReadInt32();
                case BsonType.Document:
                    return ReadNumberFromDocument(reader);
                case BsonType.Null:
                    reader.ReadNull();
                    return null;
                default:
                    throw new NotSupportedException($"Cannot deserialize BsonType {reader.CurrentBsonType} to int?");
            }
        }

        private int? ReadNumberFromDocument(IBsonReader reader)
        {
            reader.ReadStartDocument();
            int? number = null;

            while (reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var name = reader.ReadName();
                if (name == "number" && reader.CurrentBsonType == BsonType.Int32)
                    number = reader.ReadInt32();
                else
                    reader.SkipValue();
            }
            reader.ReadEndDocument();

            return number;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, int? value)
        {
            if (value.HasValue)
                context.Writer.WriteInt32(value.Value);
            else
                context.Writer.WriteNull();
        }
    }

    /// <summary>
    /// Serializer for alignment property that may be list of strings or list of documents with nested alignment arrays
    /// </summary>
    public class AlignmentListSerializer : SerializerBase<List<List<string>>>
    {
        public override List<List<string>> Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;
            var result = new List<List<string>>();

            if (reader.CurrentBsonType != BsonType.Array)
                throw new FormatException($"Expected array for alignment, found {reader.CurrentBsonType}");

            reader.ReadStartArray();

            while (reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                switch (reader.CurrentBsonType)
                {
                    case BsonType.Document:
                        result.Add(ReadAlignmentFromDocument(reader));
                        break;
                    case BsonType.String:
                        result.Add(ReadStringSequence(reader));
                        break;
                    default:
                        throw new FormatException($"Unexpected BSON type {reader.CurrentBsonType} in alignment array");
                }
            }

            reader.ReadEndArray();
            return result;
        }

        private List<string> ReadAlignmentFromDocument(IBsonReader reader)
        {
            var list = new List<string>();
            reader.ReadStartDocument();

            while (reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var name = reader.ReadName();
                if (name == "alignment" && reader.CurrentBsonType == BsonType.Array)
                {
                    list = ReadStringArray(reader);
                }
                else
                {
                    reader.SkipValue();
                }
            }

            reader.ReadEndDocument();
            return list;
        }

        private List<string> ReadStringSequence(IBsonReader reader)
        {
            var list = new List<string>();

            // At least one string is here
            list.Add(reader.ReadString());

            // Read following strings (if any)
            while (reader.CurrentBsonType == BsonType.String)
            {
                list.Add(reader.ReadString());
            }

            return list;
        }

        private List<string> ReadStringArray(IBsonReader reader)
        {
            var list = new List<string>();
            reader.ReadStartArray();

            while (reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                if (reader.CurrentBsonType == BsonType.String)
                    list.Add(reader.ReadString());
                else
                    reader.SkipValue();
            }

            reader.ReadEndArray();
            return list;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, List<List<string>> value)
        {
            var writer = context.Writer;
            writer.WriteStartArray();

            foreach (var alignmentGroup in value)
            {
                writer.WriteStartDocument();
                writer.WriteName("alignment");
                writer.WriteStartArray();

                foreach (var alignment in alignmentGroup)
                {
                    writer.WriteString(alignment);
                }

                writer.WriteEndArray();
                writer.WriteEndDocument();
            }

            writer.WriteEndArray();
        }
    }

    ///// <summary>
    ///// Serializer for ChallengeRating supporting simple string/number or detailed document form
    ///// </summary>
    //public class ChallengeRatingSerializer : SerializerBase<ChallangeRating>
    //{
    //    public override ChallangeRating Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    //    {
    //        var reader = context.Reader;
    //        var bsonType = reader.GetCurrentBsonType();
    //        var result = new ChallangeRating();

    //        switch (bsonType)
    //        {
    //            case BsonType.Int32:
    //            case BsonType.Double:
    //            case BsonType.String:
    //                result.CR = BsonHelper.ReadStringOrNumber(reader);
    //                break;

    //            case BsonType.Document:
    //                var doc = BsonDocumentSerializer.Instance.Deserialize(context);
    //                result.CR = doc.TryGetValue("cr", out var crVal) ? crVal.ToString() : null;
    //                result.Lair = doc.TryGetValue("lair", out var lairVal) && lairVal.IsInt32 ? lairVal.AsInt32 : (int?)null;
    //                result.XP = doc.TryGetValue("xp", out var xpVal) && xpVal.IsInt32 ? xpVal.AsInt32 : (int?)null;
    //                break;

    //            default:
    //                throw new FormatException($"Unexpected BSON type {bsonType} for ChallengeRating");
    //        }

    //        return result;
    //    }

    //    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ChallangeRating value)
    //    {
    //        var writer = context.Writer;

    //        if (value.Lair == null && value.XP == null)
    //        {
    //            if (int.TryParse(value.CR, out var intCR))
    //                writer.WriteInt32(intCR);
    //            else
    //                writer.WriteString(value.CR ?? "");
    //        }
    //        else
    //        {
    //            writer.WriteStartDocument();
    //            writer.WriteName("cr");
    //            writer.WriteString(value.CR ?? "");

    //            if (value.Lair != null)
    //            {
    //                writer.WriteName("lair");
    //                writer.WriteInt32(value.Lair.Value);
    //            }

    //            if (value.XP != null)
    //            {
    //                writer.WriteName("xp");
    //                writer.WriteInt32(value.XP.Value);
    //            }

    //            writer.WriteEndDocument();
    //        }
    //    }
    //}

    /// <summary>
    /// Serializer for Passive value which can be string or number but stored always as string
    /// </summary>
    public class PassiveValueSerializer : SerializerBase<string>
    {
        public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;

            return reader.CurrentBsonType switch
            {
                BsonType.String => reader.ReadString(),
                BsonType.Int32 => reader.ReadInt32().ToString(),
                BsonType.Int64 => reader.ReadInt64().ToString(),
                BsonType.Double => reader.ReadDouble().ToString(),
                _ => throw new FormatException($"Unexpected BSON type {reader.CurrentBsonType} for Passive value")
            };
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
        {
            context.Writer.WriteString(value ?? "");
        }
    }
}
