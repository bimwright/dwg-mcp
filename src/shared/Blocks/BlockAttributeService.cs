using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal sealed class BlockAttributeUpdateResult
    {
        internal BlockAttributeUpdateResult(object[] updates, string[] missingTags, object[] attributes)
        {
            Updates = updates ?? Array.Empty<object>();
            MissingTags = missingTags ?? Array.Empty<string>();
            Attributes = attributes ?? Array.Empty<object>();
        }

        internal object[] Updates { get; }
        internal string[] MissingTags { get; }
        internal object[] Attributes { get; }

        internal object ToWireObject(bool strictTags)
        {
            return new
            {
                strict_tags = strictTags,
                updates = Updates,
                missing_tags = MissingTags,
                attributes = Attributes
            };
        }
    }

    internal static class BlockAttributeService
    {
        internal static object[] ReadAttributes(BlockReference blockReference, Transaction tx)
        {
            if (blockReference == null)
            {
                throw new ArgumentNullException(nameof(blockReference));
            }

            if (tx == null)
            {
                throw new ArgumentNullException(nameof(tx));
            }

            var attributes = new List<object>();
            foreach (ObjectId attributeId in blockReference.AttributeCollection)
            {
                if (attributeId.IsNull)
                {
                    continue;
                }

                var attribute = tx.GetObject(attributeId, OpenMode.ForRead) as AttributeReference;
                if (attribute == null)
                {
                    continue;
                }

                attributes.Add(new
                {
                    tag = attribute.Tag,
                    value = attribute.TextString,
                    handle = attribute.Handle.ToString()
                });
            }

            return attributes.ToArray();
        }

        internal static BlockAttributeUpdateResult AddAttributeReferencesAndApplyValues(
            Transaction tx,
            BlockReference blockReference,
            ObjectId blockDefinitionId,
            JObject attributes)
        {
            if (tx == null)
            {
                throw new ArgumentNullException(nameof(tx));
            }

            if (blockReference == null)
            {
                throw new ArgumentNullException(nameof(blockReference));
            }

            if (blockDefinitionId.IsNull)
            {
                throw new ArgumentException("block definition id is required", nameof(blockDefinitionId));
            }

            var blockDefinition = (BlockTableRecord)tx.GetObject(blockDefinitionId, OpenMode.ForRead);
            foreach (ObjectId objectId in blockDefinition)
            {
                var attributeDefinition = tx.GetObject(objectId, OpenMode.ForRead) as AttributeDefinition;
                if (attributeDefinition == null || attributeDefinition.Constant)
                {
                    continue;
                }

                AttributeReference attributeReference = null;
                var ownsAttribute = false;
                try
                {
                    attributeReference = new AttributeReference();
                    ownsAttribute = true;
                    attributeReference.SetAttributeFromBlock(attributeDefinition, blockReference.BlockTransform);
                    if (attributes != null &&
                        TryGetAttributeValue(attributes, attributeDefinition.Tag, out var requestedValue))
                    {
                        attributeReference.TextString = requestedValue;
                    }

                    blockReference.AttributeCollection.AppendAttribute(attributeReference);
                    tx.AddNewlyCreatedDBObject(attributeReference, true);
                    ownsAttribute = false;
                }
                finally
                {
                    if (ownsAttribute)
                    {
                        attributeReference?.Dispose();
                    }
                }
            }

            return attributes == null
                ? new BlockAttributeUpdateResult(Array.Empty<object>(), Array.Empty<string>(), ReadAttributes(blockReference, tx))
                : SetAttributes(blockReference, tx, attributes, strictTags: false);
        }

        internal static BlockAttributeUpdateResult SetAttributes(
            BlockReference blockReference,
            Transaction tx,
            JObject attributes,
            bool strictTags)
        {
            if (blockReference == null)
            {
                throw new ArgumentNullException(nameof(blockReference));
            }

            if (tx == null)
            {
                throw new ArgumentNullException(nameof(tx));
            }

            if (attributes == null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }

            var attributeIdsByTag = AttributeIdsByTag(blockReference, tx);
            var updates = new List<object>();
            var missingTags = new List<string>();

            foreach (var property in attributes.Properties())
            {
                var tag = property.Name;
                var value = AttributeValueToString(property.Value);
                if (!attributeIdsByTag.TryGetValue(tag, out var attributeId))
                {
                    missingTags.Add(tag);
                    updates.Add(new
                    {
                        tag,
                        ok = false,
                        value,
                        handle = (string)null,
                        error = strictTags ? "attribute tag not found" : null
                    });
                    continue;
                }

                var attribute = (AttributeReference)tx.GetObject(attributeId, OpenMode.ForWrite);
                attribute.TextString = value;
                updates.Add(new
                {
                    tag = attribute.Tag,
                    ok = true,
                    value = attribute.TextString,
                    handle = attribute.Handle.ToString(),
                    error = (string)null
                });
            }

            return new BlockAttributeUpdateResult(
                updates.ToArray(),
                missingTags.ToArray(),
                ReadAttributes(blockReference, tx));
        }

        private static Dictionary<string, ObjectId> AttributeIdsByTag(BlockReference blockReference, Transaction tx)
        {
            var attributeIdsByTag = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attributeId in blockReference.AttributeCollection)
            {
                if (attributeId.IsNull)
                {
                    continue;
                }

                var attribute = tx.GetObject(attributeId, OpenMode.ForRead) as AttributeReference;
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.Tag))
                {
                    continue;
                }

                if (!attributeIdsByTag.ContainsKey(attribute.Tag))
                {
                    attributeIdsByTag.Add(attribute.Tag, attributeId);
                }
            }

            return attributeIdsByTag;
        }

        private static bool TryGetAttributeValue(JObject attributes, string tag, out string value)
        {
            value = null;
            if (attributes == null || string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            foreach (var property in attributes.Properties())
            {
                if (string.Equals(property.Name, tag, StringComparison.OrdinalIgnoreCase))
                {
                    value = AttributeValueToString(property.Value);
                    return true;
                }
            }

            return false;
        }

        private static string AttributeValueToString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            return token.ToString(Formatting.None);
        }
    }
}
