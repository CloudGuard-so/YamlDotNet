﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Events = YamlDotNet.Core.Events;

namespace YamlDotNet.Representation.Schemas
{
    public abstract class RegexBasedSchema : ISchema
    {
        protected interface IRegexBasedTag : ITag<Scalar>
        {
            bool Matches(string value, [NotNullWhen(true)] out ITag<Scalar>? resultingTag);
        }

        private sealed class CompositeRegexBasedTag : IRegexBasedTag
        {
            private readonly IRegexBasedTag[] subTags;

            public TagName Name { get; }

            public CompositeRegexBasedTag(TagName name, IEnumerable<IRegexBasedTag> subTags)
            {
                Name = name;
                this.subTags = subTags.ToArray();
            }


            public object? Construct(Scalar node)
            {
                var value = node.Value;
                foreach (var subTag in subTags)
                {
                    if (subTag.Matches(value, out var resultingTag))
                    {
                        return resultingTag.Construct(node);
                    }
                }

                throw new SemanticErrorException($"The value '{value}' could not be parsed as '{Name}'.");
            }

            public Scalar Represent(object? native)
            {
                throw new NotImplementedException();
            }

            public bool Matches(string value, [NotNullWhen(true)] out ITag<Scalar>? resultingTag)
            {
                foreach (var subTag in subTags)
                {
                    if (subTag.Matches(value, out resultingTag))
                    {
                        return true;
                    }
                }

                resultingTag = null;
                return false;
            }
        }

        private sealed class RegexBasedTag : IRegexBasedTag
        {
            private readonly Regex pattern;
            private readonly Func<Scalar, object?> constructor;

            public TagName Name { get; }

            public RegexBasedTag(TagName name, Regex pattern, Func<Scalar, object?> constructor)
            {
                Name = name;
                this.pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
                this.constructor = constructor ?? throw new ArgumentNullException(nameof(constructor));
            }

            public bool Matches(string value, [NotNullWhen(true)] out ITag<Scalar>? resultingTag)
            {
                resultingTag = this;
                return pattern.IsMatch(value);
            }

            public object? Construct(Scalar node) => this.constructor(node);

            public Scalar Represent(object? native)
            {
                throw new NotImplementedException();
            }
        }

        protected sealed class RegexTagMappingTable : IEnumerable<IRegexBasedTag>
        {
            private readonly List<IRegexBasedTag> entries = new List<IRegexBasedTag>();

            public void Add(string pattern, TagName tag, Func<Scalar, object?> constructor)
            {
                Add(new Regex(pattern, StandardRegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture), tag, constructor);
            }

            public void Add(Regex pattern, TagName tag, Func<Scalar, object?> constructor)
            {
                entries.Add(new RegexBasedTag(tag, pattern, constructor));
            }

            public IEnumerator<IRegexBasedTag> GetEnumerator() => entries.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private readonly IDictionary<TagName, IRegexBasedTag> tags;
        private readonly ITag<Scalar>? fallbackTag;

        protected RegexBasedSchema(RegexTagMappingTable tagMappingTable, ITag<Scalar>? fallbackTag)
        {
            this.tags = tagMappingTable
                .GroupBy(e => e.Name)
                .Select(g => g.Count() switch
                {
                    1 => g.First(),
                    _ => new CompositeRegexBasedTag(g.Key, g)
                })
                .ToDictionary(e => e.Name);

            this.fallbackTag = fallbackTag;
        }

        public bool ResolveNonSpecificTag(Events.Scalar node, IEnumerable<CollectionEvent> path, [NotNullWhen(true)] out ITag<Scalar>? resolvedTag)
        {
            if (!node.Tag.IsEmpty)
            {
                resolvedTag = FailsafeSchema.String;
                return true;
            }

            var value = node.Value;
            foreach (var tag in tags.Values)
            {
                if (tag.Matches(value, out resolvedTag))
                {
                    return true;
                }
            }

            resolvedTag = fallbackTag;
            return fallbackTag != null;
        }

        public bool ResolveNonSpecificTag(MappingStart node, IEnumerable<CollectionEvent> path, [NotNullWhen(true)] out ITag<Mapping>? resolvedTag)
        {
            resolvedTag = FailsafeSchema.Mapping;
            return true;
        }

        public bool ResolveNonSpecificTag(SequenceStart node, IEnumerable<CollectionEvent> path, [NotNullWhen(true)] out ITag<Sequence>? resolvedTag)
        {
            resolvedTag = FailsafeSchema.Sequence;
            return true;
        }

        public bool ResolveSpecificTag(TagName tag, [NotNullWhen(true)] out ITag<Scalar>? resolvedTag)
        {
            if (tags.TryGetValue(tag, out var result))
            {
                resolvedTag = result;
                return true;
            }
            else if (fallbackTag != null && tag.Equals(fallbackTag.Name))
            {
                resolvedTag = fallbackTag;
                return true;
            }

            resolvedTag = null;
            return false;
        }

        public bool ResolveSpecificTag(TagName tag, [NotNullWhen(true)] out ITag<Sequence>? resolvedTag)
        {
            resolvedTag = null;
            return false;
        }

        public bool ResolveSpecificTag(TagName tag, [NotNullWhen(true)] out ITag<Mapping>? resolvedTag)
        {
            resolvedTag = null;
            return false;
        }

        public bool IsTagImplicit(Events.Scalar node, IEnumerable<CollectionEvent> path)
        {
            // TODO: Account for style
            if (tags.TryGetValue(node.Tag, out var tag) && tag.Matches(node.Value, out _))
            {
                return true;
            }
            return false;
        }

        public bool IsTagImplicit(MappingStart node, IEnumerable<CollectionEvent> path)
        {
            return false; // TODO
        }

        public bool IsTagImplicit(SequenceStart node, IEnumerable<CollectionEvent> path)
        {
            return false; // TODO
        }
    }
}